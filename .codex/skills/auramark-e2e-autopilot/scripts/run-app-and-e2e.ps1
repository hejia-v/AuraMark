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
    [switch]$SkipScreenshots
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
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $h = [User32]::FindWindow($null, $Title)
        if ($h -ne [IntPtr]::Zero) {
            return $h
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

function Assert-UiElementPresent {
    param(
        [Parameter(Mandatory)][System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)][string]$Name
    )

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
        Stop-RunningAuraMark
        Write-Section "Launch App"
        $proc = Start-Process -FilePath $appExe -WorkingDirectory $appOutDir -PassThru
        $result.launched = $true
        $result.pid = $proc.Id
    }

    $hwnd = Wait-ForWindowHandle -Title $WindowTitle -TimeoutSeconds $LaunchTimeoutSeconds
    if ($hwnd -eq [IntPtr]::Zero) {
        throw "Window not found: title='$WindowTitle' within ${LaunchTimeoutSeconds}s."
    }

    Focus-Window -Handle $hwnd

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    if (-not $root) {
        throw "AutomationElement.FromHandle returned null."
    }

    if (-not $SkipUiChecks) {
        $names = @("New", "Open", "Save", "Export")
        foreach ($n in $names) {
            Assert-UiElementPresent -Root $root -Name $n
            $result.ui_checks += [ordered]@{ name = $n; ok = $true }
        }
    }

    if (-not $SkipScreenshots) {
        $result.checkpoints += [ordered]@{ case = "case1"; checkpoint = "app_ready"; path = (Save-Checkpoint -Handle $hwnd -CaseId "case1" -Checkpoint "app_ready" -Seq 1) }
    }

    # Minimal scripted interaction: Ctrl+N then type a short text.
    # This keeps automation lightweight; deeper cases should be added once AutomationId is in place.
    [System.Windows.Forms.SendKeys]::SendWait("^n")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait("AuraMark E2E typing sample{ENTER}line2")
    Start-Sleep -Milliseconds 1200

    if (-not $SkipScreenshots) {
        $result.checkpoints += [ordered]@{ case = "case1"; checkpoint = "after_typing"; path = (Save-Checkpoint -Handle $hwnd -CaseId "case1" -Checkpoint "after_typing" -Seq 2) }
        $result.checkpoints += [ordered]@{ case = "case1"; checkpoint = "after_autosave"; path = (Save-Checkpoint -Handle $hwnd -CaseId "case1" -Checkpoint "after_autosave" -Seq 3) }
    }
}
catch {
    $result.ok = $false
    $result.error = $_.Exception.Message
}

$reportPath = Join-Path $RunRoot "reports/run-app-and-e2e.json"
Write-JsonFile -Path $reportPath -Object $result

if (-not $result.ok) {
    Write-Host "Run/E2E failed. Report: $reportPath"
    exit 4
}

Write-Host "Run/E2E ok. Report: $reportPath"
