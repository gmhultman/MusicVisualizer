<#
.SYNOPSIS
    Publishes a new version: tags the current main commit and pushes the tag, which makes
    GitHub Actions build the installer and create the release (.github/workflows/release.yml).

.DESCRIPTION
    Checks first that you're on main, everything is committed and pushed, the version is newer
    than the last release, and the tests pass. Asks before pushing anything.
    Installed copies pick the new version up automatically.

.EXAMPLE
    ./release.ps1 1.0.1 -Notes 'Adds the Aquarium visualizer'
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Git {
    $output = git @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $args failed:`n$output" }
    $output
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }
$tag = "v$Version"

# 1. The repo is in a releasable state.
$branch = Invoke-Git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') { throw "Switch to main first (you're on '$branch')." }
if (Invoke-Git status --porcelain) { throw 'Commit or stash your changes first.' }

Write-Host 'Checking GitHub...'
Invoke-Git fetch --tags origin | Out-Null
if ((Invoke-Git rev-parse HEAD) -ne (Invoke-Git rev-parse origin/main)) {
    throw 'main is not the same as origin/main. Push (or pull) first.'
}
if (Invoke-Git tag -l $tag) { throw "Tag $tag already exists." }

$latest = Invoke-Git tag -l 'v*.*.*' |
    Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } |
    ForEach-Object { [version]$_.Substring(1) } |
    Sort-Object -Descending | Select-Object -First 1
if ($latest -and [version]$Version -le $latest) { throw "Version must be newer than the latest release (v$latest)." }

# 2. Tests pass.
Write-Host 'Running tests...'
dotnet test -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was released.' }

# 3. Confirm, then tag and push.
if (-not $Notes) { $Notes = Read-Host 'Release notes (one line, optional)' }
if (-not $Notes) { $Notes = "Music Visualizer $Version" }

Write-Host ''
Write-Host "Ready to release $tag" -ForegroundColor Cyan
Write-Host "  commit: $(Invoke-Git log -1 --format='%h %s')"
Write-Host "  notes:  $Notes"
if ((Read-Host 'Push the tag and publish the release? (y/n)') -ne 'y') { Write-Host 'Cancelled.'; return }

Invoke-Git tag -a $tag -m $Notes | Out-Null
try { Invoke-Git push origin $tag | Out-Null }
catch { Invoke-Git tag -d $tag | Out-Null; throw }

$repo = (Invoke-Git remote get-url origin) -replace '\.git$', '' -replace '^git@github\.com:', 'https://github.com/'
Write-Host ''
Write-Host "Pushed $tag. GitHub is building the release now (about 5 minutes):" -ForegroundColor Green
Write-Host "  progress: $repo/actions"
Write-Host "  release:  $repo/releases/tag/$tag"
