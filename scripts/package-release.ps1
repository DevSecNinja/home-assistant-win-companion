#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes and packages an unsigned x64 release candidate.
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
$outputRoot = if ($OutputDirectory) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    Join-Path $repoRoot "artifacts\release\$Version"
}
$publishDirectory = Join-Path $outputRoot 'publish'
$archiveName = "HaCompanion-$Version-win-x64.zip"
$archivePath = Join-Path $outputRoot $archiveName
$checksumPath = "$archivePath.sha256"

if (Test-Path $outputRoot) {
    Remove-Item $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Could not find dotnet. Install the .NET 9 SDK.' }

& $dotnet publish $project `
    -c Release `
    -p:Platform=x64 `
    -r win-x64 `
    --self-contained false `
    -p:PublishTrimmed=false `
    -p:Version=$Version `
    -o $publishDirectory `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }

Copy-Item (Join-Path $repoRoot 'LICENSE') $publishDirectory
Copy-Item (Join-Path $repoRoot 'README.md') $publishDirectory
$installationGuide = Join-Path $repoRoot 'docs\installation.md'
if (Test-Path $installationGuide) {
    Copy-Item $installationGuide $publishDirectory
}

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath
$hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $archiveName" | Set-Content $checksumPath -NoNewline -Encoding ascii

Write-Host "Archive: $archivePath"
Write-Host "Checksum: $checksumPath"
