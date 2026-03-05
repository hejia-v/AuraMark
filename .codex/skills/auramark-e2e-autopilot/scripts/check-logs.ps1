[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$logRoot = Join-Path $RunRoot "logs"
if (-not (Test-Path -LiteralPath $logRoot)) {
    throw "Missing logs folder: $logRoot"
}

$highPatterns = @(
    "Unhandled Exception",
    "NullReferenceException",
    "AccessViolationException",
    "The process cannot access the file",
    "CoreWebView2",
    "WebView2.*fail",
    "fatal"
)

$mediumPatterns = @(
    "warning",
    "timeout",
    "retry"
)

$findings = @()

$files = Get-ChildItem -Path $logRoot -Filter "*.log" -File -ErrorAction SilentlyContinue
foreach ($f in $files) {
    $content = Get-Content -Path $f.FullName -ErrorAction SilentlyContinue
    if (-not $content) { continue }

    foreach ($p in $highPatterns) {
        $hits = $content | Select-String -Pattern $p -SimpleMatch:$false -ErrorAction SilentlyContinue
        foreach ($h in $hits) {
            $findings += [ordered]@{
                severity = "high"
                pattern = $p
                file = $f.FullName
                line = $h.Line.Trim()
            }
        }
    }

    foreach ($p in $mediumPatterns) {
        $hits = $content | Select-String -Pattern $p -SimpleMatch:$false -ErrorAction SilentlyContinue
        foreach ($h in $hits) {
            $findings += [ordered]@{
                severity = "medium"
                pattern = $p
                file = $f.FullName
                line = $h.Line.Trim()
            }
        }
    }
}

$report = [ordered]@{
    ok = ($findings | Where-Object { $_.severity -eq "high" } | Measure-Object).Count -eq 0
    high_count = ($findings | Where-Object { $_.severity -eq "high" } | Measure-Object).Count
    medium_count = ($findings | Where-Object { $_.severity -eq "medium" } | Measure-Object).Count
    findings = $findings
}

$reportPath = Join-Path $RunRoot "reports/log-check.json"
Write-JsonFile -Path $reportPath -Object $report

if (-not $report.ok) {
    Write-Host "Log check failed. Report: $reportPath"
    exit 2
}

Write-Host "Log check ok. Report: $reportPath"

