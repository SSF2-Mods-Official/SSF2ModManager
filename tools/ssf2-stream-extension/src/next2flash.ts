import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import * as vscode from "vscode";

function roots(): string[] {
  const found: string[] = [];
  if (process.env.NEXT2FLASH_HOME) {
    found.push(process.env.NEXT2FLASH_HOME);
  }
  found.push(path.join(os.homedir(), "Documents", "GitHub", "Next2Flash"));
  if (process.env.LOCALAPPDATA) {
    found.push(path.join(process.env.LOCALAPPDATA, "SSF2ModManager", "Next2Flash"));
  }
  found.push(path.resolve(__dirname, "..", "..", "..", "..", "Next2Flash"));
  return found;
}

function hasAdl(dir: string): boolean {
  return fs.existsSync(path.join(dir, "bin", "adl.exe"));
}

/** Flex lite from a Next2Flash checkout or an installed release. */
export function aflexLiteDir(): string | undefined {
  for (const root of roots()) {
    const dev = path.join(root, "toolchain", "aflex_lite");
    const release = path.join(root, "runtime", "resources", "toolchain", "aflex_lite");
    if (hasAdl(dev)) {
      return dev;
    }
    if (hasAdl(release)) {
      return release;
    }
    if (!fs.existsSync(root)) {
      continue;
    }
    for (const child of fs.readdirSync(root, { withFileTypes: true })) {
      if (!child.isDirectory()) {
        continue;
      }
      const nested = path.join(root, child.name, "runtime", "resources", "toolchain", "aflex_lite");
      if (hasAdl(nested)) {
        return nested;
      }
    }
  }
  return undefined;
}

/** python + n2f.py for a Next2Flash checkout or an installed release. */
export function n2fCommand(): { python: string; script: string; cwd: string } | undefined {
  for (const root of roots()) {
    const releasePython = path.join(root, "runtime", "resources", "python", "python.exe");
    const releaseScript = path.join(root, "runtime", "resources", "app", "n2f.py");
    if (fs.existsSync(releasePython) && fs.existsSync(releaseScript)) {
      return { python: releasePython, script: releaseScript, cwd: root };
    }
    const script = path.join(root, "app", "n2f.py");
    if (fs.existsSync(script)) {
      return { python: "python.exe", script, cwd: root };
    }
  }
  return undefined;
}

export function adlExe(): string | undefined {
  const lite = aflexLiteDir();
  if (!lite) {
    return undefined;
  }
  const adl = path.join(lite, "bin", "adl.exe");
  return fs.existsSync(adl) ? adl : undefined;
}

/** Point the ActionScript extension at Next2Flash's SDK when that folder exists. */
export async function pointSdkAtNext2Flash(): Promise<void> {
  const lite = aflexLiteDir();
  if (!lite) {
    return;
  }
  const cfg = vscode.workspace.getConfiguration("as3mxml");
  const current = cfg.get<string>("sdk.framework") || "";
  if (path.normalize(current).toLowerCase() === path.normalize(lite).toLowerCase()) {
    return;
  }
  await cfg.update("sdk.framework", lite, vscode.ConfigurationTarget.Global);
}
