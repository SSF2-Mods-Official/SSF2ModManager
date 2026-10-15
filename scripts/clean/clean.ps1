# Removes local build artifacts. Safe to run anytime — only deletes generated folders.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$paths = @(
    "bin",
    "obj",
    "src\bin",
    "src\obj",
    "src\ffmpeg",
    "src\jre",
    "src\tests\SSF2ModManager.Tests\bin",
    "src\tests\SSF2ModManager.Tests\obj",
    "publish",
    "dist",
    ".tools-cache",
    "review-clonededede",
    ".tmp-bandana-scan",
    ".tmp-clonesamus-probe",
    "ssf2mm-debug.log",
    "user-settings.json",
    # Local scratch (not regenerable build output — safe to drop)
    "GokuSoundReview",
    "continue.md",
    ".cursor",
    ".as3mxml-unpackaged-anes"  # root junction only; Shell copy kept
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

    Get-ChildItem (Join-Path $root "src\tools") -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        foreach ($name in @("bin", "obj")) {
            $full = Join-Path $_.FullName $name
            if (Test-Path $full) {
                Remove-Item $full -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  removed src\tools\$($_.Name)\$name"
            }
        }
    }

    Get-ChildItem $root -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like "tmp-*.log" -or $_.Name -like "tmp-*.log.err" -or $_.Name -like "tmp_*.as" -or $_.Name -like "tmp_*.csx" -or
        $_.Name -like "clone-*-results.txt" -or $_.Name -like "debug-*.log"
    } | ForEach-Object {
        Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
        Write-Host "  removed $($_.Name)"
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
