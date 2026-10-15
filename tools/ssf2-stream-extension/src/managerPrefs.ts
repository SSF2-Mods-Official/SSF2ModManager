import * as fs from "fs";
import * as os from "os";
import * as path from "path";

/** Mod Manager Configure is the match-settings store (%AppData%/SSF2ModManager/user-settings.json). */

export function managerSettingsPath(): string {
  const base = process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming");
  return path.join(base, "SSF2ModManager", "user-settings.json");
}

type JsonObj = Record<string, unknown>;

let cachedMtime = 0;
let cachedFlat: Record<string, unknown> | undefined;

const SIMPLE: Record<string, string> = {
  startingMenu: "StartingMenu",
  loadMatchImmediate: "LoadMatchImmediate",
  bootIntro: "BootIntro",
  vaultAction: "VaultAction",
  vaultIntro: "VaultIntro",
  vaultPaletteChar: "VaultPaletteChar",
  bootRandomPool: "BootRandomPool",
  matchStage: "MatchStage",
  matchMusic: "MatchMusic",
  useMusicOnLaunch: "UseMusicOnLaunch",
  useMusicEverywhere: "UseMusicEverywhere",
  eventId: "EventId",
  eventOverride: "EventOverride",
  eventCharacter: "EventCharacter",
  sandbagOverride: "SandbagOverride",
  sandbagCharacter: "SandbagCharacter",
  matchCustomModeId: "MatchCustomModeId",
  matchDifficulty: "MatchDifficulty",
  matchScoreLimit: "MatchScoreLimit",
  matchSpecialModes: "MatchSpecialModes",
  matchHazards: "MatchHazards",
  matchUsingTime: "MatchUsingTime",
  matchUsingLives: "MatchUsingLives",
  matchLives: "MatchLives",
  matchTime: "MatchTime",
  playerTags: "PlayerTags",
};

function str(v: unknown, fallback = ""): string {
  return v == null ? fallback : String(v);
}

function flatten(launch: JsonObj): Record<string, unknown> {
  const flat: Record<string, unknown> = {};
  for (const [streamKey, jsonKey] of Object.entries(SIMPLE)) {
    if (launch[jsonKey] !== undefined) {
      flat[streamKey] = launch[jsonKey];
    }
  }
  const customs = Array.isArray(launch.BootCustomIntros) ? launch.BootCustomIntros : [];
  flat.bootCustomIntros = customs
    .map((raw) => {
      const c = (raw || {}) as JsonObj;
      const id = str(c.Id ?? c.id);
      if (!id) {
        return undefined;
      }
      return { id, label: str(c.Label ?? c.label, id), path: str(c.Path ?? c.path) };
    })
    .filter((c) => !!c);
  const players = Array.isArray(launch.Players) ? launch.Players : [];
  for (let i = 0; i < 4; i++) {
    const p = ((players[i] || {}) as JsonObj);
    const n = i + 1;
    flat[`matchP${n}`] = str(p.Character ?? p.character);
    flat[`matchP${n}Human`] = !!(p.Human ?? p.human);
    flat[`matchP${n}Level`] = Number(p.Level ?? p.level ?? (i === 0 ? 1 : 3)) || 1;
    flat[`matchP${n}Team`] = Number(p.Team ?? p.team ?? (i % 2 === 0 ? 1 : 3)) || 1;
    flat[`matchP${n}Tag`] = str(p.Tag ?? p.tag);
  }
  return flat;
}

export function hasManagerLaunch(): boolean {
  return !!readManagerFlat();
}

export function readManagerFlat(): Record<string, unknown> | undefined {
  const file = managerSettingsPath();
  let mtime = 0;
  try {
    mtime = fs.statSync(file).mtimeMs;
  } catch {
    cachedMtime = 0;
    cachedFlat = undefined;
    return undefined;
  }
  if (cachedFlat && mtime === cachedMtime) {
    return cachedFlat;
  }
  try {
    const root = JSON.parse(fs.readFileSync(file, "utf8")) as JsonObj;
    const launch = root.LaunchConfigure as JsonObj | undefined;
    if (!launch || typeof launch !== "object") {
      cachedMtime = mtime;
      cachedFlat = undefined;
      return undefined;
    }
    cachedMtime = mtime;
    cachedFlat = flatten(launch);
    return cachedFlat;
  } catch {
    return cachedFlat;
  }
}

/** Overlay Configure values onto a Stream prefs object. Editor-only keys are left alone. */
export function applyManagerOverlay(values: Record<string, unknown>): boolean {
  const flat = readManagerFlat();
  if (!flat) {
    values.managedByModManager = false;
    return false;
  }
  Object.assign(values, flat);
  values.managedByModManager = true;
  return true;
}

let suppressUntil = 0;

export function patchManagerSettings(entries: Array<[string, unknown]>): void {
  const file = managerSettingsPath();
  let root: JsonObj;
  try {
    root = JSON.parse(fs.readFileSync(file, "utf8")) as JsonObj;
  } catch {
    return;
  }
  const launch = (root.LaunchConfigure || {}) as JsonObj;
  let players = Array.isArray(launch.Players) ? (launch.Players as JsonObj[]) : [];
  while (players.length < 4) {
    players.push({});
  }
  let changed = false;
  for (const [name, value] of entries) {
    const player = /^matchP([1-4])(Human|Level|Team|Tag)?$/.exec(name);
    if (player) {
      const idx = Number(player[1]) - 1;
      const field =
        player[2] === "Human"
          ? "Human"
          : player[2] === "Level"
            ? "Level"
            : player[2] === "Team"
              ? "Team"
              : player[2] === "Tag"
                ? "Tag"
                : "Character";
      const row = (players[idx] || {}) as JsonObj;
      row[field] = value;
      players[idx] = row;
      changed = true;
      continue;
    }
    if (name === "bootCustomIntros" && Array.isArray(value)) {
      launch.BootCustomIntros = value.map((raw) => {
        const c = (raw || {}) as JsonObj;
        return { Id: str(c.id ?? c.Id), Label: str(c.label ?? c.Label), Path: str(c.path ?? c.Path) };
      });
      changed = true;
      continue;
    }
    const jsonKey = SIMPLE[name];
    if (!jsonKey) {
      continue;
    }
    launch[jsonKey] = value;
    changed = true;
  }
  if (!changed) {
    return;
  }
  launch.Players = players;
  root.LaunchConfigure = launch;
  suppressUntil = Date.now() + 800;
  cachedMtime = 0;
  try {
    fs.writeFileSync(file, JSON.stringify(root, null, 2) + "\n", "utf8");
    cachedMtime = fs.statSync(file).mtimeMs;
    cachedFlat = flatten(launch);
  } catch {
    /* ignore */
  }
}

export function watchManagerSettings(onChange: () => void): { dispose: () => void } {
  const file = managerSettingsPath();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let watcher: fs.FSWatcher | undefined;
  const fire = () => {
    if (Date.now() < suppressUntil) {
      return;
    }
    cachedMtime = 0;
    onChange();
  };
  const arm = () => {
    try {
      watcher?.close();
    } catch {
      /* ignore */
    }
    try {
      const dir = path.dirname(file);
      fs.mkdirSync(dir, { recursive: true });
      watcher = fs.watch(dir, (_event, filename) => {
        if (filename && filename !== path.basename(file)) {
          return;
        }
        if (timer) {
          clearTimeout(timer);
        }
        timer = setTimeout(fire, 200);
      });
    } catch {
      /* ignore */
    }
  };
  arm();
  return {
    dispose: () => {
      if (timer) {
        clearTimeout(timer);
      }
      try {
        watcher?.close();
      } catch {
        /* ignore */
      }
    },
  };
}
