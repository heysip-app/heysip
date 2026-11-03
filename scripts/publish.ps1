#requires -Version 7
<#
.SYNOPSIS
    Publishes a self-contained build into artifacts/publish/<rid>.
.EXAMPLE
    ./scripts/publish.ps1 -Runtime win-x64
#>
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts/publish/$Runtime"

dotnet publish (Join-Path $root 'src/HeySip.App/HeySip.App.csproj') `
    -c $Configuration -r $Runtime --self-contained -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host "Published to $out"
