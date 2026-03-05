[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function New-RunFolder {
    param(
        [Parameter(Mandatory)]
        [string]$OutputRoot,
        [Parameter(Mandatory)]
        [string]$Prefix
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $runRoot = Join-Path $OutputRoot "$Prefix-$timestamp"
    $null = New-Item -ItemType Directory -Path $runRoot -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $runRoot "logs") -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $runRoot "reports") -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $runRoot "screenshots") -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $runRoot "screenshots/baseline") -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $runRoot "screenshots/current") -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $runRoot "screenshots/diff") -Force
    return $runRoot
}

function Write-Section {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host ""
    Write-Host "==== $Text ===="
}

function Invoke-StepLogged {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogRoot
    )

    Write-Section $Name
    Write-Host "cwd: $WorkingDirectory"
    Write-Host "cmd: $Command"

    $baseName = "{0}" -f ($Name -replace "[^a-zA-Z0-9_-]", "_")
    $stdoutPath = Join-Path $LogRoot "$baseName.stdout.log"
    $stderrPath = Join-Path $LogRoot "$baseName.stderr.log"
    $combinedPath = Join-Path $LogRoot "$baseName.log"

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
    @($stdout + $stderr) | Set-Content -Path $combinedPath -Encoding UTF8

    if ($proc.ExitCode -ne 0) {
        throw "Step failed: $Name (exit code: $($proc.ExitCode))"
    }

    return $combinedPath
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Object
    )

    $json = $Object | ConvertTo-Json -Depth 20
    $json | Set-Content -Path $Path -Encoding UTF8
}

function Assert-CommandExists {
    param([Parameter(Mandatory)][string]$CommandName)
    $cmd = Get-Command $CommandName -ErrorAction SilentlyContinue
    if (-not $cmd) {
        throw "Missing required command: $CommandName"
    }
}

