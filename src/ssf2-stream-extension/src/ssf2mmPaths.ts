import * as path from "path";

/** data/ssf2mm: built-in packs and game.log (the engine writes the log here). */
export function ssf2mmDir(build: string): string {
  return path.join(build, "data", "ssf2mm");
}

/** data/ssf2mm/state: boot, launch, cmd, catalog, cursor, and last_error files. Not in git. */
export function stateDir(build: string): string {
  return path.join(ssf2mmDir(build), "state");
}
