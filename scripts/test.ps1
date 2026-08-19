#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs registered test suites through one repository entry point.
#>
[CmdletBinding()]
param(
    [switch]$Coverage,

    [switch]$EndToEnd,

    [switch]$Ui,

    [string[]]$Suite,

    [string]$Filter,

    [string]$ResultsDirectory,

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform,

    [ValidateRange(1, 10)]
    [int]$RepeatCount = 1,

    [ValidateSet('quiet', 'minimal', 'normal', 'detailed')]
    [string]$Verbosity = 'normal',

    [ValidateRange(0, 100)]
    [double]$MinimumLineCoverage = 85,

    [ValidateRange(0, 100)]
    [double]$MinimumBranchCoverage = 70
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) { $dotnetCommand.Source } else { $null }
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Could not find dotnet. Install the .NET 10 SDK.' }

if (-not $Platform) {
    $Platform = if (
        [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq
        [Runtime.InteropServices.Architecture]::Arm64) {
        'ARM64'
    }
    else {
        'x64'
    }
}
$runtimeIdentifier = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }

if ($ResultsDirectory) {
    $ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory, $repoRoot)
    New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
}

function Invoke-TestProject {
    param(
        [Parameter(Mandatory)]
        [string]$Project,

        [Parameter(Mandatory)]
        [string]$Name,

        [string]$Configuration,

        [string[]]$AdditionalArguments = @(),

        [string[]]$TestArguments = @()
    )

    $testOutput = if ($Verbosity -eq 'detailed') { 'Detailed' } else { 'Normal' }
    $arguments = @(
        'test'
        '--project'
        $Project
        '-v'
        $Verbosity
        '--output'
        $testOutput
    )
    if ($Configuration) { $arguments += @('-c', $Configuration) }
    if ($ResultsDirectory) {
        $arguments += @(
            '--results-directory'
            $ResultsDirectory
        )
        $TestArguments += @('--report-trx', '--report-trx-filename', "$Name.trx")
    }
    $arguments += $AdditionalArguments
    if ($Filter) { $TestArguments += @('--filter-query', $Filter) }
    if ($TestArguments) { $arguments += @('--') + $TestArguments }

    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name tests failed." }
}

$testSuites = [ordered]@{}
function Register-TestSuite {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [string[]]$Aliases = @(),

        [switch]$Default,

        [Parameter(Mandatory)]
        [scriptblock]$Run
    )

    if ($testSuites.Contains($Name)) {
        throw "A test suite named '$Name' is already registered."
    }

    $testSuites[$Name] = [pscustomobject]@{
        Name = $Name
        Aliases = $Aliases
        Default = $Default.IsPresent
        Run = $Run
    }
}

$suiteDirectory = Join-Path $PSScriptRoot 'test-suites'
$suiteFiles = Get-ChildItem $suiteDirectory -Filter '*.ps1' | Sort-Object Name
if (-not $suiteFiles) { throw "No test suites were found in $suiteDirectory." }
foreach ($suiteFile in $suiteFiles) {
    . $suiteFile.FullName
}

$requestedSuites = if ($Suite) {
    @($Suite)
}
elseif ($Filter -and ($EndToEnd -or $Ui)) {
    @()
}
else {
    @($testSuites.Values | Where-Object Default | ForEach-Object Name)
}
if ($EndToEnd) { $requestedSuites += 'end-to-end' }
if ($Ui) { $requestedSuites += 'ui' }

$selectedSuites = foreach ($requested in $requestedSuites) {
    $match = $testSuites.Values | Where-Object {
        $_.Name -eq $requested -or $_.Aliases -contains $requested
    } | Select-Object -First 1
    if (-not $match) {
        $available = $testSuites.Keys -join ', '
        throw "Unknown test suite '$requested'. Available suites: $available."
    }
    $match
}

foreach ($testSuite in $selectedSuites | Sort-Object Name -Unique) {
    Write-Host "Running $($testSuite.Name) test suite..."
    & $testSuite.Run
}
