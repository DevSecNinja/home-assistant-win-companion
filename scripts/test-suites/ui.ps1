Register-TestSuite -Name 'ui' -Run {
    $appProject = Join-Path $repoRoot `
        'src\WindowsCompanion.App\WindowsCompanion.App.csproj'
    $project = Join-Path $repoRoot `
        'tests\WindowsCompanion.UI.Tests\WindowsCompanion.UI.Tests.csproj'

    & $dotnet build `
        $appProject `
        -c Debug `
        "-p:Platform=$Platform" `
        -r $runtimeIdentifier `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The Debug Windows app build failed.' }

    foreach ($run in 1..$RepeatCount) {
        Invoke-TestProject `
            -Project $project `
            -Name "ui-$run" `
            -Configuration 'Debug' `
            -AdditionalArguments @("-p:Platform=$Platform", '-r', $runtimeIdentifier)
    }
}
