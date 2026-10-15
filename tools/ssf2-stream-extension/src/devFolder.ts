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

export function resolveDevFolderAt(
  shell: string,
  exists: (file: string) => boolean = (file) => fs.existsSync(file)
): string | undefined {
  for (const dir of devFolderCandidates(shell)) {
    if (exists(path.join(dir, "asconfig.json"))) {
      return dir;
    }
  }
  return undefined;
}
