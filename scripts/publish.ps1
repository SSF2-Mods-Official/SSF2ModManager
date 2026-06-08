# Builds a framework-dependent Release publish and zips it for distribution.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"

Push-Location $root
try {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    dotnet publish SSF2ModManager.csproj -c Release -r win-x64 --self-contained false -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    # Release zip should not ship debug symbols
    Get-ChildItem $outDir -Filter "*.pdb" -Recurse | Remove-Item -Force

    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $zip = Join-Path $distDir "SSF2ModManager-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zip -Force

    $count = (Get-ChildItem $outDir -Recurse -File).Count
    Write-Host "Published: $zip ($count files)"
    Write-Host ""
    Write-Host "Release contents:"
    Get-ChildItem $outDir -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($outDir.Length + 1)
        Write-Host "  $rel"
    }
}
finally {
    Pop-Location
}
