[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepoRoot,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$OutputRoot = "C:\\Dev\\AuraMark\\artifacts",

    [string]$BaselineRoot = "",

    [int]$MaxIterations = 4,

    [switch]$SkipTests,
    [switch]$SkipUi,
    [switch]$SkipScreenshots,
    [switch]$SkipDiff
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

if ($MaxIterations -lt 1 -or $MaxIterations -gt 6) {
    throw "MaxIterations must be in range 1..6"
}

$runRoot = New-RunFolder -OutputRoot $OutputRoot -Prefix "e2e"
Write-Host "Run folder: $runRoot"

if ([string]::IsNullOrWhiteSpace($BaselineRoot)) {
    $BaselineRoot = Join-Path $OutputRoot "ui-baseline"
}
$null = New-Item -ItemType Directory -Path $BaselineRoot -Force
Write-Host "Baseline folder: $BaselineRoot"

$summary = [ordered]@{
    ok = $false
    configuration = $Configuration
    max_iterations = $MaxIterations
    iterations = @()
    run_root = $runRoot
    stop_reason = $null
}

for ($i = 1; $i -le $MaxIterations; $i++) {
    Write-Section "Iteration $i/$MaxIterations"
    $iter = [ordered]@{
        iteration = $i
        gates = [ordered]@{
            build = $false
            logs = $false
            run = $false
            diff = $false
        }
        reports = [ordered]@{}
        ok = $false
    }

    # Gate: build/test
    $buildArgs = @(
        "-RepoRoot", $RepoRoot,
        "-Configuration", $Configuration,
        "-RunRoot", $runRoot
    )
    if ($SkipTests) { $buildArgs += "-SkipTests" }

    $buildOk = $true
    try {
        & (Join-Path $PSScriptRoot "build-and-unit.ps1") @buildArgs | Out-Host
    }
    catch {
        $buildOk = $false
        $iter.reports.build = (Join-Path $runRoot "reports/build-and-unit.json")
    }
    $iter.gates.build = $buildOk

    if (-not $buildOk) {
        $iter.ok = $false
        $summary.iterations += $iter
        $summary.stop_reason = "build_failed"
        break
    }

    # Gate: run app + minimal UI checks + screenshots
    $runOk = $true
    try {
        $runArgs = @(
            "-RepoRoot", $RepoRoot,
            "-Configuration", $Configuration,
            "-RunRoot", $runRoot
        )
        if ($SkipUi) { $runArgs += "-SkipUiChecks" }
        if ($SkipScreenshots) { $runArgs += "-SkipScreenshots" }
        & (Join-Path $PSScriptRoot "run-app-and-e2e.ps1") @runArgs | Out-Host
    }
    catch {
        $runOk = $false
        $iter.reports.run = (Join-Path $runRoot "reports/run-app-and-e2e.json")
    }
    $iter.gates.run = $runOk

    # Gate: log scan (build/test logs + harness logs)
    $logOk = $true
    try {
        & (Join-Path $PSScriptRoot "check-logs.ps1") -RunRoot $runRoot | Out-Host
    }
    catch {
        $logOk = $false
        $iter.reports.logs = (Join-Path $runRoot "reports/log-check.json")
    }
    $iter.gates.logs = $logOk

    # Gate: screenshot diff (optional)
    $diffOk = $true
    if (-not $SkipDiff -and -not $SkipScreenshots) {
        try {
            & (Join-Path $PSScriptRoot "analyze-screenshots.ps1") -RunRoot $runRoot -BaselineRoot $BaselineRoot | Out-Host
        }
        catch {
            $diffOk = $false
            $iter.reports.diff = (Join-Path $runRoot "reports/screenshot-diff.json")
        }
    }
    $iter.gates.diff = $diffOk

    $iter.ok = $iter.gates.build -and $iter.gates.run -and $iter.gates.logs -and $iter.gates.diff
    $summary.iterations += $iter

    if ($iter.ok) {
        $summary.ok = $true
        $summary.stop_reason = "pass"
        break
    }

    # At this point, Codex should apply auto-fixes based on reports and re-run the loop.
    # This script exits after first failing iteration to keep the "fix" decision in the agent layer.
    $summary.stop_reason = "needs_fix"
    break
}

$summaryPath = Join-Path $runRoot "reports/summary.json"
Write-JsonFile -Path $summaryPath -Object $summary

Write-Host "Summary: $summaryPath"
if (-not $summary.ok) { exit 10 }
