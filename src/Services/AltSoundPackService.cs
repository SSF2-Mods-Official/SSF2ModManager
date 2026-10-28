using System.IO;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Shrinks a GameBanana character alt to sounds only: DefineSound + SymbolClass names.
    /// Art and DoABC scripts are dropped — the resource id already comes from mappings / UrlOverride.
    /// </summary>
    public static class AltSoundPackService
    {
        // Keep: End(0), ShowFrame(1), SetBackgroundColor(9), Protect(24), FileAttributes(69),
        // ScriptLimits(65), Metadata(77), DefineSound(14), StartSound(15),
        // SymbolClass(76), ExportAssets(56), ImportAssets(57/71), EnableDebugger(64/66),
        // DefineSceneAndFrameLabelData(86).
        // Drop DoABC (72/82): register() and stats scripts are not needed for a voice UrlOverride.
        private static readonly HashSet<int> Keep = new()
        {
            0, 1, 9, 14, 15, 24, 56, 57, 64, 65, 66, 69, 71, 76, 77, 86
        };

        /// <summary>
        /// Writes a smaller FWS to <paramref name="destPath"/> (or next to the source).
        /// If shrink fails or does not help, writes the plain SWF instead.
        /// </summary>
        public static string ShrinkToSounds(string sourcePath, string? destPath = null, string? resourceId = null)
        {
            var raw = File.ReadAllBytes(sourcePath);
            var before = raw.Length;
            destPath ??= Path.Combine(Path.GetDirectoryName(sourcePath)!,
                Path.GetFileNameWithoutExtension(sourcePath) + ".soundpack.swf");

            byte[] swf;
            try
            {
                swf = SwfPackage.ToSwf(raw);
            }
            catch
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                DebugLogger.Log($"[alts] soundpack copy-only in={before} id={resourceId}");
                return destPath;
            }

            var shrunk = StripDisplayTags(swf);
            if (shrunk == null || shrunk.Length >= swf.Length || shrunk.Length < 64)
            {
                File.WriteAllBytes(destPath, swf);
                DebugLogger.Log($"[alts] soundpack keep-full in={before} out={swf.Length} id={resourceId}");
                return destPath;
            }

            File.WriteAllBytes(destPath, shrunk);
            DebugLogger.Log($"[alts] soundpack in={before} out={shrunk.Length} id={resourceId}");
            return destPath;
        }

        /// <summary>Pure transform for fixtures. Returns null when no DefineSound tag remains.</summary>
        public static byte[]? StripDisplayTags(byte[] fws)
        {
            if (fws.Length < 12 || fws[0] != (byte)'F' || fws[1] != (byte)'W' || fws[2] != (byte)'S')
                return null;

            var pos = 8;
            if (!SkipRect(fws, ref pos))
                return null;
            var headerEnd = pos + 4;
            if (headerEnd > fws.Length)
                return null;

            using var outBody = new MemoryStream();
            outBody.Write(fws, 8, headerEnd - 8);

            pos = headerEnd;
            var keptSound = false;
            while (pos + 2 <= fws.Length)
            {
                var headerAt = pos;
                var code = fws[pos] | (fws[pos + 1] << 8);
                pos += 2;
                var type = code >> 6;
                var length = code & 0x3F;
                var longHeader = false;
                if (length == 0x3F)
                {
                    if (pos + 4 > fws.Length)
                        break;
                    length = fws[pos] | (fws[pos + 1] << 8) | (fws[pos + 2] << 16) | (fws[pos + 3] << 24);
                    pos += 4;
                    longHeader = true;
                }
                if (length < 0 || pos + length > fws.Length)
                    break;
                if (type == 0)
                {
                    outBody.WriteByte(0);
                    outBody.WriteByte(0);
                    break;
                }

                if (Keep.Contains(type))
                {
                    var headerLen = longHeader ? 6 : 2;
                    outBody.Write(fws, headerAt, headerLen + length);
                    if (type == 14) keptSound = true;
                }

                pos += length;
            }

            if (!keptSound)
                return null;

            var body = outBody.ToArray();
            var result = new byte[8 + body.Length];
            result[0] = (byte)'F';
            result[1] = (byte)'W';
            result[2] = (byte)'S';
            result[3] = fws[3];
            var total = result.Length;
            result[4] = (byte)total;
            result[5] = (byte)(total >> 8);
            result[6] = (byte)(total >> 16);
            result[7] = (byte)(total >> 24);
            Buffer.BlockCopy(body, 0, result, 8, body.Length);
            return result;
        }

        private static bool SkipRect(byte[] swf, ref int pos)
        {
            if (pos >= swf.Length)
                return false;
            var nBits = swf[pos] >> 3;
            var bits = 5 + nBits * 4;
            pos += (bits + 7) / 8;
            return pos <= swf.Length;
        }
    }
}
