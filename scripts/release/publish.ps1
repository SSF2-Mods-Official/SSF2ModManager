# Self-contained single-file release — no .NET runtime install required.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"
$version = (Get-Content (Join-Path $root "version.txt") -Raw).Trim()

    Push-Location $root
try {
    & (Join-Path $root "scripts\content\generate-news-index.ps1")

    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Write-Host "Ensuring bundled ffmpeg…"
    & (Join-Path $root "scripts\deps\fetch-native-tools.ps1")

    Write-Host "Ensuring aflex_lite + asconfigc…"
    & (Join-Path $root "scripts\deps\fetch-aflex-lite.ps1")

    Write-Host "Packaging SSF2 Stream VSIX…"
    & (Join-Path $root "scripts\deps\package-ssf2-stream.ps1")

    Write-Host "Publishing FixJpexsExport (self-contained)…"
    $shellTools = Join-Path $root "SSF2 Mod Manager Release\runtime\shell-dev\tools"
    dotnet publish (Join-Path $root "src\tools\FixJpexsExport\FixJpexsExport.csproj") `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $shellTools
    if ($LASTEXITCODE -ne 0) { throw "FixJpexsExport publish failed" }
    Remove-Item (Join-Path $shellTools "win-x64") -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem $shellTools -Filter "FixJpexsExport.*" |
        Where-Object { $_.Extension -in ".dll", ".pdb", ".json" } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Write-Host "Publishing self-contained single-file (win-x64)..."
    dotnet publish (Join-Path $root "src\SSF2ModManager.csproj") `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=false `
        -p:SatelliteResourceLanguages=en `
        -p:DebugType=none `
        -o $outDir

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Get-ChildItem $outDir -Filter "*.pdb" -Recurse | Remove-Item -Force

    $vsix = Join-Path $root "SSF2 Mod Manager Release\runtime\extensions\ssf2-stream.vsix"
    if (-not (Test-Path $vsix)) { throw "ssf2-stream.vsix missing: $vsix" }
    New-Item -ItemType Directory -Force -Path (Join-Path $outDir "extensions") | Out-Null
    Copy-Item $vsix (Join-Path $outDir "extensions\ssf2-stream.vsix") -Force

    # Merge into the day-to-day release folder (does not wipe SSF2 Shell)
    Write-Host "Syncing single-file exe into SSF2 Mod Manager Release\…"
    & (Join-Path $root "scripts\release\sync-release-exe.ps1") -StageDir $outDir

    $readme = @"
SSF2 Mod Manager v$version
========================

Run SSF2ModManager.exe - no .NET install required (single-file).

Release folder layout:
  SSF2ModManager.exe
  README.txt
  extensions\   ssf2-stream.vsix
  ffmpeg\       music trim / 44.1 kHz convert / FLV to WAV
  Languages\ News\ Themes\

On first launch, setup downloads Next2Flash, asks for your SSF2 Beta 1.4.0.1
folder, and builds SSF2 Shell next to this exe. Run it again any time from
Settings > Run setup.

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
