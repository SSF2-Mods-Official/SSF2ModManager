import * as vscode from "vscode";
import { StreamServer } from "./streamServer";
import { StreamPanel } from "./streamPanel";
import {
  launchSsf2Debug,
  launchSsf2Exe,
  pickDebugFolder,
  resolveDebugFolder,
} from "./debugLaunch";
import { exitIdeFocusMode } from "./ideFocusMode";
import { pointSdkAtNext2Flash } from "./next2flash";
import { streamTabShowOptions } from "./streamTab";

let activePanel: StreamPanel | undefined;
let starting = false;
let debugStartToken = 0;
let escHoldTimer: ReturnType<typeof setInterval> | undefined;
let escHoldStarted = 0;
let escLastPulse = 0;
let replScrollTimer: ReturnType<typeof setInterval> | undefined;
let replFocusUntil = 0;

function cfg() {
  return vscode.workspace.getConfiguration("ssf2Stream");
}

export function isSsf2DebugSession(session: vscode.DebugSession): boolean {
  if (session.type === "swf") {
    return true;
  }
  const name = session.name.toLowerCase();
  return name.includes("ssf2") || name.includes("launch swf") || name.includes("shell") || name.includes("no compile");
}

export async function openStreamPanel(
  context: vscode.ExtensionContext,
  opts?: { fromDebug?: boolean; restart?: boolean; startStream?: boolean; showSettings?: boolean }
): Promise<void> {
  if (starting) {
    for (let i = 0; i < 40 && starting; i++) {
      await new Promise((r) => setTimeout(r, 50));
    }
  }
  starting = true;
  try {
    const tab = streamTabShowOptions();
    try {
      await vscode.commands.executeCommand("workbench.action.focusFirstEditorGroup");
    } catch {
      /* ignore */
    }
    const shouldStream = !!(opts?.fromDebug || opts?.startStream || opts?.restart);
    if (activePanel) {
      if (shouldStream) {
        activePanel.startStreaming(!!opts?.restart || !!opts?.fromDebug, {
          owned: !!opts?.fromDebug,
        });
      } else if (!opts?.showSettings) {
        activePanel.showLaunchScreen();
      }
      activePanel.reveal(tab.preserveFocus);
    } else {
      const server = new StreamServer(context.extensionPath, cfg());
      activePanel = new StreamPanel(context, server, () => {
        activePanel = undefined;
      });
      if (shouldStream) {
        activePanel.startStreaming(false, { owned: !!opts?.fromDebug });
      } else {
        activePanel.showLaunchScreen();
      }
      activePanel.reveal(tab.preserveFocus);
    }
    activePanel.appendGameLogLine("[ssf2-stream] stream tab column=1");

    if (opts?.showSettings) {
      activePanel.showSettingsOverlay();
    }

    if (opts?.fromDebug && cfg().get<boolean>("focusDebugConsole", true)) {
      await new Promise((r) => setTimeout(r, 120));
      await vscode.commands.executeCommand("workbench.debug.action.focusRepl");
      markReplFocused();
      activePanel.reveal(true);
    }
  } finally {
    starting = false;
  }
}

export function stopStreamPanel(): void {
  activePanel?.dispose({ stopDebug: true });
  activePanel = undefined;
}

function clearEscHold(): void {
  if (escHoldTimer) {
    clearInterval(escHoldTimer);
    escHoldTimer = undefined;
  }
  escHoldStarted = 0;
  activePanel?.notifyEscape(false);
}

function pulseEscapeHold(): void {
  if (!activePanel?.isPlayFocus) {
    clearEscHold();
    return;
  }
  const now = Date.now();
  escLastPulse = now;
  if (!escHoldStarted) {
    escHoldStarted = now;
  }
  activePanel.notifyEscape(true);
  if (!escHoldTimer) {
    escHoldTimer = setInterval(() => {
      if (Date.now() - escLastPulse > 400) {
        clearEscHold();
        return;
      }
      if (Date.now() - escHoldStarted >= 2000) {
        clearEscHold();
        void activePanel?.setPlayFocus(false);
      }
    }, 100);
  }
}

function markReplFocused(): void {
  replFocusUntil = Date.now() + 5000;
}

function isLikelyDebugConsoleFocused(): boolean {
  if (Date.now() < replFocusUntil) {
    return true;
  }
  if (activePanel?.isPanelActive) {
    return false;
  }
  if (vscode.window.activeTextEditor) {
    return false;
  }
  return true;
}

async function scrollDebugConsoleToBottom(): Promise<void> {
  if (!cfg().get<boolean>("autoScrollDebugConsole", true)) {
    return;
  }
  const session = vscode.debug.activeDebugSession;
  if (!session || !isSsf2DebugSession(session)) {
    return;
  }
  if (isLikelyDebugConsoleFocused()) {
    return;
  }
  if (activePanel?.isPlayFocus) {
    return;
  }
  const restorePanel = activePanel?.isPanelActive;
  try {
    await vscode.commands.executeCommand("workbench.debug.action.focusRepl");
    await vscode.commands.executeCommand("list.focusLast");
  } catch {
    /* ignore */
  }
  if (restorePanel && activePanel) {
    activePanel.reveal(true);
  } else {
    try {
      await vscode.commands.executeCommand("workbench.action.focusActiveEditorGroup");
    } catch {
      /* ignore */
    }
  }
}

function startReplAutoScroll(context: vscode.ExtensionContext): void {
  if (replScrollTimer) {
    return;
  }
  replScrollTimer = setInterval(() => {
    void scrollDebugConsoleToBottom();
  }, 5000);
  context.subscriptions.push({
    dispose: () => {
      if (replScrollTimer) {
        clearInterval(replScrollTimer);
        replScrollTimer = undefined;
      }
    },
  });
}

export function activate(context: vscode.ExtensionContext): void {
  void pointSdkAtNext2Flash();
  startReplAutoScroll(context);

  context.subscriptions.push(
    vscode.commands.registerCommand("ssf2Stream.openPanel", () => openStreamPanel(context)),
    vscode.commands.registerCommand("ssf2Stream.stopPanel", () => stopStreamPanel()),
    vscode.commands.registerCommand("ssf2Stream.openSettings", async () => {
      await openStreamPanel(context, { showSettings: true });
    }),
    vscode.commands.registerCommand("ssf2Stream.pickDebugFolder", () => pickDebugFolder()),
    vscode.commands.registerCommand("ssf2Stream.launchDebug", async () => {
      if (!activePanel) {
        await openStreamPanel(context);
      }
      await activePanel?.runLaunchDebug();
    }),
    vscode.commands.registerCommand("ssf2Stream.launchExe", async () => {
      if (!activePanel) {
        await openStreamPanel(context);
      }
      await activePanel?.runLaunchExe();
    }),
    vscode.commands.registerCommand("ssf2Stream.restartExe", async () => {
      if (!activePanel) {
        await openStreamPanel(context);
      }
      await activePanel?.restartExe();
    }),
    vscode.commands.registerCommand("ssf2Stream.restoreWindow", async () => {
      if (!activePanel) {
        await openStreamPanel(context);
      }
      activePanel?.requestRestoreWindow();
    }),
    vscode.commands.registerCommand("ssf2Stream.focusGameOutput", () => {
      activePanel?.focusGameOutput();
    }),
    vscode.commands.registerCommand("ssf2Stream.togglePlayFocus", async () => {
      if (!activePanel) {
        await openStreamPanel(context, { startStream: true });
      }
      if (!activePanel) {
        return;
      }
      await activePanel.setPlayFocus(!activePanel.isPlayFocus);
    }),
    vscode.commands.registerCommand("ssf2Stream.releasePlayFocus", async () => {
      await activePanel?.setPlayFocus(false);
    }),
    vscode.commands.registerCommand("ssf2Stream.escapeHold", () => {
      pulseEscapeHold();
    }),
    vscode.commands.registerCommand("ssf2Stream.gameVk", async (vk?: number) => {
      if (typeof vk !== "number") {
        return;
      }
      await activePanel?.tapGameVk(vk);
    }),
    vscode.commands.registerCommand("ssf2Stream.markReplFocus", () => {
      markReplFocused();
    }),
    vscode.commands.registerCommand("ssf2Stream.copyLastUncaughtError", async () => {
      if (!activePanel) {
        await openStreamPanel(context);
      }
      await activePanel?.copyLastUncaughtError();
    }),
    vscode.debug.onDidStartDebugSession(async (session) => {
      if (!cfg().get<boolean>("autoOpenOnDebug", true)) {
        return;
      }
      if (!isSsf2DebugSession(session)) {
        return;
      }
      const token = ++debugStartToken;
      await new Promise((r) => setTimeout(r, 600));
      if (token !== debugStartToken) {
        return;
      }
      try {
        await openStreamPanel(context, { fromDebug: true, restart: true, startStream: true });
        activePanel?.ensureGameLogWatch({ show: false, fromStart: false });
        activePanel?.appendGameLogLine(
          "[ssf2-stream] Debug session — watching game.log for UNCAUGHT (auto-dismiss via ssf2_stream)"
        );
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        void vscode.window.showErrorMessage(`SSF2 stream: ${msg}`);
      }
    }),
    vscode.debug.onDidTerminateDebugSession((session) => {
      if (isSsf2DebugSession(session)) {
        void exitIdeFocusMode();
        activePanel?.onGameClosed();
        activePanel?.showLaunchScreen();
      }
    }),
    vscode.window.onDidChangeActiveTextEditor(() => {
      if (vscode.window.activeTextEditor) {
        replFocusUntil = 0;
      }
    })
  );

  if (cfg().get<boolean>("launchOnVscStartup", true)) {
    void openStreamPanel(context);
  }

  void resolveDebugFolder();
}

export function deactivate(): void {
  clearEscHold();
  void exitIdeFocusMode();
  if (activePanel) {
    const panel = activePanel;
    activePanel = undefined;
    panel.dispose({ stopDebug: false });
  }
}
