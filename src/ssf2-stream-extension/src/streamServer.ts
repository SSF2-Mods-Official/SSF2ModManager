import * as vscode from "vscode";
import { spawn, ChildProcess } from "child_process";
import * as path from "path";
import { EventEmitter } from "events";
import { resolveBuildRoot } from "./debugLaunch";
import { stateDir } from "./ssf2mmPaths";

/** Spawns the localhost MJPEG server (no window hiding). */
export class StreamServer extends EventEmitter {
  private child: ChildProcess | undefined;
  private stderrLine = "";
  port: number | undefined;

  constructor(
    private readonly extensionPath: string,
    private readonly config: vscode.WorkspaceConfiguration
  ) {
    super();
  }

  get streamUrl(): string | undefined {
    return this.port ? `http://127.0.0.1:${this.port}/stream.mjpg` : undefined;
  }

  get inputUrl(): string | undefined {
    return this.port ? `http://127.0.0.1:${this.port}/` : undefined;
  }

  get cursorUrl(): string | undefined {
    return this.port ? `http://127.0.0.1:${this.port}/cursor` : undefined;
  }

  start(): void {
    if (this.child) {
      return;
    }
    const python = this.config.get<string>("pythonPath", "python");
    const script = path.join(this.extensionPath, "helper", "mjpeg_server.py");
    const fps = this.config.get<number>("fps", 30);
    const quality = this.config.get<number>("jpegQuality", 90);
    const wait = this.config.get<number>("windowWaitSeconds", 60);
    const maxLongEdge = this.config.get<number>("maxStreamLongEdge", 1280);
    const processes = this.config.get<string[]>("processNames", ["adl", "Super Smash Flash 2 Beta"]);
    const titles = this.config.get<string[]>("windowTitleHints", ["Super Smash Flash", "SSF2", "adl"]);
    const build = resolveBuildRoot();
    const bridgeDir = build ? stateDir(build) : "";

    const args = [
      "-u",
      script,
      "--fps",
      String(fps),
      "--quality",
      String(quality),
      "--wait",
      String(wait),
      "--max-long-edge",
      String(maxLongEdge),
    ];
    if (bridgeDir) {
      args.push("--bridge-dir", bridgeDir);
    }
    for (const p of processes) {
      args.push("--process", p);
    }
    for (const t of titles) {
      args.push("--title", t);
    }

    const child = spawn(python, args, {
      cwd: this.extensionPath,
      env: { ...process.env, PYTHONUNBUFFERED: "1" },
      stdio: ["ignore", "ignore", "pipe"],
      windowsHide: true,
    });
    this.child = child;
    child.stderr?.on("data", (chunk: Buffer) => this.onStderr(chunk));
    child.on("error", (err) => this.emit("error", err));
    child.on("exit", (code) => {
      if (this.child === child) {
        this.child = undefined;
        this.port = undefined;
      }
      this.emit("exit", code);
    });
  }

  /** Kill helper but keep event listeners so streaming can start again. */
  stop(): void {
    this.stopChild();
    this.port = undefined;
    this.stderrLine = "";
  }

  restart(): void {
    this.stop();
    this.start();
  }

  async postInput(body: object): Promise<void> {
    if (!this.port) {
      return;
    }
    try {
      await fetch(`http://127.0.0.1:${this.port}/`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    } catch {
      /* server may be restarting */
    }
  }

  async fetchCursorHand(): Promise<boolean> {
    if (!this.port) {
      return false;
    }
    try {
      const res = await fetch(`http://127.0.0.1:${this.port}/cursor`);
      if (!res.ok) {
        return false;
      }
      const data = (await res.json()) as { hand?: boolean };
      return !!data.hand;
    } catch {
      return false;
    }
  }

  dispose(): void {
    this.stop();
    this.removeAllListeners();
  }

  private stopChild(): void {
    if (!this.child) {
      return;
    }
    try {
      this.child.kill();
    } catch {
      /* ignore */
    }
    this.child = undefined;
  }

  private onStderr(chunk: Buffer): void {
    this.stderrLine += chunk.toString("utf8");
    let idx: number;
    while ((idx = this.stderrLine.indexOf("\n")) >= 0) {
      const line = this.stderrLine.slice(0, idx).replace(/\r$/, "");
      this.stderrLine = this.stderrLine.slice(idx + 1);
      if (!line) {
        continue;
      }
      if (line.startsWith("PORT:")) {
        this.port = Number(line.slice(5));
        this.emit("ready", this.port);
      } else if (line.startsWith("HWND:")) {
        this.emit("hwnd", Number(line.slice(5)));
      } else if (line.startsWith("BOUNDS:")) {
        const [w, h] = line.slice(7).split(",").map(Number);
        this.emit("bounds", w, h);
      } else if (line === "WAITING") {
        this.emit("waiting");
      } else if (line.startsWith("ERROR:")) {
        this.emit("error", new Error(line.slice(6)));
      }
    }
  }
}
