#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds tested, self-contained Windows release ZIPs and SHA-256 sidecars.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\HaCompanion.App\HaCompanion.App.csproj'
$tests = Join-Path $repoRoot 'tests\HaCompanion.Core.Tests\HaCompanion.Core.Tests.csproj'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\release\$Version"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Could not find dotnet. Install the .NET 9 SDK.' }

if (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}
[void](New-Item $OutputDirectory -ItemType Directory -Force)

Write-Host 'Running Core tests...' -ForegroundColor Cyan
& $dotnet test $tests -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$targets = @(
    @{ Platform = 'x64'; Runtime = 'win-x64' }
    @{ Platform = 'ARM64'; Runtime = 'win-arm64' }
)

foreach ($target in $targets) {
    $runtime = $target.Runtime
    $publishDirectory = Join-Path $OutputDirectory "publish\$runtime"
    $portableName = "WindowsCompanion-$Version-$runtime"
    $portableDirectory = Join-Path $OutputDirectory "portable\$portableName"
    $archiveName = "$portableName.zip"
    $archivePath = Join-Path $OutputDirectory $archiveName

    Write-Host "Publishing $runtime..." -ForegroundColor Cyan
    & $dotnet publish $project -c Release `
        -p:Platform=$($target.Platform) `
        -r $runtime `
        --self-contained true `
        -p:Version=$Version `
        -p:PublishDir=$publishDirectory `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime." }

    $executable = Join-Path $publishDirectory 'WindowsCompanion.exe'
    if (-not (Test-Path $executable)) {
        throw "Publish completed without WindowsCompanion.exe for $runtime."
    }

    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
    if (-not $fileVersion.StartsWith($Version, [StringComparison]::Ordinal)) {
        throw "Published version '$fileVersion' does not match '$Version'."
    }

    Copy-Item (Join-Path $repoRoot 'LICENSE') $publishDirectory
    Copy-Item (Join-Path $repoRoot 'docs\installation.md') `
        (Join-Path $publishDirectory 'INSTALLATION.md')

    Get-ChildItem $publishDirectory -Recurse -Filter '*.pdb' | Remove-Item -Force
    [void](New-Item $portableDirectory -ItemType Directory -Force)
    Copy-Item (Join-Path $publishDirectory '*') $portableDirectory -Recurse
    Compress-Archive -Path $portableDirectory -DestinationPath $archivePath

    $hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content "$archivePath.sha256" "$hash  $archiveName" -Encoding utf8NoBOM
}

Remove-Item (Join-Path $OutputDirectory 'portable') -Recurse -Force

Write-Host "Release assets written to $OutputDirectory" -ForegroundColor Green
Get-ChildItem $OutputDirectory -File | Select-Object Name, Length
