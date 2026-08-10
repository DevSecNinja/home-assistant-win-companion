#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Core and App-boundary tests and optionally enforces Core coverage thresholds.
#>
[CmdletBinding()]
param(
    [switch]$Coverage,

    [ValidateRange(0, 100)]
    [double]$MinimumLineCoverage = 85,

    [ValidateRange(0, 100)]
    [double]$MinimumBranchCoverage = 70
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$coreProject = Join-Path $repoRoot 'tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj'
$appProject = Join-Path $repoRoot 'tests\WindowsCompanion.App.Tests\WindowsCompanion.App.Tests.csproj'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Could not find dotnet. Install the .NET 10 SDK.' }

$arguments = @(
    'test'
    $coreProject
    '--nologo'
)

if (-not $Coverage) {
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & $dotnet test $appProject --nologo
    if ($LASTEXITCODE -ne 0) { throw 'App-boundary tests failed.' }
    return
}

$coverageDirectory = Join-Path $repoRoot 'Coverage'
if (Test-Path $coverageDirectory) {
    Remove-Item $coverageDirectory -Recurse -Force
}

$arguments += @(
    '--collect'
    'XPlat Code Coverage'
    '--results-directory'
    $coverageDirectory
)

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$report = Get-ChildItem $coverageDirectory -Recurse -Filter 'coverage.cobertura.xml' |
    Select-Object -First 1
if (-not $report) { throw 'Coverage collection completed without a Cobertura report.' }

[xml]$document = Get-Content $report.FullName
$lineCoverage = [double]$document.coverage.'line-rate' * 100
$branchCoverage = [double]$document.coverage.'branch-rate' * 100

Write-Host ('Core coverage: {0:N2}% line, {1:N2}% branch' -f $lineCoverage, $branchCoverage)

if ($lineCoverage -lt $MinimumLineCoverage) {
    throw ('Line coverage {0:N2}% is below the {1:N2}% threshold.' -f
        $lineCoverage, $MinimumLineCoverage)
}

if ($branchCoverage -lt $MinimumBranchCoverage) {
    throw ('Branch coverage {0:N2}% is below the {1:N2}% threshold.' -f
        $branchCoverage, $MinimumBranchCoverage)
}

& $dotnet test $appProject --nologo
if ($LASTEXITCODE -ne 0) { throw 'App-boundary tests failed.' }
