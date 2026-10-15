const fs = require("fs");
const os = require("os");
const path = require("path");
const { resolveDevFolderAt } = require("../out/devFolder");

const root = fs.mkdtempSync(path.join(os.tmpdir(), "ssf2-dev-"));
const shell = path.join(root, "SSF2 Mod Manager", "SSF2 Shell");
const runtimeDev = path.join(root, "SSF2 Mod Manager", "runtime", "shell-dev");
fs.mkdirSync(shell, { recursive: true });
fs.mkdirSync(runtimeDev, { recursive: true });
fs.writeFileSync(path.join(runtimeDev, "asconfig.json"), "{}");

const found = resolveDevFolderAt(shell);
if (path.resolve(found) !== path.resolve(runtimeDev)) {
  console.error("expected runtime/shell-dev, got " + found);
  process.exit(1);
}

const oldRoot = fs.mkdtempSync(path.join(os.tmpdir(), "ssf2-dev-old-"));
const oldShell = path.join(oldRoot, "SSF2 Shell");
const nested = path.join(oldShell, "shell-dev");
fs.mkdirSync(nested, { recursive: true });
fs.writeFileSync(path.join(nested, "asconfig.json"), "{}");
const oldLayout = resolveDevFolderAt(oldShell);
if (path.resolve(oldLayout) !== path.resolve(nested)) {
  console.error("expected nested shell-dev, got " + oldLayout);
  process.exit(1);
}

console.log("OK");
