import * as vscode from "vscode";
import * as path from "path";
import * as fs from "fs";
import { spawn, spawnSync, ChildProcess } from "child_process";
import { ensureMmCfgForTraces } from "./gameLogTail";
import { writeCmd, sanitizeVersusStage, listEvents } from "./catalog";
import { hasManagerLaunch, readManagerFlat } from "./managerPrefs";
import { pickAneExtdir, pickLaunchAdl } from "./adlPath";
import { adlExe } from "./next2flash";
import { resolveDevFolderAt } from "./devFolder";
import { stateDir, ssf2mmDir } from "./ssf2mmPaths";

function cfg() {
  const real = vscode.workspace.getConfiguration("ssf2Stream");
  const flat = readManagerFlat();
  if (!flat) {
    return real;
  }
  return {
    get<T>(key: string, fallback?: T): T {
      if (Object.prototype.hasOwnProperty.call(flat, key)) {
        return flat[key] as T;
      }
      return real.get<T>(key, fallback as T);
    },
    update: real.update.bind(real),
    inspect: real.inspect.bind(real),
  } as typeof real;
}

function norm(p: string): string {
  return path.normalize(p).replace(/\\/g, "/").toLowerCase();
}

export type StartingMenu =
  | "home"
  | "group"
  | "online"
  | "data"
  | "options"
  | "controls"
  | "vault"
  | "solo"
  | "versus_css"
  | "training_css"
  | "classic"
  | "allstar"
  | "btt"
  | "hrc"
  | "multiman"
  | "crystal"
  | "rttf"
  | "arena"
  | "events";

export interface LaunchPlayerPrefs {
  character: string;
  human?: boolean;
  level?: number;
  team?: number;
  exist?: boolean;
  /** CSS name tag (≤10 chars). */
  name?: string;
}

export interface LaunchMatchPrefs {
  immediate: boolean;
  stage: string;
  music?: string;
  players: LaunchPlayerPrefs[];
  hazards?: boolean;
  usingTime?: boolean;
  usingLives?: boolean;
  lives?: number;
  time?: number;
  specialModes?: number;
  difficulty?: string | number;
  customModeID?: string;
  scoreLimit?: number;
  sandbagCharacter?: string;
}

export interface LaunchPrefs {
  screen: StartingMenu;
  match?: LaunchMatchPrefs;
  gotoMenu?: boolean;
}

const MATCH_SCREENS = new Set<string>([
  "versus_css",
  "training_css",
  "classic",
  "allstar",
  "btt",
  "hrc",
  "multiman",
  "crystal",
  "rttf",
  "arena",
]);

function playerFromConfig(
  char: string,
  human: boolean,
  level: number,
  team?: number,
  name?: string
): LaunchPlayerPrefs {
  const p: LaunchPlayerPrefs = {
    character: char || "",
    human,
    level: Math.max(1, Math.min(9, level || 1)),
  };
  if (team !== undefined) {
    // SSF2 only has teams 1–3 (no team 4).
    p.team = Math.max(1, Math.min(3, team || 1));
  }
  if (name) {
    p.name = String(name).slice(0, 10);
  }
  return p;
}

function isGameRoot(dir: string): boolean {
  return (
    fs.existsSync(path.join(dir, "Super Smash Flash 2 Beta.exe")) ||
    fs.existsSync(path.join(dir, "SSF2.exe")) ||
    (fs.existsSync(path.join(dir, "SSF2.swf")) && fs.existsSync(path.join(dir, "data")))
  );
}

export function resolveDebugFolder(): string | undefined {
  const configured = (cfg().get<string>("debugFolder") || "").trim();
  if (configured && fs.existsSync(configured)) {
    return configured;
  }
  const wf = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  if (!wf) {
    return undefined;
  }
  const candidates = [
    path.join(wf, "SSF2 Mod Manager", "SSF2 Shell"),
    path.join(wf, "SSF2 Shell"),
    wf,
    path.dirname(wf),
  ];
  for (const candidate of candidates) {
    if (
      isGameRoot(candidate) ||
      fs.existsSync(path.join(candidate, "asconfig.json")) ||
      resolveDevFolderAt(candidate) !== undefined
    ) {
      return candidate;
    }
  }
  return undefined;
}

/** Playable install: legacy `build/`, or the Shell folder itself (exe + data + expansions). */
export function resolveBuildRoot(folder?: string): string | undefined {
  const shell = folder || resolveDebugFolder();
  if (!shell) {
    return undefined;
  }
  const build = path.join(shell, "build");
  if (
    fs.existsSync(build) &&
    (isGameRoot(build) || fs.existsSync(path.join(build, "SSF2-app.xml")))
  ) {
    return build;
  }
  if (isGameRoot(shell)) {
    return shell;
  }
  const parent = path.dirname(shell);
  if (isGameRoot(parent)) {
    return parent;
  }
  return fs.existsSync(build) ? build : undefined;
}

/** Folder that contains asconfig.json. Current layout is runtime/shell-dev beside the Shell. */
export function resolveDevFolder(folder?: string): string | undefined {
  const shell = folder || resolveDebugFolder();
  if (!shell) {
    return undefined;
  }
  return resolveDevFolderAt(shell);
}

/** Marker written after a successful Sorenson FLV encode (invalidates old H.264 FLVs). */
const BOOT_INTRO_OK_SUFFIX = ".ok";

const FFMPEG_FLV_ARGS = [
  "-c:v",
  "flv",
  "-b:v",
  "1200k",
  "-vf",
  "scale=trunc(iw/2)*2:trunc(ih/2)*2",
  "-c:a",
  "libmp3lame",
  "-ar",
  "44100",
  "-ac",
  "2",
  "-b:a",
  "128k",
  "-f",
  "flv",
] as const;

export type BootIntroEncodeProgress = {
  /** 0..1 when known; otherwise -1 for indeterminate */
  ratio: number;
  label: string;
};

function parseFfmpegClock(raw: string): number {
  const m = /(?:(\d+):)?(\d+):(\d+(?:\.\d+)?)/.exec(raw.trim());
  if (!m) {
    return 0;
  }
  const h = Number(m[1] || 0);
  const min = Number(m[2] || 0);
  const sec = Number(m[3] || 0);
  return h * 3600 + min * 60 + sec;
}

type BootIntroEncodePlan = {
  encodeSrc: string;
  dest: string;
  okMarker: string;
  /** Already good — skip encode */
  reuse: boolean;
};

function planBootIntroEncode(
  id: string,
  sourcePath: string,
  buildRoot?: string
): BootIntroEncodePlan | null {
  const src = path.normalize(sourcePath);
  if (!id || !src || !fs.existsSync(src)) {
    return null;
  }
  const build = buildRoot || resolveBuildRoot();
  if (!build) {
    return null;
  }
  const destDir = path.join(build, "expansions", "intros");
  const dest = path.join(destDir, `${id}.flv`);
  const okMarker = dest + BOOT_INTRO_OK_SUFFIX;
  const srcLower = src.toLowerCase();

  let encodeSrc = src;
  if (srcLower.endsWith(".flv") && norm(src).includes("/expansions/intros/")) {
    const siblingMp4 = src.replace(/\.flv$/i, ".mp4");
    if (fs.existsSync(siblingMp4)) {
      encodeSrc = siblingMp4;
    }
  }

  try {
    fs.mkdirSync(destDir, { recursive: true });
  } catch {
    return null;
  }

  try {
    if (
      fs.existsSync(dest) &&
      fs.existsSync(okMarker) &&
      fs.statSync(dest).size > 0
    ) {
      const sStat = fs.statSync(encodeSrc);
      const dStat = fs.statSync(dest);
      const oStat = fs.statSync(okMarker);
      if (dStat.mtimeMs >= sStat.mtimeMs && oStat.mtimeMs >= dStat.mtimeMs - 1000) {
        return { encodeSrc, dest, okMarker, reuse: true };
      }
    }
  } catch {
    /* encode */
  }

  try {
    if (fs.existsSync(okMarker)) {
      fs.unlinkSync(okMarker);
    }
  } catch {
    /* ignore */
  }

  return { encodeSrc, dest, okMarker, reuse: false };
}

function markBootIntroOk(okMarker: string): void {
  try {
    fs.writeFileSync(okMarker, `sorenson\n${new Date().toISOString()}\n`, "utf8");
  } catch {
    /* still usable without marker */
  }
}

/**
 * Convert a custom intro to classic Flash FLV (Sorenson H.263 + MP3)
 * under build/expansions/intros/{id}.flv.
 * Returns the .flv path on success, or "" if ffmpeg failed / missing.
 */
export function ensureBootIntroAsset(
  id: string,
  sourcePath: string,
  buildRoot?: string
): string {
  const plan = planBootIntroEncode(id, sourcePath, buildRoot);
  if (!plan) {
    return "";
  }
  if (plan.reuse) {
    return plan.dest;
  }
  try {
    const r = spawnSync(
      "ffmpeg",
      ["-y", "-i", plan.encodeSrc, ...FFMPEG_FLV_ARGS, plan.dest],
      { encoding: "utf8", timeout: 300_000, windowsHide: true }
    );
    if (r.status === 0 && fs.existsSync(plan.dest) && fs.statSync(plan.dest).size > 0) {
      markBootIntroOk(plan.okMarker);
      return plan.dest;
    }
  } catch {
    /* fail below */
  }
  return "";
}

/**
 * Async encode with ffmpeg stderr progress (time / duration).
 * onProgress receives ratio 0..1 when duration is known.
 */
export function ensureBootIntroAssetAsync(
  id: string,
  sourcePath: string,
  buildRoot?: string,
  onProgress?: (p: BootIntroEncodeProgress) => void
): Promise<string> {
  const plan = planBootIntroEncode(id, sourcePath, buildRoot);
  if (!plan) {
    return Promise.resolve("");
  }
  if (plan.reuse) {
    onProgress?.({ ratio: 1, label: "Already converted" });
    return Promise.resolve(plan.dest);
  }

  return new Promise((resolve) => {
    onProgress?.({ ratio: 0, label: "Starting ffmpeg…" });
    let durationSec = 0;
    let lastRatio = -1;
    let settled = false;
    const finish = (result: string) => {
      if (settled) {
        return;
      }
      settled = true;
      resolve(result);
    };

    let child: ChildProcess;
    try {
      child = spawn(
        "ffmpeg",
        ["-y", "-i", plan.encodeSrc, ...FFMPEG_FLV_ARGS, plan.dest],
        { windowsHide: true }
      );
    } catch {
      finish("");
      return;
    }

    const timer = setTimeout(() => {
      try {
        child.kill();
      } catch {
        /* ignore */
      }
      finish("");
    }, 300_000);

    const onChunk = (buf: Buffer | string) => {
      const text = typeof buf === "string" ? buf : buf.toString("utf8");
      const durMatch = /Duration:\s*(\d+:\d+:\d+(?:\.\d+)?)/i.exec(text);
      if (durMatch) {
        durationSec = Math.max(durationSec, parseFfmpegClock(durMatch[1]));
      }
      const timeMatch = /time=\s*(\d+:\d+:\d+(?:\.\d+)?)/i.exec(text);
      if (timeMatch) {
        const t = parseFfmpegClock(timeMatch[1]);
        let ratio = durationSec > 0.25 ? Math.min(0.99, t / durationSec) : -1;
        if (ratio >= 0 && Math.abs(ratio - lastRatio) < 0.01) {
          return;
        }
        lastRatio = ratio;
        const pct = ratio >= 0 ? `${Math.round(ratio * 100)}%` : "encoding…";
        const elapsed = `${Math.floor(t)}s`;
        onProgress?.({
          ratio,
          label: durationSec > 0 ? `${pct} (${elapsed} / ${Math.floor(durationSec)}s)` : `Encoding… ${elapsed}`,
        });
      }
    };

    child.stderr?.on("data", onChunk);
    child.stdout?.on("data", onChunk);
    child.on("error", () => {
      clearTimeout(timer);
      finish("");
    });
    child.on("close", (code) => {
      clearTimeout(timer);
      if (code === 0 && fs.existsSync(plan.dest) && fs.statSync(plan.dest).size > 0) {
        markBootIntroOk(plan.okMarker);
        onProgress?.({ ratio: 1, label: "Done" });
        finish(plan.dest);
        return;
      }
      finish("");
    });
  });
}

export function resolveGameExe(folder?: string): string | undefined {
  const build = resolveBuildRoot(folder);
  if (!build) {
    return undefined;
  }
  const candidates = [
    path.join(build, "Super Smash Flash 2 Beta.exe"),
    path.join(build, "SSF2.exe"),
  ];
  return candidates.find((p) => fs.existsSync(p));
}

export async function pickDebugFolder(): Promise<string | undefined> {
  const picked = await vscode.window.showOpenDialog({
    canSelectFiles: false,
    canSelectFolders: true,
    canSelectMany: false,
    openLabel: "Use as SSF2 Shell folder",
    title: "Select SSF2 Shell folder (contains asconfig.json)",
  });
  const folder = picked?.[0]?.fsPath;
  if (!folder) {
    return undefined;
  }
  await cfg().update("debugFolder", folder, vscode.ConfigurationTarget.Global);
  void vscode.window.showInformationMessage(`SSF2 debug folder set to: ${folder}`);
  return folder;
}

/** Real WorkspaceFolder that owns asconfig (SWF debugger rejects synthetic folders). */
async function ensureWorkspaceFolder(shellFolder: string): Promise<vscode.WorkspaceFolder | undefined> {
  const asconfigUri = vscode.Uri.file(path.join(shellFolder, "asconfig.json"));
  let wf = vscode.workspace.getWorkspaceFolder(asconfigUri);
  if (wf) {
    return wf;
  }

  const exact = vscode.workspace.workspaceFolders?.find(
    (f) => norm(f.uri.fsPath) === norm(shellFolder)
  );
  if (exact) {
    return exact;
  }

  const start = vscode.workspace.workspaceFolders?.length ?? 0;
  const ok = vscode.workspace.updateWorkspaceFolders(start, 0, {
    uri: vscode.Uri.file(shellFolder),
    name: "SSF2 Shell",
  });
  if (!ok) {
    return undefined;
  }

  for (let i = 0; i < 60; i++) {
    wf = vscode.workspace.getWorkspaceFolder(asconfigUri);
    if (wf) {
      return wf;
    }
    await new Promise((r) => setTimeout(r, 50));
  }
  return vscode.workspace.getWorkspaceFolder(asconfigUri);
}

/** Unpacked ANEs. ADL rejects -extdir paths that contain spaces, so prefer the repo-root junction. */
function resolveAneExtdir(dev?: string, folder?: string): string | undefined {
  const seen: string[] = [];
  const push = (dir: string | undefined) => {
    if (!dir) {
      return;
    }
    let cur = dir;
    for (let i = 0; i < 10; i++) {
      const link = path.join(cur, ".as3mxml-unpackaged-anes");
      if (!seen.includes(link)) {
        seen.push(link);
      }
      if (fs.existsSync(path.join(cur, ".git"))) {
        break;
      }
      const parent = path.dirname(cur);
      if (!parent || parent === cur) {
        break;
      }
      cur = parent;
    }
  };
  push(dev);
  push(folder);
  return pickAneExtdir(seen);
}

function relToWorkspace(wf: vscode.WorkspaceFolder, absPath: string): string {
  const base = wf.uri.fsPath;
  const rel = path.relative(base, absPath);
  if (!rel.startsWith("..") && !path.isAbsolute(rel)) {
    return rel.replace(/\\/g, "/");
  }
  return absPath;
}

async function runPrepareCopy(folder: string): Promise<void> {
  const prepare =
    [path.join(folder, "tools", "prepare_air_debug.ps1"), path.join(folder, "tools", "copy_starter_to_build.ps1")].find(
      (p) => fs.existsSync(p)
    );
  if (!prepare) {
    return;
  }
  try {
    const task = new vscode.Task(
      { type: "ssf2Stream", task: "prepareAirDebug" },
      vscode.TaskScope.Workspace,
      "Copy starter into build",
      "ssf2Stream",
      new vscode.ShellExecution(
        "powershell",
        ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", prepare],
        { cwd: folder }
      )
    );
    const exec = await vscode.tasks.executeTask(task);
    await new Promise<void>((resolve) => {
      const sub = vscode.tasks.onDidEndTaskProcess((e) => {
        if (e.execution === exec) {
          sub.dispose();
          resolve();
        }
      });
      setTimeout(() => {
        sub.dispose();
        resolve();
      }, 120_000);
    });
  } catch {
    /* continue */
  }
}

export function getLaunchPrefsFromConfig(): LaunchPrefs {
  const screen = (cfg().get<string>("startingMenu", "home") || "home") as StartingMenu;
  const rawStage = cfg().get<string>("matchStage", "battlefield") || "battlefield";
  const music = cfg().get<string>("matchMusic", "") || "";
  const p1 = cfg().get<string>("matchP1", "mario") || "mario";
  const p2 = cfg().get<string>("matchP2", "fox") || "fox";
  const p3 = cfg().get<string>("matchP3", "") || "";
  const p4 = cfg().get<string>("matchP4", "") || "";
  const immediate = cfg().get<boolean>("loadMatchImmediate", false);
  const versus = screen === "versus_css";
  const training = screen === "training_css";
  const arena = screen === "arena";
  const classicLike = screen === "classic" || screen === "allstar";
  const soloMode =
    classicLike ||
    screen === "btt" ||
    screen === "hrc" ||
    screen === "crystal" ||
    screen === "multiman" ||
    screen === "arena" ||
    screen === "rttf";
  const modeStage =
    screen === "btt" ||
    screen === "hrc" ||
    screen === "crystal" ||
    screen === "multiman";
  const rawModeId = cfg().get<string>("matchCustomModeId", "") || "";
  const customModeID = (() => {
    if (screen === "arena") {
      return rawModeId === "soccer" || rawModeId === "basketball" ? rawModeId : "soccer";
    }
    if (screen === "multiman") {
      const ok = ["man10", "man100", "min3", "endless", "cruel"];
      return ok.includes(rawModeId) ? rawModeId : "man10";
    }
    if (screen === "btt") {
      const id = rawModeId || "Level 1";
      return id.toLowerCase() === "character" ? "Level 1" : id;
    }
    if (screen === "crystal") {
      return rawModeId || "Level 1";
    }
    if (screen === "hrc") {
      return rawModeId || "";
    }
    if (modeStage) {
      return rawModeId || "";
    }
    return rawModeId;
  })();
  // Versus/training must use a real stage id — never arena/multiman leftovers in matchStage.
  const stage =
    versus || training || screen === "rttf"
      ? sanitizeVersusStage(rawStage)
      : modeStage
        ? customModeID || sanitizeVersusStage(rawStage)
        : sanitizeVersusStage(rawStage);
  const specialModes = Math.max(0, cfg().get<number>("matchSpecialModes", 0) || 0);
  // Versus/training/solo: honor Load immediately. Goto menu button forces immediate=false via writeApplyCommand opts.
  const wantImmediate =
    versus || training || soloMode ? !!immediate : false;

  if (!MATCH_SCREENS.has(screen)) {
    return { screen };
  }

  const players: LaunchPlayerPrefs[] = [
    playerFromConfig(
      p1,
      cfg().get<boolean>("matchP1Human", true) !== false,
      cfg().get<number>("matchP1Level", 1) || 1,
      arena ? cfg().get<number>("matchP1Team", 1) || 1 : undefined,
      cfg().get<string>("matchP1Tag", "") || ""
    ),
  ];
  if (versus || training || arena) {
    if (p2) {
      players.push(
        playerFromConfig(
          p2,
          training ? false : cfg().get<boolean>("matchP2Human", true) !== false,
          cfg().get<number>("matchP2Level", 3) || 3,
          arena ? cfg().get<number>("matchP2Team", 3) || 3 : undefined,
          cfg().get<string>("matchP2Tag", "") || ""
        )
      );
    }
    if (p3) {
      players.push(
        playerFromConfig(
          p3,
          cfg().get<boolean>("matchP3Human", false),
          cfg().get<number>("matchP3Level", 3) || 3,
          arena ? cfg().get<number>("matchP3Team", 1) || 1 : undefined,
          cfg().get<string>("matchP3Tag", "") || ""
        )
      );
    }
    if (p4) {
      players.push(
        playerFromConfig(
          p4,
          cfg().get<boolean>("matchP4Human", false),
          cfg().get<number>("matchP4Level", 3) || 3,
          arena ? cfg().get<number>("matchP4Team", 3) || 3 : undefined,
          cfg().get<string>("matchP4Tag", "") || ""
        )
      );
    }
  }

  const match: LaunchMatchPrefs = {
    immediate: wantImmediate,
    stage: modeStage ? customModeID || stage : stage,
    players,
    hazards: versus || training ? cfg().get<boolean>("matchHazards", true) : undefined,
    usingTime: versus ? cfg().get<boolean>("matchUsingTime", true) : undefined,
    usingLives: versus ? cfg().get<boolean>("matchUsingLives", true) : undefined,
    lives: versus || classicLike
      ? Math.max(
          1,
          Math.min(
            99,
            cfg().get<number>("matchLives", screen === "allstar" ? 5 : 3) ||
              (screen === "allstar" ? 5 : 3)
          )
        )
      : undefined,
    time: versus ? Math.max(1, Math.min(99, cfg().get<number>("matchTime", 8) || 8)) : undefined,
    specialModes: versus && specialModes > 0 ? specialModes : undefined,
    difficulty: classicLike
      ? cfg().get<string>("matchDifficulty", "NORMAL") || "NORMAL"
      : undefined,
    customModeID: modeStage || arena ? customModeID || undefined : undefined,
    scoreLimit: arena
      ? Math.max(1, Math.min(99, cfg().get<number>("matchScoreLimit", 5) || 5))
      : undefined,
  };
  const useMusic =
    !hasManagerLaunch() ||
    cfg().get<boolean>("useMusicOnLaunch", false) ||
    cfg().get<boolean>("useMusicEverywhere", false);
  if (music && useMusic) {
    match.music = music;
  }
  if (
    (screen === "hrc" || screen === "arena") &&
    cfg().get<boolean>("sandbagOverride", false) &&
    cfg().get<string>("sandbagCharacter", "")
  ) {
    match.sandbagCharacter = cfg().get<string>("sandbagCharacter", "");
  }
  return { screen, match };
}

export type BootCustomIntro = { id: string; label: string; path: string };

export type BootPoolEntry = {
  bootIntro: string;
  bootIntroPath?: string;
  bootIntroLabel?: string;
};

const BUILTIN_RANDOM_KEYS = ["default", "intro1", "intro2", "intro3"] as const;

function bootCustomsFromConfig(): Array<{ id: string; label: string; path: string }> {
  return (
    (cfg().get<Array<{ id?: string; label?: string; path?: string }>>("bootCustomIntros", []) ||
      []) as Array<{ id?: string; label?: string; path?: string }>
  )
    .filter((c) => c && c.id && c.path)
    .map((c) => ({
      id: String(c.id),
      label: String(c.label || c.id),
      path: path.normalize(String(c.path)),
    }));
}

/** Keys eligible for the random pool (builtins + custom:<id>). */
export function allBootIntroPoolKeys(): string[] {
  const keys: string[] = [...BUILTIN_RANDOM_KEYS];
  for (const c of bootCustomsFromConfig()) {
    keys.push(`custom:${c.id}`);
  }
  return keys;
}

/** Selected random-pool keys; empty setting means “all eligible”. */
export function getBootRandomPoolKeys(): string[] {
  const raw = (cfg().get<string[]>("bootRandomPool", []) || []).map((k) =>
    String(k || "")
      .trim()
      .toLowerCase()
  );
  const all = new Set(allBootIntroPoolKeys());
  const filtered = raw.filter((k) => k && all.has(k));
  return filtered.length ? filtered : [...all];
}

function resolvePoolEntry(
  key: string,
  build: string,
  customs: Array<{ id: string; label: string; path: string }>
): BootPoolEntry | null {
  const k = (key || "").toLowerCase();
  if (!k || k === "skip" || k === "none" || k === "random") {
    return null;
  }
  if (k === "default" || k === "intro1" || k === "intro2" || k === "intro3" || k === "v8" || k === "v9" || k === "beta") {
    return { bootIntro: k === "v8" ? "intro1" : k === "v9" ? "intro2" : k === "beta" ? "intro3" : k };
  }
  if (k.startsWith("custom:")) {
    const id = k.slice("custom:".length);
    const hit = customs.find((c) => c.id.toLowerCase() === id);
    if (!hit) {
      return null;
    }
    const flv = ensureBootIntroAsset(id, hit.path, build) || "";
    if (!flv) {
      return null;
    }
    return { bootIntro: `custom:${id}`, bootIntroPath: flv, bootIntroLabel: hit.label };
  }
  return null;
}

function buildRandomPool(build: string): BootPoolEntry[] {
  let customs = bootCustomsFromConfig();
  const keys = getBootRandomPoolKeys();
  const out: BootPoolEntry[] = [];
  let customsDirty = false;
  for (const key of keys) {
    const entry = resolvePoolEntry(key, build, customs);
    if (!entry) {
      continue;
    }
    if (entry.bootIntroPath && entry.bootIntro.startsWith("custom:")) {
      const id = entry.bootIntro.slice("custom:".length);
      customs = customs.map((c) => {
        if (c.id.toLowerCase() !== id || c.path === entry.bootIntroPath) {
          return c;
        }
        customsDirty = true;
        return { ...c, path: entry.bootIntroPath! };
      });
    }
    out.push(entry);
  }
  if (customsDirty) {
    void cfg().update("bootCustomIntros", customs, vscode.ConfigurationTarget.Global);
  }
  return out;
}

/**
 * Sticky boot-intro prefs for exe launches (launch.json is one-shot and cleared).
 * Written under build/data/ssf2mm/state/boot.json — the mod reads this and does not delete it.
 * When bootIntro is "random", the mod picks from randomPool each launch.
 */
export function writeBootPrefs(buildRoot?: string): boolean {
  const build = buildRoot || resolveBuildRoot();
  if (!build) {
    return false;
  }
  const dir = stateDir(build);
  try {
    fs.mkdirSync(dir, { recursive: true });
  } catch {
    return false;
  }

  const bootIntroRaw = (cfg().get<string>("bootIntro", "default") || "default").trim();
  const bootIntro = bootIntroRaw.toLowerCase() || "default";
  let customs = bootCustomsFromConfig();

  let bootIntroPath = "";
  let bootIntroLabel = "";
  let effectiveIntro = bootIntro;
  let randomPool: BootPoolEntry[] = [];

  if (bootIntro === "random") {
    randomPool = buildRandomPool(build);
    customs = bootCustomsFromConfig();
    effectiveIntro = "random";
  } else if (bootIntro.startsWith("custom:")) {
    const id = bootIntro.slice("custom:".length);
    const hit = customs.find((c) => c.id.toLowerCase() === id);
    if (hit?.path) {
      bootIntroPath = ensureBootIntroAsset(id, hit.path, build) || "";
      bootIntroLabel = hit.label;
      if (bootIntroPath && bootIntroPath !== hit.path) {
        customs = customs.map((c) =>
          c.id.toLowerCase() === id ? { ...c, path: bootIntroPath } : c
        );
        void cfg().update("bootCustomIntros", customs, vscode.ConfigurationTarget.Global);
      }
      if (!bootIntroPath) {
        effectiveIntro = "skip";
        bootIntroLabel = "";
      }
    } else {
      effectiveIntro = "default";
    }
  }

  const payload: Record<string, unknown> = {
    bootIntro: effectiveIntro,
    vaultAction: cfg().get<string>("vaultAction", "intro") || "intro",
    vaultIntro: cfg().get<string>("vaultIntro", "intro3") || "intro3",
    vaultPaletteChar: cfg().get<string>("vaultPaletteChar", "mario") || "mario",
    customs: customs.map((c) => ({
      id: c.id,
      label: c.label,
      path: c.path,
    })),
    randomPoolKeys: getBootRandomPoolKeys(),
    writtenUtc: new Date().toISOString(),
  };
  if (effectiveIntro === "random") {
    payload.randomPool = randomPool;
  }
  if (bootIntroPath) {
    payload.bootIntroPath = bootIntroPath;
    payload.bootIntroLabel = bootIntroLabel;
  }
  try {
    fs.writeFileSync(path.join(dir, "boot.json"), JSON.stringify(payload, null, 2), "utf8");
    return true;
  } catch {
    return false;
  }
}

/** Resolve selected boot intro (+ path) from settings; may re-encode custom FLV. */
export function resolveBootIntroForLaunch(
  buildRoot: string,
  introKey?: string
): {
  bootIntro: string;
  bootIntroPath: string;
  bootIntroLabel: string;
  randomPool?: BootPoolEntry[];
} {
  const bootIntroRaw = (introKey ?? cfg().get<string>("bootIntro", "default") ?? "default").trim();
  const bootIntro = bootIntroRaw.toLowerCase() || "default";
  if (bootIntro === "random") {
    const pool = buildRandomPool(buildRoot);
    return {
      bootIntro: "random",
      bootIntroPath: "",
      bootIntroLabel: "",
      randomPool: pool,
    };
  }
  let bootIntroPath = "";
  let bootIntroLabel = "";
  if (bootIntro.startsWith("custom:")) {
    const id = bootIntro.slice("custom:".length);
    const customs = bootCustomsFromConfig();
    const hit = customs.find((c) => c.id.toLowerCase() === id);
    if (hit?.path) {
      bootIntroPath = ensureBootIntroAsset(id, hit.path, buildRoot);
      bootIntroLabel = hit.label;
      if (bootIntroPath && bootIntroPath !== hit.path) {
        const next = customs.map((c) =>
          c.id.toLowerCase() === id ? { ...c, path: bootIntroPath } : c
        );
        void cfg().update("bootCustomIntros", next, vscode.ConfigurationTarget.Global);
      }
      if (!bootIntroPath) {
        return { bootIntro: "skip", bootIntroPath: "", bootIntroLabel: "" };
      }
    } else {
      return { bootIntro: "default", bootIntroPath: "", bootIntroLabel: "" };
    }
  }
  return { bootIntro, bootIntroPath, bootIntroLabel };
}

function resolveIntroKeyForScreen(screen: string): string | undefined {
  if (screen === "vault") {
    const vaultAction = cfg().get<string>("vaultAction", "intro") || "intro";
    if (vaultAction === "costumes") {
      return "skip";
    }
    return cfg().get<string>("vaultIntro", "intro3") || "intro3";
  }
  if (!screen || screen === "home") {
    return undefined;
  }
  return "skip";
}

function appendConfigureExtras(payload: Record<string, unknown>, screen: string, buildRoot?: string): void {
  if (!hasManagerLaunch() && !cfg().get<string>("eventId", "")) {
    return;
  }
  if (screen === "events") {
    const eventId = cfg().get<string>("eventId", "") || "";
    if (eventId) {
      payload.eventId = eventId;
      const ch = cfg().get<string>("eventCharacter", "") || "";
      const choose = listEvents(buildRoot).some((e) => e.id === eventId && e.value === 1);
      if (cfg().get<boolean>("eventOverride", false) && ch && !choose) {
        payload.eventCharacter = ch;
      }
    }
  }
  if (!hasManagerLaunch()) {
    return;
  }
  const everywhere = !!cfg().get<boolean>("useMusicEverywhere", false);
  const onLaunch = !!cfg().get<boolean>("useMusicOnLaunch", false);
  const music = cfg().get<string>("matchMusic", "") || "";
  payload.musicEverywhere = everywhere;
  payload.musicId = (everywhere || onLaunch) && music ? music : "";
}
export function writeLaunchCommand(
  buildRoot: string,
  prefs: LaunchPrefs
): { logPath: string; tracePath: string } {
  const dir = stateDir(buildRoot);
  fs.mkdirSync(dir, { recursive: true });
  const logFile = "data/ssf2mm/game.log";
  const logPath = path.join(ssf2mmDir(buildRoot), "game.log");
  const tracePath = path.join(dir, "air_trace.txt");
  try {
    fs.writeFileSync(logPath, `[ssf2-stream] launch ${new Date().toISOString()}\n`, "utf8");
  } catch {
    /* ignore */
  }
  try {
    fs.writeFileSync(tracePath, "", "utf8");
  } catch {
    /* ignore */
  }
  // Force mm.cfg TraceOutputFileName here (other projects often hijack it).
  ensureMmCfgForTraces(tracePath);

  const resolved = resolveBootIntroForLaunch(buildRoot, resolveIntroKeyForScreen(prefs.screen || "home"));
  // Keep sticky prefs in sync so a later bare exe launch uses the same intro.
  writeBootPrefs(buildRoot);

  const payload: Record<string, unknown> = {
    screen: prefs.screen || "home",
    match: prefs.match || null,
    bootIntro:
      resolved.bootIntro === "random"
        ? "random"
        : resolved.bootIntroPath
          ? resolved.bootIntro
          : resolved.bootIntro.startsWith("custom:")
            ? "skip"
            : resolved.bootIntro,
    logFile,
    logFileAbs: logPath.replace(/\\/g, "/"),
    traceFileAbs: tracePath.replace(/\\/g, "/"),
    writtenUtc: new Date().toISOString(),
  };
  const vaultAction = cfg().get<string>("vaultAction", "intro") || "intro";
  if ((prefs.screen || "home") === "vault") {
    payload.vaultAction = vaultAction;
    if (vaultAction === "costumes") {
      payload.vaultPaletteChar = cfg().get<string>("vaultPaletteChar", "mario") || "mario";
    }
  }
  if (resolved.bootIntro === "random" && resolved.randomPool) {
    payload.randomPool = resolved.randomPool;
  }
  if (resolved.bootIntroPath) {
    // Keep native Windows separators — AIR File + NetStream are happier with them.
    payload.bootIntroPath = resolved.bootIntroPath;
    payload.bootIntroLabel = resolved.bootIntroLabel;
  }
  appendConfigureExtras(payload, prefs.screen || "home", buildRoot);
  fs.writeFileSync(path.join(dir, "launch.json"), JSON.stringify(payload, null, 2), "utf8");
  // Drop leftover mid-session cmds so they cannot hijack this launch.
  try {
    const cmdPath = path.join(dir, "cmd.json");
    if (fs.existsSync(cmdPath)) {
      fs.unlinkSync(cmdPath);
    }
  } catch {
    /* ignore */
  }
  return { logPath, tracePath };
}

/** Mid-session command for the running ssf2_stream mod (cmd.json). */
export function writeApplyCommand(
  buildRoot: string,
  prefs?: LaunchPrefs,
  opts?: { gotoMenu?: boolean }
): string {
  const p = prefs || getLaunchPrefsFromConfig();
  let match = p.match ? { ...p.match } : null;
  if (match && opts?.gotoMenu) {
    match = { ...match, immediate: false };
  } else if (match && opts?.gotoMenu === false && match.immediate === false) {
    // force start when explicitly requested
    const screen = p.screen;
    const solo =
      screen === "classic" ||
      screen === "allstar" ||
      screen === "btt" ||
      screen === "hrc" ||
      screen === "crystal" ||
      screen === "multiman" ||
      screen === "arena" ||
      screen === "rttf" ||
      screen === "versus_css" ||
      screen === "training_css";
    if (solo) {
      match = { ...match, immediate: true };
    }
  }
  const logPath = path.join(ssf2mmDir(buildRoot), "game.log");
  const vaultAction = cfg().get<string>("vaultAction", "intro") || "intro";
  const screen = p.screen || "home";
  let bootIntro = cfg().get<string>("bootIntro", "default");
  let bootIntroPath = "";
  if (screen === "vault") {
    if (vaultAction === "intro") {
      const resolved = resolveBootIntroForLaunch(buildRoot, cfg().get<string>("vaultIntro", "intro3"));
      bootIntro = resolved.bootIntro;
      bootIntroPath = resolved.bootIntroPath;
    } else {
      bootIntro = "skip";
    }
  }
  const payload: Record<string, unknown> = {
    op: "applyLaunch",
    screen,
    match,
    logFileAbs: logPath.replace(/\\/g, "/"),
    bootIntro,
    bootIntroPath: bootIntroPath || undefined,
    vaultAction: screen === "vault" ? vaultAction : undefined,
    vaultPaletteChar:
      screen === "vault" && vaultAction === "costumes"
        ? cfg().get<string>("vaultPaletteChar", "mario") || "mario"
        : undefined,
  };
  appendConfigureExtras(payload, screen, buildRoot);
  return writeCmd(buildRoot, payload);
}

export function resolveAdlPath(): string | undefined {
  const cfg = vscode.workspace.getConfiguration("as3mxml");
  const framework = cfg.get<string>("sdk.framework") || "";
  const search = cfg.get<string[]>("sdk.searchPaths") || [];
  const candidates = [
    framework ? path.join(framework, "bin", "adl.exe") : "",
    adlExe() || "",
    ...search.map((dir) => path.join(dir, "bin", "adl.exe")),
  ];
  return pickLaunchAdl(candidates);
}

/**
 * Launch ADL debug without recompiling the starter SWF.
 * Only copies starter into build/, then starts the SWF debugger.
 */
export async function launchSsf2Debug(): Promise<boolean> {
  let folder = resolveDebugFolder();
  if (!folder) {
    folder = await pickDebugFolder();
  }
  if (!folder) {
    void vscode.window.showErrorMessage(
      "SSF2: set Debug Folder in extension settings (folder with asconfig.json)."
    );
    return false;
  }

  const dev = resolveDevFolder(folder);
  const game = resolveBuildRoot(folder);
  if (!dev) {
    void vscode.window.showErrorMessage(
      `SSF2: asconfig.json not found in ${folder} or ${path.join(path.dirname(folder), "runtime", "shell-dev")}`
    );
    return false;
  }

  const asconfig = path.join(dev, "asconfig.json");
  const wf = await ensureWorkspaceFolder(dev);
  if (!wf) {
    void vscode.window.showErrorMessage(
      "SSF2: Debugger for SWF needs shell-dev in the workspace. Open that folder, or add it to the workspace."
    );
    return false;
  }

  const adl = resolveAdlPath();
  if (!adl) {
    void vscode.window.showErrorMessage(
      "SSF2: ADL needs an AIR SDK with runtimes/air/win or win64."
    );
    return false;
  }

  const prefs = getLaunchPrefsFromConfig();
  if (game) {
    writeLaunchCommand(game, prefs);
  }

  await runPrepareCopy(dev);

  const appXml =
    [game ? path.join(game, "SSF2-app.xml") : "", path.join(dev, "SSF2-app.xml"), path.join(dev, "build", "SSF2-app.xml")].find(
      (p) => p && fs.existsSync(p)
    ) || "";
  if (!appXml) {
    void vscode.window.showWarningMessage(
      "SSF2: SSF2-app.xml missing — debug may fail until shell-dev overlay runs."
    );
  }

  const anes = resolveAneExtdir(dev, game || folder) || "";
  return !!(await vscode.debug.startDebugging(wf, {
    type: "swf",
    request: "launch",
    name: "SSF2 Shell (no compile)",
    extdir: relToWorkspace(wf, anes),
    program: appXml ? relToWorkspace(wf, appXml) : relToWorkspace(wf, path.join(dev, "SSF2-app.xml")),
    runtimeExecutable: adl,
    asconfigPath: relToWorkspace(wf, asconfig),
  }));
}

let exeChild: ChildProcess | undefined;
let exePid: number | undefined;

export function getExeChild(): ChildProcess | undefined {
  return exeChild;
}

export function isSsf2ExeActive(): boolean {
  return typeof exePid === "number" && exePid > 0;
}

/** Kill the process started by Launch SSF2 (and its children on Windows). */
export function closeSsf2Exe(): boolean {
  const pid = exePid;
  exePid = undefined;
  exeChild = undefined;
  if (!pid) {
    return false;
  }
  try {
    if (process.platform === "win32") {
      spawn("taskkill", ["/PID", String(pid), "/T", "/F"], {
        stdio: "ignore",
        windowsHide: true,
        detached: true,
      }).unref();
    } else {
      process.kill(pid, "SIGTERM");
    }
  } catch {
    return false;
  }
  return true;
}

/** Launch the game. Prefers ADL (traces → Output) when SDK is available; else packaged exe. */
export async function launchSsf2Exe(): Promise<{
  ok: boolean;
  logPath?: string;
  tracePath?: string;
  pid?: number;
  child?: ChildProcess;
  via?: "adl" | "exe";
}> {
  let folder = resolveDebugFolder();
  if (!folder) {
    folder = await pickDebugFolder();
  }
  if (!folder) {
    void vscode.window.showErrorMessage("SSF2: set Debug Folder first.");
    return { ok: false };
  }
  const build = resolveBuildRoot(folder);
  if (!build) {
    void vscode.window.showErrorMessage(
      "SSF2: game folder not found. Set Debug Folder to the SSF2 Shell install (the folder with Super Smash Flash 2 Beta.exe)."
    );
    return { ok: false };
  }
  const prefs = getLaunchPrefsFromConfig();
  const { logPath, tracePath } = writeLaunchCommand(build, prefs);
  closeSsf2Exe();

  const adl = resolveAdlPath() || "";
  const appXml = path.join(build, "SSF2-app.xml");
  const dev = resolveDevFolder(folder);
  if (dev) {
    await runPrepareCopy(dev);
  }
  const anes = resolveAneExtdir(dev, folder);

  if (adl && fs.existsSync(adl) && fs.existsSync(appXml) && anes) {
    const args: string[] = ["-extdir", anes, appXml];
    exeChild = spawn(adl, args, {
      cwd: build,
      detached: false,
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: false,
    });
    exePid = exeChild.pid;
    exeChild.on("exit", () => {
      if (exePid === exeChild?.pid) {
        exePid = undefined;
        exeChild = undefined;
      }
    });
    return { ok: true, logPath, tracePath, pid: exePid, child: exeChild, via: "adl" };
  }

  const exe = resolveGameExe(folder);
  if (!exe) {
    void vscode.window.showErrorMessage(
      "SSF2: install Next2Flash (adl) or keep Super Smash Flash 2 Beta.exe in the SSF2 Shell folder."
    );
    return { ok: false };
  }
  exeChild = spawn(exe, [], {
    cwd: path.dirname(exe),
    detached: true,
    stdio: "ignore",
    windowsHide: false,
  });
  exePid = exeChild.pid;
  exeChild.on("exit", () => {
    if (exePid === exeChild?.pid) {
      exePid = undefined;
      exeChild = undefined;
    }
  });
  exeChild.unref();
  return { ok: true, logPath, tracePath, pid: exePid, child: exeChild, via: "exe" };
}
