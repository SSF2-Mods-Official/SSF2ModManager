---
title: News
summary: The Mod Manager News posts and the wiki Changelog page are the same articles.
order: 50
group: Build
---

# News

## Info

| News |
| --- |
| Articles | `src/News/<id>/article.md` |
| App | `NewsService.LoadLocalArticles` |
| Wiki | `wiki/Start Here/changelog.md`, written by `wiki/_static/build-docs.ps1` |
| Related | Wiki, [Where things are](where-things-are.md) |

A post is written once, under `src/News`. The Mod Manager shows that file in News. The docs build copies the same words onto the Changelog page.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Rebuild](#rebuild)
4. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `NewsService.LoadLocalArticles` | Reads each `src/News/<id>/article.md`. A `draft: true` article is left out of the list the app shows. | none |
| `Write-NewsChangelog` | Writes `wiki/Start Here/changelog.md` from those articles in one list. The page lead is the summary line only. Newest date first. The version in the title breaks a tie on the same day. A Shell post comes before a manager post with that same version. The newest post stays open. A Shell post with that same date stays open under it. Older posts sit in `:::details Past versions`, which the docs page draws closed until the reader opens it. Skips drafts. Drops a leading `hero.svg` image, because that file sits next to the article and the app shows it. A heading inside the article becomes `###`, because the docs renderer draws `##` and `###` and shows `####` as text. | none |

## Files

Each article is a folder. The folder name is the id, such as `2026-10-28-v0-9-2`. `article.md` has the title, date, excerpt, tags, and the post. Images in that folder are for the app.

## Rebuild

After a news article changes, rebuild the docs. That rewrites the Changelog page.

```
powershell -NoProfile -ExecutionPolicy Bypass -File "wiki/_static/build-docs.ps1"
```

## References

1. Wiki — how a docs page is built and how its own changelog is stored.
2. [Where things are](where-things-are.md) — the other folders this repo builds from.
