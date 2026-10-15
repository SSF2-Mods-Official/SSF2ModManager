# Package tools/ssf2-stream-extension into SSF2 Mod Manager/runtime/extensions/ssf2-stream.vsix
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$extDir = Join-Path $root "tools\ssf2-stream-extension"
$outDir = Join-Path $root "SSF2 Mod Manager Release\runtime\extensions"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Test-Path (Join-Path $extDir "package.json"))) {
	throw "Missing $extDir\package.json"
}

Push-Location $extDir
try {
	if (-not (Test-Path "node_modules")) {
		Write-Host "npm install…"
		npm install --no-fund --no-audit
		if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
	}
	Write-Host "npm run compile…"
	npm run compile
	if ($LASTEXITCODE -ne 0) { throw "npm run compile failed" }

	Get-ChildItem -Filter "ssf2-stream-*.vsix" -ErrorAction SilentlyContinue | Remove-Item -Force
	Write-Host "vsce package…"
	npx --yes @vscode/vsce package --no-dependencies --allow-missing-repository --skip-license
	if ($LASTEXITCODE -ne 0) { throw "vsce package failed" }

	$vsix = Get-ChildItem -Filter "ssf2-stream-*.vsix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
	if (-not $vsix) { throw "No ssf2-stream-*.vsix produced" }
	$dest = Join-Path $outDir "ssf2-stream.vsix"
	Copy-Item $vsix.FullName $dest -Force
	Write-Host "  -> $dest ($([math]::Round($vsix.Length/1MB,2)) MB)"
}
finally {
	Pop-Location
}
