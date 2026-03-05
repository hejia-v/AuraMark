[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RunRoot,

    [string]$BaselineRoot = $null,

    [int]$SampleStep = 4,

    [double]$CriticalDiffRatio = 0.003,
    [double]$NonCriticalDiffRatio = 0.01
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($BaselineRoot)) {
    $baselineRoot = Join-Path $RunRoot "screenshots/baseline"
} else {
    $baselineRoot = $BaselineRoot
}
$currentRoot = Join-Path $RunRoot "screenshots/current"
$diffRoot = Join-Path $RunRoot "screenshots/diff"

if (-not (Test-Path -LiteralPath $currentRoot)) {
    throw "Missing current screenshots folder: $currentRoot"
}

function Get-DiffRatio {
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$A,
        [Parameter(Mandatory)][System.Drawing.Bitmap]$B,
        [Parameter(Mandatory)][int]$Step
    )

    if ($A.Width -ne $B.Width -or $A.Height -ne $B.Height) {
        return 1.0
    }

    $diff = 0L
    $total = 0L
    for ($y = 0; $y -lt $A.Height; $y += $Step) {
        for ($x = 0; $x -lt $A.Width; $x += $Step) {
            $ca = $A.GetPixel($x, $y)
            $cb = $B.GetPixel($x, $y)
            $total++
            if ($ca.ToArgb() -ne $cb.ToArgb()) { $diff++ }
        }
    }

    if ($total -le 0) { return 0.0 }
    return [double]$diff / [double]$total
}

function Save-DiffImage {
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$A,
        [Parameter(Mandatory)][System.Drawing.Bitmap]$B,
        [Parameter(Mandatory)][string]$OutPath,
        [Parameter(Mandatory)][int]$Step
    )

    if ($A.Width -ne $B.Width -or $A.Height -ne $B.Height) {
        return
    }

    $out = New-Object System.Drawing.Bitmap($A.Width, $A.Height)
    for ($y = 0; $y -lt $A.Height; $y += 1) {
        for ($x = 0; $x -lt $A.Width; $x += 1) {
            $ca = $A.GetPixel($x, $y)
            $cb = $B.GetPixel($x, $y)
            if ($ca.ToArgb() -eq $cb.ToArgb()) {
                $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(40, 40, 40))
            } else {
                $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, 0, 80))
            }
        }
    }

    $dir = Split-Path -Parent $OutPath
    $null = New-Item -ItemType Directory -Path $dir -Force
    $out.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
}

$current = Get-ChildItem -Path $currentRoot -Filter "*.png" -File -ErrorAction SilentlyContinue
$results = @()

foreach ($img in $current) {
    $base = Join-Path $baselineRoot $img.Name
    $entry = [ordered]@{
        name = $img.Name
        has_baseline = (Test-Path -LiteralPath $base)
        diff_ratio = $null
        threshold = $null
        ok = $true
        diff_image = $null
    }

    if (-not $entry.has_baseline) {
        $results += $entry
        continue
    }

    $a = [System.Drawing.Bitmap]::new($base)
    $b = [System.Drawing.Bitmap]::new($img.FullName)
    try {
        $ratio = Get-DiffRatio -A $a -B $b -Step ([Math]::Max(1, $SampleStep))
        $entry.diff_ratio = $ratio

        $isCritical = $img.Name -match "^case(1|2|3|4|5)__"
        $threshold = if ($isCritical) { $CriticalDiffRatio } else { $NonCriticalDiffRatio }
        $entry.threshold = $threshold
        if ($ratio -gt $threshold) {
            $entry.ok = $false
            $diffPath = Join-Path $diffRoot $img.Name
            Save-DiffImage -A $a -B $b -OutPath $diffPath -Step ([Math]::Max(1, $SampleStep))
            $entry.diff_image = $diffPath
        }
    }
    finally {
        $a.Dispose()
        $b.Dispose()
    }

    $results += $entry
}

$report = [ordered]@{
    ok = ($results | Where-Object { -not $_.ok } | Measure-Object).Count -eq 0
    total = $results.Count
    failed = ($results | Where-Object { -not $_.ok } | Measure-Object).Count
    results = $results
}

$reportPath = Join-Path $RunRoot "reports/screenshot-diff.json"
Write-JsonFile -Path $reportPath -Object $report

if (-not $report.ok) {
    Write-Host "Screenshot diff failed. Report: $reportPath"
    exit 3
}

Write-Host "Screenshot diff ok. Report: $reportPath"
