using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SSF2ModManager.Services
{
    /// <summary>One bitmap tag in a stage file. The character id is the number shapes point at, not a linkage name.</summary>
    public sealed class StageImageTag
    {
        public StageImageTag(int characterId, int width, int height, byte[]? bgra)
        {
            CharacterId = characterId;
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public int CharacterId { get; }
        public int Width { get; }
        public int Height { get; }
        public byte[]? Bgra { get; }
    }

    /// <summary>Reads image tags from a stage file and replaces their pixels in a buffer.</summary>
    public static class StageAssetSwap
    {
        public static List<StageImageTag> List(byte[] fileBytes)
        {
            var list = new List<StageImageTag>();
            if (!TryPlain(fileBytes, out var swf))
                return list;
            Walk(swf, (code, body) =>
            {
                if (!TryImage(code, body, out var id, out var width, out var height, out var bgra))
                    return;
                list.Add(new StageImageTag(id, width, height, bgra));
            });
            return list;
        }

        /// <summary>
        /// Returns a new SWF whose image tags use <paramref name="replacements"/> when the size matches.
        /// The input bytes are not changed.
        /// </summary>
        public static byte[] Swap(byte[] fileBytes, IReadOnlyDictionary<int, StageImageTag> replacements)
        {
            if (!TryPlain(fileBytes, out var swf))
                return fileBytes ?? Array.Empty<byte>();
            var header = HeaderLength(swf);
            if (header < 8 || header > swf.Length)
                return swf;
            using var output = new MemoryStream();
            output.Write(swf, 0, header);
            Walk(swf, (code, body) =>
            {
                if (TryImage(code, body, out var id, out var width, out var height, out _)
                    && replacements != null
                    && replacements.TryGetValue(id, out var next)
                    && next.Bgra != null
                    && next.Width == width
                    && next.Height == height
                    && width > 0
                    && next.Bgra.Length >= width * height * 4)
                {
                    WriteTag(output, 36, LosslessBody(id, width, height, next.Bgra));
                }
                else
                {
                    WriteTag(output, code, body);
                }
            });
            var bytes = output.ToArray();
            var length = bytes.Length;
            bytes[4] = (byte)length;
            bytes[5] = (byte)(length >> 8);
            bytes[6] = (byte)(length >> 16);
            bytes[7] = (byte)(length >> 24);
            return bytes;
        }

        public static StageImageTag? Find(byte[] fileBytes, int characterId)
        {
            foreach (var tag in List(fileBytes))
            {
                if (tag.CharacterId == characterId)
                    return tag;
            }
            return null;
        }

        private static bool TryPlain(byte[]? fileBytes, out byte[] swf)
        {
            swf = Array.Empty<byte>();
            if (fileBytes == null || fileBytes.Length < 8)
                return false;
            try
            {
                swf = SwfPackage.ToSwf(fileBytes);
            }
            catch
            {
                return false;
            }
            return swf.Length >= 8 && swf[0] == (byte)'F';
        }

        private static int HeaderLength(byte[] swf)
        {
            var rect = swf.Length > 8 ? (swf[8] >> 3) : 0;
            var rectBytes = (5 + rect * 4 + 7) / 8;
            return 8 + rectBytes + 4;
        }

        private static void Walk(byte[] swf, Action<int, byte[]> visit)
        {
            var pos = HeaderLength(swf);
            while (pos + 2 <= swf.Length)
            {
                var codeLen = swf[pos] | (swf[pos + 1] << 8);
                var code = codeLen >> 6;
                var len = codeLen & 0x3F;
                var head = 2;
                if (len == 0x3F)
                {
                    if (pos + 6 > swf.Length)
                        break;
                    len = swf[pos + 2] | (swf[pos + 3] << 8) | (swf[pos + 4] << 16) | (swf[pos + 5] << 24);
                    head = 6;
                }
                if (code == 0)
                {
                    visit(0, Array.Empty<byte>());
                    break;
                }
                if (len < 0 || pos + head + len > swf.Length)
                    break;
                var body = new byte[len];
                Buffer.BlockCopy(swf, pos + head, body, 0, len);
                visit(code, body);
                pos += head + len;
            }
        }

        private static bool TryImage(int code, byte[] body, out int id, out int width, out int height, out byte[]? bgra)
        {
            id = 0;
            width = 0;
            height = 0;
            bgra = null;
            if (body == null || body.Length < 2)
                return false;
            if (code is not (6 or 20 or 21 or 35 or 36))
                return false;
            id = body[0] | (body[1] << 8);
            if (code is 20 or 36)
                return TryLossless(body, out width, out height, out bgra);
            if (code == 21 && body.Length > 2)
                return TryJpeg(body, 2, body.Length - 2, out width, out height, out bgra);
            if (code == 35 && body.Length > 6)
            {
                var alpha = body[2] | (body[3] << 8) | (body[4] << 16) | (body[5] << 24);
                var jpegLen = Math.Min(Math.Max(alpha, 0), body.Length - 6);
                return TryJpeg(body, 6, jpegLen, out width, out height, out bgra);
            }
            return true;
        }

        private static bool TryLossless(byte[] body, out int width, out int height, out byte[]? bgra)
        {
            width = 0;
            height = 0;
            bgra = null;
            if (body.Length < 7 || body[2] != 5)
                return body.Length >= 2;
            width = body[3] | (body[4] << 8);
            height = body[5] | (body[6] << 8);
            if (width < 1 || height < 1 || width > 8192 || height > 8192)
                return true;
            try
            {
                using var input = new MemoryStream(body, 7, body.Length - 7, writable: false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
                using var raw = new MemoryStream();
                zlib.CopyTo(raw);
                var pix = raw.ToArray();
                var count = width * height;
                if (pix.Length < count * 4)
                    return true;
                bgra = new byte[count * 4];
                for (var i = 0; i < count; i++)
                {
                    var s = i * 4;
                    bgra[s] = pix[s + 3];
                    bgra[s + 1] = pix[s + 2];
                    bgra[s + 2] = pix[s + 1];
                    bgra[s + 3] = pix[s];
                }
            }
            catch
            {
                bgra = null;
            }
            return true;
        }

        private static bool TryJpeg(byte[] body, int offset, int length, out int width, out int height, out byte[]? bgra)
        {
            width = 0;
            height = 0;
            bgra = null;
            if (length < 4 || offset + length > body.Length)
                return true;
            try
            {
                using var stream = new MemoryStream(body, offset, length, writable: false);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                    return true;
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                bgra = new byte[width * height * 4];
                converted.CopyPixels(bgra, width * 4, 0);
            }
            catch
            {
                bgra = null;
            }
            return true;
        }

        private static byte[] LosslessBody(int id, int width, int height, byte[] bgra)
        {
            var count = width * height;
            var argb = new byte[count * 4];
            for (var i = 0; i < count; i++)
            {
                var s = i * 4;
                argb[s] = bgra[s + 3];
                argb[s + 1] = bgra[s + 2];
                argb[s + 2] = bgra[s + 1];
                argb[s + 3] = bgra[s];
            }
            using var raw = new MemoryStream();
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(argb, 0, argb.Length);
            var compressed = raw.ToArray();
            var body = new byte[7 + compressed.Length];
            body[0] = (byte)id;
            body[1] = (byte)(id >> 8);
            body[2] = 5;
            body[3] = (byte)width;
            body[4] = (byte)(width >> 8);
            body[5] = (byte)height;
            body[6] = (byte)(height >> 8);
            Buffer.BlockCopy(compressed, 0, body, 7, compressed.Length);
            return body;
        }

        private static void WriteTag(Stream output, int code, byte[] body)
        {
            var len = body?.Length ?? 0;
            if (len < 63)
            {
                var codeLen = (code << 6) | len;
                output.WriteByte((byte)codeLen);
                output.WriteByte((byte)(codeLen >> 8));
            }
            else
            {
                var codeLen = (code << 6) | 0x3F;
                output.WriteByte((byte)codeLen);
                output.WriteByte((byte)(codeLen >> 8));
                output.WriteByte((byte)len);
                output.WriteByte((byte)(len >> 8));
                output.WriteByte((byte)(len >> 16));
                output.WriteByte((byte)(len >> 24));
            }
            if (len > 0)
                output.Write(body!, 0, len);
        }
    }
}
