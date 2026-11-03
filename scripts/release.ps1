<#
.SYNOPSIS
    Builds a self-contained release zip and its SHA256 checksum into artifacts/release.
.DESCRIPTION
    CI runs this on a version tag; run it locally to get exactly the same files.
    Works on Windows PowerShell 5.1 and PowerShell 7.
.EXAMPLE
    ./scripts/release.ps1 -Version 1.0.0
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Accept "v1.2.3" as well as "1.2.3"; reject anything that isn't a plain semantic version.
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "'$Version' is not a valid version. Use something like 1.2.3 or 1.2.3-beta.1."
}

$root = Split-Path -Parent $PSScriptRoot
$name = "HeySip-$Version-$Runtime"
$outRoot = Join-Path $root 'artifacts/release'
$stage = Join-Path $outRoot $name
$zip = Join-Path $outRoot "$name.zip"

if (Test-Path $outRoot) { Remove-Item $outRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

dotnet publish (Join-Path $root 'src/HeySip.App/HeySip.App.csproj') `
    -c $Configuration -r $Runtime --self-contained `
    -p:Version=$Version -p:ContinuousIntegrationBuild=true `
    -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Copy-Item (Join-Path $root 'LICENSE') $stage
Copy-Item (Join-Path $root 'README.md') $stage

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

# "<hash>  <file>" is the format `sha256sum -c` and most checksum tools understand.
$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$sums = Join-Path $outRoot 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText($sums, "$hash  $name.zip`n", (New-Object System.Text.UTF8Encoding($false)))

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Built $zip ($sizeMb MB)"
Write-Host "SHA256 $hash"
