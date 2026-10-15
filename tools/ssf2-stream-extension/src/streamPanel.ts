import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";
import * as os from "os";
import {
  launchSsf2Debug,
  launchSsf2Exe,
  closeSsf2Exe,
  isSsf2ExeActive,
  pickDebugFolder,
  resolveDebugFolder,
  resolveBuildRoot,
  getLaunchPrefsFromConfig,
  writeApplyCommand,
  ensureBootIntroAssetAsync,
  writeBootPrefs,
  allBootIntroPoolKeys,
  getBootRandomPoolKeys,
} from "./debugLaunch";
import {
  loadBestCatalog,
  writeCmd,
  waitForCatalogRefresh,
  catalogPath,
  sanitizeVersusStage,
  NON_STAGE_MODE_IDS,
  type Ssf2Catalog,
} from "./catalog";
import { StreamServer } from "./streamServer";
import { GameLogTail } from "./gameLogTail";
import { enterIdeFocusMode, exitIdeFocusMode } from "./ideFocusMode";
import { streamTabShowOptions } from "./streamTab";
import { BgmPreviewServer, extractMp3FromSwf } from "./bgmPreview";
import { applyManagerOverlay, patchManagerSettings, watchManagerSettings } from "./managerPrefs";
import { compileFolder, listCompileFolders } from "./compileFolder";
import { stateDir, ssf2mmDir } from "./ssf2mmPaths";

const DOM_KEY_TO_VK: Record<string, number> = {
  Backspace: 0x08,
  Tab: 0x09,
  Enter: 0x0d,
  Shift: 0x10,
  Control: 0x11,
  Alt: 0x12,
  Escape: 0x1b,
  " ": 0x20,
  Space: 0x20,
  ArrowLeft: 0x25,
  ArrowUp: 0x26,
  ArrowRight: 0x27,
  ArrowDown: 0x28,
  Delete: 0x2e,
  F1: 0x70,
  F2: 0x71,
  F3: 0x72,
  F4: 0x73,
  F5: 0x74,
  F6: 0x75,
  F7: 0x76,
  F8: 0x77,
  F9: 0x78,
  F10: 0x79,
  F11: 0x7a,
  F12: 0x7b,
};

function vkFromMessage(key: string, code: string): number | undefined {
  if (DOM_KEY_TO_VK[key] !== undefined) {
    return DOM_KEY_TO_VK[key];
  }
  if (/^F([1-9]|1[0-2])$/.test(code)) {
    return 0x70 + (parseInt(code.slice(1), 10) - 1);
  }
  if (/^Key[A-Z]$/.test(code)) {
    return code.charCodeAt(3);
  }
  if (/^Digit[0-9]$/.test(code)) {
    return code.charCodeAt(5);
  }
  if (key.length === 1) {
    const ch = key.toUpperCase();
    if ((ch >= "A" && ch <= "Z") || (ch >= "0" && ch <= "9")) {
      return ch.charCodeAt(0);
    }
  }
  return undefined;
}

function isSsf2Session(session: vscode.DebugSession): boolean {
  if (session.type === "swf") {
    return true;
  }
  const n = session.name.toLowerCase();
  return n.includes("ssf2") || n.includes("launch swf") || n.includes("shell");
}

function conf() {
  return vscode.workspace.getConfiguration("ssf2Stream");
}

export class StreamPanel {
  private panel: vscode.WebviewPanel;
  private statusBar: vscode.StatusBarItem;
  private disposed = false;
  private ignoreExit = false;
  private inputForward = false;
  private viewAdl = true;
  private streaming = false;
  private playFocus = false;
  private hideIde = false;
  private matchSettingsOpen = false;
  private applyDebounceUntil = 0;
  /** True only when we sent pauseMatch for an overlay and still own that pause. */
  private overlayPausedMatch = false;
  private overlayPauseDepth = 0;
  private bgmAudioCacheDir: string | null = null;
  /** True while streaming a game we launched (Debug/Exe). External exe must not be parked. */
  private streamOwned = false;
  private readonly bgmServer = new BgmPreviewServer();
  private settingsWriteTimer: ReturnType<typeof setTimeout> | null = null;
  private cursorPollTimer: ReturnType<typeof setInterval> | null = null;
  private pendingSettings = new Map<string, unknown>();
  private readonly gameLog = new GameLogTail();
  private catalog: Ssf2Catalog;
  private lastAutoContinueMs = 0;
  private autoContinuePausedUntilMs = 0;
  private lastFloodWarnMs = 0;
  /** After error flood, block auto Focus re-arm (Apply/overlay hide, hwnd). */
  private focusFloodCooldownUntilMs = 0;
  private managerWatch: { dispose: () => void } | undefined;

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly server: StreamServer,
    private readonly onDispose: () => void
  ) {
    const c = conf();
    this.inputForward = c.get<boolean>("inputForwarding", false);
    this.viewAdl = c.get<boolean>("viewAdlWindow", true);
    this.hideIde = c.get<boolean>("hideIdeChrome", false);
    this.playFocus = false; // session-active; preference lives in settings
    this.catalog = this.loadCatalog();

    const bgmRoot = vscode.Uri.joinPath(this.context.globalStorageUri, "bgm-preview");
    try {
      fs.mkdirSync(bgmRoot.fsPath, { recursive: true });
    } catch {
      /* ignore */
    }
    const tab = streamTabShowOptions();
    this.panel = vscode.window.createWebviewPanel(
      "ssf2Stream",
      "SSF2 Stream",
      { viewColumn: tab.viewColumn as vscode.ViewColumn, preserveFocus: tab.preserveFocus },
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        localResourceRoots: [
          vscode.Uri.file(this.context.extensionPath),
          bgmRoot,
          vscode.Uri.file(path.join(os.tmpdir(), "ssf2-stream-bgm")),
        ],
      }
    );
    this.statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    this.statusBar.text = "$(debug-alt) SSF2: ready";
    this.statusBar.show();

    this.panel.webview.html = this.html();
    // Push remembered toggles after the webview loads.
    setTimeout(() => {
      void this.panel.webview.postMessage({
        type: "toggles",
        inputForward: this.inputForward,
        viewAdl: this.viewAdl,
        playFocus: this.playFocus,
        playFocusPref: conf().get<boolean>("playFocus", false),
        gameLive: this.streaming,
        hideIde: this.hideIde,
      });
      this.pushLaunchPrefs();
      this.pushCompileFolders();
      writeBootPrefs(resolveBuildRoot() || undefined);
      if (this.hideIde) {
        void this.syncIdeChrome();
      }
    }, 80);
    this.panel.onDidDispose(() => {
      const stop = conf().get<boolean>("closePanelStopsDebug", true);
      this.dispose({ stopDebug: stop });
    });
    this.panel.onDidChangeViewState(() => {
      if (!this.panel.active && this.playFocus) {
        void this.setPlayFocus(false);
      }
    });
    this.panel.webview.onDidReceiveMessage((msg) => void this.onMessage(msg));
    this.gameLog.setOnUncaught((blob) => this.onUncaughtFromLog(blob));
    this.gameLog.setOnFlood(() => this.onLogFlood());
    this.managerWatch = watchManagerSettings(() => {
      if (!this.disposed) {
        this.pushLaunchPrefs();
      }
    });

    this.server.on("ready", (port: number) => {
      this.statusBar.text = `$(device-camera) SSF2: :${port}`;
      void this.panel.webview.postMessage({
        type: "stream",
        url: `http://127.0.0.1:${port}/stream.mjpg`,
      });
      void this.syncConfig();
      void this.panel.webview.postMessage({
        type: "toggles",
        inputForward: this.inputForward,
        viewAdl: this.viewAdl,
        playFocus: this.playFocus,
      });
    });
    this.server.on("hwnd", () => {
      this.statusBar.text = "$(device-camera) SSF2: streaming";
      void this.panel.webview.postMessage({ type: "status", text: "" });
      void this.syncConfig();
      void this.panel.webview.postMessage({ type: "gameLive", live: true });
      this.startCursorPoll();
      if (
        Date.now() >= this.focusFloodCooldownUntilMs &&
        (conf().get<boolean>("playFocus", false) || !this.viewAdl)
      ) {
        void this.setPlayFocus(true, { persist: false });
      }
    });
    this.server.on("waiting", () => {
      this.statusBar.text = "$(sync~spin) SSF2: waiting for game…";
      void this.panel.webview.postMessage({
        type: "status",
        text: "Waiting for Super Smash Flash 2 / adl…",
      });
    });
    this.server.on("bounds", (w: number, h: number) => {
      this.statusBar.text = `$(device-camera) SSF2: ${w}×${h}`;
    });
    this.server.on("error", (err: Error) => {
      if (/window closed|not found/i.test(err.message)) {
        this.showLaunchScreen(err.message);
        return;
      }
      void vscode.window.showWarningMessage(`SSF2 stream: ${err.message}`);
    });
    this.server.on("exit", () => {
      if (this.disposed || this.ignoreExit) {
        return;
      }
      this.showLaunchScreen("Stream server stopped");
    });

    this.pushFolderLabel();
  }

  showLaunchScreen(reason?: string): void {
    this.streaming = false;
    this.streamOwned = false;
    this.matchSettingsOpen = false;
    this.stopCursorPoll();
    this.onGameClosed();
    void this.setPlayFocus(false, { persist: false });
    void this.panel.webview.postMessage({ type: "gameLive", live: false });
    void this.panel.webview.postMessage({ type: "cursorHand", hand: false });
    void this.panel.webview.postMessage({ type: "matchSettings", open: false });
    this.ignoreExit = true;
    this.server.stop();
    this.statusBar.text = "$(debug-alt) SSF2: ready";
    void this.panel.webview.postMessage({
      type: "idle",
      reason: reason || "",
      folder: resolveDebugFolder() || "(not set — open Settings)",
    });
    setTimeout(() => {
      this.ignoreExit = false;
    }, 500);
  }

  private startCursorPoll(): void {
    this.stopCursorPoll();
    this.cursorPollTimer = setInterval(() => {
      if (!this.streaming || !this.inputForward || this.disposed) {
        return;
      }
      void this.server.fetchCursorHand().then((hand) => {
        void this.panel.webview.postMessage({ type: "cursorHand", hand });
      });
    }, 80);
  }

  private stopCursorPoll(): void {
    if (this.cursorPollTimer) {
      clearInterval(this.cursorPollTimer);
      this.cursorPollTimer = null;
    }
  }

  showSettingsOverlay(): void {
    const c = conf();
    void this.panel.webview.postMessage({
      type: "settings",
      values: {
        debugFolder: resolveDebugFolder() || c.get<string>("debugFolder") || "",
        launchOnVscStartup: c.get<boolean>("launchOnVscStartup", true),
        autoOpenOnDebug: c.get<boolean>("autoOpenOnDebug", true),
        focusDebugConsole: c.get<boolean>("focusDebugConsole", true),
        closePanelStopsDebug: c.get<boolean>("closePanelStopsDebug", true),
        viewAdlWindow: c.get<boolean>("viewAdlWindow", true),
        hideIdeChrome: c.get<boolean>("hideIdeChrome", false),
        pauseMusicOnLaunch: c.get<boolean>("pauseMusicOnLaunch", true),
        bootIntro: c.get<string>("bootIntro", "default"),
        bootCustomIntros: c.get<Array<{ id: string; label: string; path: string }>>(
          "bootCustomIntros",
          []
        ) || [],
        bootRandomPool: c.get<string[]>("bootRandomPool", []) || [],
        inputForwarding: c.get<boolean>("inputForwarding", false),
        startingMenu: c.get<string>("startingMenu", "home"),
        loadMatchImmediate: c.get<boolean>("loadMatchImmediate", false),
        matchStage: sanitizeVersusStage(c.get<string>("matchStage", "battlefield") || "battlefield"),
        matchP1: c.get<string>("matchP1", "mario"),
        matchP2: c.get<string>("matchP2", "fox"),
        matchP3: c.get<string>("matchP3", ""),
        matchP4: c.get<string>("matchP4", ""),
        fps: c.get<number>("fps", 30),
        jpegQuality: c.get<number>("jpegQuality", 90),
        maxStreamLongEdge: c.get<number>("maxStreamLongEdge", 1280),
      },
      catalog: this.catalog,
    });
  }

  startStreaming(restart: boolean, opts?: { owned?: boolean }): void {
    if (opts?.owned) {
      this.streamOwned = true;
    }
    this.streaming = true;
    void this.panel.webview.postMessage({ type: "status", text: "Starting stream…" });
    if (restart) {
      this.ignoreExit = true;
      this.server.restart();
      setTimeout(() => {
        this.ignoreExit = false;
      }, 2000);
    } else {
      this.server.start();
    }
  }

  requestRestoreWindow(): void {
    void this.server.postInput({ type: "restore" });
  }

  private pushCompileFolders(): void {
    const shell = resolveBuildRoot();
    const folders = shell ? listCompileFolders(shell) : [];
    void this.panel.webview.postMessage({ type: "compileFolders", folders });
  }

  private async runCompileFolder(id: string): Promise<void> {
    const shell = resolveBuildRoot();
    if (!shell) {
      void this.panel.webview.postMessage({ type: "compileResult", text: "Choose the SSF2 Shell folder first." });
      return;
    }
    try {
      const swf = await compileFolder(shell, id);
      void this.panel.webview.postMessage({ type: "compileResult", text: "Compiled " + swf });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      void this.panel.webview.postMessage({ type: "compileResult", text: message });
    }
  }

  private loadCatalog(): Ssf2Catalog {
    return loadBestCatalog(this.context.extensionPath, resolveBuildRoot());
  }

  private pushLaunchPrefs(): void {
    const c = conf();
    const rawStage = c.get<string>("matchStage", "battlefield") || "battlefield";
    const matchStage = sanitizeVersusStage(rawStage);
    if (rawStage !== matchStage && NON_STAGE_MODE_IDS.has(rawStage)) {
      void c.update("matchStage", matchStage, vscode.ConfigurationTarget.Global);
    }
    const values: Record<string, unknown> = {
        startingMenu: c.get<string>("startingMenu", "home"),
        loadMatchImmediate: c.get<boolean>("loadMatchImmediate", false),
        stayInMatchSetupOnApply: c.get<boolean>("stayInMatchSetupOnApply", false),
        matchStage,
        matchMusic: c.get<string>("matchMusic", ""),
        matchCustomModeId: c.get<string>("matchCustomModeId", ""),
        matchDifficulty: c.get<string>("matchDifficulty", "NORMAL"),
        matchScoreLimit: c.get<number>("matchScoreLimit", 5),
        matchSpecialModes: c.get<number>("matchSpecialModes", 0),
        matchP1: c.get<string>("matchP1", "mario"),
        matchP2: c.get<string>("matchP2", "fox"),
        matchP3: c.get<string>("matchP3", ""),
        matchP4: c.get<string>("matchP4", ""),
        matchP1Human: c.get<boolean>("matchP1Human", true),
        matchP2Human: c.get<boolean>("matchP2Human", true),
        matchP3Human: c.get<boolean>("matchP3Human", false),
        matchP4Human: c.get<boolean>("matchP4Human", false),
        matchP1Level: c.get<number>("matchP1Level", 1),
        matchP2Level: c.get<number>("matchP2Level", 3),
        matchP3Level: c.get<number>("matchP3Level", 3),
        matchP4Level: c.get<number>("matchP4Level", 3),
        matchP1Team: c.get<number>("matchP1Team", 1),
        matchP2Team: c.get<number>("matchP2Team", 3),
        matchP3Team: c.get<number>("matchP3Team", 1),
        matchP4Team: c.get<number>("matchP4Team", 3),
        matchP1Tag: c.get<string>("matchP1Tag", ""),
        matchP2Tag: c.get<string>("matchP2Tag", ""),
        matchP3Tag: c.get<string>("matchP3Tag", ""),
        matchP4Tag: c.get<string>("matchP4Tag", ""),
        playerTags: c.get<string[]>("playerTags", []) || [],
        matchHazards: c.get<boolean>("matchHazards", true),
        matchUsingTime: c.get<boolean>("matchUsingTime", true),
        matchUsingLives: c.get<boolean>("matchUsingLives", true),
        matchTime: c.get<number>("matchTime", 8),
        matchLives: c.get<number>("matchLives", 3),
        bootIntro: c.get<string>("bootIntro", "default"),
        bootCustomIntros: c.get<Array<{ id: string; label: string; path: string }>>(
          "bootCustomIntros",
          []
        ) || [],
        bootRandomPool: c.get<string[]>("bootRandomPool", []) || [],
        hideIde: c.get<boolean>("hideIdeChrome", false),
        superDebug: c.get<boolean>("superDebug", false),
        vaultAction: c.get<string>("vaultAction", "intro"),
        vaultIntro: c.get<string>("vaultIntro", "intro3"),
        vaultPaletteChar: c.get<string>("vaultPaletteChar", "mario"),
        playFocus: c.get<boolean>("playFocus", false),
        sandbagOverride: c.get<boolean>("sandbagOverride", false),
        sandbagCharacter: c.get<string>("sandbagCharacter", "mario"),
        eventId: c.get<string>("eventId", ""),
        eventOverride: c.get<boolean>("eventOverride", false),
        eventCharacter: c.get<string>("eventCharacter", "mario"),
        useMusicOnLaunch: c.get<boolean>("useMusicOnLaunch", false),
        useMusicEverywhere: c.get<boolean>("useMusicEverywhere", false),
    };
    applyManagerOverlay(values);
    void this.panel.webview.postMessage({
      type: "launchPrefs",
      values,
      catalog: this.catalog,
      gameLive: this.streaming,
      matchSettings: this.matchSettingsOpen,
    });
  }

  private async refreshCatalogFromGame(): Promise<void> {
    const build = resolveBuildRoot();
    if (!build) {
      void vscode.window.showWarningMessage("SSF2: set Debug Folder first.");
      return;
    }
    if (this.streaming) {
      const live = catalogPath(build);
      let prior = 0;
      try {
        if (live && fs.existsSync(live)) {
          prior = fs.statSync(live).mtimeMs;
        }
      } catch {
        /* ignore */
      }
      writeCmd(build, { op: "refreshCatalog" });
      await waitForCatalogRefresh(build, prior, 4500);
    }
    this.catalog = loadBestCatalog(this.context.extensionPath, build);
    this.pushLaunchPrefs();
    const src = this.catalog.source || "?";
    void vscode.window.showInformationMessage(
      `SSF2 catalog refreshed (${src}: ${this.catalog.characters.length} chars, ${this.catalog.stages.length} stages).`
    );
  }

  private async persistViewAdl(on: boolean): Promise<void> {
    await conf().update("viewAdlWindow", on, vscode.ConfigurationTarget.Global);
    const inspected = conf().inspect<boolean>("viewAdlWindow");
    if (inspected?.workspaceValue !== undefined) {
      await conf().update("viewAdlWindow", on, vscode.ConfigurationTarget.Workspace);
    }
    if (inspected?.workspaceFolderValue !== undefined) {
      await conf().update("viewAdlWindow", on, vscode.ConfigurationTarget.WorkspaceFolder);
    }
  }

  private async flushPendingSettings(): Promise<void> {
    this.settingsWriteTimer = null;
    const entries = [...this.pendingSettings.entries()];
    this.pendingSettings.clear();
    let touchBoot = false;
    for (const [name, value] of entries) {
      try {
        await conf().update(name, value, vscode.ConfigurationTarget.Global);
        if (name === "bootIntro" || name === "bootCustomIntros" || name === "bootRandomPool") {
          touchBoot = true;
        }
      } catch {
        /* ignore */
      }
    }
    if (touchBoot) {
      writeBootPrefs(resolveBuildRoot() || undefined);
    }
    patchManagerSettings(entries);
  }

  private requestOverlayPause(): void {
    if (!this.streaming) {
      return;
    }
    const build = resolveBuildRoot();
    if (!build) {
      return;
    }
    this.overlayPauseDepth += 1;
    if (this.overlayPauseDepth === 1) {
      writeCmd(build, { op: "pauseMatch" });
      this.overlayPausedMatch = true;
    }
  }

  private releaseOverlayPause(): void {
    if (this.overlayPauseDepth > 0) {
      this.overlayPauseDepth -= 1;
    }
    if (this.overlayPauseDepth === 0 && this.overlayPausedMatch) {
      const build = resolveBuildRoot();
      if (build && this.streaming) {
        writeCmd(build, { op: "resumeMatch" });
      }
      this.overlayPausedMatch = false;
    }
  }

  private showMatchSettingsOverlay(): void {
    this.matchSettingsOpen = true;
    void this.setPlayFocus(false, { persist: false });
    this.requestOverlayPause();
    this.pushLaunchPrefs();
    void this.panel.webview.postMessage({
      type: "matchSettings",
      open: true,
      streamUrl: this.server.streamUrl || "",
      gameLive: this.streaming,
    });
  }

  private hideMatchSettingsOverlay(): void {
    this.matchSettingsOpen = false;
    void this.panel.webview.postMessage({ type: "matchSettings", open: false });
    this.releaseOverlayPause();
    if (
      this.streaming &&
      conf().get<boolean>("playFocus", false) &&
      Date.now() >= this.focusFloodCooldownUntilMs
    ) {
      void this.setPlayFocus(true, { persist: false });
    }
  }

  private async syncIdeChrome(): Promise<void> {
    if (this.hideIde) {
      await enterIdeFocusMode(() => this.reveal(), { panel: "debugConsole" });
    } else {
      await exitIdeFocusMode();
    }
  }

  private applyMatchToLiveGame(opts?: { gotoMenu?: boolean }): void {
    const now = Date.now();
    if (now < this.applyDebounceUntil) {
      void vscode.window.showWarningMessage("SSF2: Apply already sent — wait a moment.");
      return;
    }
    const build = resolveBuildRoot();
    if (!build) {
      void vscode.window.showWarningMessage("SSF2: set Debug Folder first.");
      return;
    }
    if (!this.streaming) {
      void vscode.window.showWarningMessage("SSF2: game is not live — use Launch instead.");
      return;
    }
    // Flush debounced prefs so Apply sees the latest dropdown values.
    void this.flushPendingSettings().then(() => {
      this.applyDebounceUntil = Date.now() + 2000;
      // Apply tears down/rebuilds match — drop overlay pause ownership.
      this.overlayPauseDepth = 0;
      this.overlayPausedMatch = false;
      writeApplyCommand(build, getLaunchPrefsFromConfig(), opts);
      const stayInMatchSetup = conf().get<boolean>("stayInMatchSetupOnApply", false);
      if (!stayInMatchSetup) {
        this.hideMatchSettingsOverlay();
      }
      void vscode.window.showInformationMessage(
        opts?.gotoMenu ? "SSF2: goto menu sent." : "SSF2: apply sent to running game."
      );
    });
  }

  private previewMusic(): void {
    const build = resolveBuildRoot();
    const id = conf().get<string>("matchMusic", "") || "";
    if (!build || !id) {
      void vscode.window.showWarningMessage("SSF2: pick a music track first.");
      return;
    }
    if (this.streaming) {
      // Feed into the live game (SoundQueue). Use musicId — writeCmd's cmdId is separate.
      writeCmd(build, { op: "previewMusic", musicId: id, id });
      return;
    }
    void this.previewMusicInPanel(build, id);
  }

  private stopMusic(): void {
    const build = resolveBuildRoot();
    if (this.streaming && build) {
      writeCmd(build, { op: "stopMusic" });
    }
    void this.panel.webview.postMessage({ type: "bgmAudio", action: "stop" });
  }

  private pauseMusic(): void {
    // Panel preview toggle; live game uses Stop (SoundQueue has no clean pause API).
    void this.panel.webview.postMessage({ type: "bgmAudio", action: "pause" });
  }

  /** Extract MP3 from bgm SWF and play inside the stream panel (no ADL window). */
  private async previewMusicInPanel(buildRoot: string, id: string): Promise<void> {
    const swfPath = path.join(buildRoot, "data", "sound", `${id}.swf`);
    if (!fs.existsSync(swfPath)) {
      void vscode.window.showWarningMessage(`SSF2: missing ${id}.swf under data/sound.`);
      return;
    }
    const cacheDir = path.join(this.context.globalStorageUri.fsPath, "bgm-preview");
    fs.mkdirSync(cacheDir, { recursive: true });
    this.bgmAudioCacheDir = cacheDir;
    const outPath = path.join(cacheDir, `${id}.mp3`);
    const swfStat = fs.statSync(swfPath);
    let needExtract = true;
    try {
      if (fs.existsSync(outPath) && fs.statSync(outPath).mtimeMs >= swfStat.mtimeMs) {
        needExtract = false;
      }
    } catch {
      needExtract = true;
    }
    if (needExtract) {
      void this.panel.webview.postMessage({
        type: "bgmAudio",
        action: "status",
        text: "Extracting " + id + "…",
      });
      try {
        extractMp3FromSwf(swfPath, outPath);
      } catch (e) {
        const msg = e instanceof Error ? e.message : String(e);
        void this.panel.webview.postMessage({
          type: "bgmAudio",
          action: "status",
          text: "Extract failed",
        });
        void vscode.window.showWarningMessage(`SSF2: could not extract ${id} (${msg}).`);
        return;
      }
    }
    try {
      const port = await this.bgmServer.start(cacheDir);
      const url = `http://127.0.0.1:${port}/${encodeURIComponent(id)}.mp3?t=${Date.now()}`;
      void this.panel.webview.postMessage({
        type: "bgmAudio",
        action: "play",
        url,
        id,
      });
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      void vscode.window.showWarningMessage(`SSF2: preview server failed (${msg}).`);
    }
  }

  private showDebugOverlay(): void {
    void this.setPlayFocus(false, { persist: false });
    this.requestOverlayPause();
    void this.panel.webview.postMessage({
      type: "debugOverlay",
      open: true,
      catalog: this.catalog,
      gameLive: this.streaming,
    });
  }

  private hideDebugOverlay(): void {
    void this.panel.webview.postMessage({ type: "debugOverlay", open: false });
    this.releaseOverlayPause();
    if (
      this.streaming &&
      conf().get<boolean>("playFocus", false) &&
      Date.now() >= this.focusFloodCooldownUntilMs
    ) {
      void this.setPlayFocus(true, { persist: false });
    }
  }

  private debugGenerate(kind: string, id: number, rare: boolean): void {
    const build = resolveBuildRoot();
    if (!build || !this.streaming) {
      void vscode.window.showWarningMessage("SSF2: Debug generate needs a live match.");
      return;
    }
    writeCmd(build, { op: "generate", kind, id, rare });
  }

  async runLaunchDebug(): Promise<void> {
    await this.flushPendingSettings();
    void this.panel.webview.postMessage({ type: "status", text: "Starting super debug…" });
    const ok = await launchSsf2Debug();
    if (!ok) {
      return;
    }
    this.streamOwned = true;
    await this.afterGameLaunch({ logTarget: "debugConsole" });
    this.gameLog.appendLine(
      "[ssf2-stream] Super debug — uncaught errors → Output: SSF2 Game (and Debug Console). Use “SSF2: Copy Last Uncaught Error” to copy."
    );
    setTimeout(() => this.startStreaming(true), 800);
  }

  async runLaunchExe(): Promise<void> {
    await this.flushPendingSettings();
    const superDebug = conf().get<boolean>("superDebug", false);
    if (superDebug) {
      void this.panel.webview.postMessage({ type: "status", text: "Starting super debug…" });
      const ok = await launchSsf2Debug();
      if (!ok) {
        return;
      }
      this.streamOwned = true;
      this.ensureGameLogWatch({ show: true, fromStart: false });
      this.gameLog.appendLine(
        "[ssf2-stream] Launch SSF2 (super debug) — full traces → Debug Console + SSF2 Game"
      );
      await this.afterGameLaunch({ logTarget: "ssf2Game" });
      setTimeout(() => this.startStreaming(true), 1200);
      return;
    }
    void this.panel.webview.postMessage({ type: "status", text: "Launching SSF2…" });
    const res = await launchSsf2Exe();
    if (!res.ok) {
      return;
    }
    this.streamOwned = true;
    const paths = [res.logPath, res.tracePath].filter(Boolean) as string[];
    this.gameLog.start(paths, {
      fromStart: false,
      show: true,
      tracePath: res.tracePath,
    });
    this.gameLog.appendLine(
      `[ssf2-stream] Launch SSF2 via ${res.via || "?"} — traces → Output: SSF2 Game (Copy Last Uncaught Error available)`
    );
    if (res.child) {
      this.gameLog.attachProcess(res.child, res.via === "adl" ? "adl" : "exe");
    }
    this.gameLog.show(false);
    this.setExeActiveUi(true);
    await this.afterGameLaunch({ skipLogStart: true, logTarget: "ssf2Game" });
    setTimeout(() => this.startStreaming(true), 1200);
  }

  closeExe(): void {
    closeSsf2Exe();
    this.setExeActiveUi(false);
    this.onGameClosed();
    this.showLaunchScreen("SSF2 closed");
  }

  /** Kill current Launch SSF2 process and start again with saved launch prefs. */
  async restartExe(): Promise<void> {
    void this.panel.webview.postMessage({ type: "status", text: "Restarting SSF2…" });
    this.gameLog.appendLine("[ssf2-stream] Restart SSF2 — closing…");
    closeSsf2Exe();
    this.setExeActiveUi(false);
    this.onGameClosed();
    // Brief pause so the process/HWND can release before relaunch.
    await new Promise((r) => setTimeout(r, 600));
    await this.runLaunchExe();
  }

  private setExeActiveUi(active: boolean): void {
    void this.panel.webview.postMessage({ type: "exeActive", active: !!active });
  }

  private async afterGameLaunch(opts?: {
    skipLogStart?: boolean;
    logTarget?: "debugConsole" | "ssf2Game";
  }): Promise<void> {
    if (conf().get<boolean>("pauseMusicOnLaunch", true)) {
      this.pauseMusic();
      this.stopMusic();
    }
    if (!opts?.skipLogStart) {
      this.ensureGameLogWatch({ show: opts?.logTarget === "ssf2Game" });
    }
    this.hideIde = conf().get<boolean>("hideIdeChrome", false);
    if (this.hideIde) {
      await enterIdeFocusMode(() => this.reveal(), {
        panel: opts?.logTarget === "ssf2Game" ? "ssf2Game" : "debugConsole",
      });
    } else if (opts?.logTarget === "ssf2Game") {
      this.gameLog.show(false);
    }
  }

  /** Start/restart tailing game.log + air_trace (Launch Debug / Launch SSF2 / F5). */
  ensureGameLogWatch(opts?: { show?: boolean; fromStart?: boolean }): void {
    const build = resolveBuildRoot();
    if (!build) {
      return;
    }
    const logPath = path.join(ssf2mmDir(build), "game.log");
    const tracePath = path.join(stateDir(build), "air_trace.txt");
    this.gameLog.start([logPath, tracePath], {
      // Default: only new bytes (capped tail if fromStart explicitly true).
      fromStart: opts?.fromStart === true,
      show: !!opts?.show,
      tracePath,
    });
  }

  private onLogFlood(): void {
    const now = Date.now();
    this.autoContinuePausedUntilMs = now + 15_000;
    this.focusFloodCooldownUntilMs = now + 15_000;
    if (this.playFocus) {
      void this.setPlayFocus(false, { persist: false });
    }
    if (now - this.lastFloodWarnMs > 8000) {
      this.lastFloodWarnMs = now;
      this.gameLog.appendLine(
        "[ssf2-stream] error flood — Focus off, auto-continue paused 15s"
      );
      void vscode.window.showWarningMessage(
        "SSF2: error flood detected — Focus disabled and auto-continue paused."
      );
    }
  }

  private onUncaughtFromLog(_blob: string): void {
    this.maybeAutoContinueOnUncaught();
    // Prefer last_error.txt (full stack written by the mod) for auto-copy.
    if (!conf().get<boolean>("autoCopyUncaughtErrors", false)) {
      return;
    }
    if (Date.now() < this.autoContinuePausedUntilMs) {
      return;
    }
    setTimeout(() => {
      void this.copyLastUncaughtError({ quiet: true });
    }, 200);
  }

  /** Resume SWF debugger when it pauses on uncaught errors Stream/CSS already dismissed. */
  private maybeAutoContinueOnUncaught(): void {
    if (!conf().get<boolean>("autoContinueOnUncaught", true)) {
      return;
    }
    if (Date.now() < this.autoContinuePausedUntilMs) {
      return;
    }
    const s = vscode.debug.activeDebugSession;
    if (!s) {
      return;
    }
    const isSwf =
      s.type === "swf" ||
      /ssf2|launch swf|shell|no compile/i.test(s.name || "");
    if (!isSwf) {
      return;
    }
    const now = Date.now();
    if (now - this.lastAutoContinueMs < 120) {
      return;
    }
    this.lastAutoContinueMs = now;
    setTimeout(() => {
      void vscode.commands.executeCommand("workbench.action.debug.continue");
    }, 50);
  }

  /**
   * Copy data/ssf2mm/state/last_error.txt (or last tailed UNCAUGHT blob) to clipboard.
   */
  async copyLastUncaughtError(opts?: { quiet?: boolean }): Promise<void> {
    let text = "";
    const fromTail = this.gameLog.getLastErrorPath();
    const candidates = [fromTail, this.resolveLastErrorPath()].filter(Boolean) as string[];
    for (const p of candidates) {
      try {
        if (fs.existsSync(p)) {
          text = fs.readFileSync(p, "utf8").trim();
          if (text) {
            break;
          }
        }
      } catch {
        /* try next */
      }
    }
    if (!text) {
      text = (this.gameLog.getLastUncaughtBlob() || "").trim();
    }
    if (!text) {
      if (!opts?.quiet) {
        void vscode.window.showWarningMessage(
          "SSF2: no uncaught error captured yet (need ssf2_stream 1.1.22+ and a live session)."
        );
      }
      return;
    }
    await vscode.env.clipboard.writeText(text);
    if (!opts?.quiet) {
      void vscode.window.showInformationMessage("SSF2: last uncaught error copied to clipboard.");
    } else {
      this.gameLog.appendLine("[ssf2-stream] Auto-copied last uncaught error to clipboard");
    }
  }

  private resolveLastErrorPath(): string | undefined {
    const build = resolveBuildRoot();
    if (!build) {
      return undefined;
    }
    return path.join(stateDir(build), "last_error.txt");
  }

  onGameClosed(): void {
    this.gameLog.stop();
    void exitIdeFocusMode();
    if (!isSsf2ExeActive()) {
      this.setExeActiveUi(false);
    }
  }

  /** Reveal Output → SSF2 Game (used by Hide IDE for Launch SSF2). */
  focusGameOutput(): void {
    this.gameLog.show(false);
  }

  appendGameLogLine(line: string): void {
    this.gameLog.appendLine(line);
  }

  get isPlayFocus(): boolean {
    return this.playFocus;
  }

  get isPanelActive(): boolean {
    return this.panel.active;
  }

  /** Notify webview of Esc keydown/keyup from IDE keybindings (hold-to-unfocus). */
  notifyEscape(down: boolean): void {
    void this.panel.webview.postMessage({ type: "escapeHold", down });
  }

  async setPlayFocus(on: boolean, opts?: { persist?: boolean }): Promise<void> {
    if (on && !this.streaming) {
      // Can't focus without a live game; keep preference if persisting.
      if (opts?.persist !== false) {
        await conf().update("playFocus", true, vscode.ConfigurationTarget.Global);
      }
      void this.panel.webview.postMessage({
        type: "toggles",
        inputForward: this.inputForward,
        viewAdl: this.viewAdl,
        playFocus: false,
        playFocusPref: conf().get<boolean>("playFocus", false),
        gameLive: false,
      });
      return;
    }
    this.playFocus = on;
    await vscode.commands.executeCommand("setContext", "ssf2Stream.playFocus", on);
    if (opts?.persist !== false) {
      await conf().update("playFocus", on, vscode.ConfigurationTarget.Global);
    }
    if (on) {
      this.inputForward = true;
      await conf().update("inputForwarding", true, vscode.ConfigurationTarget.Global);
      this.panel.reveal(undefined, false);
      this.statusBar.text = "$(target) SSF2: play focus — hold Esc 2s to release";
    } else if (this.streaming) {
      this.statusBar.text = "$(device-camera) SSF2: streaming";
    } else {
      this.statusBar.text = "$(debug-alt) SSF2: ready";
    }
    void this.panel.webview.postMessage({
      type: "toggles",
      inputForward: this.inputForward,
      viewAdl: this.viewAdl,
      playFocus: this.playFocus,
      playFocusPref: conf().get<boolean>("playFocus", false),
      gameLive: this.streaming,
    });
    if (this.streaming) {
      await this.syncConfig();
    }
    if (on) {
      void this.panel.webview.postMessage({ type: "focusPlay" });
    }
  }

  async tapGameVk(vk: number): Promise<void> {
    if (!this.playFocus || !this.streaming) {
      return;
    }
    if (!this.inputForward) {
      this.inputForward = true;
      await this.syncConfig();
    }
    await this.server.postInput({ type: "key", vk, down: true });
    await this.server.postInput({ type: "key", vk, down: false });
  }

  reveal(preserveFocus?: boolean): void {
    const tab = streamTabShowOptions();
    this.panel.reveal(tab.viewColumn as vscode.ViewColumn, preserveFocus ?? tab.preserveFocus);
  }

  dispose(options?: { stopDebug?: boolean }): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    this.managerWatch?.dispose();
    this.managerWatch = undefined;
    this.stopCursorPoll();
    void vscode.commands.executeCommand("setContext", "ssf2Stream.playFocus", false);
    this.playFocus = false;
    this.bgmServer.stop();
    this.gameLog.dispose();
    void exitIdeFocusMode();
    this.server.dispose();
    this.statusBar.dispose();
    if (options?.stopDebug !== false) {
      const session = vscode.debug.activeDebugSession;
      if (session && isSsf2Session(session)) {
        void vscode.debug.stopDebugging(session);
      }
    }
    try {
      this.panel.dispose();
    } catch {
      /* ignore */
    }
    this.onDispose();
  }

  private pushFolderLabel(): void {
    void this.panel.webview.postMessage({
      type: "folder",
      folder: resolveDebugFolder() || "(not set — open Settings)",
    });
  }

  private async syncConfig(): Promise<void> {
    const build = resolveBuildRoot();
    const bridgeDir = build ? stateDir(build) : "";
    await this.server.postInput({
      type: "config",
      inputForward: this.inputForward,
      viewAdl: this.viewAdl,
      keepActive: this.playFocus,
      owned: this.streamOwned,
      bridgeDir: bridgeDir || undefined,
    });
  }

  private async onMessage(msg: {
    type: string;
    key?: string;
    code?: string;
    nx?: number;
    ny?: number;
    button?: number;
    value?: boolean | number | string;
    id?: string;
    setting?: string;
    slot?: number;
  }): Promise<void> {
    switch (msg.type) {
      case "openSettings":
        this.showSettingsOverlay();
        break;
      case "openMatchSettings":
        this.showMatchSettingsOverlay();
        break;
      case "closeMatchSettings":
        this.hideMatchSettingsOverlay();
        break;
      case "applyMatch":
        this.applyMatchToLiveGame();
        break;
      case "gotoMenu":
        this.applyMatchToLiveGame({ gotoMenu: true });
        break;
      case "openDebug":
        this.showDebugOverlay();
        break;
      case "closeDebug":
        this.hideDebugOverlay();
        break;
      case "debugGenerate":
        this.debugGenerate(
          String(msg.value || "item"),
          Number(msg.ny ?? 0),
          !!msg.button
        );
        break;
      case "debugPause":
        {
          const build = resolveBuildRoot();
          if (build) {
            writeCmd(build, { op: msg.value ? "pauseMatch" : "resumeMatch" });
            // Manual pause/resume — stop auto-resume on overlay close.
            this.overlayPausedMatch = false;
          }
        }
        break;
      case "debugMusic":
        {
          const build = resolveBuildRoot();
          const id = String(msg.value || "");
          if (build && id) {
            if (this.streaming) {
              writeCmd(build, { op: "previewMusic", musicId: id, id });
            } else {
              void this.previewMusicInPanel(build, id);
            }
          }
        }
        break;
      case "pauseMusic":
        this.pauseMusic();
        break;
      case "stopMusic":
        this.stopMusic();
        break;
      case "previewMusic":
        this.previewMusic();
        break;
      case "refreshCatalog":
        await this.refreshCatalogFromGame();
        break;
      case "refreshPanel":
      case "reloadWindow":
        await vscode.commands.executeCommand("workbench.action.reloadWindow");
        break;
      case "closeSettings":
        break;
      case "pickFolder":
        await pickDebugFolder();
        this.pushFolderLabel();
        this.showSettingsOverlay();
        break;
      case "addPlayerTag": {
        const slot = Math.max(1, Math.min(4, Number(msg.slot ?? 1) || 1));
        const raw = await vscode.window.showInputBox({
          prompt: `Name tag for P${slot} (max 10 characters)`,
          placeHolder: "PLAYER",
          validateInput: (v) => {
            const t = (v || "").trim();
            if (!t) {
              return "Enter a tag";
            }
            if (t.length > 10) {
              return "Max 10 characters";
            }
            return undefined;
          },
        });
        const tag = (raw || "").trim().slice(0, 10);
        if (!tag) {
          return;
        }
        const tags = [...(conf().get<string[]>("playerTags", []) || [])];
        if (!tags.includes(tag)) {
          tags.push(tag);
          tags.sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }));
          await conf().update("playerTags", tags, vscode.ConfigurationTarget.Global);
        }
        await conf().update(`matchP${slot}Tag`, tag, vscode.ConfigurationTarget.Global);
        this.pushLaunchPrefs();
        break;
      }
      case "addBootIntro": {
        const labelRaw = await vscode.window.showInputBox({
          prompt: "Name for this intro (shown in the Boot intro dropdown)",
          placeHolder: "My Intro",
          validateInput: (v) => {
            const t = (v || "").trim();
            if (!t) {
              return "Enter a name";
            }
            if (t.length > 40) {
              return "Max 40 characters";
            }
            return undefined;
          },
        });
        const label = (labelRaw || "").trim();
        if (!label) {
          return;
        }
        const picked = await vscode.window.showOpenDialog({
          canSelectFiles: true,
          canSelectFolders: false,
          canSelectMany: false,
          filters: { Video: ["mp4"] },
          openLabel: "Use as boot intro",
          title: "Choose an MP4 intro",
        });
        const filePath = picked?.[0]?.fsPath;
        if (!filePath) {
          return;
        }
        const id = `c${Date.now().toString(36)}`;
        const build = resolveBuildRoot();
        const pushProg = (pct: number, text: string, active = true) => {
          void this.panel.webview.postMessage({
            type: "bootIntroProgress",
            active,
            pct,
            label: text,
          });
        };
        pushProg(0, `Converting “${label}”…`);
        let stored = "";
        let lastPct = 0;
        try {
          stored = await vscode.window.withProgress(
            {
              location: vscode.ProgressLocation.Notification,
              title: `SSF2: converting “${label}”`,
              cancellable: false,
            },
            async (progress) => {
              progress.report({ message: "Starting ffmpeg…" });
              return ensureBootIntroAssetAsync(id, filePath, build || undefined, (p) => {
                const pct = p.ratio >= 0 ? Math.round(p.ratio * 100) : lastPct;
                const delta = Math.max(0, pct - lastPct);
                lastPct = pct;
                progress.report({
                  increment: delta,
                  message: p.label,
                });
                pushProg(p.ratio >= 0 ? pct : -1, p.label);
              });
            }
          );
        } finally {
          pushProg(100, "", false);
        }
        if (!stored || !stored.toLowerCase().endsWith(".flv")) {
          void vscode.window.showErrorMessage(
            "SSF2: could not convert intro to FLV. Install ffmpeg on PATH and try again."
          );
          return;
        }
        const customs = [
          ...((conf().get<Array<{ id: string; label: string; path: string }>>("bootCustomIntros", []) ||
            []) as Array<{ id: string; label: string; path: string }>),
          { id, label, path: stored },
        ];
        await conf().update("bootCustomIntros", customs, vscode.ConfigurationTarget.Global);
        await conf().update("bootIntro", `custom:${id}`, vscode.ConfigurationTarget.Global);
        {
          const pool = [...(conf().get<string[]>("bootRandomPool", []) || [])];
          const key = `custom:${id}`;
          if (pool.length && !pool.map((k) => k.toLowerCase()).includes(key)) {
            pool.push(key);
            await conf().update("bootRandomPool", pool, vscode.ConfigurationTarget.Global);
          }
        }
        writeBootPrefs(resolveBuildRoot() || undefined);
        this.pushLaunchPrefs();
        void vscode.window.showInformationMessage(`SSF2: added boot intro “${label}” (FLV).`);
        break;
      }
      case "removeBootIntro": {
        const customs = [
          ...((conf().get<Array<{ id: string; label: string; path: string }>>("bootCustomIntros", []) ||
            []) as Array<{ id: string; label: string; path: string }>),
        ];
        if (!customs.length) {
          void vscode.window.showInformationMessage("SSF2: no custom intros to remove.");
          return;
        }
        const picked = await vscode.window.showQuickPick(
          customs.map((c) => ({
            label: c.label || c.id,
            description: c.id,
            picked: false,
            intro: c,
          })),
          {
            canPickMany: true,
            title: "Remove custom boot intros",
            placeHolder: "Select intros to remove",
          }
        );
        if (!picked?.length) {
          return;
        }
        const removeIds = new Set(picked.map((p) => p.intro.id));
        for (const c of customs) {
          if (!removeIds.has(c.id) || !c.path) {
            continue;
          }
          const base = path.normalize(c.path);
          for (const p of [base, `${base}.ok`, base.replace(/\.flv$/i, ".mp4")]) {
            try {
              if (fs.existsSync(p)) {
                fs.unlinkSync(p);
              }
            } catch {
              /* ignore */
            }
          }
        }
        const next = customs.filter((c) => !removeIds.has(c.id));
        await conf().update("bootCustomIntros", next, vscode.ConfigurationTarget.Global);
        const pool = (conf().get<string[]>("bootRandomPool", []) || []).filter((k) => {
          const key = String(k || "").toLowerCase();
          return !(key.startsWith("custom:") && removeIds.has(key.slice("custom:".length)));
        });
        await conf().update("bootRandomPool", pool, vscode.ConfigurationTarget.Global);
        const cur = (conf().get<string>("bootIntro", "default") || "default").trim().toLowerCase();
        if (cur.startsWith("custom:") && removeIds.has(cur.slice("custom:".length))) {
          await conf().update("bootIntro", "default", vscode.ConfigurationTarget.Global);
        }
        writeBootPrefs(resolveBuildRoot() || undefined);
        this.pushLaunchPrefs();
        void vscode.window.showInformationMessage(
          `SSF2: removed ${picked.length} custom intro${picked.length === 1 ? "" : "s"}.`
        );
        break;
      }
      case "configureBootRandomPool": {
        const customs = [
          ...((conf().get<Array<{ id: string; label: string; path: string }>>("bootCustomIntros", []) ||
            []) as Array<{ id: string; label: string; path: string }>),
        ];
        const labels: Record<string, string> = {
          default: "Default (disclaimer → beta)",
          intro1: "Intro 1 (v8)",
          intro2: "Intro 2 (v9)",
          intro3: "Intro 3 (beta)",
        };
        for (const c of customs) {
          labels[`custom:${c.id}`] = c.label || c.id;
        }
        const allKeys = allBootIntroPoolKeys();
        const selected = new Set(getBootRandomPoolKeys());
        const items = allKeys.map((key) => ({
          label: labels[key] || key,
          description: key,
          picked: selected.has(key),
          key,
        }));
        const picked = await vscode.window.showQuickPick(items, {
          canPickMany: true,
          title: "Random intro pool",
          placeHolder: "Select which intros can play when Boot intro is Random",
        });
        if (!picked) {
          return;
        }
        const next = picked.map((p) => p.key);
        await conf().update("bootRandomPool", next, vscode.ConfigurationTarget.Global);
        writeBootPrefs(resolveBuildRoot() || undefined);
        this.pushLaunchPrefs();
        void vscode.window.showInformationMessage(
          next.length
            ? `SSF2: random pool set to ${next.length} intro${next.length === 1 ? "" : "s"}.`
            : "SSF2: random pool cleared (all intros eligible)."
        );
        break;
      }
      case "setSetting": {
        const name = msg.setting;
        if (!name) {
          return;
        }
        // Match prefs change often — debounce Global settings writes (each write is slow)
        // and do NOT pushLaunchPrefs back (webview already has the value; re-push reflows UI).
        const isMatchPref =
          name.startsWith("match") ||
          name === "startingMenu" ||
          name === "loadMatchImmediate" ||
          name === "stayInMatchSetupOnApply" ||
          name === "hideIdeChrome" ||
          name === "superDebug" ||
          name === "vaultAction" ||
          name === "vaultIntro" ||
          name === "vaultPaletteChar" ||
          name === "bootIntro" ||
          name === "bootCustomIntros" ||
          name === "bootRandomPool" ||
          name === "playerTags" ||
          name === "sandbagOverride" ||
          name === "sandbagCharacter" ||
          name === "eventId" ||
          name === "eventOverride" ||
          name === "eventCharacter" ||
          name === "useMusicOnLaunch" ||
          name === "useMusicEverywhere";
        if (isMatchPref) {
          this.pendingSettings.set(name, msg.value);
          if (name === "hideIdeChrome") {
            this.hideIde = !!msg.value;
            void this.syncIdeChrome();
          }
          if (this.settingsWriteTimer) {
            clearTimeout(this.settingsWriteTimer);
          }
          this.settingsWriteTimer = setTimeout(() => {
            void this.flushPendingSettings();
          }, 350);
          break;
        }
        await conf().update(name, msg.value, vscode.ConfigurationTarget.Global);
        if (name === "inputForwarding") {
          this.inputForward = !!msg.value;
          if (this.streaming) {
            await this.syncConfig();
          }
          void this.panel.webview.postMessage({
            type: "toggles",
            inputForward: this.inputForward,
            viewAdl: this.viewAdl,
            playFocus: this.playFocus,
          });
        }
        if (name === "viewAdlWindow") {
          this.viewAdl = !!msg.value;
          await this.persistViewAdl(this.viewAdl);
          if (!this.viewAdl) {
            if (!this.inputForward) {
              this.inputForward = true;
              await conf().update("inputForwarding", true, vscode.ConfigurationTarget.Global);
            }
            await this.setPlayFocus(true);
          }
          if (this.streaming) {
            await this.syncConfig();
          }
          void this.panel.webview.postMessage({
            type: "toggles",
            inputForward: this.inputForward,
            viewAdl: this.viewAdl,
            playFocus: this.playFocus,
            playFocusPref: conf().get<boolean>("playFocus", false),
            hideIde: this.hideIde,
          });
        }
        if (
          this.streaming &&
          (name === "fps" || name === "jpegQuality" || name === "maxStreamLongEdge")
        ) {
          this.startStreaming(true);
        }
        this.pushFolderLabel();
        break;
      }
      case "setInputForward":
        this.inputForward = !!msg.value;
        await conf().update("inputForwarding", this.inputForward, vscode.ConfigurationTarget.Global);
        if (this.streaming) {
          await this.syncConfig();
        }
        break;
      case "setViewAdl":
        this.viewAdl = !!msg.value;
        await this.persistViewAdl(this.viewAdl);
        if (!this.viewAdl) {
          if (!this.inputForward) {
            this.inputForward = true;
            await conf().update("inputForwarding", true, vscode.ConfigurationTarget.Global);
          }
          await this.setPlayFocus(true);
        }
        void this.panel.webview.postMessage({
          type: "toggles",
          inputForward: this.inputForward,
          viewAdl: this.viewAdl,
          playFocus: this.playFocus,
          playFocusPref: conf().get<boolean>("playFocus", false),
        });
        if (this.streaming) {
          await this.syncConfig();
        }
        break;
      case "restoreWindow":
        await this.server.postInput({ type: "restore" });
        // Temporarily show ADL without rewriting the saved preference.
        void this.panel.webview.postMessage({
          type: "toggles",
          inputForward: this.inputForward,
          viewAdl: true,
          playFocus: this.playFocus,
        });
        break;
      case "setPlayFocus":
        await this.setPlayFocus(!!msg.value);
        break;
      case "keydown":
      case "keyup": {
        if (!this.inputForward || !this.streaming) {
          return;
        }
        const vk = vkFromMessage(msg.key ?? "", msg.code ?? "");
        if (vk === undefined) {
          return;
        }
        await this.server.postInput({ type: "key", vk, down: msg.type === "keydown" });
        break;
      }
      case "mousedown":
      case "mouseup":
      case "mousemove": {
        if (!this.inputForward || !this.streaming) {
          return;
        }
        const action = msg.type === "mousedown" ? "d" : msg.type === "mouseup" ? "u" : "m";
        await this.server.postInput({
          type: "mouse",
          action,
          nx: msg.nx ?? 0,
          ny: msg.ny ?? 0,
          button: msg.button ?? 0,
        });
        if (msg.type === "mousemove") {
          const hand = await this.server.fetchCursorHand();
          void this.panel.webview.postMessage({ type: "cursorHand", hand });
        }
        break;
      }
      case "launchDebug":
        await this.runLaunchDebug();
        break;
      case "launchExe":
        await this.runLaunchExe();
        break;
      case "compileFolder":
        await this.runCompileFolder(msg.id || "");
        break;
      case "closeExe":
        this.closeExe();
        break;
      case "restartExe":
        await this.restartExe();
        break;
      default:
        break;
    }
  }

  private html(): string {
    const version = String(
      (this.context.extension.packageJSON as { version?: string }).version || "?"
    );
    const cspSource = this.panel.webview.cspSource;
    const csp = [
      "default-src 'none'",
      "img-src http://127.0.0.1:* data: blob:",
      "media-src " + cspSource + " http://127.0.0.1:* blob: data:",
      "style-src 'unsafe-inline'",
      "script-src 'unsafe-inline'",
      "connect-src http://127.0.0.1:* " + cspSource,
    ].join("; ");

    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy" content="${csp}" />
  <style>
    html, body { margin:0; width:100%; height:100%; background:#000; overflow:hidden; font-family:"Segoe UI",system-ui,sans-serif; color:#ddd; }
    #bar {
      position:absolute; left:0; right:0; top:0; height:40px; z-index:5;
      display:flex; align-items:center; gap:10px; padding:0 10px;
      background:rgba(12,12,12,.94); border-bottom:1px solid #222; font-size:12px;
    }
    .tog { display:flex; align-items:center; gap:6px; user-select:none; cursor:pointer; }
    #bar button, #idleActions button, #settingsPane button, #matchBox button {
      appearance:none; border:1px solid #555; background:transparent; color:#ddd;
      padding:4px 10px; font-size:11px; cursor:pointer; border-radius:2px;
    }
    #bar button:hover, #idleActions button:hover, #settingsPane button:hover, #matchBox button:hover { border-color:#fff; color:#fff; }
    #bar button:disabled, #matchBox button:disabled { opacity:.35; cursor:default; border-color:#333; color:#666; }
    #bar button.active {
      border-color: #7eb8ff; color: #7eb8ff; background: rgba(126,184,255,.12);
    }
    #wrap.playFocus {
      outline: 2px solid #7eb8ff; outline-offset: -2px;
    }
    #launchExe {
      appearance:none; background:transparent; padding:10px 22px; letter-spacing:.08em;
      text-transform:uppercase; font-size:12px; cursor:pointer; border-radius:2px; border:1px solid #fff; color:#fff;
    }
    #launchExe:hover { background:#fff; color:#000; }
    #wrap {
      position:absolute; left:0; right:0; top:40px; bottom:0;
      background:#000; overflow:hidden;
    }
    #frame {
      position:absolute; left:50%; top:50%;
      transform: translate(-50%, -50%);
      object-fit: fill;
      outline:none; cursor:default; display:block; background:#000;
    }
    #status {
      position:absolute; left:0; right:0; top:48%; text-align:center; color:#999;
      font-size:13px; pointer-events:none; padding:0 16px; z-index:2;
    }
    #idle {
      display:none; position:absolute; left:0; right:0; top:40px; bottom:0;
      flex-direction:column; align-items:center; justify-content:center;
      gap:20px; background:#000; z-index:3; padding:16px 20px; box-sizing:border-box;
    }
    #idle.visible { display:flex; }
    #idle.cssPrefs {
      flex-direction:row; flex-wrap:wrap; align-items:flex-start; justify-content:center; gap:24px;
      overflow-x:hidden; overflow-y:auto;
    }
    #idleMain {
      display:flex; flex-direction:column; align-items:center; gap:16px;
      flex:1 1 220px; min-width:0; max-width:100%;
    }
    #logo { width:min(400px,70vw); }
    #idle.cssPrefs #logo { width:min(320px,100%); }
    #logoLive {
      display:none; width:min(320px,42vw); max-height:220px; object-fit:contain;
      border:1px solid #2a2a2a; background:#050505;
    }
    #logoLive.visible { display:block; }
    #logo.hide { display:none; }
    #folder { color:#777; font-size:11px; max-width:80%; word-break:break-all; text-align:center; }
    #idleReason { color:#888; font-size:12px; min-height:1.2em; }
    .bootProg {
      width:min(420px, 92vw); margin:0 auto 10px; text-align:left;
    }
    .bootProgTrack {
      height:8px; border-radius:4px; background:#222; border:1px solid #3a3a3a; overflow:hidden;
    }
    .bootProgFill {
      height:100%; width:0%; background:linear-gradient(90deg, #3d7a4a, #6bcf7a);
      transition:width .15s ease-out;
    }
    .bootProgFill.indeterminate {
      width:35% !important;
      animation: bootProgSlide 1.1s ease-in-out infinite;
    }
    @keyframes bootProgSlide {
      0% { transform: translateX(-120%); }
      100% { transform: translateX(320%); }
    }
    .bootProgLab { margin-top:6px; font-size:11px; color:#9a9a9a; }
    #idleActions { display:flex; gap:10px; flex-wrap:wrap; justify-content:center; }
    #idleActions .compileRow { display:flex; align-items:center; gap:6px; color:#aaa; font-size:11px; }
    #compileFolder { background:#111; color:#ddd; border:1px solid #555; font-size:11px; max-width:180px; }
    #launchPrefs {
      display:flex; flex-direction:column; gap:10px; align-items:stretch;
      width:min(420px, 92vw);
    }
    #idle.cssPrefs #launchPrefs { width:min(280px, 100%); max-width:100%; }
    #launchPrefs label { font-size:12px; color:#aaa; display:flex; flex-direction:column; gap:4px; }
    #launchPrefs select, #matchBox select {
      appearance:none; background:#161616; border:1px solid #444; color:#eee;
      padding:6px 8px; font-size:12px; border-radius:2px;
      width:100%; max-width:100%; box-sizing:border-box; min-width:0;
    }
    #matchBox {
      display:none; flex-direction:column; gap:8px;
      width:min(300px, 100%); flex:1 1 240px; min-width:0; max-width:100%;
      max-height:calc(100vh - 80px); overflow-x:hidden; overflow-y:auto;
      padding:12px 14px; border:1px solid #2a2a2a; background:#0a0a0a;
      box-sizing:border-box;
    }
    #matchBox.visible { display:flex; }
    #matchBox > .matchTitle {
      font-size:11px; color:#888; letter-spacing:.06em; text-transform:uppercase;
      margin-bottom:2px; display:flex; align-items:center; justify-content:space-between; gap:8px;
    }
    #matchBox label { font-size:12px; color:#aaa; display:flex; flex-direction:column; gap:4px; }
    #matchBox input[type="number"] {
      width:100%; background:#161616; border:1px solid #444; color:#eee; padding:6px 8px; font-size:12px;
      box-sizing:border-box;
    }
    .panel { display:none; flex-direction:column; gap:8px; }
    .panel.visible { display:flex; }
    .playerRow {
      display:grid; grid-template-columns: minmax(0, 1fr) auto auto; gap:6px; align-items:end;
    }
    .playerRow.solo { grid-template-columns: 1fr; }
    .playerRow.arena { grid-template-columns: minmax(0, 1.2fr) auto auto auto auto; }
    .playerRow .cpuLevel, .playerRow .nameTag { min-width:0; }
    .playerRow .cpuLevel select, .playerRow .nameTag select { width:100%; }
    .specialGrid {
      display:grid; grid-template-columns: 1fr 1fr; gap:4px 8px; font-size:11px; color:#aaa;
    }
    .specialGrid label { flex-direction:row !important; align-items:center; gap:6px !important; }
    .musicRow {
      display:flex; flex-wrap:wrap; gap:6px; align-items:end;
    }
    .musicRow > label { flex:1 1 140px; min-width:0; }
    .musicBtns { display:flex; flex-wrap:wrap; gap:4px; flex:0 1 auto; }
    .musicBtns button { min-width:52px; }
    .bgmStatus { font-size:11px; color:#888; min-height:14px; }
    .togRow { flex-direction:row !important; align-items:center; gap:8px !important; }
    .togRow input { width:auto; }
    #playFocus:disabled { opacity:.35; }
    #settingsPane {
      display:none; position:absolute; left:0; right:0; top:40px; bottom:0;
      z-index:6; background:#0c0c0c; overflow:auto; padding:24px 28px 40px;
    }
    #settingsPane.visible { display:block; }
    #settingsPane h1 { margin:0 0 6px; font-size:18px; font-weight:600; color:#fff; }
    #settingsPane .sub { margin:0 0 22px; font-size:12px; color:#777; }
    .row {
      display:grid; grid-template-columns: 1fr auto; gap:12px; align-items:center;
      padding:12px 0; border-bottom:1px solid #1e1e1e;
    }
    .row label { font-size:13px; color:#ddd; }
    .row .hint { display:block; margin-top:4px; font-size:11px; color:#666; }
    .row input[type="number"] {
      width:72px; background:#161616; border:1px solid #444; color:#eee; padding:4px 6px;
    }
    .folderRow { grid-template-columns: 1fr auto auto; }
    .folderPath { font-size:11px; color:#888; word-break:break-all; max-width:100%; }
    #settingsClose { position:absolute; right:20px; top:16px; }
    #idle.matchSettingsMode #idleActions .launchOnly { display:none !important; }
    #idle.matchSettingsMode #backToSsf2 { display:inline-block; }
    #backToSsf2 { display:none; }
    #applyMatch { display:none; }
    #gotoMenu { display:none; }
    #idle.matchSettingsMode #applyMatch { display:inline-block; }
    #idle.matchSettingsMode #gotoMenu { display:inline-block; }
    #idle.gameLive #idleActions .launchOnly { display:none !important; }
    #debugPane {
      display:none; position:absolute; left:12px; right:12px; bottom:12px; top:48px;
      background:#0e0e0ecc; border:1px solid #333; border-radius:8px; padding:16px;
      overflow:auto; z-index:8; color:#ddd;
    }
    #debugPane.visible { display:block; }
    #debugPane h2 { margin:0 0 12px; font-size:16px; color:#eee; }
    #debugPane .dbgRow { display:flex; gap:8px; flex-wrap:wrap; align-items:center; margin:8px 0; }
    #debugPane select, #debugPane input[type="number"] {
      background:#161616; border:1px solid #444; color:#eee; padding:4px 6px;
    }
  </style>
</head>
<body>
  <div id="bar">
    <label class="tog"><input type="checkbox" id="viewAdl" /> View ADL</label>
    <label class="tog"><input type="checkbox" id="inputFwd" /> Input</label>
    <label class="tog" title="Hide sidebar/other editors; keep Stream + console"><input type="checkbox" id="hideIde" /> Hide IDE</label>
    <label class="tog" title="When on, F-keys go to the game (hold Esc 2s to release). Remembered. Only while game is live."><input type="checkbox" id="playFocus" disabled /> Focus</label>
    <button id="closeExe" type="button" title="Quit the game started with Launch SSF2" style="display:none">Close SSF2</button>
    <button id="restartExe" type="button" title="Restart Launch SSF2 with the same menu/match settings" style="display:none">Restart SSF2</button>
    <button id="matchSettingsBtn" type="button" title="Edit match setup while streaming" disabled>Match Settings</button>
    <button id="debugBtn" type="button" title="Spawn items / pause / music mid-match" disabled>Debug</button>
    <button id="settings" type="button">Settings</button>
    <button id="refresh" type="button" title="Reload Cursor window (pick up new extension build)">Reload IDE</button>
    <span style="flex:1"></span>
    <span id="focusHint" style="color:#7eb8ff;font-size:11px;display:none">Hold Esc 2s to unfocus · F-keys → game</span>
    <span style="color:#555">F4</span>
    <span id="ver" title="SSF2 Stream extension version">v${version}</span>
  </div>
  <div id="wrap">
    <img id="frame" tabindex="0" alt="" draggable="false" />
  </div>
  <div id="idle" class="visible">
    <div id="idleMain">
      <svg id="logo" viewBox="0 0 520 160" xmlns="http://www.w3.org/2000/svg">
        <text x="50%" y="58%" text-anchor="middle" dominant-baseline="middle"
          font-family="Arial Black, Impact, sans-serif" font-size="118" font-weight="900"
          fill="#fff" letter-spacing="-4">SSF2</text>
        <text x="50%" y="92%" text-anchor="middle" font-family="Segoe UI, sans-serif"
          font-size="16" fill="#fff" opacity=".45" letter-spacing="6">SUPER SMASH FLASH</text>
      </svg>
      <img id="logoLive" alt="Live preview" draggable="false" />
      <div id="idleReason"></div>
      <div id="folder"></div>
      <div id="launchPrefs">
        <label>Starting Menu
          <select id="startingMenu"></select>
        </label>
        <label id="bootIntroRow">Boot intro
          <select id="bootIntro" title="Which intro plays when Starting Menu is Home / default"></select>
        </label>
        <label id="vaultActionRow">Vault action
          <select id="vaultAction" title="When Starting Menu is Vault">
            <option value="intro">Select intro</option>
            <option value="costumes">Manage costumes</option>
          </select>
        </label>
        <label id="vaultIntroRow">Intro
          <select id="vaultIntro" title="Plays before opening Vault"></select>
        </label>
        <label id="vaultCharRow">Character
          <select id="vaultPaletteChar" title="Opens Palette Maker for this fighter"></select>
        </label>
        <div id="panelEvents" class="panel">
          <label>Event<select id="matchEvent"></select></label>
          <div id="eventChooseNote" class="bgmStatus" hidden>This event lets you choose a character in-game.</div>
          <label class="togRow" id="eventOverrideRow"><input type="checkbox" id="eventOverride" /> Override character</label>
          <label id="eventCharRow">Character<select id="eventCharacter"></select></label>
        </div>
        <div id="bootIntroProgress" class="bootProg" hidden>
          <div class="bootProgTrack"><div id="bootIntroProgFill" class="bootProgFill"></div></div>
          <div id="bootIntroProgLab" class="bootProgLab"></div>
        </div>
        <label class="togRow"><input type="checkbox" id="idleHideIde" /> Hide IDE (stream + debug console only)</label>
        <label class="togRow" title="Attach SWF debugger (as3mxml). Off = raw ADL like before."><input type="checkbox" id="idleSuperDebug" /> Super debug (SWF debugger)</label>
      </div>
      <div id="idleActions">
        <button id="launchExe" class="launchOnly" type="button">Launch SSF2</button>
        <button id="applyMatch" type="button">Apply</button>
        <button id="gotoMenu" type="button" title="Open mode CSS with settings prefilled (do not start match)">Goto menu</button>
        <button id="backToSsf2" type="button">Back to SSF2</button>
        <button id="pickFolder" class="launchOnly" type="button">Choose folder</button>
        <label class="compileRow">Compile
          <select id="compileFolder" title="engine2 or a mod src folder"></select>
        </label>
        <button id="compileFolderBtn" type="button" title="Compile the decompiled engine or a mod">Compile folder</button>
      </div>
    </div>
    <div id="matchBox">
      <div class="matchTitle"><span>Match setup</span><button id="catalogRefresh" type="button" title="Refresh lists from live game">Refresh</button></div>
      <label class="togRow" id="immediateRow"><input type="checkbox" id="loadMatchImmediate" /> Load into match immediately</label>
      <label class="togRow"><input type="checkbox" id="stayInMatchSetup" /> Don't go back to SSF2 on Apply</label>
      <div class="musicRow">
        <label>Music<select id="matchMusic"></select></label>
        <div class="musicBtns">
          <button id="previewMusic" type="button" disabled title="Play selected track (in-game when live, otherwise in this panel)">Play</button>
          <button id="pauseMusic" type="button" disabled title="Pause panel preview">Pause</button>
          <button id="stopMusic" type="button" disabled title="Stop music">Stop</button>
        </div>
      </div>
      <label class="togRow"><input type="checkbox" id="useMusicOnLaunch" /> Use this music when launching game</label>
      <label class="togRow"><input type="checkbox" id="useMusicEverywhere" /> Use this music everywhere</label>
      <audio id="bgmAudio" preload="none"></audio>
      <div id="bgmStatus" class="bgmStatus"></div>
      <div class="panel" id="panelVersus">
        <label>Stage<select id="matchStage"></select></label>
        <div id="playerSlots"></div>
        <label class="togRow"><input type="checkbox" id="matchHazards" /> Hazards</label>
        <div id="versusRules" class="panel">
          <label class="togRow"><input type="checkbox" id="matchUsingTime" /> Time</label>
          <label>Time (min)<input type="number" id="matchTime" min="1" max="99" step="1" /></label>
          <label class="togRow"><input type="checkbox" id="matchUsingLives" /> Stock</label>
          <label>Stocks<input type="number" id="matchLives" min="1" max="99" step="1" /></label>
        </div>
        <div id="specialSmash" class="panel">
          <div class="matchTitle"><span>Special Smash</span></div>
          <div class="specialGrid" id="specialGrid"></div>
        </div>
      </div>
      <div class="panel" id="panelSoloClassic">
        <label>Difficulty<select id="matchDifficulty"></select></label>
        <label>Lives<input type="number" id="matchSoloLives" min="1" max="5" step="1" /></label>
        <label>P1<select id="matchP1Solo"></select></label>
        <label class="nameTag">Tag<select id="matchP1TagSolo"></select></label>
      </div>
      <div class="panel" id="panelModeStage">
        <label id="modeStageLab">Mode / Stage<select id="matchModeStage"></select></label>
        <label>P1<select id="matchP1Mode"></select></label>
        <label class="nameTag">Tag<select id="matchP1TagMode"></select></label>
      </div>
      <div class="panel" id="panelArena">
        <label>Arena mode<select id="matchArenaMode"></select></label>
        <label>Score limit<input type="number" id="matchScoreLimit" min="1" max="99" step="1" /></label>
        <div id="arenaSlots"></div>
      </div>
      <div class="panel" id="panelSandbag">
        <label class="togRow"><input type="checkbox" id="sandbagOverride" /> Override sandbag character</label>
        <label>Sandbag character<select id="sandbagCharacter"></select></label>
      </div>
      <div class="panel" id="panelRttf">
        <label>P1<select id="matchP1Rttf"></select></label>
        <label class="nameTag">Tag<select id="matchP1TagRttf"></select></label>
      </div>
    </div>
  </div>
  <div id="debugPane">
    <button id="debugClose" type="button" style="float:right">Close</button>
    <h2>Debug console</h2>
    <p class="sub" style="margin:0 0 8px;color:#888;font-size:12px">Live match only. Mirrors in-game tilde console generate / music.</p>
    <div class="dbgRow">
      <button id="dbgPause" type="button">Pause</button>
      <button id="dbgResume" type="button">Resume</button>
    </div>
    <div class="dbgRow">
      <label>Spawn
        <select id="dbgKind">
          <option value="item">Item</option>
          <option value="pokemon">Pokemon</option>
          <option value="assist">Assist</option>
        </select>
      </label>
      <label>ID <input type="number" id="dbgId" min="0" step="1" value="0" style="width:72px" /></label>
      <label class="tog"><input type="checkbox" id="dbgRare" /> Rare</label>
      <button id="dbgGenerate" type="button">Generate</button>
    </div>
    <div class="dbgRow">
      <label>Item list<select id="dbgItemList"></select></label>
    </div>
    <div class="dbgRow">
      <label>Music<select id="dbgMusic"></select></label>
      <button id="dbgPlayMusic" type="button">Play</button>
    </div>
  </div>
  <div id="settingsPane">
    <button id="settingsClose" type="button">Close</button>
    <h1>SSF2 Stream Settings</h1>
    <p class="sub">Saved to your Cursor user settings. Cursor's @ext Settings UI crashes this workspace, so settings live here.</p>
    <div class="row folderRow">
      <div>
        <label>Debug Folder</label>
        <span class="hint">Shell folder with asconfig.json (Launch Debug uses this path)</span>
        <div class="folderPath" id="setDebugFolder">—</div>
      </div>
      <button id="setPickFolder" type="button">Choose…</button>
    </div>
    <div class="row">
      <div><label>Auto-open Stream on launch</label><span class="hint">Open the SSF2 Stream tab when VS Code / Cursor starts (from the launcher or by itself). Turn off if you want to open it manually.</span></div>
      <input type="checkbox" id="setLaunchOnVscStartup" />
    </div>
    <div class="row">
      <div><label>Auto-open on debug</label><span class="hint">Open Stream when a debug session starts</span></div>
      <input type="checkbox" id="setAutoOpenOnDebug" />
    </div>
    <div class="row">
      <div><label>Focus Debug Console</label><span class="hint">After F5, focus the Debug Console</span></div>
      <input type="checkbox" id="setFocusDebugConsole" />
    </div>
    <div class="row">
      <div><label>Close panel stops debug</label><span class="hint">Closing this tab ends the SWF session</span></div>
      <input type="checkbox" id="setClosePanelStopsDebug" />
    </div>
    <div class="row">
      <div><label>View ADL window</label><span class="hint">Off parks the game off-screen. Input can stay on without stealing Cursor focus — use Focus for gamepads.</span></div>
      <input type="checkbox" id="setViewAdlWindow" />
    </div>
    <div class="row">
      <div><label>Hide IDE on launch</label><span class="hint">Sidebar/other editors hidden; stream + debug console until game closes</span></div>
      <input type="checkbox" id="setHideIdeChrome" />
    </div>
    <div class="row">
      <div><label>Pause music on launch</label><span class="hint">Stop Match setup music preview when debug/exe starts</span></div>
      <input type="checkbox" id="setPauseMusicOnLaunch" />
    </div>
    <div class="row">
      <div><label>Input from stream</label><span class="hint">Forward clicks/keys into the game</span></div>
      <input type="checkbox" id="setInputForwarding" />
    </div>
    <div class="row">
      <div><label>Target FPS</label><span class="hint">Quality auto-tunes toward this rate</span></div>
      <input type="number" id="setFps" min="5" max="60" step="1" />
    </div>
    <div class="row">
      <div><label>JPEG quality start</label><span class="hint">Starting encode quality (50–100)</span></div>
      <input type="number" id="setJpegQuality" min="50" max="100" step="1" />
    </div>
    <div class="row">
      <div><label>Max stream size</label><span class="hint">Longest side in pixels (0 = uncapped). 1280 is a good balance</span></div>
      <input type="number" id="setMaxLong" min="0" max="3840" step="160" />
    </div>
  </div>
  <div id="status"></div>
  <script>
    const vscode = acquireVsCodeApi();
    const wrap = document.getElementById('wrap');
    const img = document.getElementById('frame');
    const statusEl = document.getElementById('status');
    const idleEl = document.getElementById('idle');
    const idleReason = document.getElementById('idleReason');
    const bootIntroProgress = document.getElementById('bootIntroProgress');
    const bootIntroProgFill = document.getElementById('bootIntroProgFill');
    const bootIntroProgLab = document.getElementById('bootIntroProgLab');
    const folderEl = document.getElementById('folder');
    const settingsPane = document.getElementById('settingsPane');
    const logoEl = document.getElementById('logo');
    const logoLive = document.getElementById('logoLive');
    const viewAdl = document.getElementById('viewAdl');
    const inputFwd = document.getElementById('inputFwd');
    const hideIde = document.getElementById('hideIde');
    const playFocusCb = document.getElementById('playFocus');
    const focusHint = document.getElementById('focusHint');
    const startingMenu = document.getElementById('startingMenu');
    const bootIntro = document.getElementById('bootIntro');
    const bootIntroRow = document.getElementById('bootIntroRow');
    const vaultActionRow = document.getElementById('vaultActionRow');
    const vaultAction = document.getElementById('vaultAction');
    const vaultIntroRow = document.getElementById('vaultIntroRow');
    const vaultIntro = document.getElementById('vaultIntro');
    const vaultCharRow = document.getElementById('vaultCharRow');
    const vaultPaletteChar = document.getElementById('vaultPaletteChar');
    const matchEvent = document.getElementById('matchEvent');
    const eventOverride = document.getElementById('eventOverride');
    const eventCharacter = document.getElementById('eventCharacter');
    const eventChooseNote = document.getElementById('eventChooseNote');
    const eventOverrideRow = document.getElementById('eventOverrideRow');
    const eventCharRow = document.getElementById('eventCharRow');
    let bootIntroLast = 'default';
    let vaultIntroLast = 'intro3';
    const matchBox = document.getElementById('matchBox');
    const matchSettingsBtn = document.getElementById('matchSettingsBtn');
    const debugBtn = document.getElementById('debugBtn');
    const debugPane = document.getElementById('debugPane');
    const previewMusicBtn = document.getElementById('previewMusic');
    const pauseMusicBtn = document.getElementById('pauseMusic');
    const stopMusicBtn = document.getElementById('stopMusic');
    const bgmAudio = document.getElementById('bgmAudio');
    const bgmStatus = document.getElementById('bgmStatus');
    const loadMatchImmediate = document.getElementById('loadMatchImmediate');
    const stayInMatchSetup = document.getElementById('stayInMatchSetup');
    const matchStage = document.getElementById('matchStage');
    const matchMusic = document.getElementById('matchMusic');
    const matchHazards = document.getElementById('matchHazards');
    const matchUsingTime = document.getElementById('matchUsingTime');
    const matchUsingLives = document.getElementById('matchUsingLives');
    const matchTime = document.getElementById('matchTime');
    const matchLives = document.getElementById('matchLives');
    const matchDifficulty = document.getElementById('matchDifficulty');
    const matchSoloLives = document.getElementById('matchSoloLives');
    const matchP1Solo = document.getElementById('matchP1Solo');
    const matchModeStage = document.getElementById('matchModeStage');
    const matchP1Mode = document.getElementById('matchP1Mode');
    const matchArenaMode = document.getElementById('matchArenaMode');
    const matchScoreLimit = document.getElementById('matchScoreLimit');
    const sandbagOverride = document.getElementById('sandbagOverride');
    const sandbagCharacter = document.getElementById('sandbagCharacter');
    const useMusicOnLaunch = document.getElementById('useMusicOnLaunch');
    const useMusicEverywhere = document.getElementById('useMusicEverywhere');
    const matchP1Rttf = document.getElementById('matchP1Rttf');
    const matchP1TagSolo = document.getElementById('matchP1TagSolo');
    const matchP1TagMode = document.getElementById('matchP1TagMode');
    const matchP1TagRttf = document.getElementById('matchP1TagRttf');
    const idleHideIde = document.getElementById('idleHideIde');
    const idleSuperDebug = document.getElementById('idleSuperDebug');
    const playerSlots = document.getElementById('playerSlots');
    const arenaSlots = document.getElementById('arenaSlots');
    const specialGrid = document.getElementById('specialGrid');
    let down = false, lastMove = 0, inputOn = false, playFocus = false, gameLive = false;
    let playFocusPref = false;
    let streamUrl = '';
    let matchSettingsOpen = false;
    let debugOpen = false;
    let escHoldTimer = null;
    let escHoldStarted = 0;
    let catalog = { characters: [], stages: [], music: [], menus: [], specialModes: [] };
    let prefsCache = {};

    const MATCH_SCREENS = new Set([
      'versus_css','training_css','classic','allstar','btt','hrc','multiman','crystal','rttf','arena'
    ]);

    function syncMusicButtons() {
      const has = !!(matchMusic && matchMusic.value);
      if (previewMusicBtn) previewMusicBtn.disabled = !has;
      if (pauseMusicBtn) pauseMusicBtn.disabled = !has;
      if (stopMusicBtn) stopMusicBtn.disabled = !has;
    }

    function setBgmStatus(text) {
      if (bgmStatus) bgmStatus.textContent = text || '';
    }

    function refillIntroSelect(sel, selected, customs, opts) {
      if (!sel) return;
      const includeDefault = !(opts && opts.noDefault);
      const builtins = includeDefault
        ? [
            ['default', 'Default (disclaimer → beta)'],
            ['skip', 'Skip (title)'],
            ['random', 'Random'],
            ['intro1', 'Intro 1 (v8)'],
            ['intro2', 'Intro 2 (v9)'],
            ['intro3', 'Intro 3 (beta)'],
          ]
        : [
            ['skip', 'Skip intro'],
            ['random', 'Random'],
            ['intro1', 'Intro 1 (v8)'],
            ['intro2', 'Intro 2 (v9)'],
            ['intro3', 'Intro 3 (beta)'],
          ];
      const lastKey = sel === bootIntro ? 'bootIntroLast' : 'vaultIntroLast';
      const cur = selected || sel.value || (lastKey === 'bootIntroLast' ? bootIntroLast : vaultIntroLast);
      sel.innerHTML = '';
      for (const [val, lab] of builtins) {
        const o = document.createElement('option');
        o.value = val;
        o.textContent = lab;
        sel.appendChild(o);
      }
      for (const c of (customs || [])) {
        if (!c || !c.id) continue;
        const o = document.createElement('option');
        o.value = 'custom:' + c.id;
        o.textContent = c.label || c.id;
        sel.appendChild(o);
      }
      const pool = document.createElement('option');
      pool.value = '__pool__';
      pool.textContent = 'Manage random pool…';
      sel.appendChild(pool);
      const add = document.createElement('option');
      add.value = '__add__';
      add.textContent = 'Add custom intro…';
      sel.appendChild(add);
      if (sel === bootIntro && (customs || []).length) {
        const rem = document.createElement('option');
        rem.value = '__remove__';
        rem.textContent = 'Remove custom intro…';
        sel.appendChild(rem);
      }
      if (cur && cur !== '__add__' && cur !== '__remove__' && cur !== '__pool__' && [...sel.options].some(o => o.value === cur)) {
        sel.value = cur;
        if (lastKey === 'bootIntroLast') bootIntroLast = cur;
        else vaultIntroLast = cur;
      } else if (includeDefault) {
        sel.value = 'default';
        bootIntroLast = 'default';
      } else {
        sel.value = 'intro3';
        vaultIntroLast = 'intro3';
      }
    }

    function refillBootIntro(selected, customs) {
      refillIntroSelect(bootIntro, selected, customs, { noDefault: false });
    }

    function refillVaultIntro(selected, customs) {
      refillIntroSelect(vaultIntro, selected, customs, { noDefault: true });
    }
    refillBootIntro('default', []);

    function fillSelect(sel, items, includeBlank) {
      if (!sel) return;
      const cur = sel.value;
      sel.innerHTML = '';
      if (includeBlank) {
        const o = document.createElement('option');
        o.value = ''; o.textContent = '(none)';
        sel.appendChild(o);
      }
      for (const it of (items || [])) {
        const o = document.createElement('option');
        o.value = it.id; o.textContent = it.label || it.id;
        if (it.value === 1) o.dataset.choose = '1';
        sel.appendChild(o);
      }
      if ([...sel.options].some(o => o.value === cur)) sel.value = cur;
    }

    function saveLaunchPref(setting, value) {
      prefsCache[setting] = value;
      vscode.postMessage({ type: 'setSetting', setting, value });
    }

    function buildPlayerRow(n, opts) {
      const row = document.createElement('div');
      row.className = 'playerRow' + (opts.arena ? ' arena' : '');
      row.dataset.slot = String(n);
      const lab = document.createElement('label');
      lab.textContent = 'P' + n;
      const sel = document.createElement('select');
      sel.id = 'matchP' + n;
      fillSelect(sel, catalog.characters || [], n > 1);
      sel.value = prefsCache['matchP' + n] || (n === 1 ? 'mario' : n === 2 ? 'fox' : '');
      sel.onchange = () => saveLaunchPref('matchP' + n, sel.value);
      lab.appendChild(sel);
      row.appendChild(lab);

      const humanLab = document.createElement('label');
      humanLab.className = 'togRow';
      const humanCb = document.createElement('input');
      humanCb.type = 'checkbox';
      humanCb.id = 'matchP' + n + 'Human';
      if (n === 1) humanCb.checked = prefsCache.matchP1Human !== false;
      else if (n === 2) humanCb.checked = prefsCache.matchP2Human !== false;
      else if (n === 3) humanCb.checked = !!prefsCache.matchP3Human;
      else humanCb.checked = !!prefsCache.matchP4Human;

      const lvlWrap = document.createElement('label');
      lvlWrap.className = 'cpuLevel';
      lvlWrap.textContent = 'CPU';
      const lvl = document.createElement('select');
      lvl.id = 'matchP' + n + 'Level';
      lvl.title = 'CPU level';
      for (let lv = 1; lv <= 9; lv++) {
        const o = document.createElement('option');
        o.value = String(lv); o.textContent = String(lv);
        lvl.appendChild(o);
      }
      lvl.value = String(prefsCache['matchP' + n + 'Level'] || (n === 1 ? 1 : 3));
      lvl.onchange = () => saveLaunchPref('matchP' + n + 'Level', Number(lvl.value) || 1);
      lvlWrap.appendChild(lvl);

      const tagWrap = document.createElement('label');
      tagWrap.className = 'nameTag';
      tagWrap.textContent = 'Tag';
      const tagSel = document.createElement('select');
      tagSel.id = 'matchP' + n + 'Tag';
      tagSel.title = 'Name tag';
      function refillTags() {
        const cur = prefsCache['matchP' + n + 'Tag'] || '';
        const tags = Array.isArray(prefsCache.playerTags) ? prefsCache.playerTags.slice() : [];
        tagSel.innerHTML = '';
        const none = document.createElement('option');
        none.value = ''; none.textContent = '(none)';
        tagSel.appendChild(none);
        for (const t of tags) {
          if (!t) continue;
          const o = document.createElement('option');
          o.value = t; o.textContent = t;
          tagSel.appendChild(o);
        }
        const add = document.createElement('option');
        add.value = '__add_new__'; add.textContent = 'Add new…';
        tagSel.appendChild(add);
        if ([...tagSel.options].some(o => o.value === cur)) tagSel.value = cur;
        else tagSel.value = '';
      }
      refillTags();
      tagSel.onchange = () => {
        if (tagSel.value === '__add_new__') {
          tagSel.value = prefsCache['matchP' + n + 'Tag'] || '';
          vscode.postMessage({ type: 'addPlayerTag', slot: n });
          return;
        }
        saveLaunchPref('matchP' + n + 'Tag', tagSel.value || '');
      };
      tagWrap.appendChild(tagSel);

      function syncHumanUi() {
        const human = humanCb.checked;
        lvlWrap.style.display = human ? 'none' : '';
        tagWrap.style.display = human ? '' : 'none';
      }
      humanCb.onchange = () => {
        saveLaunchPref('matchP' + n + 'Human', humanCb.checked);
        syncHumanUi();
      };
      humanLab.appendChild(humanCb);
      humanLab.appendChild(document.createTextNode('Human'));
      row.appendChild(humanLab);
      row.appendChild(lvlWrap);
      row.appendChild(tagWrap);
      syncHumanUi();

      if (opts.arena) {
        const team = document.createElement('select');
        team.id = 'matchP' + n + 'Team';
        fillSelect(team, [
          {id:'1',label:'Team 1'},
          {id:'2',label:'Team 2'},
          {id:'3',label:'Team 3'}
        ], false);
        team.value = String(prefsCache['matchP' + n + 'Team'] || (n % 2 === 1 ? 1 : 3));
        team.onchange = () => saveLaunchPref('matchP' + n + 'Team', Number(team.value) || 1);
        row.appendChild(team);
      }
      return row;
    }

    function refillTagSelect(sel, cur) {
      if (!sel) return;
      const tags = Array.isArray(prefsCache.playerTags) ? prefsCache.playerTags.slice() : [];
      const keep = cur !== undefined ? cur : (prefsCache.matchP1Tag || '');
      sel.innerHTML = '';
      const none = document.createElement('option');
      none.value = ''; none.textContent = '(none)';
      sel.appendChild(none);
      for (const t of tags) {
        if (!t) continue;
        const o = document.createElement('option');
        o.value = t; o.textContent = t;
        sel.appendChild(o);
      }
      const add = document.createElement('option');
      add.value = '__add_new__'; add.textContent = 'Add new…';
      sel.appendChild(add);
      if ([...sel.options].some(o => o.value === keep)) sel.value = keep;
      else sel.value = '';
    }

    function bindP1TagSelect(sel) {
      if (!sel || sel.dataset.bound) return;
      sel.dataset.bound = '1';
      sel.onchange = () => {
        if (sel.value === '__add_new__') {
          sel.value = prefsCache.matchP1Tag || '';
          vscode.postMessage({ type: 'addPlayerTag', slot: 1 });
          return;
        }
        saveLaunchPref('matchP1Tag', sel.value || '');
        // Keep sibling P1 tag selects in sync.
        for (const other of [matchP1TagSolo, matchP1TagMode, matchP1TagRttf]) {
          if (other && other !== sel) other.value = sel.value;
        }
      };
    }

    function rebuildPlayerSlots() {
      const v = startingMenu.value;
      playerSlots.innerHTML = '';
      arenaSlots.innerHTML = '';
      if (v === 'versus_css' || v === 'training_css') {
        const max = 4;
        for (let i = 1; i <= max; i++) {
          playerSlots.appendChild(buildPlayerRow(i, { arena: false }));
        }
      } else if (v === 'arena') {
        for (let i = 1; i <= 4; i++) {
          arenaSlots.appendChild(buildPlayerRow(i, { arena: true }));
        }
      }
    }

    function rebuildSpecialGrid() {
      specialGrid.innerHTML = '';
      const flags = catalog.specialModes || [];
      const bits = Number(prefsCache.matchSpecialModes || 0);
      for (const sm of flags) {
        const lab = document.createElement('label');
        const cb = document.createElement('input');
        cb.type = 'checkbox';
        const bit = Number(sm.bit || 0);
        cb.checked = bit > 0 && (bits & bit) !== 0;
        cb.onchange = () => {
          let next = Number(prefsCache.matchSpecialModes || 0);
          if (cb.checked) next |= bit; else next &= ~bit;
          saveLaunchPref('matchSpecialModes', next);
        };
        lab.appendChild(cb);
        lab.appendChild(document.createTextNode(sm.label || sm.id));
        specialGrid.appendChild(lab);
      }
    }

    function setPanel(id, on) {
      const el = document.getElementById(id);
      if (el) el.classList.toggle('visible', !!on);
    }

    function syncEventUi() {
      const opt = matchEvent && matchEvent.selectedOptions ? matchEvent.selectedOptions[0] : null;
      const choose = !!(opt && opt.dataset && opt.dataset.choose === '1');
      if (eventChooseNote) eventChooseNote.hidden = !choose;
      if (eventOverrideRow) eventOverrideRow.style.display = choose ? 'none' : '';
      if (eventCharRow) eventCharRow.style.display = choose ? 'none' : '';
      if (eventCharacter) eventCharacter.disabled = !(eventOverride && eventOverride.checked);
    }

    function syncMatchVisibility() {
      const v = startingMenu.value;
      const show = MATCH_SCREENS.has(v) || v === 'events';
      matchBox.classList.toggle('visible', show);
      idleEl.classList.toggle('cssPrefs', show);
      if (bootIntroRow) {
        bootIntroRow.style.display = (!v || v === 'home') ? '' : 'none';
      }
      const isVault = v === 'vault';
      if (vaultActionRow) vaultActionRow.style.display = isVault ? '' : 'none';
      const vaultIntroMode = isVault && vaultAction && vaultAction.value === 'intro';
      if (vaultIntroRow) vaultIntroRow.style.display = vaultIntroMode ? '' : 'none';
      if (vaultCharRow) vaultCharRow.style.display = isVault && vaultAction && vaultAction.value === 'costumes' ? '' : 'none';

      const versus = v === 'versus_css';
      const training = v === 'training_css';
      const classicLike = v === 'classic' || v === 'allstar';
      const modeStage = v === 'btt' || v === 'hrc' || v === 'crystal' || v === 'multiman';
      const arena = v === 'arena';
      const rttf = v === 'rttf';
      const events = v === 'events';
      const solo = classicLike || modeStage || arena || rttf;

      setPanel('panelVersus', versus || training);
      setPanel('versusRules', versus);
      setPanel('specialSmash', versus);
      setPanel('panelSoloClassic', classicLike);
      setPanel('panelModeStage', modeStage);
      setPanel('panelArena', arena);
      setPanel('panelSandbag', v === 'hrc' || arena);
      setPanel('panelRttf', rttf);
      setPanel('panelEvents', events);
      document.getElementById('immediateRow').style.display = (versus || training || solo) ? '' : 'none';
      if (sandbagCharacter) sandbagCharacter.disabled = !(sandbagOverride && sandbagOverride.checked);
      syncEventUi();
      // Default immediate once per screen (versus/training/solo) unless user toggled it.
      if (!prefsCache.managedByModManager && (versus || training || solo) && loadMatchImmediate.dataset.immediateDefaulted !== startingMenu.value) {
        loadMatchImmediate.dataset.immediateDefaulted = startingMenu.value;
        if (!loadMatchImmediate.dataset.userTouched) {
          loadMatchImmediate.checked = true;
          saveLaunchPref('loadMatchImmediate', true);
        }
      }

      if (modeStage) {
        let items = catalog.stages || [];
        let lab = 'Mode / Stage';
        if (v === 'btt') { items = catalog.targetStages?.length ? catalog.targetStages : items; lab = 'BTT stage'; }
        else if (v === 'hrc') { items = catalog.hrcStages?.length ? catalog.hrcStages : items; lab = 'HRC stage'; }
        else if (v === 'crystal') { items = catalog.crystalStages?.length ? catalog.crystalStages : items; lab = 'Crystal stage'; }
        else if (v === 'multiman') { items = catalog.multimanModes || []; lab = 'Multi-Man type'; }
        document.getElementById('modeStageLab').childNodes[0].textContent = lab + ' ';
        fillSelect(matchModeStage, items, false);
        // Seed empty/stale mode id when entering BTT/crystal/hrc/multiman.
        const cur = matchModeStage.value;
        const valid = items.some((it) => String(it.id ?? it.value ?? it) === cur);
        if ((!cur || !valid) && items.length) {
          const first = String(items[0].id ?? items[0].value ?? items[0]);
          matchModeStage.value = first;
          saveLaunchPref('matchCustomModeId', first);
        }
      }
      rebuildPlayerSlots();
    }

    function applyCatalog(cat) {
      if (!cat) return;
      catalog = cat;
      // Harden BTT list: never show meta "Character"; prefer levels + targettest_*.
      if (Array.isArray(catalog.targetStages)) {
        const levels = catalog.targetStages.filter((it) => {
          const id = String(it.id || '');
          return id && id.toLowerCase() !== 'character' && /^Level\s/i.test(id);
        });
        const charLabels = new Map((catalog.characters || []).map((c) => [String(c.id).toLowerCase(), c.label]));
        const seen = new Set(levels.map((i) => String(i.id).toLowerCase()));
        const chars = [];
        for (const st of (catalog.stages || [])) {
          const id = String(st.id || '');
          const low = id.toLowerCase();
          if (!low.startsWith('targettest_')) continue;
          const charId = id.slice('targettest_'.length);
          if (!charId || charId === '2' || charId === '3' || charId === '4') continue;
          if (seen.has(low)) continue;
          seen.add(low);
          chars.push({ id, label: charLabels.get(charId.toLowerCase()) || st.label || charId });
        }
        chars.sort((a, b) => String(a.label).localeCompare(String(b.label)));
        catalog.targetStages = [...(levels.length ? levels : [
          { id: 'Level 1', label: 'Level 1' },
          { id: 'Level 2', label: 'Level 2' },
          { id: 'Level 3', label: 'Level 3' },
          { id: 'Level 4', label: 'Level 4' },
        ]), ...chars];
      }
      fillSelect(startingMenu, cat.menus || [], false);
      fillSelect(matchStage, cat.stages || [], false);
      fillSelect(matchMusic, cat.music || [], true);
      fillSelect(matchDifficulty, cat.difficulties || [], false);
      fillSelect(matchP1Solo, cat.characters || [], false);
      fillSelect(matchP1Mode, cat.characters || [], false);
      fillSelect(matchP1Rttf, cat.characters || [], false);
      fillSelect(vaultPaletteChar, cat.characters || [], false);
      fillSelect(eventCharacter, cat.characters || [], false);
      fillSelect(sandbagCharacter, cat.characters || [], false);
      fillSelect(matchEvent, cat.events || [], false);
      fillSelect(matchArenaMode, cat.arenaModes || [], false);
      const dbgMusic = document.getElementById('dbgMusic');
      const dbgItemList = document.getElementById('dbgItemList');
      if (dbgMusic) fillSelect(dbgMusic, cat.music || [], true);
      if (dbgItemList) {
        const items = (cat.items || []).map((it) => ({ id: String(it.value ?? it.id), label: (it.value ?? it.id) + ' — ' + it.label }));
        fillSelect(dbgItemList, items, true);
      }
      rebuildSpecialGrid();
      syncMatchVisibility();
      syncMusicButtons();
      refillTagSelect(matchP1TagSolo, prefsCache.matchP1Tag);
      refillTagSelect(matchP1TagMode, prefsCache.matchP1Tag);
      refillTagSelect(matchP1TagRttf, prefsCache.matchP1Tag);
      bindP1TagSelect(matchP1TagSolo);
      bindP1TagSelect(matchP1TagMode);
      bindP1TagSelect(matchP1TagRttf);
    }

    function applyPrefsValues(v) {
      prefsCache = Object.assign({}, prefsCache, v || {});
      if (v.startingMenu) startingMenu.value = v.startingMenu;
      loadMatchImmediate.checked = !!v.loadMatchImmediate;
      if (stayInMatchSetup) stayInMatchSetup.checked = !!v.stayInMatchSetupOnApply;
      if (v.matchStage && matchStage) {
        matchStage.value = v.matchStage;
        if (!matchStage.value && matchStage.options.length) {
          matchStage.selectedIndex = 0;
          saveLaunchPref('matchStage', matchStage.value);
        }
      }
      if (matchMusic && v.matchMusic !== undefined) matchMusic.value = v.matchMusic || '';
      if (matchHazards) matchHazards.checked = v.matchHazards !== false;
      if (matchUsingTime) matchUsingTime.checked = v.matchUsingTime !== false;
      if (matchUsingLives) matchUsingLives.checked = v.matchUsingLives !== false;
      if (matchTime && v.matchTime) matchTime.value = String(v.matchTime);
      if (matchLives && v.matchLives) matchLives.value = String(v.matchLives);
      if (matchDifficulty && v.matchDifficulty) matchDifficulty.value = v.matchDifficulty;
      if (matchSoloLives) matchSoloLives.value = String(v.matchLives || (v.startingMenu === 'allstar' ? 5 : 3));
      if (matchP1Solo && v.matchP1) matchP1Solo.value = v.matchP1;
      if (matchP1Mode && v.matchP1) matchP1Mode.value = v.matchP1;
      if (matchP1Rttf && v.matchP1) matchP1Rttf.value = v.matchP1;
      if (matchModeStage && (v.matchCustomModeId || v.matchStage)) {
        matchModeStage.value = v.matchCustomModeId || v.matchStage;
      }
      if (matchArenaMode && (v.matchCustomModeId || v.matchStage)) {
        matchArenaMode.value = v.matchCustomModeId || 'soccer';
      }
      if (matchScoreLimit && v.matchScoreLimit) matchScoreLimit.value = String(v.matchScoreLimit);
      hideIde.checked = !!v.hideIde;
      idleHideIde.checked = !!v.hideIde;
      if (idleSuperDebug) idleSuperDebug.checked = !!v.superDebug;
      rebuildSpecialGrid();
      syncMatchVisibility();
      // re-apply player selects after rebuild
      for (let n = 1; n <= 4; n++) {
        const sel = document.getElementById('matchP' + n);
        if (sel && v['matchP' + n] !== undefined) sel.value = v['matchP' + n];
        const hum = document.getElementById('matchP' + n + 'Human');
        const lvlWrap = document.querySelector('#matchP' + n + 'Level')?.parentElement;
        const tagWrap = document.querySelector('#matchP' + n + 'Tag')?.parentElement;
        if (hum && v['matchP' + n + 'Human'] !== undefined) {
          hum.checked = !!v['matchP' + n + 'Human'];
        }
        const humanOn = hum ? hum.checked : false;
        if (lvlWrap) lvlWrap.style.display = humanOn ? 'none' : '';
        if (tagWrap) tagWrap.style.display = humanOn ? '' : 'none';
        const lvl = document.getElementById('matchP' + n + 'Level');
        if (lvl && v['matchP' + n + 'Level'] !== undefined) lvl.value = String(v['matchP' + n + 'Level']);
        const tag = document.getElementById('matchP' + n + 'Tag');
        if (tag && v['matchP' + n + 'Tag'] !== undefined) {
          const cur = v['matchP' + n + 'Tag'] || '';
          if ([...tag.options].some(o => o.value === cur)) tag.value = cur;
        }
        const team = document.getElementById('matchP' + n + 'Team');
        if (team && v['matchP' + n + 'Team'] !== undefined) team.value = String(v['matchP' + n + 'Team']);
      }
      refillTagSelect(matchP1TagSolo, v.matchP1Tag);
      refillTagSelect(matchP1TagMode, v.matchP1Tag);
      refillTagSelect(matchP1TagRttf, v.matchP1Tag);
      bindP1TagSelect(matchP1TagSolo);
      bindP1TagSelect(matchP1TagMode);
      bindP1TagSelect(matchP1TagRttf);
      refillBootIntro(v.bootIntro || 'default', v.bootCustomIntros || []);
      if (vaultAction && v.vaultAction) vaultAction.value = v.vaultAction;
      refillVaultIntro(v.vaultIntro || 'intro3', v.bootCustomIntros || []);
      fillSelect(vaultPaletteChar, catalog.characters || [], false);
      if (vaultPaletteChar && v.vaultPaletteChar) {
        if ([...vaultPaletteChar.options].some(o => o.value === v.vaultPaletteChar)) {
          vaultPaletteChar.value = v.vaultPaletteChar;
        }
      }
      if (matchEvent && v.eventId) {
        if ([...matchEvent.options].some(o => o.value === v.eventId)) matchEvent.value = v.eventId;
      }
      if (eventOverride) eventOverride.checked = !!v.eventOverride;
      if (eventCharacter && v.eventCharacter) {
        if ([...eventCharacter.options].some(o => o.value === v.eventCharacter)) eventCharacter.value = v.eventCharacter;
      }
      if (sandbagOverride) sandbagOverride.checked = !!v.sandbagOverride;
      if (sandbagCharacter && v.sandbagCharacter) {
        if ([...sandbagCharacter.options].some(o => o.value === v.sandbagCharacter)) sandbagCharacter.value = v.sandbagCharacter;
      }
      if (useMusicOnLaunch) useMusicOnLaunch.checked = !!v.useMusicOnLaunch;
      if (useMusicEverywhere) useMusicEverywhere.checked = !!v.useMusicEverywhere;
      prefsCache.managedByModManager = !!v.managedByModManager;
      if (sandbagCharacter) sandbagCharacter.disabled = !(sandbagOverride && sandbagOverride.checked);
      syncEventUi();
    }

    function setGameLiveUi(live) {
      gameLive = !!live;
      idleEl.classList.toggle('gameLive', gameLive);
      matchSettingsBtn.disabled = !gameLive;
      debugBtn.disabled = !gameLive;
      syncMusicButtons();
      playFocusCb.disabled = !gameLive;
      playFocusCb.checked = gameLive ? playFocus : playFocusPref;
    }

    function openMatchSettingsUi(url) {
      matchSettingsOpen = true;
      idleEl.classList.add('visible', 'matchSettingsMode');
      if (url) {
        logoEl.classList.add('hide');
        logoLive.classList.add('visible');
        logoLive.src = url + (url.includes('?') ? '&' : '?') + 't=' + Date.now();
      }
      idleReason.textContent = 'Match Settings — Apply sends to the live game';
      syncMatchVisibility();
    }

    function closeMatchSettingsUi() {
      matchSettingsOpen = false;
      idleEl.classList.remove('matchSettingsMode');
      logoEl.classList.remove('hide');
      logoLive.classList.remove('visible');
      logoLive.removeAttribute('src');
      if (gameLive && streamUrl) {
        idleEl.classList.remove('visible');
      }
    }

    function setPlayFocusUi(on, pref, live) {
      playFocus = !!on;
      if (typeof pref === 'boolean') playFocusPref = pref;
      if (typeof live === 'boolean') setGameLiveUi(live);
      focusHint.style.display = playFocus ? '' : 'none';
      wrap.classList.toggle('playFocus', playFocus);
      if (playFocus) {
        inputOn = true;
        inputFwd.checked = true;
        img.focus();
      } else {
        clearEscHold();
      }
    }

    function clearEscHold() {
      if (escHoldTimer) { clearInterval(escHoldTimer); escHoldTimer = null; }
      escHoldStarted = 0;
      if (playFocus) {
        focusHint.textContent = 'Hold Esc 2s to unfocus · F-keys → game';
      }
    }

    function tickEscHold() {
      if (!playFocus || !escHoldStarted) { clearEscHold(); return; }
      const left = Math.ceil((2000 - (performance.now() - escHoldStarted)) / 1000);
      if (left <= 0) {
        clearEscHold();
        vscode.postMessage({ type: 'setPlayFocus', value: false });
        return;
      }
      focusHint.textContent = 'Keep holding Esc… ' + left + 's';
    }

    function beginEscHold() {
      if (!playFocus) return;
      if (escHoldStarted) return;
      escHoldStarted = performance.now();
      focusHint.textContent = 'Keep holding Esc… 2s';
      escHoldTimer = setInterval(tickEscHold, 200);
    }

    function fit() {
      const ww = Math.max(1, wrap.clientWidth | 0);
      const wh = Math.max(1, wrap.clientHeight | 0);
      const nw = img.naturalWidth || 0;
      const nh = img.naturalHeight || 0;
      if (nw > 0 && nh > 0) {
        const scale = Math.min(ww / nw, wh / nh);
        img.style.width = Math.max(1, Math.round(nw * scale)) + 'px';
        img.style.height = Math.max(1, Math.round(nh * scale)) + 'px';
      } else {
        img.style.width = ww + 'px';
        img.style.height = wh + 'px';
      }
    }
    new ResizeObserver(fit).observe(wrap);
    window.addEventListener('resize', fit);
    img.addEventListener('load', fit);
    setInterval(() => { if (streamUrl) fit(); }, 1000);
    fit();

    function showIdle(reason) {
      idleEl.classList.add('visible');
      idleEl.classList.remove('matchSettingsMode');
      logoEl.classList.remove('hide');
      logoLive.classList.remove('visible');
      logoLive.removeAttribute('src');
      matchSettingsOpen = false;
      idleReason.textContent = reason || '';
      statusEl.textContent = '';
      streamUrl = '';
      img.removeAttribute('src');
      img.style.cursor = 'default';
      setGameLiveUi(false);
    }
    function hideIdle() {
      if (matchSettingsOpen) return;
      idleEl.classList.remove('visible');
    }

    function attachStream(url) {
      streamUrl = url;
      img.src = url + (url.includes('?') ? '&' : '?') + 't=' + Date.now();
      fit();
    }

    function bindBool(id, setting) {
      const el = document.getElementById(id);
      el.onchange = () => vscode.postMessage({ type: 'setSetting', setting, value: el.checked });
      return el;
    }
    const setLaunch = bindBool('setLaunchOnVscStartup', 'launchOnVscStartup');
    const setAuto = bindBool('setAutoOpenOnDebug', 'autoOpenOnDebug');
    const setFocus = bindBool('setFocusDebugConsole', 'focusDebugConsole');
    const setClose = bindBool('setClosePanelStopsDebug', 'closePanelStopsDebug');
    const setView = bindBool('setViewAdlWindow', 'viewAdlWindow');
    const setInput = bindBool('setInputForwarding', 'inputForwarding');
    const setFps = document.getElementById('setFps');
    const setJpeg = document.getElementById('setJpegQuality');
    const setMaxLong = document.getElementById('setMaxLong');
    const setFolderLabel = document.getElementById('setDebugFolder');
    setFps.onchange = () => vscode.postMessage({ type: 'setSetting', setting: 'fps', value: Number(setFps.value) });
    setJpeg.onchange = () => vscode.postMessage({ type: 'setSetting', setting: 'jpegQuality', value: Number(setJpeg.value) });
    setMaxLong.onchange = () => vscode.postMessage({ type: 'setSetting', setting: 'maxStreamLongEdge', value: Number(setMaxLong.value) });
    setView.onchange = () => {
      viewAdl.checked = setView.checked;
      if (!setView.checked) {
        inputOn = true;
        inputFwd.checked = true;
        setInput.checked = true;
        vscode.postMessage({ type: 'setInputForward', value: true });
        playFocusCb.checked = true;
        vscode.postMessage({ type: 'setPlayFocus', value: true });
      }
      vscode.postMessage({ type: 'setSetting', setting: 'viewAdlWindow', value: setView.checked });
    };
    const setHideIde = bindBool('setHideIdeChrome', 'hideIdeChrome');
    setHideIde.onchange = () => {
      hideIde.checked = setHideIde.checked;
      idleHideIde.checked = setHideIde.checked;
      vscode.postMessage({ type: 'setSetting', setting: 'hideIdeChrome', value: setHideIde.checked });
    };
    const setPauseMusicOnLaunch = bindBool('setPauseMusicOnLaunch', 'pauseMusicOnLaunch');
    setInput.onchange = () => {
      inputOn = setInput.checked;
      inputFwd.checked = setInput.checked;
      vscode.postMessage({ type: 'setSetting', setting: 'inputForwarding', value: setInput.checked });
    };

    function showSettings(values) {
      settingsPane.classList.add('visible');
      setFolderLabel.textContent = values.debugFolder || '(not set)';
      setLaunch.checked = !!values.launchOnVscStartup;
      setAuto.checked = !!values.autoOpenOnDebug;
      setFocus.checked = !!values.focusDebugConsole;
      setClose.checked = !!values.closePanelStopsDebug;
      setView.checked = !!values.viewAdlWindow;
      setHideIde.checked = !!values.hideIdeChrome;
      setPauseMusicOnLaunch.checked = values.pauseMusicOnLaunch !== false;
      setInput.checked = !!values.inputForwarding;
      setFps.value = values.fps ?? 30;
      setJpeg.value = values.jpegQuality ?? 90;
      setMaxLong.value = values.maxStreamLongEdge ?? 1280;
    }
    function hideSettings() {
      settingsPane.classList.remove('visible');
      vscode.postMessage({ type: 'closeSettings' });
    }

    function norm(ev) {
      const r = img.getBoundingClientRect();
      if (r.width <= 0 || r.height <= 0) return null;
      const nx = (ev.clientX - r.left) / r.width;
      const ny = (ev.clientY - r.top) / r.height;
      if (nx < 0 || nx > 1 || ny < 0 || ny > 1) return null;
      return { nx, ny };
    }

    let bgmUnlocked = false;
    async function unlockBgmAudio() {
      if (bgmUnlocked || !bgmAudio) return;
      // Must run in the click gesture so later async play() is allowed.
      bgmAudio.src =
        'data:audio/wav;base64,UklGRnoGAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQoGAACBhYqFbF1fdH2Onp6ekJCQkJCQkJCQkA==';
      try {
        await bgmAudio.play();
      } catch (e) {
        /* ignore */
      }
      try {
        bgmAudio.pause();
        bgmAudio.currentTime = 0;
        bgmAudio.removeAttribute('src');
        bgmAudio.load();
      } catch (e2) {
        /* ignore */
      }
      bgmUnlocked = true;
    }

    function playBgmUrl(url, id) {
      if (!bgmAudio || !url) return;
      setBgmStatus('Starting ' + (id || '') + '…');
      try {
        bgmAudio.onerror = () => setBgmStatus('Audio load error');
        bgmAudio.onplaying = () => setBgmStatus('Playing ' + (id || ''));
        bgmAudio.src = url;
        bgmAudio.load();
        const p = bgmAudio.play();
        if (p && p.then) {
          p.then(() => setBgmStatus('Playing ' + (id || ''))).catch((err) =>
            setBgmStatus('Play failed: ' + (err && err.message ? err.message : err))
          );
        }
      } catch (err) {
        setBgmStatus('Play failed: ' + err);
      }
    }

    function setBootIntroProgress(active, pct, text) {
      if (!bootIntroProgress || !bootIntroProgFill || !bootIntroProgLab) return;
      if (!active) {
        bootIntroProgress.hidden = true;
        bootIntroProgFill.classList.remove('indeterminate');
        bootIntroProgFill.style.width = '0%';
        bootIntroProgLab.textContent = '';
        return;
      }
      bootIntroProgress.hidden = false;
      const n = Number(pct);
      if (!(n >= 0)) {
        bootIntroProgFill.classList.add('indeterminate');
        bootIntroProgFill.style.width = '35%';
      } else {
        bootIntroProgFill.classList.remove('indeterminate');
        bootIntroProgFill.style.width = Math.max(0, Math.min(100, n)) + '%';
      }
      bootIntroProgLab.textContent = text || 'Converting intro…';
      if (idleReason && text) idleReason.textContent = text;
    }

    window.addEventListener('message', (event) => {
      const msg = event.data;
      if (msg.type === 'stream' && msg.url) {
        streamUrl = msg.url;
        if (!matchSettingsOpen) {
          hideIdle();
          attachStream(msg.url);
        } else {
          logoLive.src = msg.url + (msg.url.includes('?') ? '&' : '?') + 't=' + Date.now();
        }
        statusEl.textContent = '';
        setGameLiveUi(true);
      } else if (msg.type === 'bootIntroProgress') {
        setBootIntroProgress(!!msg.active, msg.pct, msg.label || '');
      } else if (msg.type === 'cursorHand') {
        if (img) img.style.cursor = msg.hand ? 'pointer' : 'default';
      } else if (msg.type === 'settings') {
        showSettings(msg.values || {});
        if (msg.catalog) applyCatalog(msg.catalog);
      } else if (msg.type === 'launchPrefs') {
        if (msg.catalog) applyCatalog(msg.catalog);
        applyPrefsValues(msg.values || {});
        if (typeof msg.gameLive === 'boolean') setGameLiveUi(msg.gameLive);
        if (msg.matchSettings) openMatchSettingsUi(streamUrl);
      } else if (msg.type === 'matchSettings') {
        if (msg.open) openMatchSettingsUi(msg.streamUrl || streamUrl);
        else closeMatchSettingsUi();
        if (typeof msg.gameLive === 'boolean') setGameLiveUi(msg.gameLive);
      } else if (msg.type === 'debugOverlay') {
        if (msg.open) {
          if (msg.catalog) applyCatalog(msg.catalog);
          debugPane.classList.add('visible');
          debugOpen = true;
        } else {
          debugPane.classList.remove('visible');
          debugOpen = false;
        }
        if (typeof msg.gameLive === 'boolean') setGameLiveUi(msg.gameLive);
      } else if (msg.type === 'status') {
        statusEl.textContent = msg.text || '';
      } else if (msg.type === 'idle') {
        showIdle(msg.reason || '');
        if (msg.folder) folderEl.textContent = 'Debug folder: ' + msg.folder;
        setPlayFocusUi(false, playFocusPref, false);
      } else if (msg.type === 'folder') {
        folderEl.textContent = 'Debug folder: ' + (msg.folder || '');
        setFolderLabel.textContent = msg.folder || '(not set)';
      } else if (msg.type === 'gameLive') {
        setPlayFocusUi(playFocus, playFocusPref, !!msg.live);
      } else if (msg.type === 'toggles') {
        inputOn = !!msg.inputForward;
        inputFwd.checked = inputOn;
        viewAdl.checked = !!msg.viewAdl;
        hideIde.checked = !!msg.hideIde;
        idleHideIde.checked = !!msg.hideIde;
        setInput.checked = inputOn;
        setView.checked = !!msg.viewAdl;
        if (setHideIde) setHideIde.checked = !!msg.hideIde;
        setPlayFocusUi(!!msg.playFocus, msg.playFocusPref, msg.gameLive);
      } else if (msg.type === 'exeActive') {
        const closeBtn = document.getElementById('closeExe');
        const restartBtn = document.getElementById('restartExe');
        if (closeBtn) closeBtn.style.display = msg.active ? '' : 'none';
        if (restartBtn) restartBtn.style.display = msg.active ? '' : 'none';
      } else if (msg.type === 'focusPlay') {
        setPlayFocusUi(true, true, true);
        img.focus();
      } else if (msg.type === 'escapeHold') {
        if (msg.down) beginEscHold();
        else clearEscHold();
      } else if (msg.type === 'bgmAudio') {
        if (!bgmAudio) return;
        if (msg.action === 'play' && msg.url) {
          playBgmUrl(msg.url, msg.id || '');
        } else if (msg.action === 'pause') {
          if (bgmAudio.paused) {
            bgmAudio.play().then(() => setBgmStatus('Playing')).catch(() => {});
          } else {
            bgmAudio.pause();
            setBgmStatus('Paused');
          }
        } else if (msg.action === 'stop') {
          bgmAudio.pause();
          bgmAudio.removeAttribute('src');
          bgmAudio.load();
          setBgmStatus('');
        } else if (msg.action === 'status') {
          setBgmStatus(msg.text || '');
        }
      }
    });

    viewAdl.onchange = () => {
      setView.checked = viewAdl.checked;
      if (!viewAdl.checked) {
        inputOn = true;
        inputFwd.checked = true;
        setInput.checked = true;
        vscode.postMessage({ type: 'setInputForward', value: true });
        playFocusCb.checked = true;
        vscode.postMessage({ type: 'setPlayFocus', value: true });
      }
      vscode.postMessage({ type: 'setViewAdl', value: viewAdl.checked });
    };
    inputFwd.onchange = () => {
      inputOn = inputFwd.checked;
      setInput.checked = inputOn;
      vscode.postMessage({ type: 'setInputForward', value: inputOn });
    };
    playFocusCb.onchange = () => {
      vscode.postMessage({ type: 'setPlayFocus', value: playFocusCb.checked });
    };
    matchSettingsBtn.onclick = () => vscode.postMessage({ type: 'openMatchSettings' });
    debugBtn.onclick = () => vscode.postMessage({ type: 'openDebug' });
    document.getElementById('debugClose').onclick = () => {
      debugPane.classList.remove('visible');
      debugOpen = false;
      vscode.postMessage({ type: 'closeDebug' });
    };
    document.getElementById('dbgPause').onclick = () => vscode.postMessage({ type: 'debugPause', value: true });
    document.getElementById('dbgResume').onclick = () => vscode.postMessage({ type: 'debugPause', value: false });
    document.getElementById('dbgGenerate').onclick = () => {
      const kind = document.getElementById('dbgKind').value;
      const id = Number(document.getElementById('dbgId').value) || 0;
      const rare = document.getElementById('dbgRare').checked;
      vscode.postMessage({ type: 'debugGenerate', value: kind, ny: id, button: rare ? 1 : 0 });
    };
    document.getElementById('dbgItemList').onchange = (e) => {
      document.getElementById('dbgId').value = e.target.value || '0';
    };
    previewMusicBtn.onclick = async () => {
      saveLaunchPref('matchMusic', matchMusic.value || '');
      setBgmStatus('Preparing…');
      await unlockBgmAudio();
      vscode.postMessage({ type: 'previewMusic' });
    };
    pauseMusicBtn.onclick = () => vscode.postMessage({ type: 'pauseMusic' });
    stopMusicBtn.onclick = () => vscode.postMessage({ type: 'stopMusic' });
    document.getElementById('dbgPlayMusic').onclick = () => {
      const id = document.getElementById('dbgMusic').value || '';
      if (id) vscode.postMessage({ type: 'debugMusic', value: id });
    };
    if (bootIntro) {
      bootIntro.onchange = () => {
        if (bootIntro.value === '__add__') {
          bootIntro.value = bootIntroLast || 'default';
          vscode.postMessage({ type: 'addBootIntro' });
          return;
        }
        if (bootIntro.value === '__remove__') {
          bootIntro.value = bootIntroLast || 'default';
          vscode.postMessage({ type: 'removeBootIntro' });
          return;
        }
        if (bootIntro.value === '__pool__') {
          bootIntro.value = bootIntroLast || 'default';
          vscode.postMessage({ type: 'configureBootRandomPool' });
          return;
        }
        bootIntroLast = bootIntro.value || 'default';
        saveLaunchPref('bootIntro', bootIntroLast);
      };
    }
    if (vaultAction) {
      vaultAction.onchange = () => {
        syncMatchVisibility();
        saveLaunchPref('vaultAction', vaultAction.value);
      };
    }
    if (vaultIntro) {
      vaultIntro.onchange = () => {
        if (vaultIntro.value === '__add__') {
          vaultIntro.value = vaultIntroLast || 'intro3';
          vscode.postMessage({ type: 'addBootIntro' });
          return;
        }
        if (vaultIntro.value === '__pool__') {
          vaultIntro.value = vaultIntroLast || 'intro3';
          vscode.postMessage({ type: 'configureBootRandomPool' });
          return;
        }
        vaultIntroLast = vaultIntro.value || 'intro3';
        saveLaunchPref('vaultIntro', vaultIntroLast);
      };
    }
    if (vaultPaletteChar) {
      vaultPaletteChar.onchange = () => {
        saveLaunchPref('vaultPaletteChar', vaultPaletteChar.value);
      };
    }
    document.getElementById('backToSsf2').onclick = () => vscode.postMessage({ type: 'closeMatchSettings' });
    document.getElementById('applyMatch').onclick = () => vscode.postMessage({ type: 'applyMatch' });
    document.getElementById('gotoMenu').onclick = () => vscode.postMessage({ type: 'gotoMenu' });
    document.getElementById('catalogRefresh').onclick = () => vscode.postMessage({ type: 'refreshCatalog' });
    document.getElementById('settings').onclick = () => vscode.postMessage({ type: 'openSettings' });
    document.getElementById('refresh').onclick = () => vscode.postMessage({ type: 'reloadWindow' });
    document.getElementById('settingsClose').onclick = hideSettings;
    document.getElementById('setPickFolder').onclick = () => vscode.postMessage({ type: 'pickFolder' });
    document.getElementById('pickFolder').onclick = () => vscode.postMessage({ type: 'pickFolder' });
    const compileFolderSel = document.getElementById('compileFolder');
    document.getElementById('compileFolderBtn').onclick = () => {
      idleReason.textContent = 'Compiling…';
      vscode.postMessage({ type: 'compileFolder', id: compileFolderSel.value });
    };
    window.addEventListener('message', (event) => {
      const msg = event.data;
      if (!msg || (msg.type !== 'compileFolders' && msg.type !== 'compileResult')) return;
      if (msg.type === 'compileResult') {
        idleReason.textContent = msg.text || '';
        return;
      }
      compileFolderSel.innerHTML = '';
      (msg.folders || []).forEach((item) => {
        const opt = document.createElement('option');
        opt.value = item.id;
        opt.textContent = item.label;
        compileFolderSel.appendChild(opt);
      });
    });
    document.getElementById('launchExe').onclick = () => {
      idleReason.textContent = idleSuperDebug && idleSuperDebug.checked
        ? 'Starting super debug…'
        : 'Launching SSF2…';
      vscode.postMessage({ type: 'launchExe' });
    };
    document.getElementById('closeExe').onclick = () => vscode.postMessage({ type: 'closeExe' });
    document.getElementById('restartExe').onclick = () => {
      idleReason.textContent = 'Restarting SSF2…';
      vscode.postMessage({ type: 'restartExe' });
    };

    startingMenu.onchange = () => {
      syncMatchVisibility();
      saveLaunchPref('startingMenu', startingMenu.value);
      const v = startingMenu.value;
      if (v === 'arena') {
        const cur = matchArenaMode && matchArenaMode.value;
        const id = cur === 'soccer' || cur === 'basketball' ? cur : 'soccer';
        if (matchArenaMode) matchArenaMode.value = id;
        saveLaunchPref('matchCustomModeId', id);
      } else if (v === 'multiman') {
        const ok = { man10: 1, man100: 1, min3: 1, endless: 1, cruel: 1 };
        const cur = matchModeStage && matchModeStage.value;
        const id = ok[cur] ? cur : 'man10';
        if (matchModeStage) matchModeStage.value = id;
        saveLaunchPref('matchCustomModeId', id);
      } else if (v === 'btt' || v === 'hrc' || v === 'crystal') {
        const cur = matchModeStage && matchModeStage.value;
        if (matchModeStage && matchModeStage.options.length) {
          const ok = Array.from(matchModeStage.options).some((o) => o.value === cur);
          const id = ok && cur ? cur : matchModeStage.options[0].value;
          matchModeStage.value = id;
          saveLaunchPref('matchCustomModeId', id);
        }
      }
    };
    loadMatchImmediate.onchange = () => {
      loadMatchImmediate.dataset.userTouched = '1';
      saveLaunchPref('loadMatchImmediate', loadMatchImmediate.checked);
    };
    if (stayInMatchSetup) {
      stayInMatchSetup.onchange = () => {
        saveLaunchPref('stayInMatchSetupOnApply', stayInMatchSetup.checked);
      };
    }
    matchStage.onchange = () => saveLaunchPref('matchStage', matchStage.value);
    matchMusic.onchange = () => {
      saveLaunchPref('matchMusic', matchMusic.value || '');
      syncMusicButtons();
    };
    if (matchHazards) matchHazards.onchange = () => saveLaunchPref('matchHazards', matchHazards.checked);
    if (matchUsingTime) matchUsingTime.onchange = () => saveLaunchPref('matchUsingTime', matchUsingTime.checked);
    if (matchUsingLives) matchUsingLives.onchange = () => saveLaunchPref('matchUsingLives', matchUsingLives.checked);
    if (matchTime) matchTime.onchange = () => saveLaunchPref('matchTime', Number(matchTime.value) || 8);
    if (matchLives) matchLives.onchange = () => saveLaunchPref('matchLives', Number(matchLives.value) || 3);
    matchDifficulty.onchange = () => saveLaunchPref('matchDifficulty', matchDifficulty.value);
    matchSoloLives.onchange = () => saveLaunchPref('matchLives', Number(matchSoloLives.value) || 3);
    matchP1Solo.onchange = () => saveLaunchPref('matchP1', matchP1Solo.value);
    matchP1Mode.onchange = () => saveLaunchPref('matchP1', matchP1Mode.value);
    matchP1Rttf.onchange = () => saveLaunchPref('matchP1', matchP1Rttf.value);
    matchModeStage.onchange = () => {
      saveLaunchPref('matchCustomModeId', matchModeStage.value);
    };
    matchArenaMode.onchange = () => {
      saveLaunchPref('matchCustomModeId', matchArenaMode.value);
    };
    matchScoreLimit.onchange = () => saveLaunchPref('matchScoreLimit', Number(matchScoreLimit.value) || 5);
    if (sandbagOverride) sandbagOverride.onchange = () => {
      saveLaunchPref('sandbagOverride', sandbagOverride.checked);
      if (sandbagCharacter) sandbagCharacter.disabled = !sandbagOverride.checked;
    };
    if (sandbagCharacter) sandbagCharacter.onchange = () => saveLaunchPref('sandbagCharacter', sandbagCharacter.value);
    if (useMusicOnLaunch) useMusicOnLaunch.onchange = () => saveLaunchPref('useMusicOnLaunch', useMusicOnLaunch.checked);
    if (useMusicEverywhere) useMusicEverywhere.onchange = () => saveLaunchPref('useMusicEverywhere', useMusicEverywhere.checked);
    if (matchEvent) matchEvent.onchange = () => {
      saveLaunchPref('eventId', matchEvent.value);
      syncEventUi();
    };
    if (eventOverride) eventOverride.onchange = () => {
      saveLaunchPref('eventOverride', eventOverride.checked);
      syncEventUi();
    };
    if (eventCharacter) eventCharacter.onchange = () => saveLaunchPref('eventCharacter', eventCharacter.value);
    idleHideIde.onchange = () => {
      hideIde.checked = idleHideIde.checked;
      setHideIde.checked = idleHideIde.checked;
      saveLaunchPref('hideIdeChrome', idleHideIde.checked);
    };
    if (idleSuperDebug) {
      idleSuperDebug.onchange = () => {
        saveLaunchPref('superDebug', idleSuperDebug.checked);
      };
    }
    hideIde.onchange = () => {
      idleHideIde.checked = hideIde.checked;
      setHideIde.checked = hideIde.checked;
      saveLaunchPref('hideIdeChrome', hideIde.checked);
    };

    img.addEventListener('mousedown', (ev) => {
      if (!inputOn || settingsPane.classList.contains('visible') || matchSettingsOpen || debugOpen) return;
      const p = norm(ev); if (!p) return;
      down = true; img.focus();
      vscode.postMessage({ type: 'mousedown', ...p, button: ev.button });
    });
    window.addEventListener('mouseup', (ev) => {
      if (!down) return; down = false;
      if (!inputOn) return;
      const p = norm(ev) || { nx: 0, ny: 0 };
      vscode.postMessage({ type: 'mouseup', ...p, button: ev.button });
    });
    img.addEventListener('mousemove', (ev) => {
      if (!inputOn || matchSettingsOpen || debugOpen) return;
      const p = norm(ev); if (!p) return;
      const now = performance.now();
      if (!down && now - lastMove < 16) return;
      lastMove = now;
      vscode.postMessage({ type: 'mousemove', ...p, button: down ? ev.button : -1 });
    });
    function forwardKey(ev, type) {
      if (!inputOn && !playFocus) return;
      if (settingsPane.classList.contains('visible') || matchSettingsOpen || debugOpen) return;
      if (ev.key === 'Escape' && playFocus) {
        ev.preventDefault();
        ev.stopPropagation();
        if (type === 'keydown') {
          if (!ev.repeat) beginEscHold();
        } else {
          clearEscHold();
        }
        return;
      }
      if (!inputOn && !playFocus) return;
      ev.preventDefault();
      ev.stopPropagation();
      vscode.postMessage({ type, key: ev.key, code: ev.code });
    }
    img.addEventListener('keydown', (ev) => forwardKey(ev, 'keydown'));
    img.addEventListener('keyup', (ev) => forwardKey(ev, 'keyup'));
    window.addEventListener('keydown', (ev) => {
      if (!playFocus && !inputOn) return;
      if (ev.target === img) return;
      if (settingsPane.classList.contains('visible') || matchSettingsOpen || debugOpen) return;
      forwardKey(ev, 'keydown');
    }, true);
    window.addEventListener('keyup', (ev) => {
      if (!playFocus && !inputOn) return;
      if (ev.target === img) return;
      if (settingsPane.classList.contains('visible') || matchSettingsOpen || debugOpen) return;
      forwardKey(ev, 'keyup');
    }, true);
  </script>
</body>
</html>`;
  }
}
