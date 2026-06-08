# Builds a framework-dependent Release publish and zips it for distribution.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"

Push-Location $root
try {
    dotnet publish SSF2ModManager.csproj -c Release -r win-x64 --self-contained false -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $zip = Join-Path $distDir "SSF2ModManager-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zip -Force
    Write-Host "Published: $zip"
}
finally {
    Pop-Location
}
