param(
    [string]$StageDir = ""
)

# Publish single-file SSF2ModManager.exe into the release folder WITHOUT wiping SSF2 Shell.
# Layout: exe + README + SSF2 Shell + runtime\ (all tool/content sidecars).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$release = Join-Path $root "SSF2 Mod Manager Release"
$runtime = Join-Path $release "runtime"
$stage = if ($StageDir) { $StageDir } else { Join-Path $root ".tools-cache\single-file-stage" }
$ownedStage = [string]::IsNullOrWhiteSpace($StageDir)

# Explorer / some shells omit user PATH — find the SDK explicitly.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnetExe = Join-Path ${env:ProgramFiles} "dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $dotnetExe) {
        $env:Path = "$(Split-Path -Parent $dotnetExe);$env:Path"
    } else {
        throw "dotnet SDK not found on PATH. Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
    }
}

function Copy-FileRetry([string]$Source, [string]$Destination, [int]$Attempts = 20) {
    $dir = Split-Path -Parent $Destination
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    for ($i = 1; $i -le $Attempts; $i++) {
        try {
            Copy-Item -LiteralPath $Source -Destination $Destination -Force
            return
        } catch {
            if ($i -eq $Attempts) { throw }
            Write-Host "  file locked, retry $i/$Attempts..."
            Start-Sleep -Seconds 1
        }
    }
}

New-Item -ItemType Directory -Force -Path $release | Out-Null

if ($ownedStage) {
    Write-Host "Ensuring native tools (ffmpeg/jre) in src for content copy..."
    & (Join-Path $root "scripts\deps\fetch-native-tools.ps1")

    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    Write-Host "Publishing self-contained single-file to staging..."
    dotnet publish (Join-Path $root "src\SSF2ModManager.csproj") `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=false `
        -p:SatelliteResourceLanguages=en `
        -o $stage

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

$exe = Join-Path $stage "SSF2ModManager.exe"
if (-not (Test-Path $exe)) { throw "Publish did not produce SSF2ModManager.exe at $stage" }

# Stop a leftover process that still owns the release exe.
Get-Process -Name "SSF2ModManager" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Copy-FileRetry $exe (Join-Path $release "SSF2ModManager.exe")
Write-Host "  updated SSF2ModManager.exe ($([math]::Round((Get-Item $exe).Length/1MB,1)) MB)"

New-Item -ItemType Directory -Force -Path $runtime | Out-Null

# Staged content may already be under stage\runtime\ (csproj Link) or legacy stage\<name>
function Get-StageContent([string]$name) {
    $underRuntime = Join-Path $stage "runtime\$name"
    if (Test-Path $underRuntime) { return $underRuntime }
    $legacy = Join-Path $stage $name
    if (Test-Path $legacy) { return $legacy }
    return $null
}

foreach ($name in @("Languages", "News", "Themes", "ffdec")) {
    $src = Get-StageContent $name
    if ($src) {
        $dest = Join-Path $runtime $name
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        Copy-Item $src $dest -Recurse -Force
        Write-Host "  synced runtime\$name"
    }
}

# Always prefer full trees from src\ for native toolchains (publish stage can omit large binaries).
function Sync-FromSrc([string]$name) {
    $src = Join-Path $root "src\$name"
    if (-not (Test-Path $src)) { return }
    $dest = Join-Path $runtime $name
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Copy-Item $src $dest -Recurse -Force
    Write-Host "  synced runtime\$name from src\"
}

Sync-FromSrc "ffmpeg"
Sync-FromSrc "jre"
# ffdec: prefer stage (includes worker), else src; then ensure jar from src if missing
$ffdecDest = Join-Path $runtime "ffdec"
$ffdecJar = Join-Path $ffdecDest "ffdec.jar"
if (-not (Test-Path $ffdecJar)) {
    $srcFfdec = Join-Path $root "src\ffdec"
    if (Test-Path $srcFfdec) {
        if (Test-Path $ffdecDest) { Remove-Item $ffdecDest -Recurse -Force }
        Copy-Item $srcFfdec $ffdecDest -Recurse -Force
        Write-Host "  synced runtime\ffdec from src\ (jar was missing)"
    }
}

# Move legacy root-level sidecars into runtime\ if still present (do not delete fuller trees)
$legacyNames = @(
    "Languages", "News", "Themes", "jsfl", "ffdec", "ffmpeg", "jre",
    "aflex_lite", "extensions", "tools"
)
foreach ($name in $legacyNames) {
    $legacy = Join-Path $release $name
    $dest = Join-Path $runtime $name
    if (-not (Test-Path $legacy)) { continue }
    if (Test-Path $dest) {
        # Keep runtime copy; drop empty/leftover root folder only
        Remove-Item $legacy -Recurse -Force
        Write-Host "  removed legacy root $name (runtime\$name already present)"
    } else {
        Move-Item $legacy $dest -Force
        Write-Host "  moved $name -> runtime\$name"
    }
}

$junkNames = @(
    "SSF2ModManager.dll", "SSF2ModManager.pdb", "SSF2ModManager.deps.json",
    "SSF2ModManager.runtimeconfig.json",
    "Markdig.dll", "Microsoft.Windows.SDK.NET.dll", "Newtonsoft.Json.dll",
    "NLayer.dll", "SharpCompress.dll", "WinRT.Runtime.dll", "ZstdSharp.dll",
    "FlaStepWizard.exe"
)
foreach ($name in $junkNames) {
    $p = Join-Path $release $name
    if (Test-Path $p) {
        Remove-Item $p -Force
        Write-Host "  removed $name"
    }
}

$flv = Join-Path $runtime "tools\flv2wav"
if (Test-Path $flv) {
    Remove-Item $flv -Recurse -Force
    Write-Host "  removed runtime\tools\flv2wav"
}

$legacyWinX64 = Join-Path $release "win-x64"
if (Test-Path $legacyWinX64) {
    Remove-Item $legacyWinX64 -Recurse -Force
    Write-Host "  removed legacy win-x64 publish folder"
}

$readme = @"
SSF2 Mod Manager
================

Run SSF2ModManager.exe - no .NET install required (single-file).

Release folder layout:
  SSF2ModManager.exe
  README.txt
  runtime\        tools and app content
    shell-dev\    open in VS Code for F5
    aflex_lite\   Flex/AIR lite SDK for Shell F5 / compile
    extensions\   ssf2-stream.vsix
    ffdec\        JPEXS Free Flash Decompiler
    ffmpeg\       music trim / 44.1 kHz convert / FLV→WAV
    jre\          Eclipse Temurin Java runtime
    Languages\ News\ Themes\

The playable SSF2 Shell folder is chosen in the launcher. It is not copied here.

Make Mods (launcher) or Setup Modding Tools installs VS Code + AS3MXML + SSF2 Stream.

Your settings and mod data are stored in:
  %AppData%\SSF2ModManager\
"@
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $release "README.txt"), $readme, $utf8NoBom)
Write-Host "  updated README.txt"

if ($ownedStage) {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "Done. Release root: exe + README + SSF2 Shell + runtime\."
