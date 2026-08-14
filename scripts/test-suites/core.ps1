Register-TestSuite -Name 'core' -Default -Run {
    $project = Join-Path $repoRoot `
        'tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj'
    if (-not $Coverage) {
        Invoke-TestProject -Project $project -Name 'core'
        return
    }

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

    & $dotnet test $project --nologo @coverageArguments
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }

    $report = Get-ChildItem $coverageDirectory -Recurse -Filter 'coverage.cobertura.xml' |
        Select-Object -First 1
    if (-not $report) {
        throw 'Coverage collection completed without a Cobertura report.'
    }

    [xml]$document = Get-Content $report.FullName
    $lineCoverage = [double]$document.coverage.'line-rate' * 100
    $branchCoverage = [double]$document.coverage.'branch-rate' * 100

    Write-Host ('Core coverage: {0:N2}% line, {1:N2}% branch' -f
        $lineCoverage, $branchCoverage)

    if ($lineCoverage -lt $MinimumLineCoverage) {
        throw ('Line coverage {0:N2}% is below the {1:N2}% threshold.' -f
            $lineCoverage, $MinimumLineCoverage)
    }
    if ($branchCoverage -lt $MinimumBranchCoverage) {
        throw ('Branch coverage {0:N2}% is below the {1:N2}% threshold.' -f
            $branchCoverage, $MinimumBranchCoverage)
    }
}
