const path = require("path");
const { pickLaunchAdl } = require("../out/adlPath");

const lite = path.join("C:", "aflex_lite");
const sdk = path.join("C:", "aflex_sdk");
const files = new Set([
  path.join(lite, "bin", "adl.exe"),
  path.join(sdk, "bin", "adl.exe"),
  path.join(lite, "runtimes", "air", "win64"),
  path.join(sdk, "runtimes", "air", "win"),
]);

const picked = pickLaunchAdl(
  [path.join(lite, "bin", "adl.exe"), path.join(sdk, "bin", "adl.exe")],
  (file) => files.has(file)
);

if (picked !== path.join(lite, "bin", "adl.exe")) {
  console.error("expected win64 lite adl, got " + picked);
  process.exit(1);
}

const bare = new Set([path.join(lite, "bin", "adl.exe"), path.join(sdk, "bin", "adl.exe"), path.join(sdk, "runtimes", "air", "win")]);
const skipped = pickLaunchAdl([path.join(lite, "bin", "adl.exe")], (file) => bare.has(file));
if (skipped !== undefined) {
  console.error("expected no adl, got " + skipped);
  process.exit(1);
}

const full = pickLaunchAdl(
  [path.join(lite, "bin", "adl.exe"), path.join(sdk, "bin", "adl.exe")],
  (file) => bare.has(file)
);
if (full !== path.join(sdk, "bin", "adl.exe")) {
  console.error("expected full sdk adl, got " + full);
  process.exit(1);
}

console.log("OK");
