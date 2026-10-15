import * as fs from "fs";
import * as path from "path";

/** ADL exits with code 3 until the SDK has runtimes/air/win or win64. */
export function sdkHasAirRuntime(
  adlPath: string,
  exists: (file: string) => boolean = (file) => fs.existsSync(file)
): boolean {
  const sdk = path.resolve(path.dirname(adlPath), "..");
  return (
    exists(path.join(sdk, "runtimes", "air", "win")) ||
    exists(path.join(sdk, "runtimes", "air", "win64"))
  );
}

/**
 * ADL drops -extdir when the path contains a space.
 * Prefer a path that exists and has no space.
 */
export function pickAneExtdir(
  candidates: string[],
  exists: (file: string) => boolean = (file) => fs.existsSync(file)
): string | undefined {
  const existing = candidates.filter((p) => p && exists(p));
  return existing.find((p) => !p.includes(" ")) || existing[0];
}

/** First adl.exe whose SDK contains a Windows AIR runtime. */
export function pickLaunchAdl(
  candidates: string[],
  exists: (file: string) => boolean = (file) => fs.existsSync(file)
): string | undefined {
  for (const adl of candidates) {
    if (adl && exists(adl) && sdkHasAirRuntime(adl, exists)) {
      return adl;
    }
  }
  return undefined;
}
