# Third-Party Notices

SSF2 Mod Manager bundles third-party software. Each component is the property of its
respective authors and is distributed under its own license.

## JPEXS Free Flash Decompiler (FFDec)

- Project: https://github.com/jindrapetrik/jpexs-decompiler
- License: GNU General Public License v3.0 (GPLv3)
- Bundled location: `src/ffdec/` (copied to the application output directory at build time)
- Full license text: `src/ffdec/license.txt`

FFDec is bundled and used **unmodified** as jars under `src/ffdec/`. SSF2 Mod Manager
invokes FFDec only as a separate process and does not link FFDec into its own binary.

Additionally, SSF2 Mod Manager ships a small GPLv3 **FFDec warm worker**
(`src/ffdec-worker/`, built jar `src/ffdec/ffdec-worker.jar`) that loads FFDec on
the classpath and runs successive CLI commands in one JVM. That worker is a
derivative work under GPLv3; its source is included in the repository. The upstream
FFDec source remains available at the project URL above.

## Bundled Java Runtime (Eclipse Temurin JRE)

- Project: https://adoptium.net/
- License: GNU General Public License, version 2, with the Classpath Exception
  (see `jre/legal/` after fetch)
- Bundled location: `src/jre/` (fetched by `scripts/deps/fetch-native-tools.ps1`, copied to output)
- Used only to launch the bundled FFDec JAR. Not linked into the Mod Manager process.

## Bundled ffmpeg

- Project: https://ffmpeg.org/
- Builds used: https://www.gyan.dev/ffmpeg/builds/ (essentials)
- License: LGPL/GPL depending on build configuration — see license files under `ffmpeg/`
  after fetch
- Bundled location: `src/ffmpeg/` (fetched by `scripts/deps/fetch-native-tools.ps1`, copied to output)
- Used as a separate process for FLA FLV→WAV and music trim / resample (44.1 kHz). FLA export does not use a PATH install.

## Other NuGet dependencies

See the project’s package references (Newtonsoft.Json, SharpCompress, Markdig) for their
respective licenses.
