<#
.SYNOPSIS
    Builds an optimized, standalone MusicVisualizer.exe and installs it to the install folder.

.DESCRIPTION
    Produces a single self-contained exe (includes .NET, no runtime install needed).
    The build runs as you; only the final copy asks for admin (UAC) when the destination
    is protected, as Program Files folders are. If the installed copy is running it is
    closed first and relaunched afterwards.

.EXAMPLE
    ./publish.ps1
    ./publish.ps1 -Destination 'D:\Somewhere\MusicVisualizer'
#>
param(
    [string]$Destination = 'E:\Apps\MusicVisualizer'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\MusicVisualizer.App\MusicVisualizer.App.csproj'
$staging = Join-Path $PSScriptRoot 'artifacts\publish'
$exeName = 'MusicVisualizer.exe'
$target = Join-Path $Destination $exeName

# 1. Build into a local staging folder (no admin needed).
Write-Host 'Building release...'
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:AssemblyName=MusicVisualizer `
    -p:DebugType=none `
    -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
$built = Join-Path $staging $exeName

# 2. Close the installed copy if it's running (the exe is locked while open).
$running = Get-Process -Name 'MusicVisualizer' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ($_.Path -ieq $target) }
if ($running) {
    Write-Host 'Closing running copy...'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

# 3. Copy into place, elevating only if the destination needs it.
Write-Host "Installing to $Destination ..."
try {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    # A just-closed exe can stay locked for a moment, so retry briefly before giving up.
    for ($attempt = 1; ; $attempt++) {
        try { Copy-Item $built $target -Force; break }
        catch [System.IO.IOException] {
            if ($attempt -ge 10) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}
catch [System.UnauthorizedAccessException] {
    Write-Host 'Destination needs admin rights - approve the UAC prompt...'
    $cmd = "New-Item -ItemType Directory -Force -Path '$Destination' | Out-Null; Copy-Item '$built' '$target' -Force"
    $shell = (Get-Process -Id $PID).Path
    $p = Start-Process $shell -Verb RunAs -Wait -PassThru -WindowStyle Hidden `
        -ArgumentList '-NoProfile', '-Command', $cmd
    if ($p.ExitCode -ne 0 -or -not (Test-Path $target)) { throw 'Elevated copy failed or was cancelled.' }
}

$size = [math]::Round((Get-Item $target).Length / 1MB, 1)
Write-Host "Done: $target ($size MB)"

if ($running) {
    Write-Host 'Relaunching...'
    Start-Process $target
}
