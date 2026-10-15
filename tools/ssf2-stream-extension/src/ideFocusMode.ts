import * as vscode from "vscode";

let active = false;
let savedSidebarVisible: boolean | null = null;
let savedAuxiliaryVisible: boolean | null = null;

export type IdeFocusPanel = "debugConsole" | "ssf2Game";

async function isSideBarVisible(): Promise<boolean> {
  try {
    return !!(await vscode.commands.executeCommand<boolean>(
      "workbench.action.isSideBarVisible"
    ));
  } catch {
    return true;
  }
}

async function isAuxiliaryBarVisible(): Promise<boolean> {
  try {
    return !!(await vscode.commands.executeCommand<boolean>(
      "workbench.action.isAuxiliaryBarVisible"
    ));
  } catch {
    return false;
  }
}

async function hideSideBarIfVisible(): Promise<void> {
  if (!(await isSideBarVisible())) {
    return;
  }
  try {
    await vscode.commands.executeCommand("workbench.action.closeSidebar");
  } catch {
    /* ignore */
  }
}

async function hideAuxiliaryBarIfVisible(): Promise<void> {
  if (!(await isAuxiliaryBarVisible())) {
    return;
  }
  try {
    await vscode.commands.executeCommand("workbench.action.closeAuxiliaryBar");
  } catch {
    /* ignore */
  }
}

async function restoreSideBarIfNeeded(): Promise<void> {
  if (savedSidebarVisible !== true) {
    return;
  }
  try {
    if (!(await isSideBarVisible())) {
      await vscode.commands.executeCommand("workbench.action.toggleSidebarVisibility");
    }
  } catch {
    /* ignore */
  }
}

async function restoreAuxiliaryBarIfNeeded(): Promise<void> {
  if (savedAuxiliaryVisible !== true) {
    return;
  }
  try {
    if (!(await isAuxiliaryBarVisible())) {
      await vscode.commands.executeCommand("workbench.action.toggleAuxiliaryBar");
    }
  } catch {
    /* ignore */
  }
}

/** Hide sidebar / chat (auxiliary) bar; keep stream editor + chosen bottom panel. */
export async function enterIdeFocusMode(
  revealStream: () => void,
  opts?: { panel?: IdeFocusPanel }
): Promise<void> {
  const panel = opts?.panel || "debugConsole";
  if (active) {
    await hideSideBarIfVisible();
    await hideAuxiliaryBarIfVisible();
    revealStream();
    await focusBottom(panel);
    return;
  }
  active = true;
  savedSidebarVisible = await isSideBarVisible();
  savedAuxiliaryVisible = await isAuxiliaryBarVisible();
  await hideSideBarIfVisible();
  await hideAuxiliaryBarIfVisible();
  revealStream();
  try {
    await vscode.commands.executeCommand("workbench.action.closeOtherEditors");
  } catch {
    /* ignore */
  }
  try {
    await vscode.commands.executeCommand("workbench.action.togglePanel");
  } catch {
    /* ignore */
  }
  await focusBottom(panel);
  revealStream();
}

async function focusBottom(panel: IdeFocusPanel): Promise<void> {
  try {
    if (panel === "ssf2Game") {
      // Output: SSF2 Game channel (created by GameLogTail).
      await vscode.commands.executeCommand("workbench.action.output.toggleOutput");
      await vscode.commands.executeCommand(
        "workbench.action.output.show",
        "ssf2-shell.ssf2-stream.SSF2 Game"
      );
      // Fallback: some builds only accept the channel name.
      await vscode.commands.executeCommand("ssf2Stream.focusGameOutput");
    } else {
      await vscode.commands.executeCommand("workbench.debug.action.focusRepl");
    }
  } catch {
    /* ignore */
  }
}

export async function exitIdeFocusMode(): Promise<void> {
  if (!active) {
    return;
  }
  active = false;
  await restoreSideBarIfNeeded();
  await restoreAuxiliaryBarIfNeeded();
  savedSidebarVisible = null;
  savedAuxiliaryVisible = null;
}

export function isIdeFocusMode(): boolean {
  return active;
}
