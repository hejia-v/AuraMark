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

function Stop-RunningAuraMark {
    $running = Get-Process -Name "AuraMark.App" -ErrorAction SilentlyContinue
    if (-not $running) { return }
    Write-Section "Stop Running AuraMark"
    foreach ($proc in $running) {
        Write-Host "Stopping process: $($proc.ProcessName) ($($proc.Id))"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
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
        if (-not [string]::IsNullOrWhiteSpace($Title)) {
            $h = [User32]::FindWindow($null, $Title)
            if ($h -ne [IntPtr]::Zero) {
                return $h
            }
        }

        if ($TargetProcessId -gt 0) {
            $proc = Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue
            if ($proc -and $proc.MainWindowHandle -ne 0) {
                return [IntPtr]$proc.MainWindowHandle
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
    checkpoints = @()
    ui_checks = @()
}

try {
    if (-not (Test-Path -LiteralPath $appExe)) {
        throw "Missing app exe: $appExe. Build first."
    }

    if (-not $SkipLaunch) {
        if (-not $NonDisruptive) {
            Stop-RunningAuraMark
        }
        Write-Section "Launch App"
        $startArgs = @{
            FilePath = $appExe
            WorkingDirectory = $appOutDir
            PassThru = $true
        }
        if ($NonDisruptive) {
            $startArgs.WindowStyle = "Minimized"
        } else {
            $startArgs.ArgumentList = @("--e2e")
        }
        $proc = Start-Process @startArgs
        $result.launched = $true
        $result.pid = $proc.Id
    }

    $hwnd = Wait-ForWindowHandle -Title $WindowTitle -TimeoutSeconds $LaunchTimeoutSeconds -TargetProcessId $result.pid
    if ($hwnd -eq [IntPtr]::Zero) {
        throw "Window not found: title='$WindowTitle' within ${LaunchTimeoutSeconds}s."
    }

    if (-not $NonDisruptive) {
        Focus-Window -Handle $hwnd
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    if (-not $root) {
        throw "AutomationElement.FromHandle returned null."
    }

    if (-not $shouldSkipUiChecks) {
        $names = @("New", "Open", "Save", "Export")
        foreach ($n in $names) {
            Assert-UiElementPresent -Root $root -Name $n
            $result.ui_checks += [ordered]@{ name = $n; ok = $true }
        }

        $ids = @("WindowMinimizeButton", "WindowCloseButton")
        foreach ($id in $ids) {
            Assert-UiElementPresent -Root $root -AutomationId $id
            $result.ui_checks += [ordered]@{ name = $id; ok = $true }
        }
    }

    if (-not $shouldSkipScreenshots) {
        $result.checkpoints += [ordered]@{ case = "case1"; checkpoint = "app_ready"; path = (Save-Checkpoint -Handle $hwnd -CaseId "case1" -Checkpoint "app_ready" -Seq 1) }
    }

    if (-not $NonDisruptive -and -not $shouldSkipScreenshots) {
        # App is launched with --e2e to inject markdown programmatically (no OS SendKeys / IME dependency).
        Start-Sleep -Milliseconds 250
        $result.checkpoints += [ordered]@{ case = "case1"; checkpoint = "after_typing"; path = (Save-Checkpoint -Handle $hwnd -CaseId "case1" -Checkpoint "after_typing" -Seq 2) }
        Start-Sleep -Milliseconds 900
        $result.checkpoints += [ordered]@{ case = "case1"; checkpoint = "after_autosave"; path = (Save-Checkpoint -Handle $hwnd -CaseId "case1" -Checkpoint "after_autosave" -Seq 3) }
    }
}
catch {
    $result.ok = $false
    $result.error = $_.Exception.Message
}
finally {
    if ($shouldCloseAfter -and $result.launched -and $result.pid) {
        Stop-Process -Id $result.pid -Force -ErrorAction SilentlyContinue
    }
}

$reportPath = Join-Path $RunRoot "reports/run-app-and-e2e.json"
Write-JsonFile -Path $reportPath -Object $result

if (-not $result.ok) {
    Write-Host "Run/E2E failed. Report: $reportPath"
    exit 4
}

Write-Host "Run/E2E ok. Report: $reportPath"
