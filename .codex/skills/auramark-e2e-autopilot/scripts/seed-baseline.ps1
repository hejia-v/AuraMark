[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [Parameter(Mandatory)]
    [string]$BaselineRoot,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$currentRoot = Join-Path $RunRoot "screenshots/current"
if (-not (Test-Path -LiteralPath $currentRoot)) {
    throw "Missing current screenshots folder: $currentRoot"
}

$null = New-Item -ItemType Directory -Path $BaselineRoot -Force

$copied = 0
$skipped = 0
$overwritten = 0

$imgs = Get-ChildItem -Path $currentRoot -Filter "*.png" -File -ErrorAction SilentlyContinue
foreach ($img in $imgs) {
    $dest = Join-Path $BaselineRoot $img.Name
    if ((Test-Path -LiteralPath $dest) -and -not $Force) {
        $skipped++
        continue
    }

    Copy-Item -LiteralPath $img.FullName -Destination $dest -Force
    if (Test-Path -LiteralPath $dest) {
        if ($Force) { $overwritten++ } else { $copied++ }
    }
}

$report = [ordered]@{
    ok = $true
    baseline_root = $BaselineRoot
    copied = $copied
    skipped = $skipped
    overwritten = $overwritten
}

$reportPath = Join-Path $RunRoot "reports/seed-baseline.json"
Write-JsonFile -Path $reportPath -Object $report
Write-Host "Seed baseline ok. Report: $reportPath"

