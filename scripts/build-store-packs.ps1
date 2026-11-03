<#
.SYNOPSIS
  Zips the pack sources in scripts\store-packs into .heysippack files in src\HeySip.App\Assets\Store\packs, and prints the
  "sha256" and "size" lines each one needs in src\HeySip.App\Assets\Store\catalog.json.

.DESCRIPTION
  A .heysippack is a zip holding one pack.json. The catalog must list the zip's exact size and SHA-256, or the app throws the
  download away, so rebuilding a pack means pasting the new numbers into the catalog. Run this after editing a source file.
  The pack's own "version" must match the catalog's "version", and a changed pack needs a new version so installed copies update.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$sources = Join-Path $PSScriptRoot 'store-packs'
$target = Join-Path $PSScriptRoot '..\src\HeySip.App\Assets\Store\packs'
New-Item -ItemType Directory -Force -Path $target | Out-Null
$target = (Resolve-Path $target).Path

foreach ($file in Get-ChildItem $sources -Filter '*.json') {
    $json = Get-Content $file.FullName -Raw | ConvertFrom-Json
    $output = Join-Path $target "$($json.id)-$($json.version).heysippack"
    if (Test-Path $output) { Remove-Item $output -Force }

    $zip = [IO.Compression.ZipFile]::Open($output, 'Create')
    try {
        $entry = $zip.CreateEntry('pack.json', [IO.Compression.CompressionLevel]::Optimal)
        $stream = $entry.Open()
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes((Get-Content $file.FullName -Raw))
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Dispose()
    }
    finally { $zip.Dispose() }

    $hash = (Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item $output).Length
    "{0}`n  ""pack"": ""packs/{1}"",`n  ""sha256"": ""{2}"",`n  ""size"": {3}`n" -f $json.id, (Split-Path $output -Leaf), $hash, $size
}
