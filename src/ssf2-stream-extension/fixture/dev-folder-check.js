const fs = require("fs");
const os = require("os");
const path = require("path");
const { launcherInstallFolder, resolveDevFolderAt } = require("../out/devFolder");
const { prepareLaunchSpec } = require("../out/prepareLaunch");

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

const hub = fs.mkdtempSync(path.join(os.tmpdir(), "ssf2-dev-hub-"));
const build = path.join(hub, "SSF2 Shell Base", "Build");
const managerDev = path.join(hub, "SSF2ModManager", "SSF2 Mod Manager Release", "runtime", "shell-dev");
fs.mkdirSync(build, { recursive: true });
fs.mkdirSync(managerDev, { recursive: true });
fs.writeFileSync(path.join(managerDev, "asconfig.json"), "{}");
const sibling = resolveDevFolderAt(build);
if (path.resolve(sibling) !== path.resolve(managerDev)) {
  console.error("expected sibling shell-dev, got " + sibling);
  process.exit(1);
}

const spec = prepareLaunchSpec("prepare_air_debug.ps1");
if (spec.command !== "powershell" || spec.args.indexOf("ssf2Stream") !== -1) {
  console.error("prepare must be a powershell script, got " + JSON.stringify(spec));
  process.exit(1);
}
if (spec.args[spec.args.length - 1] !== "prepare_air_debug.ps1") {
  console.error("prepare -File path missing");
  process.exit(1);
}

const saved = launcherInstallFolder('{"LauncherInstallFolder":"C:\\\\Games\\\\Build"}');
if (saved !== "C:\\Games\\Build") {
  console.error("expected launcher folder, got " + saved);
  process.exit(1);
}
if (launcherInstallFolder("{}") !== undefined) {
  console.error("empty launcher folder should be missing");
  process.exit(1);
}

console.log("OK");
