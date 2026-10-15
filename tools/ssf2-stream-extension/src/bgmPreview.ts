import * as fs from "fs";
import * as http from "http";
import * as path from "path";
import * as zlib from "zlib";

/** Extract MPEG audio payload from an SSF2 bgm_*.swf (FWS/CWS). */
export function extractMp3FromSwf(swfPath: string, outPath: string): number {
  const data = fs.readFileSync(swfPath);
  const sig = data.subarray(0, 3).toString("ascii");
  let body: Buffer;
  if (sig === "FWS") {
    body = data.subarray(8);
  } else if (sig === "CWS") {
    body = zlib.inflateSync(data.subarray(8));
  } else if (sig === "ZWS") {
    throw new Error("LZMA SWF not supported");
  } else {
    throw new Error(`Not a SWF (${sig})`);
  }

  // Prefer DefineSound (tag 14) MP3 when tags parse cleanly.
  const fromTag = findDefineSoundMp3(body);
  const mp3 = fromTag || findMpegSync(body);
  if (!mp3 || mp3.length < 64) {
    throw new Error("No MP3 found in SWF");
  }
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, mp3);
  return mp3.length;
}

function findDefineSoundMp3(body: Buffer): Buffer | null {
  if (body.length < 2) {
    return null;
  }
  const nbits = body[0] >> 3;
  const rectBytes = Math.floor((5 + nbits * 4 + 7) / 8);
  let i = rectBytes + 4;
  while (i + 2 <= body.length) {
    const codeLen = body[i] | (body[i + 1] << 8);
    const tag = codeLen >> 6;
    let length = codeLen & 0x3f;
    i += 2;
    if (length === 0x3f) {
      if (i + 4 > body.length) {
        break;
      }
      length = body.readUInt32LE(i);
      i += 4;
    }
    if (i + length > body.length) {
      break;
    }
    const payload = body.subarray(i, i + length);
    i += length;
    if (tag === 0) {
      break;
    }
    if (tag === 14 && payload.length >= 9) {
      const fmt = (payload[2] >> 4) & 0x0f;
      if (fmt === 2) {
        // id(2) + flags(1) + sampleCount(4) + seekSamples(2) + mp3
        const mp3 = payload.subarray(9);
        if (mp3.length > 64 && (mp3[0] === 0xff || mp3.subarray(0, 3).toString() === "ID3")) {
          return Buffer.from(mp3);
        }
      }
    }
  }
  return null;
}

function findMpegSync(body: Buffer): Buffer | null {
  const magics = [
    Buffer.from([0xff, 0xfb]),
    Buffer.from([0xff, 0xfa]),
    Buffer.from([0xff, 0xf3]),
    Buffer.from([0xff, 0xf2]),
    Buffer.from("ID3"),
  ];
  for (const magic of magics) {
    const at = body.indexOf(magic);
    if (at >= 0) {
      return body.subarray(at);
    }
  }
  return null;
}

/** Tiny localhost static server for cached preview MP3s. */
export class BgmPreviewServer {
  private server: http.Server | undefined;
  private port: number | undefined;
  private root = "";

  start(rootDir: string): Promise<number> {
    this.root = rootDir;
    fs.mkdirSync(rootDir, { recursive: true });
    if (this.server && this.port) {
      return Promise.resolve(this.port);
    }
    return new Promise((resolve, reject) => {
      const server = http.createServer((req, res) => {
        res.setHeader("Access-Control-Allow-Origin", "*");
        res.setHeader("Cache-Control", "no-store");
        if (req.method === "OPTIONS") {
          res.writeHead(204);
          res.end();
          return;
        }
        const raw = (req.url || "/").split("?")[0];
        const name = path.basename(decodeURIComponent(raw));
        if (!name || name === "/" || name.includes("..")) {
          res.writeHead(404);
          res.end();
          return;
        }
        const file = path.join(this.root, name);
        if (!file.startsWith(this.root) || !fs.existsSync(file)) {
          res.writeHead(404);
          res.end("missing");
          return;
        }
        res.writeHead(200, { "Content-Type": "audio/mpeg" });
        fs.createReadStream(file).pipe(res);
      });
      server.once("error", reject);
      server.listen(0, "127.0.0.1", () => {
        const addr = server.address();
        if (!addr || typeof addr === "string") {
          reject(new Error("bgm server bind failed"));
          return;
        }
        this.server = server;
        this.port = addr.port;
        resolve(addr.port);
      });
    });
  }

  stop(): void {
    try {
      this.server?.close();
    } catch {
      /* ignore */
    }
    this.server = undefined;
    this.port = undefined;
  }
}
