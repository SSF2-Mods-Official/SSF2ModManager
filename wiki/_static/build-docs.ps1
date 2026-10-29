# Rebuilds _static/content.js from Markdown section folders (for file:// viewing).
# Called by ../Open Docs.bat — keep this under _static, not docs root.
# -IncludePrivate adds wiki/Private Wiki. The public site build does not pass that switch.
param(
	[switch]$IncludePrivate,
	[switch]$PrivateOnly
)
$ErrorActionPreference = "Stop"
$staticRoot = $PSScriptRoot
$docsRoot = Split-Path -Parent $staticRoot
$contentPath = Join-Path $staticRoot "content.js"

function Get-FrontMatter([string]$text) {
	$title = $null
	$summary = $null
	$group = ""
	$order = 100
	$body = $text
	if ($text -match '(?s)\A---\r?\n(.*?)\r?\n---\r?\n(.*)\z') {
		$fm = $Matches[1]
		$body = $Matches[2]
		foreach ($line in ($fm -split '\r?\n')) {
			if ($line -match '^\s*title\s*:\s*(.+)\s*$') { $title = $Matches[1].Trim().Trim('"').Trim("'") }
			elseif ($line -match '^\s*summary\s*:\s*(.+)\s*$') { $summary = $Matches[1].Trim().Trim('"').Trim("'") }
			elseif ($line -match '^\s*order\s*:\s*(\d+)\s*$') { $order = [int]$Matches[1] }
			elseif ($line -match '^\s*group\s*:\s*(.+)\s*$') { $group = $Matches[1].Trim().Trim('"').Trim("'") }
		}
	}
	if (-not $title -and $body -match '(?m)^\s*#\s+(.+)\s*$') {
		$title = $Matches[1].Trim()
	}
	if (-not $title) { $title = "Untitled" }
	if (-not $summary) { $summary = "" }
	return @{ Title = $title; Summary = $summary; Order = $order; Group = $group }
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

function Write-CreditsPage {
	$jsonPath = Join-Path $staticRoot "credits.json"
	$outPath = Join-Path $docsRoot "Start Here\credits.md"
	if (-not (Test-Path -LiteralPath $jsonPath)) { return }
	$utf8 = New-Object System.Text.UTF8Encoding $false
	$data = [System.IO.File]::ReadAllText($jsonPath, $utf8) | ConvertFrom-Json
	$dash = [char]0x2014
	$sb = New-Object System.Text.StringBuilder
	[void]$sb.AppendLine("---")
	[void]$sb.AppendLine("title: Credits")
	[void]$sb.AppendLine("summary: The same names as Credits in the Mod Manager.")
	[void]$sb.AppendLine("order: 18")
	[void]$sb.AppendLine("---")
	[void]$sb.AppendLine("")
	[void]$sb.AppendLine([string]$data.product)
	[void]$sb.AppendLine("")
	[void]$sb.AppendLine("Created by $($data.createdBy)")
	[void]$sb.AppendLine("")
	foreach ($line in @($data.lines)) {
		[void]$sb.AppendLine("$($line.name) $dash $($line.by)")
		[void]$sb.AppendLine("")
	}
	[void]$sb.AppendLine("Installed mods are listed on the Credits page in the Mod Manager.")
	[void]$sb.AppendLine("")
	[System.IO.File]::WriteAllText($outPath, $sb.ToString().TrimEnd() + "`n", $utf8)
	Write-Host "Wrote $outPath"
}

if (-not $PrivateOnly) {
	Write-NewsChangelog
	Write-CreditsPage
}

$sections = @()
$pages = @{}

$skip = @('wiki-app')
if (-not $IncludePrivate -and -not $PrivateOnly) { $skip += 'Private Wiki' }
Get-ChildItem -LiteralPath $docsRoot -Directory |
		Where-Object {
			if ($PrivateOnly) { $_.Name -eq 'Private Wiki' }
			else { $_.Name -notlike '_*' -and $skip -notcontains $_.Name }
		} |
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
					group   = $fm.Group
				}
			}
		$groupRank = @{
			"Characters" = 1
			"Stages" = 2
			"Palette" = 3
			"Events" = 4
			"Match" = 5
			"Mods" = 6
			"Build" = 7
		}
		$list = @($list | Sort-Object { if ($_.group -and $groupRank.ContainsKey($_.group)) { $groupRank[$_.group] } else { 0 } }, { $_.order }, { $_.title })
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
$outDir = $staticRoot
if ($PrivateOnly) {
	$outDir = Join-Path $docsRoot "_private"
	New-Item -ItemType Directory -Force -Path $outDir | Out-Null
	foreach ($file in @("index.html", "app.js", "md.js", "site.css", "ssf2m.png")) {
		Copy-Item (Join-Path $staticRoot $file) (Join-Path $outDir $file) -Force
	}
}
[System.IO.File]::WriteAllText((Join-Path $outDir "content.js"), $js, $utf8)

$imgSrc = Join-Path $docsRoot "_images"
$imgDest = Join-Path $staticRoot "images"
if (Test-Path $imgSrc) {
	New-Item -ItemType Directory -Force -Path $imgDest | Out-Null
	Get-ChildItem $imgSrc -File | ForEach-Object {
		Copy-Item $_.FullName (Join-Path $imgDest $_.Name) -Force
	}
	Write-Host "Copied images into $imgDest"
}

Write-Host "Wrote $(Join-Path $outDir 'content.js') ($($sections.Count) section(s))"
foreach ($s in $sections) {
	Write-Host ("  {0}: {1} page(s)" -f $s.title, $s.pages.Count)
}

if (-not $PrivateOnly) {
	$site = "https://ssf2-mods-official.github.io/SSF2ModManager"
	$indexPath = Join-Path $staticRoot "index.html"
	$indexHtml = [System.IO.File]::ReadAllText($indexPath, $utf8)
	function Encode-Attr([string]$s) {
		if (-not $s) { return "" }
		return ($s -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;' -replace '"', '&quot;')
	}
	function Share-Text([string]$summary, [string]$raw) {
		$body = $raw
		if ($raw -match '(?s)\A---\r?\n.*?\r?\n---\r?\n(.*)\z') { $body = $Matches[1] }
		$text = ""
		if ($body -match '(?s)##\s+([^\r\n]+)\s+(?:[0-9]{4}-[0-9]{2}-[0-9]{2}\s+)?(.+?)(?:\r?\n){2,}') {
			$blurb = ($Matches[1].Trim() + ". " + $Matches[2].Trim())
			$blurb = [regex]::Replace($blurb, '!\[[^\]]*\]\([^)]*\)', '')
			$blurb = [regex]::Replace($blurb, '\[([^\]]+)\]\([^)]*\)', '$1')
			$blurb = [regex]::Replace($blurb, '[#>*_`|]', '')
			$text = ([regex]::Replace($blurb, '\s+', ' ')).Trim()
		}
		if (-not $text) { $text = $summary }
		if (-not $text) { $text = "SSF2 Mod Manager Wiki" }
		if ($text.Length -gt 220) { $text = $text.Substring(0, 217).TrimEnd() + "..." }
		return $text
	}
	Get-ChildItem -LiteralPath $staticRoot -Filter *.html -File | ForEach-Object {
		if ($_.Name -eq "index.html") { return }
		$head = [System.IO.File]::ReadAllText($_.FullName, $utf8)
		if ($head.StartsWith("<!-- share-page -->")) { Remove-Item -LiteralPath $_.FullName -Force }
	}
	$urls = New-Object System.Collections.Generic.List[string]
	$urls.Add("$site/")
	foreach ($sec in $sections) {
		if ($sec.id -eq "Dev Wiki") { continue }
		foreach ($p in $sec.pages) {
			$raw = [string]$pages[$p.file]
			$stem = [System.IO.Path]::GetFileNameWithoutExtension(($p.file -split '/')[-1])
			$desc = Share-Text $p.summary $raw
			$pageTitle = "$($p.title) - SSF2 Mod Manager Wiki"
			$url = "$site/$stem.html"
			$descAttr = Encode-Attr $desc
			$titleAttr = Encode-Attr $p.title
			$json = (@{
				"@context" = "https://schema.org"
				"@type" = "TechArticle"
				headline = $p.title
				description = $desc
				url = $url
				image = "$site/ssf2m.png"
				publisher = @{ "@type" = "Organization"; name = "SSF2 Mods Official" }
			} | ConvertTo-Json -Compress -Depth 5)
			$meta = @"
<meta name="description" content="$descAttr" />
  <meta name="robots" content="index,follow" />
  <link rel="canonical" href="$url" />
  <meta property="og:site_name" content="SSF2 Mod Manager Wiki" />
  <meta property="og:type" content="article" />
  <meta property="og:title" content="$titleAttr" />
  <meta property="og:description" content="$descAttr" />
  <meta property="og:url" content="$url" />
  <meta property="og:image" content="$site/ssf2m.png" />
  <meta name="twitter:card" content="summary" />
  <meta name="twitter:title" content="$titleAttr" />
  <meta name="twitter:description" content="$descAttr" />
  <meta name="twitter:image" content="$site/ssf2m.png" />
  <link rel="icon" href="ssf2m.png" />
  <script type="application/ld+json">$json</script>
"@
			$html = $indexHtml
			$html = $html -replace '<html lang="en">', ('<html lang="en" data-page="' + (Encode-Attr $p.file) + '">')
			$html = $html -replace '<title>[^<]*</title>', ("<title>" + (Encode-Attr $pageTitle) + "</title>")
			$html = [regex]::Replace($html, '(?s)<!-- share-meta -->.*?<!-- /share-meta -->', "<!-- share-meta -->`n  $meta  <!-- /share-meta -->")
			$html = $html -replace '<p class="lead">Loading.+</p>', ('<p class="lead">' + (Encode-Attr $desc) + '</p>')
			$html = "<!-- share-page -->`n" + $html
			[System.IO.File]::WriteAllText((Join-Path $staticRoot "$stem.html"), $html, $utf8)
			$urls.Add($url)
		}
	}
	$robots = "User-agent: *`nAllow: /`n`nSitemap: $site/sitemap.xml`n"
	[System.IO.File]::WriteAllText((Join-Path $staticRoot "robots.txt"), $robots, $utf8)
	$sm = New-Object System.Text.StringBuilder
	[void]$sm.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
	[void]$sm.AppendLine('<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">')
	foreach ($u in $urls) { [void]$sm.AppendLine("  <url><loc>$u</loc></url>") }
	[void]$sm.AppendLine('</urlset>')
	[System.IO.File]::WriteAllText((Join-Path $staticRoot "sitemap.xml"), $sm.ToString(), $utf8)
	Write-Host "Wrote $($urls.Count) search pages, robots.txt, and sitemap.xml"
}
