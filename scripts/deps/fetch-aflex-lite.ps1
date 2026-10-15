# Builds Next2Flash\toolchain\aflex_lite from a lite Flex tree + minimal AIR (adl + runtimes/air/win and win64).
# Falls back to SSF2 Mod Manager\runtime\aflex_lite only when Next2Flash is not a sibling repo.
# Sources (first hit wins for lite tree):
#   $env:SSF2_AFLEX_LITE_SRC
# AIR pieces from:
#   $env:SSF2_FLEX_SDK / C:\aflex_sdk
#
# Also vendors asconfigc.jar into SSF2 Shell\tools\asconfigc\
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$release = Join-Path $root "SSF2 Mod Manager"
$n2f = Join-Path (Split-Path $root) "Next2Flash"
if ($env:NEXT2FLASH_HOME) { $n2f = $env:NEXT2FLASH_HOME }
if (Test-Path (Join-Path $n2f "app\n2f.py")) {
	$dest = Join-Path $n2f "toolchain\aflex_lite"
	$asconfigcDir = Join-Path $n2f "toolchain\asconfigc"
} else {
	$dest = Join-Path $release "runtime\aflex_lite"
	$asconfigcDir = Join-Path $release "SSF2 Shell\tools\asconfigc"
}
$cache = Join-Path $root ".tools-cache"
New-Item -ItemType Directory -Force -Path $cache | Out-Null

function Find-LiteSource {
	$candidates = @()
	if ($env:SSF2_AFLEX_LITE_SRC) { $candidates += $env:SSF2_AFLEX_LITE_SRC }
	foreach ($c in $candidates) {
		if ($c -and (Test-Path (Join-Path $c "bin\mxmlc.bat"))) {
			return (Resolve-Path $c).Path
		}
	}
	return $null
}

function Find-FullFlex {
	$candidates = @()
	if ($env:SSF2_FLEX_SDK) { $candidates += $env:SSF2_FLEX_SDK }
	if ($env:SSF2_AIR_SDK) { $candidates += $env:SSF2_AIR_SDK }
	$candidates += @("C:\aflex_sdk", "c:\aflex_sdk")
	foreach ($c in $candidates) {
		if (-not $c) { continue }
		$adl = Join-Path $c "bin\adl.exe"
		if (Test-Path -LiteralPath $adl) {
			return [System.IO.Path]::GetFullPath($c)
		}
	}
	return $null
}

$liteSrc = Find-LiteSource
$fullFlex = Find-FullFlex

if (-not $liteSrc -and -not $fullFlex) {
	throw "Need either an aflex_lite source tree or a full Flex SDK at C:\aflex_sdk (set SSF2_AFLEX_LITE_SRC / SSF2_FLEX_SDK)."
}

Write-Host "Building aflex_lite at $dest"
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dest | Out-Null

if ($liteSrc) {
	Write-Host "  copying lite tree from $liteSrc"
	Copy-Item (Join-Path $liteSrc "*") $dest -Recurse -Force
}
elseif ($fullFlex) {
	Write-Host "  no lite source; copying compile essentials from full SDK $fullFlex"
	foreach ($rel in @("bin", "lib", "frameworks", "legacy", "api")) {
		$src = Join-Path $fullFlex $rel
		if (Test-Path $src) {
			Copy-Item $src (Join-Path $dest $rel) -Recurse -Force
		}
	}
}

if (-not $fullFlex) {
	Write-Host "WARNING: full Flex SDK not found — adl/runtimes not merged. F5 will fail until AIR is added."
}
else {
	Write-Host "  merging AIR from $fullFlex"
	# Ensure bin exists
	New-Item -ItemType Directory -Force -Path (Join-Path $dest "bin") | Out-Null
	New-Item -ItemType Directory -Force -Path (Join-Path $dest "lib") | Out-Null
	foreach ($name in @(
		"adl.exe", "adl", "adt.bat", "adt", "mxmlc.bat", "mxmlc", "compc.bat", "compc",
		"asc.bat", "asc", "fcsh.bat", "fcsh", "fdb.bat", "fdb"
	)) {
		$src = Join-Path $fullFlex "bin\$name"
		if (Test-Path $src) {
			Copy-Item $src (Join-Path $dest "bin\$name") -Force
		}
	}
	# AIR DLLs / helpers often sit next to adl
	Get-ChildItem (Join-Path $fullFlex "bin") -File -ErrorAction SilentlyContinue |
		Where-Object { $_.Extension -in ".dll", ".exe", ".bat" -or $_.Name -like "air*" } |
		ForEach-Object {
			$target = Join-Path $dest "bin\$($_.Name)"
			if (-not (Test-Path $target)) {
				Copy-Item $_.FullName $target -Force
			}
		}
	foreach ($name in @("adt.jar", "compiler.jar", "mxmlc.jar")) {
		$src = Join-Path $fullFlex "lib\$name"
		if (Test-Path $src) {
			Copy-Item $src (Join-Path $dest "lib\$name") -Force
		}
	}
	# Copy any other jars referenced by mxmlc if missing from lite
	if (-not (Test-Path (Join-Path $dest "lib"))) {
		Copy-Item (Join-Path $fullFlex "lib") (Join-Path $dest "lib") -Recurse -Force
	}
	# ADL exits 3 until both Windows runtimes are present. Leave out android and captive.
	foreach ($rel in @("runtimes\air\win", "runtimes\air\win64")) {
		$rtSrc = Join-Path $fullFlex $rel
		if (-not (Test-Path $rtSrc)) { continue }
		$rtDest = Join-Path $dest $rel
		if (Test-Path $rtDest) { Remove-Item $rtDest -Recurse -Force }
		New-Item -ItemType Directory -Force -Path (Split-Path $rtDest) | Out-Null
		Copy-Item $rtSrc $rtDest -Recurse -Force
	}
	# AIR SDK frameworks pieces sometimes needed
	$airFw = Join-Path $fullFlex "frameworks\libs\air"
	if (Test-Path $airFw) {
		$destAir = Join-Path $dest "frameworks\libs\air"
		New-Item -ItemType Directory -Force -Path $destAir | Out-Null
		Copy-Item (Join-Path $airFw "*") $destAir -Recurse -Force
	}
}

$adl = Join-Path $dest "bin\adl.exe"
$mxmlc = Join-Path $dest "bin\mxmlc.bat"
Write-Host "  mxmlc: $(Test-Path $mxmlc)  adl: $(Test-Path $adl)"
if (-not (Test-Path $mxmlc)) { throw "aflex_lite incomplete: missing bin\mxmlc.bat" }
if (-not (Test-Path $adl)) {
	Write-Host "WARNING: bin\adl.exe missing — F5 will not work until AIR is merged."
}

# Vendor asconfigc.jar
New-Item -ItemType Directory -Force -Path $asconfigcDir | Out-Null
$asJar = Join-Path $asconfigcDir "asconfigc.jar"
if (-not (Test-Path $asJar)) {
	Write-Host "Fetching asconfigc.jar…"
	# Prefer already-installed extension
	$found = $null
	foreach ($base in @(
		(Join-Path $env:USERPROFILE ".cursor\extensions"),
		(Join-Path $env:USERPROFILE ".vscode\extensions")
	)) {
		if (Test-Path $base) {
			$hit = Get-ChildItem $base -Filter "asconfigc.jar" -Recurse -ErrorAction SilentlyContinue |
				Sort-Object LastWriteTime -Descending | Select-Object -First 1
			if ($hit) { $found = $hit.FullName; break }
		}
	}
	if ($found) {
		Copy-Item $found $asJar -Force
		Write-Host "  -> copied from extension $found"
	}
	else {
		# Download vscode-as3mxml VSIX and extract asconfigc.jar
		$vsix = Join-Path $cache "as3mxml.vsix"
		$api = "https://marketplace.visualstudio.com/_apis/public/gallery/publishers/bowlerhatllc/vsextensions/vscode-as3mxml/latest/vspackage"
		try {
			Invoke-WebRequest -Uri $api -OutFile $vsix -UseBasicParsing
			$extract = Join-Path $cache "as3mxml-extract"
			if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
			# VSIX is a zip
			Expand-Archive -Path $vsix -DestinationPath $extract -Force
			$hit = Get-ChildItem $extract -Filter "asconfigc.jar" -Recurse | Select-Object -First 1
			if (-not $hit) { throw "asconfigc.jar not inside AS3MXML VSIX" }
			Copy-Item $hit.FullName $asJar -Force
			Write-Host "  -> $asJar"
		}
		catch {
			Write-Host "WARNING: could not fetch asconfigc.jar: $_"
			Write-Host "  Install ActionScript & MXML in VS Code, or place asconfigc.jar at $asJar"
		}
	}
}
else {
	Write-Host "asconfigc.jar already vendored."
}

$sizeMb = [math]::Round(((Get-ChildItem $dest -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Done. aflex_lite ≈ $sizeMb MB"
