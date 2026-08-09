<#
.SYNOPSIS
    Regenerates every shipped brand asset from the vector masters in brand/src.

.DESCRIPTION
    The Windows application icon, the packaging PNGs under
    src/HaCompanion.App/Assets, the distributable artwork in brand/dist and the
    GitHub social preview are all generated. None of those files should ever be
    edited by hand.

    Node.js is required. It is pinned in .mise.toml, so `mise install` provides
    a matching version.

.PARAMETER Check
    Verify that the committed assets still match the masters instead of
    rewriting them. Exits non-zero when anything is stale. Used by CI.

.EXAMPLE
    .\scripts\build-brand-assets.ps1

.EXAMPLE
    .\scripts\build-brand-assets.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$brandDir = Join-Path $PSScriptRoot '..\brand' | Resolve-Path

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js was not found on PATH. Run 'mise install' first, or install the version pinned in .mise.toml."
}

Push-Location $brandDir
try {
    if (-not (Test-Path 'node_modules')) {
        Write-Host 'Installing brand tooling dependencies...' -ForegroundColor Cyan
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    }

    $arguments = @('build-assets.mjs')
    if ($Check) { $arguments += '--check' }

    node @arguments
    if ($LASTEXITCODE -ne 0) { throw "Brand asset generation failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
