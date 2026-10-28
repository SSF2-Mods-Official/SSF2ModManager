# Rebuilds _static/content.js from Markdown section folders (for file:// viewing).
# Called by ../Open Docs.bat — keep this under _static, not docs root.
$ErrorActionPreference = "Stop"
$staticRoot = $PSScriptRoot
$docsRoot = Split-Path -Parent $staticRoot
$contentPath = Join-Path $staticRoot "content.js"

function Get-FrontMatter([string]$text) {
	$title = $null
	$summary = $null
	$order = 100
	$body = $text
	if ($text -match '(?s)\A---\r?\n(.*?)\r?\n---\r?\n(.*)\z') {
		$fm = $Matches[1]
		$body = $Matches[2]
		foreach ($line in ($fm -split '\r?\n')) {
			if ($line -match '^\s*title\s*:\s*(.+)\s*$') { $title = $Matches[1].Trim().Trim('"').Trim("'") }
			elseif ($line -match '^\s*summary\s*:\s*(.+)\s*$') { $summary = $Matches[1].Trim().Trim('"').Trim("'") }
			elseif ($line -match '^\s*order\s*:\s*(\d+)\s*$') { $order = [int]$Matches[1] }
		}
	}
	if (-not $title -and $body -match '(?m)^\s*#\s+(.+)\s*$') {
		$title = $Matches[1].Trim()
	}
	if (-not $title) { $title = "Untitled" }
	if (-not $summary) { $summary = "" }
	return @{ Title = $title; Summary = $summary; Order = $order }
}

$sections = @()
$pages = @{}

Get-ChildItem -LiteralPath $docsRoot -Directory |
	Where-Object { $_.Name -notlike '_*' -and $_.Name -ne 'wiki-app' } |
	Sort-Object Name |
	ForEach-Object {
		$sectionDir = $_
		$list = @()
		Get-ChildItem -LiteralPath $sectionDir.FullName -Filter *.md -Recurse -File |
			Where-Object { $_.FullName -notmatch '\\node_modules\\|\\_history\\|\\_images\\' } |
			ForEach-Object {
				$raw = [System.IO.File]::ReadAllText($_.FullName, [System.Text.UTF8Encoding]::new($false))
				$fm = Get-FrontMatter $raw
				$tail = $_.FullName.Substring($sectionDir.FullName.Length).TrimStart('\', '/') -replace '\\', '/'
				$rel = ($sectionDir.Name + "/" + $tail) -replace '\\', '/'
				$pages[$rel] = $raw
				$list += [ordered]@{
					file    = $rel
					title   = $fm.Title
					summary = $fm.Summary
					order   = $fm.Order
				}
			}
		$list = @($list | Sort-Object { $_.order }, { $_.title })
		if ($list.Count -gt 0) {
			$sections += [ordered]@{
				id    = $sectionDir.Name
				title = $sectionDir.Name
				pages = $list
			}
		}
	}

$preferred = @("Start Here", "Next Steps", "Dev Wiki")
$sections = @(
	$preferred | ForEach-Object {
		$want = $_
		$sections | Where-Object { $_.id -eq $want }
	}
) + @(
	$sections | Where-Object { $preferred -notcontains $_.id } | Sort-Object { $_.id }
)

$doc = [ordered]@{
	generated = (Get-Date).ToString("o")
	sections  = $sections
	pages     = $pages
}

$json = $doc | ConvertTo-Json -Depth 8 -Compress
$js = "window.SSF2_DOCS = $json;"
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($contentPath, $js, $utf8)

Write-Host "Wrote $contentPath ($($sections.Count) section(s))"
foreach ($s in $sections) {
	Write-Host ("  {0}: {1} page(s)" -f $s.title, $s.pages.Count)
}
