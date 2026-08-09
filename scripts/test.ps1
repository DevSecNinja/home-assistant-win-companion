#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Core tests and optionally enforces coverage thresholds.
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
$project = Join-Path $repoRoot 'tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Could not find dotnet. Install the .NET 9 SDK.' }

$arguments = @(
    'test'
    $project
    '--nologo'
)

if (-not $Coverage) {
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
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
