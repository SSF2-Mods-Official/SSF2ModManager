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

function Write-NewsChangelog {
	$repo = Split-Path -Parent $docsRoot
	$newsRoot = Join-Path $repo "src\News"
	$outPath = Join-Path $docsRoot "Start Here\changelog.md"
	if (-not (Test-Path -LiteralPath $newsRoot)) { return }
	$posts = @()
	Get-ChildItem -LiteralPath $newsRoot -Directory | ForEach-Object {
		$articlePath = Join-Path $_.FullName "article.md"
		if (-not (Test-Path -LiteralPath $articlePath)) { return }
		$text = [System.IO.File]::ReadAllText($articlePath, [System.Text.UTF8Encoding]::new($false))
		if ($text -notmatch '(?s)\A---\r?\n(.*?)\r?\n---\r?\n(.*)\z') { return }
		$fm = $Matches[1]
		$body = $Matches[2].Trim()
		$title = $_.Name
		$date = ""
		$draft = $false
		$shell = $false
		$inTags = $false
		foreach ($line in ($fm -split '\r?\n')) {
			if ($inTags -and $line -match '^\s*-\s+(.+)\s*$') {
				if ($Matches[1].Trim().Trim('"') -eq "shell") { $shell = $true }
				continue
			}
			$inTags = $false
			if ($line -match '^\s*title\s*:\s*(.+)\s*$') { $title = $Matches[1].Trim().Trim('"').Trim("'") }
			elseif ($line -match '^\s*date\s*:\s*(.+)\s*$') { $date = $Matches[1].Trim().Trim('"').Trim("'") }
			elseif ($line -match '^\s*draft\s*:\s*true\s*$') { $draft = $true }
			elseif ($line -match '^\s*tags\s*:') { $inTags = $true }
		}
		if ($draft) { return }
		$body = [regex]::Replace($body, '(?s)\A!\[[^\]]*\]\(hero\.svg\)\s*', '')
		$body = [regex]::Replace($body, '(?m)^#{1,6}\s+', '### ')
		$version = "0"
		if ($title -match 'v(\d+(?:\.\d+)*)') { $version = $Matches[1] }
		$posts += [pscustomobject]@{ Title = $title; Date = $date; Version = $version; Id = $_.Name; Shell = $shell; Body = $body.Trim() }
	}
	$posts = @($posts | Sort-Object { $_.Date }, { $_.Version }, { $_.Shell }, { $_.Id } -Descending)
	function Write-ChangelogPost($sb, $post) {
		[void]$sb.AppendLine("## $($post.Title)")
		[void]$sb.AppendLine("")
		if ($post.Date) {
			[void]$sb.AppendLine($post.Date)
			[void]$sb.AppendLine("")
		}
		[void]$sb.AppendLine($post.Body)
		[void]$sb.AppendLine("")
	}
	$openCount = 0
	if ($posts.Count -gt 0) { $openCount = 1 }
	if ($posts.Count -gt 1 -and $posts[1].Shell -and $posts[1].Date -eq $posts[0].Date) { $openCount = 2 }
	$sb = New-Object System.Text.StringBuilder
	[void]$sb.AppendLine("---")
	[void]$sb.AppendLine("title: Changelog")
	[void]$sb.AppendLine("summary: The same posts as News in the Mod Manager.")
	[void]$sb.AppendLine("order: 15")
	[void]$sb.AppendLine("---")
	[void]$sb.AppendLine("")
	for ($n = 0; $n -lt $openCount; $n++) { Write-ChangelogPost $sb $posts[$n] }
	if ($posts.Count -gt $openCount) {
		[void]$sb.AppendLine(":::details Past versions")
		[void]$sb.AppendLine("")
		for ($n = $openCount; $n -lt $posts.Count; $n++) { Write-ChangelogPost $sb $posts[$n] }
		[void]$sb.AppendLine(":::")
		[void]$sb.AppendLine("")
	}
	$utf8 = New-Object System.Text.UTF8Encoding $false
	[System.IO.File]::WriteAllText($outPath, $sb.ToString().TrimEnd() + "`n", $utf8)
	Write-Host "Wrote $outPath ($($posts.Count) post(s))"
}

Write-NewsChangelog

$sections = @()
$pages = @{}

Get-ChildItem -LiteralPath $docsRoot -Directory |
		Where-Object { $_.Name -notlike '_*' -and $_.Name -ne 'wiki-app' -and $_.Name -ne 'Private Wiki' } |
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

$preferred = @("Start Here", "Mod Manager Features", "SSF2 Shell Features")
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

$imgSrc = Join-Path $docsRoot "_images"
$imgDest = Join-Path $staticRoot "images"
if (Test-Path $imgSrc) {
	New-Item -ItemType Directory -Force -Path $imgDest | Out-Null
	Get-ChildItem $imgSrc -File | ForEach-Object {
		Copy-Item $_.FullName (Join-Path $imgDest $_.Name) -Force
	}
	Write-Host "Copied images into $imgDest"
}

Write-Host "Wrote $contentPath ($($sections.Count) section(s))"
foreach ($s in $sections) {
	Write-Host ("  {0}: {1} page(s)" -f $s.title, $s.pages.Count)
}
