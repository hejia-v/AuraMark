[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [switch]$OpenApp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$srcRoot = Join-Path $repoRoot "src"
$webRoot = Join-Path $srcRoot "AuraMark.Web"
$solutionPath = Join-Path $srcRoot "AuraMark.sln"
$outputRoot = Join-Path $repoRoot "artifacts"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path $outputRoot "acceptance-$timestamp"
$logRoot = Join-Path $runRoot "logs"
$fixtureRoot = Join-Path $runRoot "fixtures"

New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null

function Write-Section {
    param([string]$Text)
    Write-Host ""
    Write-Host "==== $Text ====" -ForegroundColor Cyan
}

function Invoke-Step {
    param(
        [string]$Name,
        [string]$Command,
        [string]$WorkingDirectory
    )

    Write-Section $Name
    Write-Host "cwd: $WorkingDirectory"
    Write-Host "cmd: $Command"

    $baseName = "{0}" -f ($Name -replace "[^a-zA-Z0-9_-]", "_")
    $logPath = Join-Path $logRoot "$baseName.log"
    $stdoutPath = Join-Path $logRoot "$baseName.stdout.log"
    $stderrPath = Join-Path $logRoot "$baseName.stderr.log"

    $proc = Start-Process `
        -FilePath "cmd.exe" `
        -ArgumentList "/c $Command" `
        -WorkingDirectory $WorkingDirectory `
        -NoNewWindow `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath

    $stdout = if (Test-Path $stdoutPath) { Get-Content -Path $stdoutPath -Encoding UTF8 } else { @() }
    $stderr = if (Test-Path $stderrPath) { Get-Content -Path $stderrPath -Encoding UTF8 } else { @() }
    $combined = @($stdout + $stderr)
    $combined | Set-Content -Path $logPath -Encoding UTF8
    $combined | ForEach-Object { Write-Host $_ }

    if ($proc.ExitCode -ne 0) {
        throw "Step failed: $Name (exit code: $($proc.ExitCode))"
    }
}

function Assert-PathExists {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing required artifact: $Label ($Path)"
    }

    Write-Host "[OK] $Label -> $Path" -ForegroundColor Green
}

function Stop-RunningAuraMark {
    $running = Get-Process -Name "AuraMark.App" -ErrorAction SilentlyContinue
    if (-not $running) {
        return
    }

    Write-Section "Stop Running AuraMark"
    foreach ($proc in $running) {
        Write-Host "Stopping process: $($proc.ProcessName) ($($proc.Id))"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Seconds 1
}

Stop-RunningAuraMark

if (-not $SkipBuild) {
    Invoke-Step -Name "npm_ci" -Command "npm.cmd ci" -WorkingDirectory $webRoot
    Invoke-Step -Name "npm_build" -Command "npm.cmd run build" -WorkingDirectory $webRoot
    Invoke-Step -Name "dotnet_build_$Configuration" -Command "dotnet build AuraMark.sln -c $Configuration" -WorkingDirectory $srcRoot
}

$appOutDir = Join-Path $srcRoot ("AuraMark.App\bin\{0}\net8.0-windows" -f $Configuration)
$appExe = Join-Path $appOutDir "AuraMark.App.exe"
$editorIndex = Join-Path $appOutDir "EditorView\index.html"
$editorAssetsDir = Join-Path $appOutDir "EditorView\assets"

Write-Section "Artifact Checks"
Assert-PathExists -Path $appExe -Label "AuraMark executable"
Assert-PathExists -Path $editorIndex -Label "EditorView index"
Assert-PathExists -Path $editorAssetsDir -Label "EditorView assets folder"

Write-Section "Fixture Generation"

$largeFilePath = Join-Path $fixtureRoot "large-6mb.md"
$targetBytes = 6MB + 128KB
$line = "## AuraMark large-file test line with deterministic payload." + [Environment]::NewLine
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

$pngPath = Join-Path $fixtureRoot "sample.png"
$pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7YQm0AAAAASUVORK5CYII="
[System.IO.File]::WriteAllBytes($pngPath, [Convert]::FromBase64String($pngBase64))

$imageCasePath = Join-Path $fixtureRoot "image-case.md"
@"
# Image Case

Inline local image should render:

![sample](sample.png)
"@ | Set-Content -Path $imageCasePath -Encoding UTF8

$readonlyPath = Join-Path $fixtureRoot "readonly.md"
@"
# Readonly Case

Try editing this file and trigger autosave.
Expected: soft save error + retry hint, editor should not freeze.
"@ | Set-Content -Path $readonlyPath -Encoding UTF8
(Get-Item -LiteralPath $readonlyPath).IsReadOnly = $true

Write-Host "[OK] Large file -> $largeFilePath"
Write-Host "[OK] Image case -> $imageCasePath"
Write-Host "[OK] Readonly case -> $readonlyPath"

$manualChecklistPath = Join-Path $runRoot "manual-checklist.md"
@"
# AuraMark Manual Acceptance Checklist

Generated at: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Build configuration: $Configuration

## Environment
- App: $appExe
- Fixture folder: $fixtureRoot
- Large file: $largeFilePath
- Image case: $imageCasePath
- Readonly case: $readonlyPath

## PRD 6.3 Cases

| Case | Steps | Expected | Result |
|---|---|---|---|
| 1. New -> type -> autosave -> restart | Open app; Ctrl+N; type text; idle > 500ms; close and reopen app | Saving dot disappears; content is restored | [ ] Pass / [ ] Fail |
| 2. 5MB+ file loading | Ctrl+O and open large-6mb.md | Loading overlay appears; UI remains responsive; file renders | [ ] Pass / [ ] Fail |
| 3. Immersive mode | Keep typing for >=3s; then move mouse across a large distance | Top bar/sidebar auto hide then wake up | [ ] Pass / [ ] Fail |
| 4. External hot reload | Open any .md in fixture; edit same file externally and save | Editor content updates automatically | [ ] Pass / [ ] Fail |
| 5. Save failure hint | Open readonly.md; type and wait autosave | Soft error appears with retry hint; editing remains available | [ ] Pass / [ ] Fail |

## Notes
- Remember to clear readonly flag after test:
  Set-ItemProperty -Path "$readonlyPath" -Name IsReadOnly -Value $false
"@ | Set-Content -Path $manualChecklistPath -Encoding UTF8

Write-Section "Summary"
Write-Host "Checklist file: $manualChecklistPath" -ForegroundColor Yellow
Write-Host "Run folder: $runRoot" -ForegroundColor Yellow

if ($OpenApp) {
    Write-Section "Launch App"
    Start-Process -FilePath $appExe
    Write-Host "AuraMark launched. Follow manual-checklist.md."
}

Write-Host ""
Write-Host "Acceptance prep finished." -ForegroundColor Green
