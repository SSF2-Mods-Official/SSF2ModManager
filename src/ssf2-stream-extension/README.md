# SSF2 Stream (MJPEG)

Mirrors the live Adobe AIR game into a Cursor tab via a local MJPEG HTTP server.

- **F4** opens / reveals the SSF2 Stream tab
- **View ADL window** toggle shows the real game, or parks it off-screen + cloaked (full size; enable **Input** to control from the stream)
- **Input from stream** toggle forwards clicks/keys (off by default so the game is not focus-fought)
- **Restore window** brings the game back on-screen if it got stuck
- Stream uses `object-fit: contain` (aspect-preserving fit with letterboxing; input maps into the game area only)
- **Uncaught errors** are auto-dismissed by `ssf2_stream` / CSS preview and mirrored to **Output → SSF2 Game**. Use **SSF2: Copy Last Uncaught Error** (or enable `ssf2Stream.autoCopyUncaughtErrors`). On Launch Debug, `ssf2Stream.autoContinueOnUncaught` (default on) resumes the SWF debugger when an UNCAUGHT line appears.

## Install

```powershell
pip install Pillow
npm install
npm run compile
npx vsce package --no-dependencies --allow-missing-repository
cursor --install-extension .\ssf2-stream-0.4.1.vsix --force
```

Reload Cursor. Press **F4** or start with F5.
