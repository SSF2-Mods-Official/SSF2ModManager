# Self-contained single-file release — no .NET runtime install required.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"

Push-Location $root
try {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Write-Host "Publishing self-contained single-file (win-x64)..."
    dotnet publish SSF2ModManager.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -o $outDir

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Get-ChildItem $outDir -Filter "*.pdb" -Recurse | Remove-Item -Force

    # Optional readme dropped next to the exe
    @"
SSF2 Mod Manager v1.0.0
========================

Run SSF2ModManager.exe — no .NET install required.

Languages and themes are bundled inside the executable and
extract automatically on first launch.

User data: %AppData%\SSF2ModManager\
"@ | Set-Content (Join-Path $outDir "README.txt") -Encoding UTF8

    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $zip = Join-Path $distDir "SSF2ModManager-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zip -Force

    $files = Get-ChildItem $outDir -Recurse -File
    $totalMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host ""
    Write-Host "Published: $zip"
    Write-Host "Files in publish folder: $($files.Count) ($totalMb MB)"
    $files | ForEach-Object {
        $rel = $_.FullName.Substring($outDir.Length + 1)
        $mb = [math]::Round($_.Length / 1MB, 2)
        Write-Host ("  {0,-40} {1,8} MB" -f $rel, $mb)
    }
}
finally {
    Pop-Location
}
