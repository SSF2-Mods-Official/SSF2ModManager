using System.IO;
using System.IO.Compression;
using System.Windows.Media;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Plays a stage track the same way SSF2 Stream does: extract the MP3 inside
    /// <c>data/sound/bgm_*.swf</c> and play it here when the game is not running.
    /// </summary>
    public static class BgmPreviewService
    {
        private static MediaPlayer? _player;
        private static bool _paused;

        public static string PlayLocal(string installRoot, string musicId)
        {
            var swf = Path.Combine(installRoot, "data", "sound", musicId + ".swf");
            if (!File.Exists(swf))
                throw new FileNotFoundException("Missing " + musicId + ".swf under data/sound.", swf);

            var cache = Path.Combine(Path.GetTempPath(), "SSF2ModManager", "bgm-preview");
            Directory.CreateDirectory(cache);
            var mp3 = Path.Combine(cache, musicId + ".mp3");
            if (!File.Exists(mp3) || File.GetLastWriteTimeUtc(mp3) < File.GetLastWriteTimeUtc(swf))
                ExtractMp3(swf, mp3);

            _player ??= new MediaPlayer();
            _player.Open(new Uri(mp3));
            _player.Play();
            _paused = false;
            return "Playing " + musicId;
        }

        public static void Pause()
        {
            if (_player == null)
                return;
            if (_paused)
            {
                _player.Play();
                _paused = false;
            }
            else
            {
                _player.Pause();
                _paused = true;
            }
        }

        public static void Stop()
        {
            try { _player?.Stop(); } catch { }
            _paused = false;
        }

        public static void ExtractMp3(string swfPath, string outPath)
        {
            var data = File.ReadAllBytes(swfPath);
            if (data.Length < 8)
                throw new InvalidDataException("Not a SWF.");
            var sig = System.Text.Encoding.ASCII.GetString(data, 0, 3);
            byte[] body = sig switch
            {
                "FWS" => data[8..],
                "CWS" => InflateZlib(data[8..]),
                "ZWS" => throw new InvalidDataException("LZMA SWF is not supported."),
                _ => throw new InvalidDataException("Not a SWF (" + sig + ").")
            };

            var mp3 = FindDefineSoundMp3(body) ?? FindMpegSync(body);
            if (mp3 == null || mp3.Length < 64)
                throw new InvalidDataException("No MP3 found in " + Path.GetFileName(swfPath) + ".");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllBytes(outPath, mp3);
        }

        private static byte[] InflateZlib(byte[] zlib)
        {
            using var input = new MemoryStream(zlib);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }

        private static byte[]? FindDefineSoundMp3(byte[] body)
        {
            if (body.Length < 2)
                return null;
            var nbits = body[0] >> 3;
            var rectBytes = (5 + nbits * 4 + 7) / 8;
            var i = rectBytes + 4;
            while (i + 2 <= body.Length)
            {
                var codeLen = body[i] | (body[i + 1] << 8);
                var tag = codeLen >> 6;
                var length = codeLen & 0x3f;
                i += 2;
                if (length == 0x3f)
                {
                    if (i + 4 > body.Length)
                        break;
                    length = body[i] | (body[i + 1] << 8) | (body[i + 2] << 16) | (body[i + 3] << 24);
                    i += 4;
                }
                if (length < 0 || i + length > body.Length)
                    break;
                if (tag == 0)
                    break;
                if (tag == 14 && length >= 9)
                {
                    var fmt = (body[i + 2] >> 4) & 0x0f;
                    if (fmt == 2 && length > 9)
                    {
                        var mp3 = new byte[length - 9];
                        Buffer.BlockCopy(body, i + 9, mp3, 0, mp3.Length);
                        if (mp3.Length > 64 && (mp3[0] == 0xff || (mp3[0] == (byte)'I' && mp3[1] == (byte)'D' && mp3[2] == (byte)'3')))
                            return mp3;
                    }
                }
                i += length;
            }
            return null;
        }

        private static byte[]? FindMpegSync(byte[] body)
        {
            byte[][] magics =
            {
                new byte[] { 0xff, 0xfb },
                new byte[] { 0xff, 0xfa },
                new byte[] { 0xff, 0xf3 },
                new byte[] { 0xff, 0xf2 },
                "ID3"u8.ToArray()
            };
            foreach (var magic in magics)
            {
                var at = IndexOf(body, magic);
                if (at < 0)
                    continue;
                var mp3 = new byte[body.Length - at];
                Buffer.BlockCopy(body, at, mp3, 0, mp3.Length);
                return mp3;
            }
            return null;
        }

        private static int IndexOf(byte[] hay, byte[] needle)
        {
            for (var i = 0; i + needle.Length <= hay.Length; i++)
            {
                var ok = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (hay[i + j] != needle[j])
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    return i;
            }
            return -1;
        }
    }
}
