# Removes local build artifacts. Safe to run anytime — only deletes generated folders.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$paths = @(
    "bin",
    "obj",
    "publish",
    "dist",
    "SSF2ModManager.Tests\bin",
    "SSF2ModManager.Tests\obj",
    "ssf2mm-debug.log",
    "user-settings.json"
)

Push-Location $root
try {
    Write-Host "Cleaning SSF2 Mod Manager build artifacts..."
    foreach ($rel in $paths) {
        $full = Join-Path $root $rel
        if (Test-Path $full) {
            Remove-Item $full -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  removed $rel"
        }
    }

    dotnet clean SSF2ModManager.sln -c Release -v q 2>$null
    dotnet clean SSF2ModManager.sln -c Debug -v q 2>$null

    $remaining = (Get-ChildItem -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    $tracked = (git ls-files | Measure-Object).Count
    Write-Host ""
    Write-Host "Done. ~$tracked source files in git; $remaining files on disk (mostly .git + source)."
}
finally {
    Pop-Location
}
