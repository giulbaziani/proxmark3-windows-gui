<#
.SYNOPSIS
    Mirror the upstream dependencies into your own GitHub account so the app never depends on
    an upstream that might be deleted, renamed, or made private.

.DESCRIPTION
    Creates/updates a bare mirror of RfidResearchGroup/proxmark3 and pushes every branch and tag
    into https://github.com/<Owner>/proxmark3-mirror. The repository on your account must already
    exist and be empty.

.PARAMETER Owner
    Your GitHub username (or org) that owns the mirror repositories.

.PARAMETER MirrorName
    Name of the mirror repository. Default: proxmark3-mirror.

.EXAMPLE
    ./sync-mirrors.ps1 -Owner my-github-name
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Owner,
    [string] $MirrorName = "proxmark3-mirror",
    [string] $UpstreamUrl = "https://github.com/RfidResearchGroup/proxmark3.git"
)

$ErrorActionPreference = "Stop"

function Require-Cmd([string]$name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "$name is not installed or not on PATH."
    }
}

Require-Cmd git

$targetUrl = "https://github.com/$Owner/$MirrorName.git"
$workDir   = Join-Path $env:TEMP "pm3-mirror-sync"
$bareDir   = Join-Path $workDir "proxmark3.git"

Write-Host "Mirroring $UpstreamUrl  ->  $targetUrl" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $workDir | Out-Null

if (Test-Path $bareDir) {
    Write-Host "Updating existing bare clone..." -ForegroundColor DarkGray
    git -C $bareDir remote update --prune
} else {
    Write-Host "Creating bare mirror clone (this downloads the full history)..." -ForegroundColor DarkGray
    git clone --mirror $UpstreamUrl $bareDir
}

# Point the bare mirror at your repository and push everything.
git -C $bareDir remote remove mirror 2>$null
git -C $bareDir remote add mirror $targetUrl

Write-Host "Pushing all refs and tags to your mirror..." -ForegroundColor DarkGray
git -C $bareDir push --mirror mirror

Write-Host ""
Write-Host "Done. Your mirror is up to date:" -ForegroundColor Green
Write-Host "  $targetUrl"
Write-Host ""
Write-Host "Next: set Defaults.MirrorOwner in AppSettings.cs to '$Owner', or put"
Write-Host "  $targetUrl"
Write-Host "into the app's Settings -> Source repository field."
