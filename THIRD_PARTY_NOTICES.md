# Third-Party Notices

SSF2 Mod Manager includes the third-party software below. Each component belongs to its
authors and is distributed under its own license.

## ffmpeg

- Project: https://ffmpeg.org/
- Build: https://www.gyan.dev/ffmpeg/builds/ (essentials)
- License: GNU General Public License v3.0. See `ffmpeg/LICENSE` and `ffmpeg/README.txt` in the release.
- Source: https://github.com/FFmpeg/FFmpeg
- Fetched by `scripts/deps/fetch-native-tools.ps1` and run as a separate process for sound and music conversion.

## NuGet packages

| Package | License |
| --- | --- |
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) | Microsoft WebView2 SDK license |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT |
| [Markdig](https://github.com/xoofx/markdig) | BSD-2-Clause |
| [NLayer](https://github.com/naudio/NLayer) | MIT |

## Downloaded separately

[Next2Flash](https://github.com/SSF2-Mods-Official/Next2Flash) is downloaded by setup, not
bundled. Its release includes Python, a Java runtime, JPEXS Free Flash Decompiler and the
Flex/AIR SDK, each under its own license; see its repository.
