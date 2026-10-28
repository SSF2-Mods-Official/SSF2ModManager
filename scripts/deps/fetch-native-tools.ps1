# Downloads portable ffmpeg (and optional Temurin JRE) into src/.
# Next2Flash's release zip already carries Python + JRE; this JRE is a ship fallback
# under runtime\jre when the manager zip is used without N2F yet.
# Large binaries are gitignored; run before publish (release\publish.ps1 calls it).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src = Join-Path $root "src"
$ffmpegDir = Join-Path $src "ffmpeg"
$jreDir = Join-Path $src "jre"
$cache = Join-Path $root ".tools-cache"
New-Item -ItemType Directory -Force -Path $cache | Out-Null

function Test-FfmpegBundle {
    Test-Path (Join-Path $ffmpegDir "ffmpeg.exe")
}

function Test-JreBundle {
    Test-Path (Join-Path $jreDir "bin\java.exe")
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

if (-not (Test-JreBundle)) {
    Write-Host "Fetching Eclipse Temurin JRE 17 (Windows x64)…"
    $jreZip = Join-Path $cache "temurin-jre-17.zip"
    $jreUrl = "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jre/hotspot/normal/eclipse?project=jdk"
    Invoke-WebRequest -Uri $jreUrl -OutFile $jreZip -UseBasicParsing
    $extract = Join-Path $cache "jre-extract"
    Expand-ZipSafe $jreZip $extract
    $java = Get-ChildItem $extract -Recurse -Filter "java.exe" |
        Where-Object { $_.Directory.Name -eq "bin" } |
        Select-Object -First 1
    if (-not $java) { throw "java.exe not found in Temurin archive." }
    $jreHome = $java.Directory.Parent.FullName
    if (Test-Path $jreDir) { Remove-Item $jreDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $jreDir | Out-Null
    Copy-Item (Join-Path $jreHome "*") $jreDir -Recurse -Force
    Write-Host "  -> $jreDir\bin\java.exe"
} else {
    Write-Host "JRE bundle already present."
}

Write-Host "ffmpeg + JRE ready. Python comes from the Next2Flash release zip (or PortableToolchain fetch)."
