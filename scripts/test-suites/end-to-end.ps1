Register-TestSuite -Name 'end-to-end' -Aliases @('e2e') -Run {
    $project = Join-Path $repoRoot `
        'tests\WindowsCompanion.E2E.Tests\WindowsCompanion.E2E.Tests.csproj'
    foreach ($run in 1..$RepeatCount) {
        Invoke-TestProject `
            -Project $project `
            -Name "end-to-end-$run" `
            -Configuration 'Release' `
            -AdditionalArguments @(
                "-p:Platform=$Platform"
                '-p:PublishReadyToRun=false'
                '-r'
                $runtimeIdentifier
            ) `
            -TestArguments @(
                '--hangdump'
                '--hangdump-timeout'
                '2m'
                '--hangdump-type'
                'None'
            )
    }
}
