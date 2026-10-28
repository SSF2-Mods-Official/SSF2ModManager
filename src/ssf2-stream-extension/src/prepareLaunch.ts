/** Args for prepare_air_debug.ps1. Launch runs this process; it does not use a task type. */
export function prepareLaunchSpec(scriptPath: string): { command: string; args: string[] } {
  return {
    command: "powershell",
    args: ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
  };
}
