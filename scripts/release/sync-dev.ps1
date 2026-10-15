# Fast daily sync for developers:
#  - Framework-dependent single-file exe into "SSF2 Mod Manager Release\" (needs .NET 8 runtime)
#  - Refresh runtime content from src (Languages, News, Themes, ffdec)
#  - Refresh extensions VSIX when sources are newer
# Does NOT bake in the .NET runtime (~200MB self-contained). Use sync-release-exe.ps1 / run.bat ship for that.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$release = Join-Path $root "SSF2 Mod Manager Release"
$runtime = Join-Path $release "runtime"
$stage = Join-Path $root ".tools-cache\dev-stage"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnetExe = Join-Path ${env:ProgramFiles} "dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $dotnetExe) {
        $env:Path = "$(Split-Path -Parent $dotnetExe);$env:Path"
    } else {
        throw "dotnet SDK not found. Install .NET 8 SDK."
    }
}

New-Item -ItemType Directory -Force -Path $release | Out-Null
$legacyDev = Join-Path $root "SSF2 Mod Manager\runtime\shell-dev"
$releaseDev = Join-Path $runtime "shell-dev"
if (-not (Test-Path -LiteralPath (Join-Path $releaseDev "asconfig.json")) -and (Test-Path -LiteralPath (Join-Path $legacyDev "asconfig.json"))) {
    Write-Host "Copying shell-dev into the new release runtime..."
    New-Item -ItemType Directory -Force -Path $runtime | Out-Null
    & robocopy $legacyDev $releaseDev /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for shell-dev (exit $LASTEXITCODE)" }
    Write-Host "  synced runtime\shell-dev"
}

function Copy-FileRetry([string]$Source, [string]$Destination, [int]$Attempts = 15) {
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

function Sync-Tree([string]$Source, [string]$Destination, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Source)) {
        Write-Host "  skip $Label (no source at $Source)"
        return
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    # /E copy subdirs incl empty; /XO skip older dest; /NFL /NDL /NJH /NJS quieter
    & robocopy $Source $Destination /E /XO /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    $code = $LASTEXITCODE
    # robocopy: 0-7 success-ish; >=8 failure
    if ($code -ge 8) { throw "robocopy failed for $Label (exit $code)" }
    Write-Host "  synced $Label"
}

Get-Process -Name "SSF2ModManager" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

New-Item -ItemType Directory -Force -Path $stage | Out-Null
New-Item -ItemType Directory -Force -Path $runtime | Out-Null

Write-Host "Publishing framework-dependent single-file (dev)..."
dotnet publish (Join-Path $root "src\SSF2ModManager.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=false `
    -p:SkipRuntimeSidecars=true `
    -p:SatelliteResourceLanguages=en `
    -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $stage "SSF2ModManager.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing $exe" }
Copy-FileRetry $exe (Join-Path $release "SSF2ModManager.exe")
Write-Host "  updated SSF2ModManager.exe ($([math]::Round((Get-Item -LiteralPath $exe).Length/1MB,1)) MB) [framework-dependent]"

# App content. FFDec, the JRE, and Flex lite live in the Next2Flash repo.
Sync-Tree (Join-Path $root "src\Languages") (Join-Path $runtime "Languages") "runtime\Languages"
Sync-Tree (Join-Path $root "src\News") (Join-Path $runtime "News") "runtime\News"
Sync-Tree (Join-Path $root "src\Themes") (Join-Path $runtime "Themes") "runtime\Themes"

$n2fFfdec = Join-Path (Split-Path $root) "Next2Flash\toolchain\ffdec\ffdec.jar"
if (-not (Test-Path -LiteralPath $n2fFfdec)) {
    Write-Host "  WARNING: Next2Flash FFDec missing at $n2fFfdec"
}

# ffmpeg stays with the mod manager. Java and Flex lite come from Next2Flash.
$ffmpegDest = Join-Path $runtime "ffmpeg"
if (-not (Test-Path -LiteralPath (Join-Path $ffmpegDest "ffmpeg.exe"))) {
    & (Join-Path $root "scripts\deps\fetch-native-tools.ps1")
    $ffmpegSrc = Join-Path $root "src\ffmpeg"
    if (Test-Path -LiteralPath (Join-Path $ffmpegSrc "ffmpeg.exe")) {
        Sync-Tree $ffmpegSrc $ffmpegDest "runtime\ffmpeg"
    } else {
        Write-Host "  WARNING: runtime\ffmpeg still missing after fetch"
    }
} else {
    Write-Host "  keep runtime\ffmpeg (already present)"
}

# SSF2 Stream VSIX: rebuild only when extension sources are newer than the installed vsix
$extSrc = Join-Path $root "tools\ssf2-stream-extension"
$vsixDest = Join-Path $runtime "extensions\ssf2-stream.vsix"
$needVsix = $false
if (-not (Test-Path -LiteralPath $vsixDest)) {
    $needVsix = Test-Path -LiteralPath (Join-Path $extSrc "package.json")
} elseif (Test-Path -LiteralPath $extSrc) {
    $vsixTime = (Get-Item -LiteralPath $vsixDest).LastWriteTimeUtc
    $watch = @("package.json", "package-lock.json", "tsconfig.json") |
        ForEach-Object { Join-Path $extSrc $_ } |
        Where-Object { Test-Path -LiteralPath $_ }
    $watchDirs = @("src", "media", "helper") | ForEach-Object { Join-Path $extSrc $_ } |
        Where-Object { Test-Path -LiteralPath $_ }
    $newer = @($watch) + @(
        Get-ChildItem -LiteralPath $watchDirs -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\node_modules\\' }
    ) | Where-Object { $_.LastWriteTimeUtc -gt $vsixTime } | Select-Object -First 1
    if ($newer) { $needVsix = $true }
}
if ($needVsix) {
    Write-Host "Packaging SSF2 Stream VSIX (extension sources newer or missing)..."
    & (Join-Path $root "scripts\deps\package-ssf2-stream.ps1")
} elseif (Test-Path -LiteralPath $vsixDest) {
    Write-Host "  keep runtime\extensions\ssf2-stream.vsix"
} else {
    Write-Host "  skip extensions (no package.json / vsix)"
}

# Drop loose DLLs if a previous framework multi-file build polluted the release root
$junkNames = @(
    "SSF2ModManager.dll", "SSF2ModManager.pdb", "SSF2ModManager.deps.json",
    "SSF2ModManager.runtimeconfig.json",
    "Markdig.dll", "Microsoft.Windows.SDK.NET.dll", "Newtonsoft.Json.dll",
    "NLayer.dll", "SharpCompress.dll", "WinRT.Runtime.dll", "ZstdSharp.dll",
    "FlaStepWizard.exe"
)
foreach ($name in $junkNames) {
    $p = Join-Path $release $name
    if (Test-Path -LiteralPath $p) {
        Remove-Item -LiteralPath $p -Force
        Write-Host "  removed root $name"
    }
}

Write-Host 'Done (dev). Layout: SSF2 Mod Manager Release\ exe + runtime\'
Write-Host 'Note: needs .NET 8 desktop runtime. For a fully portable ship build use: run.bat ship'
