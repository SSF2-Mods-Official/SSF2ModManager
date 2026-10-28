import * as fs from "fs";
import * as path from "path";

/** Places asconfig.json can live, newest layout first after the folder itself. */
export function devFolderCandidates(shell: string): string[] {
  const parent = path.dirname(shell);
  return [
    shell,
    path.join(parent, "runtime", "shell-dev"),
    path.join(shell, "shell-dev"),
    path.join(shell, "expansions", "shell-dev"),
    path.join(parent, "shell-dev"),
  ];
}

/**
 * Shell Base Build is a sibling of the Mod Manager repo, not a child of it.
 * From the game folder, walk up and check each neighbor for shell-dev.
 */
export function siblingDevFolders(
  shell: string,
  listDirs: (dir: string) => string[] = listDirNames
): string[] {
  const extra: string[] = [];
  let dir = shell;
  for (let level = 0; level < 4; level++) {
    const parent = path.dirname(dir);
    if (parent === dir) {
      break;
    }
    let names: string[] = [];
    try {
      names = listDirs(parent);
    } catch {
      names = [];
    }
    for (const name of names) {
      const sibling = path.join(parent, name);
      extra.push(path.join(sibling, "runtime", "shell-dev"));
      extra.push(path.join(sibling, "SSF2 Mod Manager Release", "runtime", "shell-dev"));
    }
    dir = parent;
  }
  return extra;
}

function listDirNames(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).filter((ent) => ent.isDirectory()).map((ent) => ent.name);
}

export function resolveDevFolderAt(
  shell: string,
  exists: (file: string) => boolean = (file) => fs.existsSync(file),
  listDirs?: (dir: string) => string[]
): string | undefined {
  const dirs = devFolderCandidates(shell).concat(siblingDevFolders(shell, listDirs));
  for (const dir of dirs) {
    if (exists(path.join(dir, "asconfig.json"))) {
      return dir;
    }
  }
  return undefined;
}

/** Mod Manager settings `LauncherInstallFolder`. That folder is the playable game. */
export function launcherInstallFolder(settingsJson: string): string | undefined {
  try {
    const root = JSON.parse(settingsJson) as { LauncherInstallFolder?: unknown };
    const folder = typeof root.LauncherInstallFolder === "string" ? root.LauncherInstallFolder.trim() : "";
    return folder || undefined;
  } catch {
    return undefined;
  }
}
