#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs Core, App-boundary, and optional end-to-end or interactive UI suites.
#>
[CmdletBinding()]
param(
    [switch]$Coverage,

    [switch]$EndToEnd,

    [switch]$Ui,

    [string]$Filter,

    [string]$ResultsDirectory,

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform,

    [ValidateRange(0, 100)]
    [double]$MinimumLineCoverage = 85,

    [ValidateRange(0, 100)]
    [double]$MinimumBranchCoverage = 70
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$coreProject = Join-Path $repoRoot 'tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj'
$e2eProject = Join-Path $repoRoot 'tests\WindowsCompanion.E2E.Tests\WindowsCompanion.E2E.Tests.csproj'
$uiProject = Join-Path $repoRoot 'tests\WindowsCompanion.UI.Tests\WindowsCompanion.UI.Tests.csproj'
$appProject = Join-Path $repoRoot 'src\WindowsCompanion.App\WindowsCompanion.App.csproj'
$appBoundaryProject = Join-Path $repoRoot 'tests\WindowsCompanion.App.Tests\WindowsCompanion.App.Tests.csproj'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
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

        [string[]]$AdditionalArguments = @()
    )

    $arguments = @('test', $Project, '--nologo')
    if ($Configuration) { $arguments += @('-c', $Configuration) }
    if ($Filter) { $arguments += @('--filter', $Filter) }
    if ($ResultsDirectory) {
        $arguments += @(
            '--results-directory'
            $ResultsDirectory
            '--logger'
            "trx;LogFileName=$Name.trx"
        )
    }
    $arguments += $AdditionalArguments

    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name tests failed." }
}

if ($Coverage) {
    $coverageDirectory = Join-Path $repoRoot 'Coverage'
    if (Test-Path $coverageDirectory) {
        Remove-Item $coverageDirectory -Recurse -Force
    }

    $coverageArguments = @(
        '--collect'
        'XPlat Code Coverage'
        '--results-directory'
        $coverageDirectory
    )
    if ($Filter) { $coverageArguments += @('--filter', $Filter) }

    & $dotnet test $coreProject --nologo @coverageArguments
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }

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
}
else {
    Invoke-TestProject -Project $coreProject -Name 'core'
}

Invoke-TestProject -Project $appBoundaryProject -Name 'app-boundary'

if ($EndToEnd) {
    Invoke-TestProject `
        -Project $e2eProject `
        -Name 'end-to-end' `
        -Configuration 'Release' `
        -AdditionalArguments @("-p:Platform=$Platform", '-r', $runtimeIdentifier)
}

if ($Ui) {
    & $dotnet build `
        $appProject `
        -c Debug `
        "-p:Platform=$Platform" `
        -r $runtimeIdentifier `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The Debug Windows app build failed.' }

    Invoke-TestProject `
        -Project $uiProject `
        -Name 'ui' `
        -Configuration 'Debug' `
        -AdditionalArguments @("-p:Platform=$Platform", '-r', $runtimeIdentifier)
}
