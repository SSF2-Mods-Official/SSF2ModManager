---
title: Portable Python and JRE
summary: Players never install Python or Java. Next2Flash and the manager use bundled copies only.
order: 20
group: Build
---

# Portable Python and JRE

## Info

| Portable Python and JRE |
| --- |
| Python | Next2Flash `runtime/resources/python/python.exe` (release), or `dist/python` after prepare |
| JRE | Next2Flash `runtime/resources/toolchain/jre` or `toolchain/jre`, else manager `runtime/jre` |
| Ensure | `Next2FlashSetup.EnsureAsync` then `PortableToolchain.EnsureAsync` |
| Related | [Where things are](where-things-are.md), [Launcher and shell](launcher-shell.md) |

Make Mods and Convert to FLA use portable Python and a portable JRE. A player does not install either from the web.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Check](#check)
4. [References](#references)

## How to use it

On Setup, **Download** Next2Flash. The GitHub zip already carries Python and the JRE. If either file is missing after download, the manager fetches a portable copy into the install. Source checkouts get the same treatment under `dist/python` and `toolchain/jre`.

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `Next2FlashSetup.EnsureAsync` | Downloads the win-x64 release into `{exe folder}\Next2Flash`, then calls `PortableToolchain.EnsureAsync`. An update copies only files whose size changed and writes `release-tag.txt`. A source checkout is left alone. A saved path that is not on disk is ignored. | `[n2f] update copied=<n> skipped=<n>` |
| `PortableToolchain.EnsureAsync` | If `python.exe` or `java.exe` is missing, downloads the embed Python and/or Temurin JRE into the N2F tree (or manager `runtime`). Never uses PATH. | `[portable] python ready …`, `[portable] jre ready …` |
| `Next2FlashSetup.PythonExe` | Release python, then prepared `dist/python`, then manager sidecar. Throws if none. | — |
| `AppPaths.JreJavaExe` / `JpexsCliService.ResolveJavaExecutable` | Bundled JRE only. No `where java` / PATH. | — |

## Check

On a PC with no Python or Java on PATH: Setup → Download Next2Flash → Convert to FLA or Compile a mod. `game.log` / debug log shows `[portable]` only when a fetch ran.

## References

1. [Where things are](where-things-are.md) — compile and Convert to FLA entry points.
2. [Launcher and shell](launcher-shell.md) — Setup window that downloads Next2Flash.
