# Generates news-index.json from src/News for remote sync.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$newsRoot = Join-Path $root "src\News"
$outFile = Join-Path $root "news-index.json"

function Get-FrontmatterValue([string]$raw, [string]$key) {
    if ($raw -notmatch "(?ms)^---\s*(.*?)\s*---") { return $null }
    $fm = $Matches[1]
    foreach ($line in ($fm -split "`n")) {
        if ($line -match "^\s*$key\s*:\s*(.+)\s*$") {
            return $Matches[1].Trim().Trim('"')
        }
    }
    return $null
}

function Get-FrontmatterTags([string]$raw) {
    $tags = @()
    if ($raw -notmatch "(?ms)^---\s*(.*?)\s*---") { return $tags }
    $fm = $Matches[1]
    $inTags = $false
    foreach ($line in ($fm -split "`n")) {
        if ($line -match "^\s*tags\s*:\s*$") { $inTags = $true; continue }
        if ($inTags -and $line -match "^\s*-\s*(.+)\s*$") { $tags += $Matches[1].Trim().Trim('"'); continue }
        if ($inTags -and $line -match "^\s*\w+\s*:") { $inTags = $false }
    }
    return $tags
}

$articles = @()
Get-ChildItem $newsRoot -Directory | Sort-Object Name | ForEach-Object {
    $id = $_.Name
    $articleMd = Join-Path $_.FullName "article.md"
    if (-not (Test-Path $articleMd)) { return }

    $raw = Get-Content $articleMd -Raw -Encoding UTF8
    $title = Get-FrontmatterValue $raw "title"
    $date = Get-FrontmatterValue $raw "date"
    $draftVal = Get-FrontmatterValue $raw "draft"
    $draft = $draftVal -eq "true"
    $tags = @(Get-FrontmatterTags $raw)
    $assets = @(Get-ChildItem $_.FullName -File | ForEach-Object { $_.Name })

    $articles += [ordered]@{
        id = $id
        path = "src/News/$id"
        title = if ($title) { $title } else { $id }
        date = if ($date) { $date } else { "" }
        tags = $tags
        draft = $draft
        assets = $assets
    }
}

$index = [ordered]@{
    version = 1
    updated = (Get-Date).ToUniversalTime().ToString("o")
    articles = $articles
}

$json = $index | ConvertTo-Json -Depth 6
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outFile, $json + "`n", $utf8NoBom)
Write-Host "Wrote $outFile ($($articles.Count) articles)"
