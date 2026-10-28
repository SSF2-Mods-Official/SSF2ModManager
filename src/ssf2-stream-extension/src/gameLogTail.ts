import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";
import * as os from "os";
import { ChildProcess } from "child_process";

const UNCAUGHT_LINE =
  /\[(?:ssf2_stream|css_sprite_preview)\]\s+UNCAUGHT\b/;
const UNCAUGHT_STACK_CONT =
  /\[(?:ssf2_stream|css_sprite_preview)\]\s+UNCAUGHT stack:/;

/** Max bytes ingested from each log file per poll tick. */
const MAX_READ_PER_POLL = 64 * 1024;
/** Soft rate limit for Output channel text. */
const MAX_APPEND_BYTES_PER_SEC = 12 * 1024;
/** When attaching mid-session, only dump this much of existing file if fromStart. */
const FROM_START_TAIL_BYTES = 8 * 1024;
/** Min gap between channel.show() on UNCAUGHT. */
const UNCAUGHT_SHOW_COOLDOWN_MS = 5000;

/** Tails game/trace logs and pipes process stdout into the SSF2 Game output channel. */
export class GameLogTail {
  private channel: vscode.OutputChannel;
  private timer: ReturnType<typeof setInterval> | undefined;
  private watches: { path: string; offset: number }[] = [];
  private proc: ChildProcess | undefined;
  private lastUncaughtBlob = "";
  private collectingStack = false;
  private lastErrorPath: string | undefined;
  private onUncaughtCb: ((blob: string) => void) | undefined;
  private appendBytesWindow = 0;
  private appendWindowStartMs = 0;
  private floodNoticed = false;
  private lastUncaughtShowMs = 0;
  private uncaughtTimestamps: number[] = [];
  private onFloodCb: (() => void) | undefined;

  constructor() {
    this.channel = vscode.window.createOutputChannel("SSF2 Game");
  }

  /** Reveal the Output channel (for Launch SSF2). */
  show(preserveFocus = false): void {
    this.channel.show(preserveFocus);
  }

  appendLine(line: string): void {
    this.appendRateLimited(line.endsWith("\n") ? line : line + "\n");
  }

  /** Called when a new UNCAUGHT block is assembled from game.log. */
  setOnUncaught(cb: ((blob: string) => void) | undefined): void {
    this.onUncaughtCb = cb;
  }

  /** Called when UNCAUGHT flood is detected (≥5 in 2s). */
  setOnFlood(cb: (() => void) | undefined): void {
    this.onFloodCb = cb;
  }

  getLastUncaughtBlob(): string {
    return this.lastUncaughtBlob;
  }

  getLastErrorPath(): string | undefined {
    return this.lastErrorPath;
  }

  /**
   * Watch one or more log files. fromStart reads a tail of existing bytes (capped).
   * Always ensures mm.cfg points TraceOutputFileName at our trace path when provided.
   */
  start(
    paths: string | string[],
    opts?: { fromStart?: boolean; show?: boolean; tracePath?: string }
  ): void {
    const list = (Array.isArray(paths) ? paths : [paths]).filter(Boolean);
    if (opts?.tracePath) {
      ensureMmCfgForTraces(opts.tracePath);
      if (!list.includes(opts.tracePath)) {
        list.push(opts.tracePath);
      }
    }
    this.lastErrorPath = undefined;
    this.floodNoticed = false;
    this.appendBytesWindow = 0;
    this.appendWindowStartMs = Date.now();
    this.uncaughtTimestamps = [];
    for (const p of list) {
      if (path.basename(p).toLowerCase() === "game.log") {
        this.lastErrorPath = path.join(path.dirname(p), "state", "last_error.txt");
        break;
      }
    }
    if (!this.lastErrorPath && opts?.tracePath) {
      this.lastErrorPath = path.join(path.dirname(opts.tracePath), "last_error.txt");
    }
    const fromStart = !!opts?.fromStart;
    this.watches = list.map((p) => {
      let offset = 0;
      try {
        if (fs.existsSync(p)) {
          const size = fs.statSync(p).size;
          if (fromStart) {
            // Only last N KiB so huge air_trace/game.log don't dump into Output.
            offset = Math.max(0, size - FROM_START_TAIL_BYTES);
          } else {
            offset = size;
          }
        }
      } catch {
        offset = 0;
      }
      return { path: p, offset };
    });
    for (const w of this.watches) {
      this.channel.appendLine(`[ssf2-stream] Watching ${w.path}`);
    }
    if (opts?.show !== false) {
      this.channel.show(true);
    }
    this.stopTimer();
    this.timer = setInterval(() => this.poll(), 250);
  }

  attachProcess(child: ChildProcess | undefined, label = "process"): void {
    this.proc = child;
    if (!child) {
      return;
    }
    this.channel.appendLine(`[ssf2-stream] Attached ${label} stdout/stderr`);
    this.channel.show(true);
    const pipe = (buf: Buffer | string) => {
      const text = typeof buf === "string" ? buf : buf.toString("utf8");
      if (text) {
        this.appendRateLimited(text.endsWith("\n") ? text : text + "\n");
      }
    };
    child.stdout?.on("data", pipe);
    child.stderr?.on("data", pipe);
    child.on("exit", (code, signal) => {
      this.channel.appendLine(
        `[ssf2-stream] ${label} exited code=${code ?? "?"} signal=${signal ?? "-"}`
      );
    });
  }

  stop(): void {
    this.stopTimer();
    this.watches = [];
    this.proc = undefined;
    this.collectingStack = false;
  }

  dispose(): void {
    this.stop();
    this.channel.dispose();
  }

  private stopTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
  }

  private appendRateLimited(text: string): void {
    const now = Date.now();
    if (now - this.appendWindowStartMs >= 1000) {
      this.appendWindowStartMs = now;
      this.appendBytesWindow = 0;
      this.floodNoticed = false;
    }
    if (this.appendBytesWindow >= MAX_APPEND_BYTES_PER_SEC) {
      if (!this.floodNoticed) {
        this.floodNoticed = true;
        this.channel.appendLine(
          "[ssf2-stream] log flood — suppressing Output until rate drops"
        );
      }
      return;
    }
    const room = MAX_APPEND_BYTES_PER_SEC - this.appendBytesWindow;
    if (text.length > room) {
      this.channel.append(text.slice(0, room) + "\n");
      this.appendBytesWindow += room;
      if (!this.floodNoticed) {
        this.floodNoticed = true;
        this.channel.appendLine(
          `[ssf2-stream] log flood — truncated +${text.length - room} bytes this second`
        );
      }
      return;
    }
    this.channel.append(text);
    this.appendBytesWindow += text.length;
  }

  private poll(): void {
    for (const w of this.watches) {
      try {
        if (!fs.existsSync(w.path)) {
          continue;
        }
        const size = fs.statSync(w.path).size;
        if (size < w.offset) {
          w.offset = 0;
        }
        if (size === w.offset) {
          continue;
        }
        const pending = size - w.offset;
        const toRead = Math.min(pending, MAX_READ_PER_POLL);
        const fd = fs.openSync(w.path, "r");
        const buf = Buffer.alloc(toRead);
        fs.readSync(fd, buf, 0, toRead, w.offset);
        fs.closeSync(fd);
        w.offset += toRead;
        const text = buf.toString("utf8");
        if (text) {
          this.appendRateLimited(text.endsWith("\n") ? text : text + "\n");
          if (path.basename(w.path).toLowerCase() === "game.log") {
            this.ingestUncaughtChunk(text);
          }
        }
        if (pending > toRead) {
          this.appendRateLimited(
            `[ssf2-stream] truncated ${pending - toRead} bytes from ${path.basename(w.path)}\n`
          );
        }
      } catch {
        /* ignore transient locks */
      }
    }
  }

  private ingestUncaughtChunk(text: string): void {
    const lines = text.split(/\r?\n/);
    for (const line of lines) {
      if (!line) {
        continue;
      }
      if (UNCAUGHT_LINE.test(line) && !UNCAUGHT_STACK_CONT.test(line)) {
        this.finishUncaughtCollect();
        this.lastUncaughtBlob = line + "\n";
        this.collectingStack = true;
        this.onUncaughtNew();
        continue;
      }
      if (this.collectingStack) {
        if (UNCAUGHT_STACK_CONT.test(line) || line.startsWith("\t") || /^\s+at\s/.test(line)) {
          this.lastUncaughtBlob += line + "\n";
          continue;
        }
        if (line.includes("[ssf2_stream]") || line.includes("[css_sprite_preview]")) {
          this.finishUncaughtCollect();
        } else {
          this.lastUncaughtBlob += line + "\n";
        }
      }
    }
  }

  private finishUncaughtCollect(): void {
    this.collectingStack = false;
  }

  private onUncaughtNew(): void {
    const now = Date.now();
    this.uncaughtTimestamps.push(now);
    this.uncaughtTimestamps = this.uncaughtTimestamps.filter((t) => now - t < 2000);
    if (this.uncaughtTimestamps.length >= 5) {
      try {
        this.onFloodCb?.();
      } catch {
        /* ignore */
      }
      this.uncaughtTimestamps = [];
    }
    if (now - this.lastUncaughtShowMs >= UNCAUGHT_SHOW_COOLDOWN_MS) {
      this.lastUncaughtShowMs = now;
      this.channel.show(true);
    }
    try {
      this.onUncaughtCb?.(this.lastUncaughtBlob);
    } catch {
      /* ignore */
    }
  }
}

/** Force AIR/Flash traces into our known file (overrides other projects' mm.cfg paths). */
export function ensureMmCfgForTraces(tracePath: string): void {
  try {
    const mm = path.join(os.homedir(), "mm.cfg");
    const abs = tracePath.replace(/\//g, "\\");
    const want: Record<string, string> = {
      ErrorReportingEnable: "1",
      TraceOutputFileEnable: "1",
      TraceOutputFileName: abs,
    };
    let existing = "";
    if (fs.existsSync(mm)) {
      existing = fs.readFileSync(mm, "utf8");
    }
    const map = new Map<string, string>();
    for (const line of existing.split(/\r?\n/)) {
      const m = /^([^=]+)=(.*)$/.exec(line.trim());
      if (m) {
        map.set(m[1].trim(), m[2].trim());
      }
    }
    let changed = false;
    for (const [k, v] of Object.entries(want)) {
      if (map.get(k) !== v) {
        map.set(k, v);
        changed = true;
      }
    }
    if (changed) {
      const body = [...map.entries()].map(([k, v]) => `${k}=${v}`).join("\n") + "\n";
      fs.writeFileSync(mm, body, "utf8");
    }
  } catch {
    /* ignore */
  }
}
