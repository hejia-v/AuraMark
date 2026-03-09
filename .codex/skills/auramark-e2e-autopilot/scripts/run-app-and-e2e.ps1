[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepoRoot,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [Parameter(Mandatory)]
    [string]$RunRoot,

    [int]$LaunchTimeoutSeconds = 25,

    [string]$WindowTitle = "AuraMark",

    [switch]$SkipLaunch,
    [switch]$SkipUiChecks,
    [switch]$SkipScreenshots,

    [switch]$NonDisruptive,
    [switch]$CloseAfter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient

$srcRoot = Join-Path $RepoRoot "src"
$appOutDir = Join-Path $srcRoot ("AuraMark.App/bin/{0}/net8.0-windows" -f $Configuration)
$appExe = Join-Path $appOutDir "AuraMark.App.exe"

$logRoot = Join-Path $RunRoot "logs"
$shotRoot = Join-Path $RunRoot "screenshots/current"
$fixtureRoot = Join-Path $RunRoot "fixtures"

function Stop-RunningAuraMark {
    $running = Get-Process -Name "AuraMark.App" -ErrorAction SilentlyContinue
    if (-not $running) { return }
    Write-Section "Stop Running AuraMark"
    foreach ($proc in $running) {
        Write-Host "Stopping process: $($proc.ProcessName) ($($proc.Id))"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $proc.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 1
}

function Add-User32Interop {
    $signature = @"
using System;
using System.Runtime.InteropServices;

public static class User32 {
  [DllImport("user32.dll", SetLastError=true)]
  public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  [DllImport("user32.dll")]
  public static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool SetCursorPos(int X, int Y);

  public const int SW_RESTORE = 9;

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }
}
"@

    Add-Type -TypeDefinition $signature -Language CSharp -ErrorAction Stop | Out-Null
}

function Wait-ForWindowHandle {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [int]$TargetProcessId = 0
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($TargetProcessId -gt 0) {
            $proc = Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue
            if ($proc -and $proc.MainWindowHandle -ne 0) {
                return [IntPtr]$proc.MainWindowHandle
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($Title) -and $TargetProcessId -le 0) {
            $h = [User32]::FindWindow($null, $Title)
            if ($h -ne [IntPtr]::Zero) {
                return $h
            }
        }
        Start-Sleep -Milliseconds 200
    }
    return [IntPtr]::Zero
}

function Take-WindowScreenshot {
    param(
        [Parameter(Mandatory)][IntPtr]$Handle,
        [Parameter(Mandatory)][string]$Path
    )

    $rect = New-Object User32+RECT
    $ok = [User32]::GetWindowRect($Handle, [ref]$rect)
    if (-not $ok) {
        throw "GetWindowRect failed."
    }

    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)

    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
        $dir = Split-Path -Parent $Path
        $null = New-Item -ItemType Directory -Path $dir -Force
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

function Save-Checkpoint {
    param(
        [Parameter(Mandatory)][IntPtr]$Handle,
        [Parameter(Mandatory)][string]$CaseId,
        [Parameter(Mandatory)][string]$Checkpoint,
        [int]$Seq = 1
    )

    $rect = New-Object User32+RECT
    $ok = [User32]::GetWindowRect($Handle, [ref]$rect)
    if (-not $ok) { throw "GetWindowRect failed." }
    $w = [Math]::Max(1, $rect.Right - $rect.Left)
    $h = [Math]::Max(1, $rect.Bottom - $rect.Top)

    $name = "{0}__{1}__{2}x{3}__{4:000}.png" -f $CaseId, $Checkpoint, $w, $h, $Seq
    $path = Join-Path $shotRoot $name
    Take-WindowScreenshot -Handle $Handle -Path $path
    $metaPath = [System.IO.Path]::ChangeExtension($path, ".json")
    Write-JsonFile -Path $metaPath -Object ([ordered]@{
        case = $CaseId
        checkpoint = $Checkpoint
        seq = $Seq
        window_title = $WindowTitle
        rect = [ordered]@{ left = $rect.Left; top = $rect.Top; right = $rect.Right; bottom = $rect.Bottom }
        captured_at = (Get-Date).ToString("s")
    })
    return $path
}

function Find-UiElementByName {
    param(
        [Parameter(Mandatory)][System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)][string]$Name
    )

    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-UiElementByAutomationId {
    param(
        [Parameter(Mandatory)][System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)][string]$AutomationId
    )

    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Assert-UiElementPresent {
    param(
        [Parameter(Mandatory)][System.Windows.Automation.AutomationElement]$Root,
        [string]$Name = "",
        [string]$AutomationId = ""
    )

    if ([string]::IsNullOrWhiteSpace($Name) -and [string]::IsNullOrWhiteSpace($AutomationId)) {
        throw "Assert-UiElementPresent requires Name or AutomationId."
    }

    if (-not [string]::IsNullOrWhiteSpace($AutomationId)) {
        $el = Find-UiElementByAutomationId -Root $Root -AutomationId $AutomationId
        if (-not $el) {
            throw "UI element not found by AutomationId='$AutomationId'."
        }
        return
    }

    $el = Find-UiElementByName -Root $Root -Name $Name
    if (-not $el) {
        throw "UI element not found by Name='$Name'. Consider adding AutomationId for stable E2E."
    }
}

function Focus-Window {
    param([Parameter(Mandatory)][IntPtr]$Handle)
    [User32]::ShowWindow($Handle, [User32]::SW_RESTORE) | Out-Null
    [User32]::SetForegroundWindow($Handle) | Out-Null
    Start-Sleep -Milliseconds 250
}

function New-E2eFixtures {
    param([Parameter(Mandatory)][string]$Root)

    $null = New-Item -ItemType Directory -Path $Root -Force

    $largeFilePath = Join-Path $Root "large-6mb.md"
    $targetBytes = 6MB + 128KB
    $line = "## AuraMark e2e large-file payload line." + [Environment]::NewLine
    $utf8 = [System.Text.Encoding]::UTF8
    $lineBytes = $utf8.GetByteCount($line)
    $writtenBytes = 0
    $writer = [System.IO.StreamWriter]::new($largeFilePath, $false, [System.Text.UTF8Encoding]::new($false))
    try {
        while ($writtenBytes -lt $targetBytes) {
            $writer.Write($line)
            $writtenBytes += $lineBytes
        }
    }
    finally {
        $writer.Dispose()
    }

    $externalPath = Join-Path $Root "external-sync.md"
    @"
# External Sync

Initial content before external update.
"@ | Set-Content -Path $externalPath -Encoding UTF8

    $readonlyPath = Join-Path $Root "readonly.md"
@"
# Readonly Case

This file is readonly. E2E should click Save and show a soft save error plus retry.
"@ | Set-Content -Path $readonlyPath -Encoding UTF8
    (Get-Item -LiteralPath $readonlyPath).IsReadOnly = $true

    $sourceModePath = Join-Path $Root "source-mode.md"
@"
# Source Mode

Host-side source editor should append deterministic text and save it back to disk.
"@ | Set-Content -Path $sourceModePath -Encoding UTF8

    return [ordered]@{
        large = $largeFilePath
        external = $externalPath
        readonly = $readonlyPath
        source = $sourceModePath
    }
}

function Start-AuraMarkSession {
    param(
        [string[]]$LaunchArguments = @(),
        [switch]$SkipUiChecks,
        [string]$CaseId
    )

    if (-not (Test-Path -LiteralPath $appExe)) {
        throw "Missing app exe: $appExe. Build first."
    }

    if (-not $NonDisruptive) {
        Stop-RunningAuraMark
    }

    $startArgs = @{
        FilePath = $appExe
        WorkingDirectory = $appOutDir
        PassThru = $true
    }
    if ($LaunchArguments.Count -gt 0) {
        $escapedArgs = $LaunchArguments | ForEach-Object {
            if ($_ -match "\s") { '"' + $_ + '"' } else { $_ }
        }
        $startArgs["ArgumentList"] = ($escapedArgs -join " ")
    }
    if ($NonDisruptive) {
        $startArgs.WindowStyle = "Minimized"
    }

    $proc = Start-Process @startArgs
    $launchCmd = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)" -ErrorAction SilentlyContinue).CommandLine
    $result.launches += [ordered]@{
        case = $CaseId
        pid = $proc.Id
        command_line = $launchCmd
    }
    $hwnd = Wait-ForWindowHandle -Title $WindowTitle -TimeoutSeconds $LaunchTimeoutSeconds -TargetProcessId $proc.Id
    if ($hwnd -eq [IntPtr]::Zero) {
        throw "Window not found: title='$WindowTitle' case='$CaseId' within ${LaunchTimeoutSeconds}s."
    }

    if (-not $NonDisruptive) {
        Focus-Window -Handle $hwnd
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    if (-not $root) {
        throw "AutomationElement.FromHandle returned null for case '$CaseId'."
    }

    if (-not $SkipUiChecks) {
        $ids = @(
            "QuickOpenButton",
            "SaveFileButton",
            "WindowMinimizeButton",
            "WindowCloseButton"
        )
        foreach ($id in $ids) {
            Assert-UiElementPresent -Root $root -AutomationId $id
            $result.ui_checks += [ordered]@{ case = $CaseId; name = $id; ok = $true }
        }
    }

    return [ordered]@{
        process = $proc
        handle = $hwnd
        root = $root
    }
}

function Stop-AuraMarkSession {
    param($Session)
    if ($null -eq $Session) { return }
    if ($Session.process -and $Session.process.Id) {
        Stop-Process -Id $Session.process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $Session.process.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
}

function Add-Checkpoint {
    param(
        [Parameter(Mandatory)][IntPtr]$Handle,
        [Parameter(Mandatory)][string]$CaseId,
        [Parameter(Mandatory)][string]$Checkpoint,
        [int]$Seq = 1
    )

    $path = Save-Checkpoint -Handle $Handle -CaseId $CaseId -Checkpoint $Checkpoint -Seq $Seq
    $result.checkpoints += [ordered]@{
        case = $CaseId
        checkpoint = $Checkpoint
        path = $path
    }
}

function Invoke-ButtonByAutomationId {
    param(
        [Parameter(Mandatory)][System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)][string]$AutomationId,
        [int]$TimeoutSeconds = 6
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $button = $null
    while ((Get-Date) -lt $deadline) {
        $button = Find-UiElementByAutomationId -Root $Root -AutomationId $AutomationId
        if ($button) {
            break
        }
        Start-Sleep -Milliseconds 200
    }

    if (-not $button) {
        throw "Button not found by AutomationId='$AutomationId'."
    }

    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if (-not $pattern) {
        throw "Button '$AutomationId' does not support InvokePattern."
    }

    $pattern.Invoke()
}

function Move-MouseAcrossWindow {
    param([Parameter(Mandatory)][IntPtr]$Handle)

    $rect = New-Object User32+RECT
    $ok = [User32]::GetWindowRect($Handle, [ref]$rect)
    if (-not $ok) { return }

    $startX = $rect.Left + 80
    $startY = $rect.Top + 80
    $endX = $rect.Right - 80
    $endY = $rect.Bottom - 80

    [User32]::SetCursorPos($startX, $startY) | Out-Null
    Start-Sleep -Milliseconds 100
    [User32]::SetCursorPos($endX, $endY) | Out-Null
}

function Wait-UntilFileContains {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Needle,
        [int]$TimeoutSeconds = 8
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            $content = Get-Content -Raw -Path $Path -ErrorAction SilentlyContinue
            if ($content -and $content.IndexOf($Needle, [System.StringComparison]::Ordinal) -ge 0) {
                return $true
            }
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

Add-User32Interop

$shouldSkipUiChecks = $SkipUiChecks
$shouldSkipScreenshots = $SkipScreenshots
$shouldCloseAfter = $CloseAfter
if ($NonDisruptive) {
    if (-not $PSBoundParameters.ContainsKey("SkipUiChecks")) { $shouldSkipUiChecks = $true }
    if (-not $PSBoundParameters.ContainsKey("SkipScreenshots")) { $shouldSkipScreenshots = $true }
    if (-not $PSBoundParameters.ContainsKey("CloseAfter")) { $shouldCloseAfter = $true }
}

$result = [ordered]@{
    ok = $true
    app_exe = $appExe
    launched = $false
    pid = $null
    fixture_root = $fixtureRoot
    launches = @()
    checkpoints = @()
    ui_checks = @()
}

try {
    $fixtures = New-E2eFixtures -Root $fixtureRoot

    if (-not $SkipLaunch) {
        Write-Section "Case1: New -> save"
        $session = Start-AuraMarkSession -LaunchArguments @("--e2e") -SkipUiChecks:$shouldSkipUiChecks -CaseId "case1"
        try {
            $result.launched = $true
            $result.pid = $session.process.Id
            if (-not $shouldSkipScreenshots) {
                Add-Checkpoint -Handle $session.handle -CaseId "case1" -Checkpoint "app_ready" -Seq 1
                Start-Sleep -Milliseconds 300
                Add-Checkpoint -Handle $session.handle -CaseId "case1" -Checkpoint "after_typing" -Seq 2
            }
            Invoke-ButtonByAutomationId -Root $session.root -AutomationId "SaveFileButton"
            Start-Sleep -Milliseconds 500
            if (-not $shouldSkipScreenshots) {
                Add-Checkpoint -Handle $session.handle -CaseId "case1" -Checkpoint "after_save" -Seq 3
            }
        }
        finally {
            Stop-AuraMarkSession -Session $session
        }

        if (-not $NonDisruptive) {
            Write-Section "Case2: Large file loading"
            $session = Start-AuraMarkSession -LaunchArguments @("--e2e-open", $fixtures.large) -SkipUiChecks:$true -CaseId "case2"
            try {
                if (-not $shouldSkipScreenshots) {
                    Add-Checkpoint -Handle $session.handle -CaseId "case2" -Checkpoint "largefile_loading" -Seq 1
                    Start-Sleep -Milliseconds 1200
                    Add-Checkpoint -Handle $session.handle -CaseId "case2" -Checkpoint "largefile_loaded" -Seq 2
                }
            }
            finally {
                Stop-AuraMarkSession -Session $session
            }

            Write-Section "Case3: Immersive enter/exit"
            $session = Start-AuraMarkSession -LaunchArguments @("--e2e-force-immersive") -SkipUiChecks:$true -CaseId "case3"
            try {
                Start-Sleep -Milliseconds 500
                if (-not $shouldSkipScreenshots) {
                    Add-Checkpoint -Handle $session.handle -CaseId "case3" -Checkpoint "immersive_entered" -Seq 1
                }
                Move-MouseAcrossWindow -Handle $session.handle
                Start-Sleep -Milliseconds 400
                if (-not $shouldSkipScreenshots) {
                    Add-Checkpoint -Handle $session.handle -CaseId "case3" -Checkpoint "immersive_exited" -Seq 2
                }
            }
            finally {
                Stop-AuraMarkSession -Session $session
            }

            Write-Section "Case4: External change hot reload"
            $session = Start-AuraMarkSession -LaunchArguments @("--e2e-open", $fixtures.external) -SkipUiChecks:$true -CaseId "case4"
            try {
                Start-Sleep -Milliseconds 500
                @"
# External Sync

Updated by run-app-and-e2e at $(Get-Date -Format "s").
"@ | Set-Content -Path $fixtures.external -Encoding UTF8
                Start-Sleep -Milliseconds 900
                if (-not $shouldSkipScreenshots) {
                    Add-Checkpoint -Handle $session.handle -CaseId "case4" -Checkpoint "external_change_detected" -Seq 1
                }
            }
            finally {
                Stop-AuraMarkSession -Session $session
            }

            Write-Section "Case5: Save error + retry"
            $session = Start-AuraMarkSession -LaunchArguments @("--e2e", "--e2e-open", $fixtures.readonly) -SkipUiChecks:$true -CaseId "case5"
            try {
                Start-Sleep -Milliseconds 400
                Invoke-ButtonByAutomationId -Root $session.root -AutomationId "SaveFileButton"
            Start-Sleep -Milliseconds 1200
            if (-not $shouldSkipScreenshots) {
                Add-Checkpoint -Handle $session.handle -CaseId "case5" -Checkpoint "save_error_toast_shown" -Seq 1
            }
                if (-not $shouldSkipUiChecks) {
                    try {
                        Invoke-ButtonByAutomationId -Root $session.root -AutomationId "RetrySaveButton"
                        $result.ui_checks += [ordered]@{ case = "case5"; name = "RetrySaveButton"; ok = $true }
                    }
                    catch {
                        $result.ui_checks += [ordered]@{ case = "case5"; name = "RetrySaveButton"; ok = $false; note = $_.Exception.Message }
                    }
                }
                Start-Sleep -Milliseconds 500
                if (-not $shouldSkipScreenshots) {
                    Add-Checkpoint -Handle $session.handle -CaseId "case5" -Checkpoint "save_error_retry_clicked" -Seq 2
                }
            }
            finally {
                Stop-AuraMarkSession -Session $session
            }

            Write-Section "Case6: Close with unsaved changes"
            $session = Start-AuraMarkSession -LaunchArguments @("--e2e") -SkipUiChecks:$true -CaseId "case6"
            try {
                Start-Sleep -Milliseconds 400
                Invoke-ButtonByAutomationId -Root $session.root -AutomationId "WindowCloseButton"
                $promptHandle = Wait-ForWindowHandle -Title "Unsaved changes" -TimeoutSeconds 6 -TargetProcessId $session.process.Id
                if ($promptHandle -eq [IntPtr]::Zero) {
                    throw "Unsaved changes dialog not shown when closing dirty document."
                }
                $result.ui_checks += [ordered]@{ case = "case6"; name = "UnsavedChangesDialog"; ok = $true }
                if (-not $shouldSkipScreenshots) {
                    Add-Checkpoint -Handle $promptHandle -CaseId "case6" -Checkpoint "unsaved_changes_prompt" -Seq 1
                }
            }
            finally {
                Stop-AuraMarkSession -Session $session
            }

            Write-Section "Case7: Source mode edit + save"
            $sourceAppendToken = "<!-- E2E source mode append -->"
            $session = Start-AuraMarkSession -LaunchArguments @(
                "--e2e-open", $fixtures.source,
                "--e2e-source-mode",
                "--e2e-source-append", $sourceAppendToken) -SkipUiChecks:$true -CaseId "case7"
            try {
                Start-Sleep -Milliseconds 1200
                if (-not $shouldSkipUiChecks) {
                    try {
                        Assert-UiElementPresent -Root $session.root -AutomationId "SourceModeToggleButton"
                        $result.ui_checks += [ordered]@{ case = "case7"; name = "SourceModeToggleButton"; ok = $true }
                    }
                    catch {
                        $result.ui_checks += [ordered]@{ case = "case7"; name = "SourceModeToggleButton"; ok = $false; note = $_.Exception.Message }
                    }
                }
                if (-not $shouldSkipScreenshots) {
                    Add-Checkpoint -Handle $session.handle -CaseId "case7" -Checkpoint "source_mode_ready" -Seq 1
                }

                Invoke-ButtonByAutomationId -Root $session.root -AutomationId "SaveFileButton"
                $saved = Wait-UntilFileContains -Path $fixtures.source -Needle $sourceAppendToken -TimeoutSeconds 8
                if (-not $saved) {
                    throw "Source mode content was not persisted to disk."
                }
                $result.ui_checks += [ordered]@{ case = "case7"; name = "SourceModeSavedContent"; ok = $true }

                if (-not $shouldSkipScreenshots) {
                    Add-Checkpoint -Handle $session.handle -CaseId "case7" -Checkpoint "source_mode_saved" -Seq 2
                }
            }
            finally {
                Stop-AuraMarkSession -Session $session
            }
        }
    }
}
catch {
    $result.ok = $false
    $result.error = $_.Exception.Message
}
finally {
    if (Test-Path -LiteralPath (Join-Path $fixtureRoot "readonly.md")) {
        try {
            Set-ItemProperty -Path (Join-Path $fixtureRoot "readonly.md") -Name IsReadOnly -Value $false
        }
        catch {
            # ignore cleanup failures
        }
    }

    if ($shouldCloseAfter) {
        Stop-RunningAuraMark
    }
}

$reportPath = Join-Path $RunRoot "reports/run-app-and-e2e.json"
Write-JsonFile -Path $reportPath -Object $result

if (-not $result.ok) {
    Write-Host "Run/E2E failed. Report: $reportPath"
    exit 4
}

Write-Host "Run/E2E ok. Report: $reportPath"
