import * as fs from "fs";
import * as path from "path";
import { resolveBuildRoot } from "./debugLaunch";
import { stateDir } from "./ssf2mmPaths";

export interface CatalogItem {
  id: string;
  label: string;
  value?: number;
  bit?: number;
}

export interface Ssf2Catalog {
  characters: CatalogItem[];
  stages: CatalogItem[];
  music: CatalogItem[];
  menus: CatalogItem[];
  items?: CatalogItem[];
  targetStages: CatalogItem[];
  hrcStages: CatalogItem[];
  crystalStages: CatalogItem[];
  events?: CatalogItem[];
  multimanModes: CatalogItem[];
  arenaModes: CatalogItem[];
  difficulties: CatalogItem[];
  specialModes: CatalogItem[];
  source?: string;
  writtenUtc?: string;
}

export const DEFAULT_MENUS: CatalogItem[] = [
  { id: "home", label: "(default / home)" },
  { id: "group", label: "Group" },
  { id: "online", label: "Online" },
  { id: "data", label: "Data" },
  { id: "options", label: "Options" },
  { id: "controls", label: "Controls" },
  { id: "vault", label: "Vault" },
  { id: "solo", label: "Solo" },
  { id: "events", label: "Events" },
  { id: "versus_css", label: "Versus CSS" },
  { id: "training_css", label: "Training CSS" },
  { id: "classic", label: "Classic" },
  { id: "allstar", label: "All-Star" },
  { id: "btt", label: "Break the Targets" },
  { id: "hrc", label: "Home-Run Contest" },
  { id: "multiman", label: "Multi-Man" },
  { id: "crystal", label: "Crystal Smash" },
  { id: "rttf", label: "Race to the Finish" },
  { id: "arena", label: "Arena" },
];

const MULTIMAN: CatalogItem[] = [
  { id: "man10", label: "10-Man" },
  { id: "man100", label: "100-Man" },
  { id: "min3", label: "3-Minute" },
  { id: "endless", label: "Endless" },
  { id: "cruel", label: "Cruel" },
];

const ARENA: CatalogItem[] = [
  { id: "soccer", label: "Soccer" },
  { id: "basketball", label: "Basketball" },
];

const DIFFICULTIES: CatalogItem[] = [
  { id: "EASY", label: "Easy", value: 1 },
  { id: "NORMAL", label: "Normal", value: 2 },
  { id: "HARD", label: "Hard", value: 3 },
  { id: "INSANE", label: "Insane", value: 5 },
];

const SPECIAL: CatalogItem[] = [
  { id: "MINI", label: "Mini", bit: 1 << 0 },
  { id: "MEGA", label: "Mega", bit: 1 << 1 },
  { id: "SLOW", label: "Slow", bit: 1 << 2 },
  { id: "LIGHTNING", label: "Lightning", bit: 1 << 3 },
  { id: "VAMPIRE", label: "Vampire", bit: 1 << 4 },
  { id: "VENGEANCE", label: "Vengeance", bit: 1 << 5 },
  { id: "FREEZE", label: "Freeze", bit: 1 << 6 },
  { id: "EGG", label: "Egg", bit: 1 << 7 },
  { id: "DRAMATIC", label: "Dramatic", bit: 1 << 8 },
  { id: "TURBO", label: "Turbo", bit: 1 << 9 },
  { id: "INVISIBLE", label: "Invisible", bit: 1 << 10 },
  { id: "METAL", label: "Metal", bit: 1 << 11 },
  { id: "LIGHT", label: "Light", bit: 1 << 12 },
  { id: "HEAVY", label: "Heavy", bit: 1 << 13 },
  { id: "SSF1", label: "SSF1", bit: 1 << 14 },
];

function emptyCatalog(source: string): Ssf2Catalog {
  return {
    characters: [],
    stages: [],
    music: [],
    menus: DEFAULT_MENUS,
    items: [],
    targetStages: [],
    hrcStages: [],
    crystalStages: [],
    events: [],
    multimanModes: MULTIMAN,
    arenaModes: ARENA,
    difficulties: DIFFICULTIES,
    specialModes: SPECIAL,
    source,
  };
}

function mapEntries(obj: Record<string, { name?: string }> | undefined): CatalogItem[] {
  if (!obj || typeof obj !== "object") {
    return [];
  }
  return Object.keys(obj)
    .map((id) => ({
      id,
      label: (obj[id] && obj[id].name) || id,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

function musicEntries(obj: Record<string, unknown> | undefined): CatalogItem[] {
  if (!obj || typeof obj !== "object") {
    return [];
  }
  return Object.keys(obj)
    .map((id) => ({
      id,
      label: id.startsWith("bgm_") ? id.slice(4) : id,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

/** Cold fallback from disk mappings.json (no mode stage lists). */
export function buildCatalogFromMappings(buildRoot?: string): Ssf2Catalog {
  const build = buildRoot || resolveBuildRoot();
  const cat = emptyCatalog("mappings");
  if (!build) {
    return cat;
  }
  const p = path.join(build, "data", "misc", "mappings.json");
  try {
    const raw = JSON.parse(fs.readFileSync(p, "utf8")) as {
      character?: Record<string, { name?: string }>;
      stage?: Record<string, { name?: string }>;
      music?: Record<string, unknown>;
    };
    cat.characters = mapEntries(raw.character);
    cat.stages = mapEntries(raw.stage);
    cat.music = musicEntries(raw.music);
    cat.targetStages = buildTargetStagesList(cat.stages, cat.characters);
  } catch {
    /* keep empty */
  }
  return cat;
}

export function catalogPath(buildRoot?: string): string | undefined {
  const build = buildRoot || resolveBuildRoot();
  if (!build) {
    return undefined;
  }
  return path.join(stateDir(build), "catalog.json");
}

/** Prefer live dump from game; else mappings; else bundled media file. Always prefer on-disk SWFs. */
export function loadBestCatalog(
  extensionPath: string,
  buildRoot?: string
): Ssf2Catalog {
  const build = buildRoot || resolveBuildRoot();
  let cat: Ssf2Catalog | undefined;
  const livePath = catalogPath(build);
  if (livePath && fs.existsSync(livePath)) {
    try {
      const live = JSON.parse(fs.readFileSync(livePath, "utf8")) as Ssf2Catalog;
      cat = normalizeCatalog(live, "live");
    } catch {
      /* fall through */
    }
  }
  if (!cat) {
    const fromMaps = buildCatalogFromMappings(build);
    if (fromMaps.characters.length > 0) {
      cat = fromMaps;
    }
  }
  if (!cat) {
    const bundled = path.join(extensionPath, "media", "ssf2-catalog.json");
    try {
      const raw = JSON.parse(fs.readFileSync(bundled, "utf8")) as Partial<Ssf2Catalog>;
      cat = normalizeCatalog(raw, "bundled");
    } catch {
      cat = emptyCatalog("empty");
    }
  }
  return applyConfigureLists(applyDiskPresence(cat, build), build);
}

function readPrintableStrings(data: Buffer): string[] {
  const strings: string[] = [];
  let i = 0;
  while (i < data.length - 2) {
    const n = data[i];
    if (n >= 3 && n <= 90 && i + 1 + n <= data.length) {
      let ok = true;
      for (let j = 0; j < n; j++) {
        const b = data[i + 1 + j];
        if (b < 32 || b > 126) {
          ok = false;
          break;
        }
      }
      if (ok) {
        strings.push(data.toString("ascii", i + 1, i + 1 + n));
        i += 1 + n;
        continue;
      }
    }
    i++;
  }
  return strings;
}

function isModeToken(value: string): boolean {
  if (!value || value.length > 40) {
    return false;
  }
  return /^[A-Za-z0-9 ]+$/.test(value);
}

function isModeId(value: string): boolean {
  if (!value || value.includes(" ") || value.length < 4 || value[0] !== value[0].toUpperCase()) {
    return false;
  }
  if (value.startsWith("HRC")) {
    return true;
  }
  if (/\d/.test(value)) {
    return true;
  }
  return value.length >= 10;
}

function isEventId(value: string): boolean {
  return !!value && value.length <= 40 && /^[A-Za-z][A-Za-z0-9]*$/.test(value);
}

/** Same pairs Configure reads after the mode token in a mode SWF. */
function listModeOptions(build: string | undefined, swfFileName: string): CatalogItem[] {
  if (!build) {
    return [];
  }
  const file = path.join(build, "data", "modes", swfFileName);
  if (!fs.existsSync(file)) {
    return [];
  }
  const strings = readPrintableStrings(fs.readFileSync(file));
  let modeAt = -1;
  for (let i = 0; i < strings.length; i++) {
    if (strings[i] === "mode") {
      modeAt = i;
    }
  }
  if (modeAt < 0) {
    return [];
  }
  const tail: string[] = [];
  for (let i = modeAt + 1; i < strings.length; i++) {
    if (!isModeToken(strings[i])) {
      break;
    }
    tail.push(strings[i]);
  }
  const list: CatalogItem[] = [];
  let index = 0;
  while (index < tail.length) {
    const cur = tail[index];
    if (cur.includes(" ")) {
      list.push({ id: cur, label: cur });
      index++;
      continue;
    }
    if (index + 1 < tail.length && !isModeId(tail[index + 1])) {
      list.push({ id: cur, label: tail[index + 1] });
      index += 2;
      continue;
    }
    list.push({ id: cur, label: cur });
    index++;
  }
  return list;
}

export function listEvents(build: string | undefined): CatalogItem[] {
  if (!build) {
    return [];
  }
  const file = path.join(build, "data", "modes", "event_mode.swf");
  if (!fs.existsSync(file)) {
    return [];
  }
  const strings = readPrintableStrings(fs.readFileSync(file));
  const found: Array<{ id: string; label: string; order: number; choose: boolean }> = [];
  for (let s = 1; s < strings.length; s++) {
    const cur = strings[s];
    const prev = strings[s - 1];
    if (cur.length > 3 && /\d/.test(cur[0]) && cur.includes(". ") && isEventId(prev)) {
      const dot = cur.indexOf(".");
      const order = Number.parseInt(cur.slice(0, dot), 10);
      found.push({
        id: prev,
        label: cur,
        order: Number.isFinite(order) ? order : 1000,
        choose: s >= 2 && strings[s - 2] === "chooseCharacter",
      });
    } else if (prev.startsWith("AllStarBattle") && cur.startsWith("All-Star")) {
      found.push({ id: prev, label: cur, order: 2000 + found.length, choose: false });
    }
  }
  found.sort((a, b) => a.order - b.order || a.label.localeCompare(b.label));
  const seen = new Set<string>();
  const list: CatalogItem[] = [];
  for (const item of found) {
    const key = item.id.toLowerCase();
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    list.push({
      id: item.id,
      label: item.id.toLowerCase() === "acelebration" ? "51. A Celebration" : item.label,
      value: item.choose ? 1 : 0,
    });
  }
  return list;
}

function withEventsMenu(menus: CatalogItem[]): CatalogItem[] {
  if (menus.some((m) => m.id === "events")) {
    return menus;
  }
  const next = menus.slice();
  const solo = next.findIndex((m) => m.id === "solo");
  next.splice(solo >= 0 ? solo + 1 : next.length, 0, { id: "events", label: "Events" });
  return next;
}

function applyConfigureLists(cat: Ssf2Catalog, build?: string): Ssf2Catalog {
  const hrc = listModeOptions(build, "homeruncontest_mode.swf");
  const crystal = listModeOptions(build, "crystals_mode.swf");
  const events = listEvents(build);
  return {
    ...cat,
    menus: withEventsMenu(cat.menus || []),
    hrcStages: hrc.length
      ? hrc
      : [
          { id: "HRCBarrier", label: "Barrier" },
          { id: "HRCNoBarrier", label: "No Barrier" },
        ],
    crystalStages: crystal.length
      ? crystal
      : [
          { id: "Level 1", label: "Level 1" },
          { id: "Level 2", label: "Level 2" },
        ],
    events: events.length ? events : cat.events || [],
  };
}

const BTT_LEVELS: CatalogItem[] = [
  { id: "Level 1", label: "Level 1" },
  { id: "Level 2", label: "Level 2" },
  { id: "Level 3", label: "Level 3" },
  { id: "Level 4", label: "Level 4" },
];

/** Level 1–4 + character targettest_* stages; never include meta "Character". */
export function buildTargetStagesList(
  stages: CatalogItem[],
  characters: CatalogItem[],
  rawTargetStages?: CatalogItem[]
): CatalogItem[] {
  const levels: CatalogItem[] = [];
  const fromRaw = rawTargetStages || [];
  for (const it of fromRaw) {
    const id = String(it.id || "");
    if (!id || id.toLowerCase() === "character") {
      continue;
    }
    if (/^Level\s/i.test(id)) {
      levels.push({ id, label: it.label || id });
    }
  }
  const levelOut = levels.length > 0 ? levels : BTT_LEVELS.slice();
  const charLabels = new Map(characters.map((c) => [c.id.toLowerCase(), c.label]));
  const seen = new Set(levelOut.map((i) => i.id.toLowerCase()));
  const charsOnly: CatalogItem[] = [];
  for (const st of stages) {
    const id = String(st.id || "");
    const low = id.toLowerCase();
    if (!low.startsWith("targettest_")) {
      continue;
    }
    const charId = id.slice("targettest_".length);
    if (!charId || charId === "2" || charId === "3" || charId === "4") {
      continue;
    }
    if (seen.has(low)) {
      continue;
    }
    seen.add(low);
    charsOnly.push({
      id,
      label: charLabels.get(charId.toLowerCase()) || st.label || charId,
    });
  }
  charsOnly.sort((a, b) => a.label.localeCompare(b.label, undefined, { sensitivity: "base" }));
  return [...levelOut, ...charsOnly];
}

function normalizeCatalog(raw: Partial<Ssf2Catalog>, source: string): Ssf2Catalog {
  const base = emptyCatalog(raw.source || source);
  const characters = raw.characters?.length ? raw.characters : base.characters;
  const stages = raw.stages?.length ? raw.stages : base.stages;
  return {
    ...base,
    characters,
    stages,
    music: raw.music?.length ? raw.music : base.music,
    menus: raw.menus?.length ? raw.menus : DEFAULT_MENUS,
    items: raw.items || [],
    targetStages: buildTargetStagesList(stages, characters, raw.targetStages),
    hrcStages: raw.hrcStages || [],
    crystalStages: raw.crystalStages || [],
    multimanModes: raw.multimanModes?.length ? raw.multimanModes : MULTIMAN,
    arenaModes: raw.arenaModes?.length ? raw.arenaModes : ARENA,
    difficulties: raw.difficulties?.length ? raw.difficulties : DIFFICULTIES,
    specialModes: raw.specialModes?.length ? raw.specialModes : SPECIAL,
    source: raw.source || source,
    writtenUtc: raw.writtenUtc,
  };
}

function listSwfBaseNames(dir: string): string[] {
  try {
    if (!fs.existsSync(dir)) {
      return [];
    }
    return fs
      .readdirSync(dir)
      .filter((f) => f.toLowerCase().endsWith(".swf"))
      .map((f) => f.replace(/\.swf$/i, ""));
  } catch {
    return [];
  }
}

function prettyDiskLabel(id: string): string {
  return id
    .replace(/^bgm_/i, "")
    .replace(/_/g, " ")
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function mergeDiskIds(
  diskIds: string[],
  labeled: CatalogItem[],
  pretty: (id: string) => string
): CatalogItem[] {
  const labels = new Map(labeled.map((i) => [i.id, i.label]));
  const out = diskIds.map((id) => ({
    id,
    label: labels.get(id) || pretty(id),
  }));
  out.sort((a, b) => a.label.localeCompare(b.label, undefined, { sensitivity: "base" }));
  return out;
}

/** Rebuild char/stage/music lists from SWFs that exist on disk; keep mapping labels when known. */
export function applyDiskPresence(catalog: Ssf2Catalog, buildRoot?: string): Ssf2Catalog {
  const build = buildRoot || resolveBuildRoot();
  if (!build) {
    return catalog;
  }
  const charDir = path.join(build, "data", "character");
  const stageDir = path.join(build, "data", "stage");
  const soundDir = path.join(build, "data", "sound");
  const charIds = new Set(
    listSwfBaseNames(charDir).filter((id) => id.toLowerCase() !== "assets")
  );
  const expRoot = path.join(build, "expansions", "character");
  try {
    if (fs.existsSync(expRoot)) {
      for (const folder of fs.readdirSync(expRoot, { withFileTypes: true })) {
        if (!folder.isDirectory()) {
          continue;
        }
        for (const id of listSwfBaseNames(path.join(expRoot, folder.name))) {
          if (id.toLowerCase() === "assets") {
            continue;
          }
          charIds.add(id);
        }
      }
    }
  } catch {
    /* ignore */
  }
  const stageIds = listSwfBaseNames(stageDir);
  const musicIds = listSwfBaseNames(soundDir).filter((id) =>
    id.toLowerCase().startsWith("bgm_")
  );
  // If folders are empty/missing, keep prior lists rather than wiping the UI.
  const characters =
    charIds.size > 0
      ? mergeDiskIds([...charIds], catalog.characters, prettyDiskLabel)
      : catalog.characters;
  const stages =
    stageIds.length > 0
      ? mergeDiskIds(stageIds, catalog.stages, prettyDiskLabel)
      : catalog.stages;
  const music =
    musicIds.length > 0
      ? mergeDiskIds(musicIds, catalog.music, (id) =>
          prettyDiskLabel(id).replace(/^Bgm /i, "")
        )
      : catalog.music;
  return {
    ...catalog,
    characters,
    stages,
    music,
    targetStages: buildTargetStagesList(stages, characters, catalog.targetStages),
    source: `${catalog.source || "disk"}+disk`,
  };
}

/** Mode / arena ids that must never be used as versus/training stage. */
export const NON_STAGE_MODE_IDS = new Set([
  "soccer",
  "basketball",
  "man10",
  "man100",
  "min3",
  "endless",
  "cruel",
]);

export function sanitizeVersusStage(stage: string, fallback = "battlefield"): string {
  const s = String(stage || "").trim();
  if (!s || NON_STAGE_MODE_IDS.has(s) || /^Level\s/i.test(s)) {
    return fallback;
  }
  return s;
}

export function writeCmd(
  buildRoot: string,
  payload: Record<string, unknown>
): string {
  const dir = stateDir(buildRoot);
  fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, "cmd.json");
  // Use cmdId for dedupe — never overwrite payload.id (music/generate ids).
  const body = {
    ...payload,
    cmdId: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    writtenUtc: new Date().toISOString(),
  };
  fs.writeFileSync(file, JSON.stringify(body, null, 2), "utf8");
  return file;
}

export async function waitForCatalogRefresh(
  buildRoot: string,
  priorMtimeMs: number,
  timeoutMs = 4000
): Promise<boolean> {
  const p = path.join(stateDir(buildRoot), "catalog.json");
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      if (fs.existsSync(p)) {
        const st = fs.statSync(p);
        if (st.mtimeMs > priorMtimeMs) {
          return true;
        }
      }
    } catch {
      /* retry */
    }
    await new Promise((r) => setTimeout(r, 200));
  }
  return false;
}
