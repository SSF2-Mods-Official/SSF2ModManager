const fs = require("fs");
const path = require("path");
const { streamTabShowOptions } = require("../out/streamTab");

const opts = streamTabShowOptions();
if (opts.viewColumn !== 1 || opts.preserveFocus !== false) {
  console.error("stream tab must open in editor group 1, got " + JSON.stringify(opts));
  process.exit(1);
}

const ext = fs.readFileSync(path.join(__dirname, "..", "src", "extension.ts"), "utf8");
if (!ext.includes("streamTabShowOptions()")) {
  console.error("openStreamPanel does not use streamTabShowOptions");
  process.exit(1);
}
if (!ext.includes("focusFirstEditorGroup")) {
  console.error("openStreamPanel does not focus the editor group");
  process.exit(1);
}
if (!ext.includes("[ssf2-stream] stream tab column=1")) {
  console.error("openStreamPanel does not log the stream tab");
  process.exit(1);
}

const panel = fs.readFileSync(path.join(__dirname, "..", "src", "streamPanel.ts"), "utf8");
if (!panel.includes("panel.reveal(tab.viewColumn as vscode.ViewColumn, preserveFocus ?? tab.preserveFocus)")) {
  console.error("reveal does not use streamTabShowOptions");
  process.exit(1);
}

const outDir = path.join(__dirname, "..", "out");
const files = fs.readdirSync(outDir).filter((name) => name.endsWith(".js"));
const requireRe = /require\("(\.\/[^"]+)"\)/g;
for (const name of files) {
  const text = fs.readFileSync(path.join(outDir, name), "utf8");
  requireRe.lastIndex = 0;
  let match;
  while ((match = requireRe.exec(text))) {
    const rel = match[1] + ".js";
    const target = path.normalize(path.join(outDir, rel));
    if (!fs.existsSync(target)) {
      console.error(name + " cannot find module '" + match[1] + "'");
      process.exit(1);
    }
  }
}

console.log("OK");
