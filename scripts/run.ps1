#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the companion and runs the executable it just produced.

.DESCRIPTION
    The one supported way to run the app from source.

    Building and launching are deliberately tied together: they used to be separate
    steps against paths that could drift apart, which made it possible to build one
    binary and silently run an older one.

    `dotnet run` is not used because it resolves the .NET root to the app's own
    output folder for this project shape and fails with a misleading
    "You must install or update .NET" dialog, even though the runtime is present.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\HaCompanion.App\HaCompanion.App.csproj'
$sourceRoot = [System.IO.Path]::TrimEndingDirectorySeparator(
    [System.IO.Path]::GetFullPath($repoRoot)
) + [System.IO.Path]::DirectorySeparatorChar

function Get-RunningSourceInstances {
    @(Get-Process -Name 'HaCompanion.App' -ErrorAction SilentlyContinue | Where-Object {
        try {
            [System.IO.Path]::GetFullPath($_.Path).StartsWith(
                $sourceRoot,
                [System.StringComparison]::OrdinalIgnoreCase)
        } catch {
            $false
        }
    })
}

function Confirm-Choice([string]$Prompt, [bool]$DefaultYes) {
    $suffix = if ($DefaultYes) { '[Y/n]' } else { '[y/N]' }
    $answer = (Read-Host "$Prompt $suffix").Trim()
    if (-not $answer) { return $DefaultYes }
    return $answer -in @('y', 'yes')
}

function Request-GracefulExit([System.Diagnostics.Process[]]$Processes) {
    foreach ($process in $Processes) {
        $signalName = "Local\HaCompanion.App.Shutdown.$($process.Id)"
        try {
            $signal = [System.Threading.EventWaitHandle]::OpenExisting($signalName)
        } catch [System.Threading.WaitHandleCannotBeOpenedException] {
            return $false
        }

        try {
            [void]$signal.Set()
        } finally {
            $signal.Dispose()
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    foreach ($process in $Processes) {
        $remaining = [Math]::Max(0, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if (-not $process.WaitForExit($remaining)) { return $false }
    }
    return $true
}

$running = Get-RunningSourceInstances
if ($running.Count -gt 0) {
    $details = ($running | ForEach-Object {
        $path = try { $_.Path } catch { '<unknown path>' }
        "pid $($_.Id): $path"
    }) -join [Environment]::NewLine
    Write-Host "A source-built companion is already running:`n$details" -ForegroundColor Yellow

    if (-not (Confirm-Choice 'Close it before building?' $true)) {
        Write-Host 'Build cancelled; the running app may lock the output files.' -ForegroundColor Yellow
        return
    }

    Write-Host 'Requesting graceful shutdown...' -ForegroundColor DarkGray
    if (-not (Request-GracefulExit $running)) {
        if (-not (Confirm-Choice 'Graceful shutdown was unavailable or timed out. Force close it?' $false)) {
            Write-Host 'Build cancelled; the running app may lock the output files.' -ForegroundColor Yellow
            return
        }

        $running | Where-Object { -not $_.HasExited } | ForEach-Object {
            Stop-Process -Id $_.Id -Force
            $_.WaitForExit(5000)
        }
    }
}

# Build for one explicit platform. Left to itself, a solution build and a project
# build choose different platforms and therefore different output folders, which is
# how a stale binary gets launched. Pinning it here means "build" and "run" always
# refer to the same file.
$platform = 'x64'

# dotnet is not always on PATH.
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Could not find dotnet. Install the .NET 9 SDK.' }

Write-Host "Building ($Configuration|$platform)..." -ForegroundColor Cyan
& $dotnet build $project -c $Configuration -p:Platform=$platform --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$outputRoot = Join-Path $repoRoot "src\HaCompanion.App\bin\$platform\$Configuration"
$exe = Get-ChildItem $outputRoot -Recurse -Filter 'HaCompanion.App.exe' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $exe) { throw "Build succeeded but no executable was found under $outputRoot." }

if ($NoLaunch) {
    Write-Host $exe.FullName
    return
}

# Do not silently terminate an instance that appeared while the build was running.
if ((Get-RunningSourceInstances).Count -gt 0) {
    throw 'A companion instance started during the build. Exit it and run the script again.'
}

Write-Host "Launching $($exe.FullName)" -ForegroundColor Green
$process = Start-Process $exe.FullName -PassThru

# A failed launch does not exit: the .NET apphost stays alive showing an error
# dialog, whose window title is the executable name. Treating "a process exists" as
# success therefore reports a broken build as working, so check the window title.
Start-Sleep -Seconds 8
$process.Refresh()

if ($process.HasExited) {
    throw "The app exited immediately (exit code $($process.ExitCode))."
}

if ($process.MainWindowTitle -eq 'HaCompanion.App.exe') {
    Stop-Process -Id $process.Id -Force
    throw 'The app failed to start: the .NET apphost showed an error dialog. ' +
          'This usually means the runtime could not be resolved, not that it is missing.'
}

Write-Host "Running (pid $($process.Id)): $($process.MainWindowTitle)" -ForegroundColor Green
