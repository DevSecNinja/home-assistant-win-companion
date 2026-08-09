#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds per-user x64 and ARM64 setup packages from published app files.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$ReleaseDirectory,

    [Parameter(Mandatory)]
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not [System.IO.Path]::IsPathRooted($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $repoRoot $ReleaseDirectory
}
if (-not [System.IO.Path]::IsPathRooted($InnoCompiler)) {
    $InnoCompiler = Join-Path $repoRoot $InnoCompiler
}
if (-not (Test-Path $InnoCompiler)) {
    throw "Inno Setup compiler not found: $InnoCompiler"
}

$script = Join-Path $repoRoot 'installer\WindowsCompanion.iss'
$targets = @(
    @{ Architecture = 'x64'; Runtime = 'win-x64' }
    @{ Architecture = 'arm64'; Runtime = 'win-arm64' }
)

foreach ($target in $targets) {
    $source = Join-Path $ReleaseDirectory "publish\$($target.Runtime)"
    if (-not (Test-Path (Join-Path $source 'WindowsCompanion.exe'))) {
        throw "Published application not found for $($target.Runtime): $source"
    }

    $packageName = "WindowsCompanion-$Version-win-$($target.Architecture)-setup"
    Get-ChildItem $ReleaseDirectory -File -Filter "$packageName*" -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Host "Building $($target.Architecture) installer..." -ForegroundColor Cyan
    & $InnoCompiler `
        "/DMyAppVersion=$Version" `
        "/DArchitecture=$($target.Architecture)" `
        "/DSourceDir=$source" `
        "/DOutputDir=$ReleaseDirectory" `
        /Qp `
        $script
    if ($LASTEXITCODE -ne 0) {
        throw "Installer compilation failed for $($target.Architecture)."
    }

    $name = "WindowsCompanion-$Version-win-$($target.Architecture)-setup.exe"
    $path = Join-Path $ReleaseDirectory $name
    if (-not (Test-Path $path)) {
        throw "Installer compiler did not create $name."
    }

    $packageDirectory = Join-Path $ReleaseDirectory "installer\$packageName"
    $archiveName = "$packageName.zip"
    $archivePath = Join-Path $ReleaseDirectory $archiveName
    [void](New-Item $packageDirectory -ItemType Directory -Force)

    $parts = @(
        Get-Item $path
        Get-ChildItem $ReleaseDirectory -File -Filter "$packageName-*.bin"
    )
    if ($parts.Count -lt 3) {
        throw "Loader-free installer for $($target.Architecture) is incomplete."
    }
    foreach ($part in $parts) {
        Copy-Item $part.FullName $packageDirectory
        Remove-Item $part.FullName -Force
    }

    Compress-Archive -Path $packageDirectory -DestinationPath $archivePath
    $hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content "$archivePath.sha256" "$hash  $archiveName" -Encoding utf8NoBOM
}

Remove-Item (Join-Path $ReleaseDirectory 'installer') -Recurse -Force

Get-ChildItem $ReleaseDirectory -File -Filter '*-setup.zip*' |
    Select-Object Name, Length
