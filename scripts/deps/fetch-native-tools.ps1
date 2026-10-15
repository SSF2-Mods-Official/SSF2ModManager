# Downloads portable ffmpeg into src/. Java comes from Next2Flash toolchain\jre.
# Large binaries are gitignored; run before publish (release\publish.ps1 calls it).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src = Join-Path $root "src"
$ffmpegDir = Join-Path $src "ffmpeg"
$cache = Join-Path $root ".tools-cache"
New-Item -ItemType Directory -Force -Path $cache | Out-Null

function Test-FfmpegBundle {
    Test-Path (Join-Path $ffmpegDir "ffmpeg.exe")
}

function Expand-ZipSafe([string]$zipPath, [string]$dest) {
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $dest -Force
}

if (-not (Test-FfmpegBundle)) {
    Write-Host "Fetching ffmpeg essentials (Windows x64)…"
    $ffmpegZip = Join-Path $cache "ffmpeg-essentials.zip"
    $url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    Invoke-WebRequest -Uri $url -OutFile $ffmpegZip -UseBasicParsing
    $extract = Join-Path $cache "ffmpeg-extract"
    Expand-ZipSafe $ffmpegZip $extract
    $bin = Get-ChildItem $extract -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
    if (-not $bin) { throw "ffmpeg.exe not found in downloaded archive." }
    if (Test-Path $ffmpegDir) { Remove-Item $ffmpegDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ffmpegDir | Out-Null
    Copy-Item (Join-Path $bin.DirectoryName "*") $ffmpegDir -Recurse -Force
    $pkgRoot = $bin.Directory.Parent
    if ($pkgRoot) {
        foreach ($name in @("LICENSE", "LICENSE.txt", "README.txt", "README.md")) {
            $lic = Join-Path $pkgRoot.FullName $name
            if (Test-Path $lic) { Copy-Item $lic $ffmpegDir -Force }
        }
    }
    Write-Host "  -> $ffmpegDir\ffmpeg.exe"
} else {
    Write-Host "ffmpeg bundle already present."
}

Write-Host "ffmpeg ready. Java and FFDec come from the Next2Flash toolchain (toolchain)."
