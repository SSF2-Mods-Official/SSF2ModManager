using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Edit exported alt voice files under <c>sounds/</c> (stock character SWF stays loaded in-game).
    /// </summary>
    public static class AltSoundEditService
    {
        public const string SoundsDirName = "sounds";
        public const string OriginalsDirName = "_originals";
        public const string ReplacementsFileName = "replacements.json";
        public const string LinkagesFileName = "linkages.json";

        public sealed class SoundItem
        {
            public string RelativePath { get; init; } = "";
            public string FileName { get; init; } = "";
            public bool Replaced { get; init; }
            public string FullPath { get; init; } = "";
            public string? OriginalPath { get; init; }
            public long CurrentBytes { get; init; }
            public long? OriginalBytes { get; init; }
            public string? SoundId { get; init; }

            /// <summary>Leading asterisk so replaced clips stand out in the dropdown.</summary>
            public string DisplayLabel => Replaced ? "* " + FileName : FileName;
        }

        public static string SoundsDir(string altFolder) => Path.Combine(altFolder, SoundsDirName);
        public static string OriginalsDir(string altFolder) => Path.Combine(SoundsDir(altFolder), OriginalsDirName);
        public static string ReplacementsPath(string altFolder) => Path.Combine(SoundsDir(altFolder), ReplacementsFileName);

        public static string? FindCharacterSwf(string altFolder, string? characterId = null)
        {
            if (!Directory.Exists(altFolder))
                return null;
            if (!string.IsNullOrWhiteSpace(characterId))
            {
                var preferred = Path.Combine(altFolder, characterId.Trim() + ".swf");
                if (File.Exists(preferred))
                    return preferred;
            }
            return Directory.GetFiles(altFolder, "*.swf")
                .OrderBy(f => Path.GetFileName(f).Length)
                .FirstOrDefault();
        }

        public static List<SoundItem> ListSounds(string altFolder)
        {
            var list = new List<SoundItem>();
            var sounds = SoundsDir(altFolder);
            if (!Directory.Exists(sounds))
                return list;
            var map = LoadReplacements(altFolder);
            var originals = OriginalsDir(altFolder);
            foreach (var file in Directory.GetFiles(sounds, "*.*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (name.Equals(ReplacementsFileName, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(LinkagesFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is not (".wav" or ".mp3" or ".flv" or ".ogg"))
                    continue;
                var replaced = map.ContainsKey(name)
                    || map.Keys.Any(k => Path.GetFileNameWithoutExtension(k)
                        .Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase));
                string? origPath = null;
                long? origBytes = null;
                var candidate = Path.Combine(originals, name);
                if (!File.Exists(candidate) && Directory.Exists(originals))
                {
                    candidate = Directory.GetFiles(originals).FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f)
                            .Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase))
                        ?? "";
                }
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                {
                    origPath = candidate;
                    origBytes = new FileInfo(candidate).Length;
                }
                list.Add(new SoundItem
                {
                    RelativePath = name,
                    FileName = name,
                    Replaced = replaced,
                    FullPath = file,
                    OriginalPath = origPath,
                    CurrentBytes = new FileInfo(file).Length,
                    OriginalBytes = origBytes,
                    SoundId = ParseSoundId(name)
                });
            }
            // Replaced clips first so the * marks are easy to spot when opening the list.
            return list
                .OrderByDescending(s => s.Replaced)
                .ThenBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static int ReplacedCount(string altFolder) => LoadReplacements(altFolder).Count;

        public static string? ParseSoundId(string fileName)
        {
            var m = Regex.Match(Path.GetFileName(fileName), @"^(-?\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Backs up the current file once, writes a volume-matched replacement into <c>sounds/</c> (no SWF inject).
        /// </summary>
        public static async Task ReplaceAsync(
            string altFolder,
            string soundFileName,
            string newAudioPath,
            string? characterId = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(newAudioPath))
                throw new FileNotFoundException("Replacement audio not found.", newAudioPath);
            var sounds = SoundsDir(altFolder);
            var target = Path.Combine(sounds, Path.GetFileName(soundFileName));
            if (!File.Exists(target))
                throw new FileNotFoundException("Sound not in alt folder: " + soundFileName, target);
            Directory.CreateDirectory(OriginalsDir(altFolder));
            var backup = Path.Combine(OriginalsDir(altFolder), Path.GetFileName(soundFileName));
            if (!File.Exists(backup))
                File.Copy(target, backup, overwrite: false);

            var matched = await MatchVolumeToOriginalAsync(backup, newAudioPath, ct).ConfigureAwait(false);
            var outExt = Path.GetExtension(target).ToLowerInvariant();
            if (outExt is ".wav" or ".mp3")
            {
                if (string.Equals(Path.GetExtension(matched), outExt, StringComparison.OrdinalIgnoreCase))
                    CopyOverwriteRelease(matched, target);
                else
                {
                    // Prefer wav on disk for AIR Sound playback when original was flv-named.
                    var wavTarget = Path.ChangeExtension(target, ".wav");
                    CopyOverwriteRelease(matched, wavTarget);
                    if (!string.Equals(wavTarget, target, StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                        try { File.Delete(target); } catch { }
                    target = wavTarget;
                    soundFileName = Path.GetFileName(wavTarget);
                }
            }
            else
            {
                var wavTarget = Path.ChangeExtension(target, ".wav");
                CopyOverwriteRelease(matched, wavTarget);
                try { File.Delete(target); } catch { }
                target = wavTarget;
                soundFileName = Path.GetFileName(wavTarget);
                RewriteLinkageFileName(altFolder, Path.GetFileName(backup), soundFileName);
            }

            var map = LoadReplacements(altFolder);
            map[Path.GetFileName(soundFileName)] = new JObject
            {
                ["replaced"] = true,
                ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            // If we renamed flv→wav, drop old key.
            var oldName = Path.GetFileName(backup);
            if (!string.Equals(oldName, Path.GetFileName(soundFileName), StringComparison.OrdinalIgnoreCase))
                map.Remove(oldName);
            SaveReplacements(altFolder, map);
            DebugLogger.Log($"[alts] sound replace file={soundFileName} bytes={new FileInfo(target).Length}");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public static Task RestoreAsync(
            string altFolder,
            string soundFileName,
            string? characterId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(soundFileName);
            var backup = Path.Combine(OriginalsDir(altFolder), name);
            var target = Path.Combine(SoundsDir(altFolder), name);
            if (!File.Exists(backup))
            {
                // May have been renamed flv→wav; try matching stem in originals.
                backup = Directory.Exists(OriginalsDir(altFolder))
                    ? Directory.GetFiles(OriginalsDir(altFolder))
                        .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                            .Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase))
                    : null;
                if (backup == null)
                    throw new FileNotFoundException("No original backup for " + name);
                target = Path.Combine(SoundsDir(altFolder), Path.GetFileName(backup));
                name = Path.GetFileName(backup);
            }
            CopyOverwriteRelease(backup, target);
            var map = LoadReplacements(altFolder);
            map.Remove(name);
            map.Remove(Path.GetFileName(soundFileName));
            SaveReplacements(altFolder, map);
            DebugLogger.Log($"[alts] sound restore file={name}");
            return Task.CompletedTask;
        }

        /// <summary>Copy via temp + retries so MediaPlayer / game locks do not fail replace.</summary>
        public static void CopyOverwriteRelease(string source, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var tmp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
            File.Copy(source, tmp, overwrite: true);
            Exception? last = null;
            for (var i = 0; i < 8; i++)
            {
                try
                {
                    if (File.Exists(dest))
                        File.Delete(dest);
                    File.Move(tmp, dest);
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(40 * (i + 1));
                }
                catch (UnauthorizedAccessException ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(40 * (i + 1));
                }
            }
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw last ?? new IOException("Could not overwrite " + dest);
        }

        public static async Task RestoreAllAsync(
            string altFolder,
            string? characterId = null,
            CancellationToken ct = default)
        {
            var map = LoadReplacements(altFolder);
            foreach (var name in map.Keys.ToList())
                await RestoreAsync(altFolder, name, characterId, ct).ConfigureAwait(false);
        }

        /// <summary>Build <c>linkages.json</c> from FFDec export names (<c>id_LinkageName.ext</c>).</summary>
        public static int WriteLinkagesJson(string soundsDir)
        {
            var jo = new JObject();
            if (!Directory.Exists(soundsDir))
                return 0;
            foreach (var file in Directory.GetFiles(soundsDir))
            {
                var name = Path.GetFileName(file);
                if (name.Equals(LinkagesFileName, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(ReplacementsFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is not (".wav" or ".mp3" or ".flv" or ".ogg"))
                    continue;
                var stem = Path.GetFileNameWithoutExtension(name);
                var m = Regex.Match(stem, @"^-?\d+_(.+)$");
                var linkage = m.Success ? m.Groups[1].Value : stem;
                if (linkage.Length == 0) continue;
                jo[linkage] = name;
            }
            File.WriteAllText(Path.Combine(soundsDir, LinkagesFileName), jo.ToString(Formatting.Indented));
            return jo.Count;
        }

        /// <summary>Convert flv/ogg under sounds/ to wav for AIR <c>Sound</c> playback; refresh linkages.</summary>
        public static async Task<int> ConvertNonAirSoundsToWavAsync(string soundsDir, CancellationToken ct = default)
        {
            if (!Directory.Exists(soundsDir)) return 0;
            var n = 0;
            foreach (var file in Directory.GetFiles(soundsDir).ToList())
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".flv" or ".ogg")) continue;
                var playable = await EnsurePlayableAsync(file, ct).ConfigureAwait(false);
                if (playable == null) continue;
                var dest = Path.ChangeExtension(file, ".wav");
                File.Copy(playable, dest, overwrite: true);
                try { File.Delete(file); } catch { }
                n++;
            }
            WriteLinkagesJson(soundsDir);
            return n;
        }

        /// <summary>Peak-match <paramref name="replacementPath"/> loudness to <paramref name="originalPath"/>; returns a wav path.</summary>
        public static async Task<string> MatchVolumeToOriginalAsync(
            string originalPath, string replacementPath, CancellationToken ct = default)
        {
            var origPlay = await EnsurePlayableAsync(originalPath, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Could not decode original for volume match.");
            var replPlay = await EnsurePlayableAsync(replacementPath, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Could not decode replacement for volume match.");
            if (!TryReadPcm16(origPlay, out var oCh, out var oRate, out var oSamples)
                || !TryReadPcm16(replPlay, out var rCh, out var rRate, out var rSamples))
            {
                // Fall back: copy replacement as-is (already playable).
                return replPlay;
            }
            var oPeak = Peak(oSamples);
            var rPeak = Peak(rSamples);
            if (rPeak < 1e-6 || oPeak < 1e-6)
                return replPlay;
            var gain = oPeak / rPeak;
            if (gain > 8f) gain = 8f;
            if (gain < 0.05f) gain = 0.05f;
            for (var i = 0; i < rSamples.Length; i++)
            {
                var v = rSamples[i] * gain;
                if (v > short.MaxValue) v = short.MaxValue;
                if (v < short.MinValue) v = short.MinValue;
                rSamples[i] = (short)v;
            }
            var outPath = Path.Combine(Path.GetTempPath(), "ssf2mm-alt-vol-" + Guid.NewGuid().ToString("N") + ".wav");
            WritePcm16Wav(outPath, rSamples, rCh, rRate);
            DebugLogger.Log($"[alts] sound volume gain={gain:0.###} oPeak={oPeak} rPeak={rPeak}");
            return outPath;
        }

        public static float Peak(short[] samples)
        {
            var peak = 0;
            foreach (var s in samples)
            {
                var a = Math.Abs((int)s);
                if (a > peak) peak = a;
            }
            return peak;
        }

        /// <summary>Returns a path MediaPlayer can open (wav/mp3). Converts flv via ffmpeg when needed.</summary>
        public static async Task<string?> EnsurePlayableAsync(string audioPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
                return null;
            var ext = Path.GetExtension(audioPath).ToLowerInvariant();
            if (ext is ".wav" or ".mp3" or ".wma")
                return audioPath;
            var cache = Path.Combine(Path.GetTempPath(), "ssf2mm-alt-preview");
            Directory.CreateDirectory(cache);
            var outWav = Path.Combine(cache,
                Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(audioPath) + "|" + File.GetLastWriteTimeUtc(audioPath).Ticks)))
                + ".wav");
            if (File.Exists(outWav) && new FileInfo(outWav).Length > 44)
                return outWav;
            var ffmpeg = AppPaths.FfmpegExe;
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
            {
                // Try raw decode for flv/mp3 payloads dumped by FFDec.
                var raw = await File.ReadAllBytesAsync(audioPath, ct).ConfigureAwait(false);
                if (FlaSoundDecoder.TryDecodeToPcm16Wav(raw, out var wav) && wav.Length > 44)
                {
                    await File.WriteAllBytesAsync(outWav, wav, ct).ConfigureAwait(false);
                    return outWav;
                }
                return null;
            }
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -i \"{audioPath}\" -acodec pcm_s16le -ar 44100 \"{outWav}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return File.Exists(outWav) && new FileInfo(outWav).Length > 44 ? outWav : null;
        }

        public static double? TryReadDurationSeconds(string? playablePath)
        {
            if (string.IsNullOrWhiteSpace(playablePath) || !File.Exists(playablePath))
                return null;
            try
            {
                var ext = Path.GetExtension(playablePath).ToLowerInvariant();
                if (ext == ".wav")
                {
                    using var fs = File.OpenRead(playablePath);
                    if (fs.Length < 44) return null;
                    var br = new BinaryReader(fs);
                    br.ReadBytes(22);
                    var channels = br.ReadUInt16();
                    var sampleRate = br.ReadInt32();
                    br.ReadInt32();
                    br.ReadUInt16();
                    var bits = br.ReadUInt16();
                    // find data chunk
                    fs.Position = 12;
                    while (fs.Position + 8 <= fs.Length)
                    {
                        var id = new string(br.ReadChars(4));
                        var size = br.ReadInt32();
                        if (id == "data")
                        {
                            if (channels == 0 || sampleRate == 0 || bits == 0) return null;
                            var bytesPerSec = channels * sampleRate * (bits / 8.0);
                            return bytesPerSec > 0 ? size / bytesPerSec : null;
                        }
                        fs.Position += size;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void RewriteLinkageFileName(string altFolder, string oldFile, string newFile)
        {
            var path = Path.Combine(SoundsDir(altFolder), LinkagesFileName);
            if (!File.Exists(path)) return;
            try
            {
                var jo = JObject.Parse(File.ReadAllText(path));
                foreach (var prop in jo.Properties().ToList())
                {
                    if (string.Equals((string?)prop.Value, oldFile, StringComparison.OrdinalIgnoreCase))
                        prop.Value = newFile;
                }
                File.WriteAllText(path, jo.ToString(Formatting.Indented));
            }
            catch { }
        }

        public static bool TryReadPcm16(string wavPath, out int channels, out int sampleRate, out short[] samples)
        {
            channels = 0;
            sampleRate = 0;
            samples = Array.Empty<short>();
            try
            {
                using var fs = File.OpenRead(wavPath);
                if (fs.Length < 44) return false;
                using var br = new BinaryReader(fs);
                if (new string(br.ReadChars(4)) != "RIFF") return false;
                br.ReadInt32();
                if (new string(br.ReadChars(4)) != "WAVE") return false;
                short bits = 0;
                byte[]? data = null;
                while (fs.Position + 8 <= fs.Length)
                {
                    var id = new string(br.ReadChars(4));
                    var size = br.ReadInt32();
                    if (id == "fmt ")
                    {
                        var fmtPos = fs.Position;
                        br.ReadUInt16();
                        channels = br.ReadUInt16();
                        sampleRate = br.ReadInt32();
                        br.ReadInt32();
                        br.ReadUInt16();
                        bits = (short)br.ReadUInt16();
                        fs.Position = fmtPos + size;
                    }
                    else if (id == "data")
                    {
                        data = br.ReadBytes(size);
                        break;
                    }
                    else
                    {
                        fs.Position += size;
                    }
                }
                if (data == null || bits != 16 || channels < 1) return false;
                samples = new short[data.Length / 2];
                Buffer.BlockCopy(data, 0, samples, 0, samples.Length * 2);
                return samples.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void WritePcm16Wav(string path, short[] samples, int channels, int sampleRate)
        {
            var dataBytes = samples.Length * 2;
            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataBytes);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((ushort)1);
            bw.Write((ushort)channels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * channels * 2);
            bw.Write((ushort)(channels * 2));
            bw.Write((ushort)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataBytes);
            var buf = new byte[dataBytes];
            Buffer.BlockCopy(samples, 0, buf, 0, dataBytes);
            bw.Write(buf);
        }

        /// <summary>File-only replace for tests (no volume match). Same backups / replacements.json as ReplaceAsync.</summary>
        public static void ReplaceFileOnly(string altFolder, string soundFileName, string newAudioPath)
        {
            var sounds = SoundsDir(altFolder);
            Directory.CreateDirectory(sounds);
            var target = Path.Combine(sounds, Path.GetFileName(soundFileName));
            if (!File.Exists(target))
                throw new FileNotFoundException("Sound not in alt folder: " + soundFileName, target);
            Directory.CreateDirectory(OriginalsDir(altFolder));
            var backup = Path.Combine(OriginalsDir(altFolder), Path.GetFileName(soundFileName));
            if (!File.Exists(backup))
                File.Copy(target, backup, overwrite: false);
            File.Copy(newAudioPath, target, overwrite: true);
            var map = LoadReplacements(altFolder);
            map[Path.GetFileName(soundFileName)] = new JObject
            {
                ["replaced"] = true,
                ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            SaveReplacements(altFolder, map);
        }

        public static void RestoreFileOnly(string altFolder, string soundFileName)
        {
            var name = Path.GetFileName(soundFileName);
            var backup = Path.Combine(OriginalsDir(altFolder), name);
            var target = Path.Combine(SoundsDir(altFolder), name);
            if (!File.Exists(backup))
                throw new FileNotFoundException("No original backup for " + name, backup);
            File.Copy(backup, target, overwrite: true);
            var map = LoadReplacements(altFolder);
            map.Remove(name);
            SaveReplacements(altFolder, map);
        }

        private static Dictionary<string, JObject> LoadReplacements(string altFolder)
        {
            var path = ReplacementsPath(altFolder);
            var dict = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return dict;
                var jo = JObject.Parse(File.ReadAllText(path));
                foreach (var prop in jo.Properties())
                {
                    if (prop.Value is JObject obj)
                        dict[prop.Name] = obj;
                    else
                        dict[prop.Name] = new JObject { ["replaced"] = true };
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[alts] replacements read failed " + ex.Message);
            }
            return dict;
        }

        private static void SaveReplacements(string altFolder, Dictionary<string, JObject> map)
        {
            Directory.CreateDirectory(SoundsDir(altFolder));
            var jo = new JObject();
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                jo[kv.Key] = kv.Value;
            File.WriteAllText(ReplacementsPath(altFolder), jo.ToString(Formatting.Indented));
        }
    }
}
