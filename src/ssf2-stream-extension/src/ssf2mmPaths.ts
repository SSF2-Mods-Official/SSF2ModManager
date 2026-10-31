import * as path from "path";

/** data/ssf2mm: built-in packs and game.log (the engine writes the log here). */
export function ssf2mmDir(build: string): string {
  return path.join(build, "data", "ssf2mm");
}

/** expansions/state: boot, launch, cmd, catalog, cursor, game.log, and last_error. Not in git. */
export function stateDir(build: string): string {
  return path.join(build, "expansions", "state");
}
