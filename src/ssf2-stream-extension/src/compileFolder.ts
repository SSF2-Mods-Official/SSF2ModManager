import { spawn } from "child_process";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { resolveDevFolderAt } from "./devFolder";
import { aflexLiteDir, n2fCommand } from "./next2flash";

export interface CompileFolderItem {
  id: string;
  label: string;
}

function externsDir(shell: string): string | undefined {
  const dir = path.join(shell, "expansions", "decompiled", "engine2", "externs");
  return fs.existsSync(path.join(dir, "link-report.xml")) ? dir : undefined;
}

/** Built-in pack source (src/mods) and drop-in mods (expansions/mods). */
const MOD_ROOTS: { prefix: string; label: string; rel: string[] }[] = [
  { prefix: "builtin:", label: "Built-in: ", rel: ["src", "mods"] },
  { prefix: "mod:", label: "", rel: ["expansions", "mods"] },
];

/** engine2 under expansions/decompiled, plus mod folders that have src. template is skipped. */
export function listCompileFolders(shell: string): CompileFolderItem[] {
  const items: CompileFolderItem[] = [];
  const engine2 = path.join(shell, "expansions", "decompiled", "engine2");
  if (fs.existsSync(engine2)) {
    items.push({ id: "engine2", label: "engine2" });
  }
  for (const root of MOD_ROOTS) {
    const mods = path.join(shell, ...root.rel);
    if (!fs.existsSync(mods)) {
      continue;
    }
    for (const name of fs.readdirSync(mods).sort((a, b) => a.localeCompare(b))) {
      if (name.toLowerCase() === "template" || name.startsWith("_") || name.startsWith(".")) {
        continue;
      }
      const dir = path.join(mods, name);
      if (!fs.existsSync(path.join(dir, "src")) || !fs.existsSync(path.join(dir, "asconfig.json"))) {
        continue;
      }
      items.push({ id: root.prefix + name, label: root.label + name });
    }
  }
  return items;
}

function engineSourcePaths(engine2Dir: string): string[] {
  const unchanged = path.join(engine2Dir, "unchanged");
  const changed = path.join(engine2Dir, "changed");
  const split = [unchanged, changed].filter((dir) => fs.existsSync(dir));
  if (split.length > 0) {
    return split;
  }
  const scripts = path.join(engine2Dir, "scripts");
  return [fs.existsSync(path.join(scripts, "com")) ? scripts : engine2Dir];
}

function scriptClassNames(sourceRoots: string[]): string[] {
  const names = new Set<string>();
  for (const root of sourceRoots) {
    const walk = (dir: string) => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          walk(full);
          continue;
        }
        if (!entry.name.endsWith(".as") || entry.name.includes("$") || entry.name.includes(" ")) {
          continue;
        }
        const rel = path.relative(root, full).replace(/\\/g, "/").replace(/\.as$/, "");
        names.add(rel.split("/").join("."));
      }
    };
    if (fs.existsSync(root)) {
      walk(root);
    }
  }
  return [...names].sort();
}

function writeEngineProject(shell: string, externs: string): string {
  const engine2 = path.join(shell, "expansions", "decompiled", "engine2");
  const sources = engineSourcePaths(engine2);
  const classes = scriptClassNames(sources);
  const mainClass = classes.find((name) => name === "Main" || name.endsWith(".Main")) || classes[0] || "Main";
  const swcs = fs.readdirSync(externs).filter((name) => name.endsWith(".swc")).sort()
    .map((name) => path.join(externs, name));
  const lite = aflexLiteDir();
  if (lite) {
    const air = path.join(lite, "frameworks", "libs", "air", "airglobal.swc");
    if (fs.existsSync(air)) {
      swcs.push(air);
    }
  }
  const raw = {
    mainClass,
    compilerOptions: {
      "source-path": sources,
      includes: classes,
      "external-library-path": swcs,
      "target-player": "32.0",
      optimize: true,
      strict: false,
      warnings: false,
      "static-link-runtime-shared-libraries": true,
      "load-externs": [path.join(externs, "link-report.xml")],
      "link-report": path.join(externs, "engine2-link-report.xml"),
      output: path.join(shell, "data", "engine", "engine2.swf"),
    },
  };
  const tmp = path.join(os.tmpdir(), "ssf2mm-asconfig-engine2.json");
  fs.writeFileSync(tmp, JSON.stringify(raw));
  return tmp;
}

const EXTERN_MAP: Record<string, string> = {
  "engine1-code.swc": "engine1.swc",
  "engine1-link-report.xml": "link-report.xml",
  "engine2-link-report.xml": "engine2-link-report.xml",
  "SSF2.swc": "SSF2.swc",
};

/** Lists every decompiled engine2 class so mods load them as externs instead of compiling them in. */
function ensureEngine2LinkReport(externs: string): void {
  const report = path.join(externs, "engine2-link-report.xml");
  if (fs.existsSync(report)) {
    return;
  }
  const lines = ["<report><scripts>"];
  for (const name of scriptClassNames(engineSourcePaths(path.dirname(externs)))) {
    const dot = name.lastIndexOf(".");
    const id = dot < 0 ? name : name.slice(0, dot) + ":" + name.slice(dot + 1);
    lines.push('<script name="' + id + '"><def id="' + id + '" /></script>');
  }
  lines.push("</scripts></report>");
  fs.writeFileSync(report, lines.join("\n"));
}

function resolveEntry(entry: string, dir: string, externs: string | undefined, devRoot: string | undefined): string | undefined {
  if (externs) {
    for (const name of Object.keys(EXTERN_MAP)) {
      const re = new RegExp("(?:(?:\\.\\./)|(?:\\.\\.\\\\))*engine[/\\\\]resources[/\\\\]" + name.replace(/\./g, "\\.") + "$");
      if (re.test(entry)) {
        const abs = path.join(externs, EXTERN_MAP[name]);
        return fs.existsSync(abs) ? abs : undefined;
      }
    }
  }
  const full = path.resolve(dir, entry);
  if (fs.existsSync(full)) {
    return full;
  }
  if (devRoot && entry.toLowerCase().endsWith(".ane")) {
    const devAne = path.join(devRoot, path.basename(entry));
    if (fs.existsSync(devAne)) {
      return devAne;
    }
  }
  return undefined;
}

/**
 * Temp copy of a mod's asconfig.json with every path absolute (relative to the mod folder),
 * engine/resources names pointed at the decompiled externs, and the output resolved.
 */
function resolveModProject(projectPath: string, shell: string): { project: string; output: string } {
  const dir = path.dirname(path.resolve(projectPath));
  const json = JSON.parse(fs.readFileSync(projectPath, "utf8"));
  const opts = json.compilerOptions || (json.compilerOptions = {});
  const text = JSON.stringify(opts);
  const usesEngine = text.includes("engine/resources") || text.includes("engine\\\\resources");
  const externs = usesEngine ? externsDir(shell) : undefined;
  if (usesEngine && !externs) {
    throw new Error("Decompile the SWF first. Linked libraries are written once next to the scripts.");
  }
  if (externs) {
    ensureEngine2LinkReport(externs);
  }
  const devRoot = resolveDevFolderAt(shell);
  for (const key of ["source-path", "external-library-path", "load-externs", "library-path"]) {
    if (!Array.isArray(opts[key])) {
      continue;
    }
    opts[key] = (opts[key] as string[])
      .map((entry) => resolveEntry(String(entry), dir, externs, devRoot))
      .filter((p): p is string => !!p)
      .map((p) => p.replace(/\\/g, "/"));
  }
  if (externs && Array.isArray(opts["source-path"])) {
    opts["source-path"] = [...engineSourcePaths(path.dirname(externs)).map((p) => p.replace(/\\/g, "/")), ...opts["source-path"]];
  }
  const output = path.resolve(dir, opts.output || "mod.swf");
  opts.output = output.replace(/\\/g, "/");
  fs.mkdirSync(path.dirname(output), { recursive: true });
  const tmp = path.join(os.tmpdir(), "ssf2mm-" + path.basename(dir) + "-asconfig.json");
  fs.writeFileSync(tmp, JSON.stringify(json));
  return { project: tmp, output };
}

function runN2f(args: string[]): Promise<string> {
  const cmd = n2fCommand();
  if (!cmd) {
    return Promise.reject(new Error("Next2Flash is not installed."));
  }
  return new Promise((resolve, reject) => {
    const child = spawn(cmd.python, [cmd.script, ...args], { cwd: cmd.cwd, windowsHide: true });
    let out = "";
    child.stdout.on("data", (buf) => {
      out += buf.toString();
    });
    child.stderr.on("data", (buf) => {
      out += buf.toString();
    });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) {
        resolve(out.trim());
      } else {
        reject(new Error((out.trim() || "n2f failed") + " (exit " + code + ")"));
      }
    });
  });
}

/** Compile engine2 or one mod src folder. Returns the SWF path. */
export async function compileFolder(shell: string, id: string): Promise<string> {
  if (id === "engine2") {
    const externs = externsDir(shell);
    if (!externs) {
      throw new Error("Decompile the SWF first. Linked libraries are written once next to the scripts.");
    }
    const project = writeEngineProject(shell, externs);
    await runN2f(["asconfig", "--project", project]);
    return path.join(shell, "data", "engine", "engine2.swf");
  }
  const root = MOD_ROOTS.find((r) => id.startsWith(r.prefix));
  if (!root) {
    throw new Error("Pick engine2 or a mod.");
  }
  const name = id.slice(root.prefix.length);
  const folder = path.join(shell, ...root.rel, name);
  const asconfig = path.join(folder, "asconfig.json");
  if (!fs.existsSync(asconfig)) {
    throw new Error(name + " has no asconfig.json.");
  }
  const { project, output } = resolveModProject(asconfig, shell);
  await runN2f(["asconfig", "--project", project, "--debug"]);
  for (const dir of [folder, path.dirname(output)]) {
    for (const extra of fs.existsSync(dir) ? fs.readdirSync(dir) : []) {
      if (extra.endsWith("-app.xml")) {
        try {
          fs.unlinkSync(path.join(dir, extra));
        } catch {
          // the SWF is the output
        }
      }
    }
  }
  return fs.existsSync(output) ? output : folder;
}
