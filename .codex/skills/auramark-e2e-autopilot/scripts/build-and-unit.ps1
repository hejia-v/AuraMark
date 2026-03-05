[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepoRoot,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [Parameter(Mandatory)]
    [string]$RunRoot,

    [switch]$SkipNpm,
    [switch]$SkipDotnet,
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$logRoot = Join-Path $RunRoot "logs"
$srcRoot = Join-Path $RepoRoot "src"
$webRoot = Join-Path $srcRoot "AuraMark.Web"

Assert-CommandExists -CommandName "dotnet"
if (-not $SkipNpm) {
    Assert-CommandExists -CommandName "npm.cmd"
}

$result = [ordered]@{
    ok = $true
    configuration = $Configuration
    steps = @()
}

try {
    if (-not $SkipNpm) {
        $result.steps += [ordered]@{ name = "npm_ci"; log = (Invoke-StepLogged -Name "npm_ci" -Command "npm.cmd ci" -WorkingDirectory $webRoot -LogRoot $logRoot) }
        $result.steps += [ordered]@{ name = "npm_build"; log = (Invoke-StepLogged -Name "npm_build" -Command "npm.cmd run build" -WorkingDirectory $webRoot -LogRoot $logRoot) }
    }

    if (-not $SkipDotnet) {
        $result.steps += [ordered]@{ name = "dotnet_build"; log = (Invoke-StepLogged -Name "dotnet_build_$Configuration" -Command "dotnet build AuraMark.sln -c $Configuration" -WorkingDirectory $srcRoot -LogRoot $logRoot) }
    }

    if (-not $SkipTests) {
        $result.steps += [ordered]@{ name = "dotnet_test"; log = (Invoke-StepLogged -Name "dotnet_test_$Configuration" -Command "dotnet test AuraMark.sln -c $Configuration" -WorkingDirectory $srcRoot -LogRoot $logRoot) }
    }
}
catch {
    $result.ok = $false
    $result.error = $_.Exception.Message
}

$reportPath = Join-Path $RunRoot "reports/build-and-unit.json"
Write-JsonFile -Path $reportPath -Object $result

if (-not $result.ok) {
    Write-Host "Build/Test failed. Report: $reportPath"
    exit 1
}

Write-Host "Build/Test ok. Report: $reportPath"

