Register-TestSuite -Name 'app-boundary' -Default -Run {
    $project = Join-Path $repoRoot `
        'tests\WindowsCompanion.App.Tests\WindowsCompanion.App.Tests.csproj'
    Invoke-TestProject -Project $project -Name 'app-boundary'
}
