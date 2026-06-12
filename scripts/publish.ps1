# Self-contained single-file release — no .NET runtime install required.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"
$version = (Get-Content (Join-Path $root "version.txt") -Raw).Trim()

    Push-Location $root
try {
    & (Join-Path $root "scripts\generate-news-index.ps1")

    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Write-Host "Publishing self-contained single-file (win-x64)..."
    dotnet publish (Join-Path $root "src\SSF2ModManager.csproj") `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:SatelliteResourceLanguages=en `
        -o $outDir

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Get-ChildItem $outDir -Filter "*.pdb" -Recurse | Remove-Item -Force

    $readme = @"
SSF2 Mod Manager v$version
========================

Run SSF2ModManager.exe - no .NET install required.

Languages and themes are bundled inside the executable.

Your settings and mod data are stored in:
  %AppData%\SSF2ModManager\
"@
    $readmePath = Join-Path $outDir "README.txt"
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($readmePath, $readme, $utf8NoBom)

    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $zip = Join-Path $distDir "SSF2ModManager-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zip -Force

    $files = Get-ChildItem $outDir -Recurse -File
    $totalMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
    $zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Published: $zip"
    Write-Host "Zip contents: $($files.Count) files ($totalMb MB uncompressed, $zipMb MB zip)"

    Write-Host ""
    Write-Host "Cleaning up build artifacts..."
    Remove-Item $outDir -Recurse -Force
    Write-Host "  removed publish"

    foreach ($rel in @("src\bin", "src\obj")) {
        $full = Join-Path $root $rel
        if (Test-Path $full) {
            Remove-Item $full -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  removed $rel"
        }
    }

    dotnet clean (Join-Path $root "SSF2ModManager.sln") -c Release -v q 2>$null | Out-Null
    Write-Host ""
    Write-Host "Done. Release ready at: $zip"
}
finally {
    Pop-Location
}
