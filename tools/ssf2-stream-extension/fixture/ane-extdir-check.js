const fs = require("fs");
const path = require("path");
const { pickAneExtdir } = require("../out/adlPath");

const spaced = path.join("C:", "SSF2 Mod Manager", ".as3mxml-unpackaged-anes");
const clear = path.join("C:", "SSF2ModManager", ".as3mxml-unpackaged-anes");
const both = new Set([spaced, clear]);
const picked = pickAneExtdir([spaced, clear], (file) => both.has(file));
if (picked !== clear) {
  console.error("expected the path with no space, got " + picked);
  process.exit(1);
}

const onlySpaced = pickAneExtdir([spaced, clear], (file) => file === spaced);
if (onlySpaced !== spaced) {
  console.error("expected the only existing path, got " + onlySpaced);
  process.exit(1);
}

const none = pickAneExtdir([spaced], () => false);
if (none !== undefined) {
  console.error("expected no path, got " + none);
  process.exit(1);
}

const src = fs.readFileSync(path.join(__dirname, "..", "src", "debugLaunch.ts"), "utf8");
const start = src.indexOf("export async function launchSsf2Debug");
const slice = src.slice(start, start + 4000);
if (!/const anes = resolveAneExtdir\(/.test(slice)) {
  console.error("launchSsf2Debug does not pass resolveAneExtdir");
  process.exit(1);
}

const ext = fs.readFileSync(path.join(__dirname, "..", "src", "extension.ts"), "utf8");
if (!ext.includes("ensureGameLogWatch({ show: true })")) {
  console.error("debug start does not show the SSF2 Game output");
  process.exit(1);
}

console.log("OK");
