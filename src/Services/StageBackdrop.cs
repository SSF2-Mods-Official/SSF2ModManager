using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>One decoded SWF bitmap, BGRA.</summary>
    public readonly struct StageBitmap
    {
        public StageBitmap(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Bgra { get; }
    }

    public static class MenuBackgroundStore
    {
        public static string ReadStageId(string shell)
        {
            var path = InfoPath(shell);
            if (!File.Exists(path))
                return "";
            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                var row = obj["settings"]?.FirstOrDefault(s =>
                    string.Equals(s?["id"]?.ToString(), "stageId", StringComparison.OrdinalIgnoreCase));
                return row?["value"]?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static void WriteStageId(string shell, string stageId)
        {
            var path = InfoPath(shell);
            var dir = Path.GetDirectoryName(path);
            if (dir != null)
                Directory.CreateDirectory(dir);
            JObject obj;
            if (File.Exists(path))
            {
                try { obj = JObject.Parse(File.ReadAllText(path)); }
                catch { obj = new JObject(); }
            }
            else obj = new JObject();
            var settings = obj["settings"] as JArray ?? new JArray();
            JObject? row = null;
            foreach (var token in settings)
            {
                if (token is JObject o && string.Equals(o["id"]?.ToString(), "stageId", StringComparison.OrdinalIgnoreCase))
                {
                    row = o;
                    break;
                }
            }
            if (row == null)
            {
                row = new JObject
                {
                    ["id"] = "stageId",
                    ["label"] = "Stage",
                    ["type"] = "dropdown",
                    ["optionsSource"] = "stages"
                };
                settings.Add(row);
            }
            row["value"] = stageId ?? "";
            obj["settings"] = settings;
            File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public static string? FindStageFile(string shell, string stageId)
        {
            if (string.IsNullOrWhiteSpace(shell) || string.IsNullOrWhiteSpace(stageId))
                return null;
            foreach (var ext in new[] { ".swf", ".ssf" })
            {
                var direct = Path.Combine(shell, "data", "stage", stageId + ext);
                if (File.Exists(direct))
                    return direct;
            }
            var exp = ShellWorkspaceService.StageExpansionsDirectory(shell);
            if (!Directory.Exists(exp))
                return null;
            foreach (var dir in Directory.GetDirectories(exp))
            {
                foreach (var ext in new[] { ".swf", ".ssf" })
                {
                    var path = Path.Combine(dir, stageId + ext);
                    if (File.Exists(path))
                        return path;
                }
            }
            return null;
        }

        private static string InfoPath(string shell) =>
            Path.Combine(shell, "data", "ssf2mm", "menu_background", "mod.json");
    }

    public static class StageBackdrop
    {
        /// <summary>SSF2 <c>Main.m_width</c> / <c>Main.m_height</c>. The launcher client matches this.</summary>
        public const double ClientWidth = 640;
        public const double ClientHeight = 360;

        private static readonly Dictionary<string, (DateTime Write, BitmapSource? Image)> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource? LoadSnapshot(string? stageFile)
        {
            if (string.IsNullOrWhiteSpace(stageFile) || !File.Exists(stageFile))
                return null;
            var write = File.GetLastWriteTimeUtc(stageFile);
            if (Cache.TryGetValue(stageFile, out var hit) && hit.Write == write)
                return hit.Image;
            BitmapSource? image = null;
            try
            {
                var bytes = File.ReadAllBytes(stageFile);
                var stageId = Path.GetFileNameWithoutExtension(stageFile);
                var decoded = ComposeBackground(bytes, stageId) ?? DecodeLargest(bytes);
                if (decoded != null)
                {
                    var bmp = BitmapSource.Create(
                        decoded.Value.Width,
                        decoded.Value.Height,
                        96, 96,
                        PixelFormats.Bgra32,
                        null,
                        decoded.Value.Bgra,
                        decoded.Value.Width * 4);
                    bmp.Freeze();
                    image = bmp;
                }
            }
            catch
            {
                image = null;
            }
            Cache[stageFile] = (write, image);
            return image;
        }

        /// <summary>
        /// Largest DefineBitsLossless / Lossless2 format-5 bitmap.
        /// Pixels are opaque byte, R, G, B. Output is BGRA.
        /// </summary>
        public static StageBitmap? DecodeLargest(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length < 8)
                return null;
            byte[] swf;
            try { swf = SwfPackage.ToSwf(fileBytes); }
            catch { return null; }
            if (swf.Length < 20 || swf[0] != (byte)'F')
                return null;

            var rectBytes = RectByteLength(swf, 8);
            var pos = 8 + rectBytes + 4;
            StageBitmap? best = null;
            var bestArea = 0;
            while (pos + 2 <= swf.Length)
            {
                var codeLen = swf[pos] | (swf[pos + 1] << 8);
                pos += 2;
                var code = codeLen >> 6;
                var len = codeLen & 0x3F;
                if (len == 0x3F)
                {
                    if (pos + 4 > swf.Length) break;
                    len = swf[pos] | (swf[pos + 1] << 8) | (swf[pos + 2] << 16) | (swf[pos + 3] << 24);
                    pos += 4;
                }
                if (code == 0) break;
                if (len < 0 || pos + len > swf.Length) break;
                if ((code == 20 || code == 36) && len > 8)
                {
                    var bmp = TryLossless(swf, pos, len);
                    if (bmp != null)
                    {
                        var area = bmp.Value.Width * bmp.Value.Height;
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = bmp;
                        }
                    }
                }
                pos += len;
            }
            return best;
        }

        private static StageBitmap? TryLossless(byte[] swf, int pos, int len)
        {
            var format = swf[pos + 2];
            if (format != 5) return null;
            var width = swf[pos + 3] | (swf[pos + 4] << 8);
            var height = swf[pos + 5] | (swf[pos + 6] << 8);
            if (width < 2 || height < 2 || width > 4096 || height > 4096)
                return null;
            byte[] pix;
            try
            {
                using var input = new MemoryStream(swf, pos + 7, len - 7, writable: false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
                using var raw = new MemoryStream();
                zlib.CopyTo(raw);
                pix = raw.ToArray();
            }
            catch
            {
                return null;
            }
            var count = width * height;
            if (pix.Length < count * 4)
                return null;
            var bgra = new byte[count * 4];
            for (var i = 0; i < count; i++)
            {
                var s = i * 4;
                bgra[s] = pix[s + 3];
                bgra[s + 1] = pix[s + 2];
                bgra[s + 2] = pix[s + 1];
                bgra[s + 3] = 255;
            }
            return new StageBitmap(width, height, bgra);
        }

        /// <summary>
        /// Bitmaps placed in the <c>{stageId}_bg</c> sprite, drawn low depth first.
        /// A shape contributes each bitmap fill. <see cref="DecodeLargest"/> is the fallback when this returns null.
        /// </summary>
        public static StageBitmap? ComposeBackground(byte[] fileBytes, string stageId)
        {
            if (fileBytes == null || fileBytes.Length < 8 || string.IsNullOrWhiteSpace(stageId))
                return null;
            byte[] swf;
            try { swf = SwfPackage.ToSwf(fileBytes); }
            catch { return null; }
            if (swf.Length < 20 || swf[0] != (byte)'F')
                return null;

            var bitmaps = new Dictionary<int, (int Tag, int Pos, int Len)>();
            var sprites = new Dictionary<int, (int Pos, int Len)>();
            var shapes = new Dictionary<int, (int Tag, int Pos, int Len)>();
            var symbols = new Dictionary<int, string>();
            if (!WalkTags(swf, bitmaps, sprites, shapes, symbols))
                return null;

            var bgId = FindBackgroundSprite(symbols, sprites, stageId);
            if (bgId == null)
                return null;

            var layers = new List<Layer>();
            var stack = new HashSet<int>();
            CollectSprite(swf, bgId.Value, Mx.Identity, bitmaps, sprites, shapes, layers, stack);
            if (layers.Count == 0)
                return null;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var layer in layers)
            {
                minX = Math.Min(minX, layer.X);
                minY = Math.Min(minY, layer.Y);
                maxX = Math.Max(maxX, layer.X + layer.W);
                maxY = Math.Max(maxY, layer.Y + layer.H);
            }
            var rawW = (int)Math.Ceiling(maxX) - (int)Math.Floor(minX);
            var rawH = (int)Math.Ceiling(maxY) - (int)Math.Floor(minY);
            if (rawW < 2 || rawH < 2 || rawW > 8192 || rawH > 8192)
                return null;
            var fit = 1.0;
            if (rawW > 2048 || rawH > 2048)
                fit = 2048.0 / Math.Max(rawW, rawH);
            var width = Math.Max(2, (int)Math.Ceiling(rawW * fit));
            var height = Math.Max(2, (int)Math.Ceiling(rawH * fit));
            var canvas = new byte[width * height * 4];
            var originX = Math.Floor(minX);
            var originY = Math.Floor(minY);
            foreach (var layer in layers)
                Blit(canvas, width, height, layer, originX, originY, fit);
            return new StageBitmap(width, height, canvas);
        }

        private readonly struct Mx
        {
            public Mx(double sx, double sy, double tx, double ty)
            {
                Sx = sx;
                Sy = sy;
                Tx = tx;
                Ty = ty;
            }

            public double Sx { get; }
            public double Sy { get; }
            public double Tx { get; }
            public double Ty { get; }
            public static Mx Identity => new(1, 1, 0, 0);

            public Mx Mul(Mx child) =>
                new(Sx * child.Sx, Sy * child.Sy, Sx * child.Tx + Tx, Sy * child.Ty + Ty);
        }

        private readonly struct Layer
        {
            public Layer(StageBitmap bitmap, double x, double y, double w, double h)
            {
                Bitmap = bitmap;
                X = x;
                Y = y;
                W = w;
                H = h;
            }

            public StageBitmap Bitmap { get; }
            public double X { get; }
            public double Y { get; }
            public double W { get; }
            public double H { get; }
        }

        private static int? FindBackgroundSprite(
            Dictionary<int, string> symbols,
            Dictionary<int, (int Pos, int Len)> sprites,
            string stageId)
        {
            var exact = stageId + "_bg";
            int? fallback = null;
            foreach (var pair in symbols)
            {
                if (!sprites.ContainsKey(pair.Key))
                    continue;
                if (pair.Value.Equals(exact, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
                if (pair.Value.EndsWith("_bg", StringComparison.OrdinalIgnoreCase) && fallback == null
                    && pair.Value.IndexOf("light", StringComparison.OrdinalIgnoreCase) < 0)
                    fallback = pair.Key;
            }
            if (fallback != null)
                return fallback;
            foreach (var pair in symbols)
            {
                if (sprites.ContainsKey(pair.Key) && pair.Value.EndsWith("_bg", StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            }
            return null;
        }

        private static void CollectSprite(
            byte[] swf,
            int spriteId,
            Mx place,
            Dictionary<int, (int Tag, int Pos, int Len)> bitmaps,
            Dictionary<int, (int Pos, int Len)> sprites,
            Dictionary<int, (int Tag, int Pos, int Len)> shapes,
            List<Layer> layers,
            HashSet<int> stack)
        {
            if (!stack.Add(spriteId) || !sprites.TryGetValue(spriteId, out var sprite))
                return;
            var list = new SortedDictionary<int, (int Cid, Mx Matrix, bool Mask)>();
            var pos = sprite.Pos + 4;
            var end = sprite.Pos + sprite.Len;
            while (pos + 2 <= end)
            {
                var codeLen = swf[pos] | (swf[pos + 1] << 8);
                pos += 2;
                var code = codeLen >> 6;
                var len = codeLen & 0x3F;
                if (len == 0x3F)
                {
                    if (pos + 4 > end) break;
                    len = swf[pos] | (swf[pos + 1] << 8) | (swf[pos + 2] << 16) | (swf[pos + 3] << 24);
                    pos += 4;
                }
                if (code == 0 || code == 1) break;
                if (len < 0 || pos + len > end) break;
                if (code == 5 && len >= 4)
                    list.Remove(swf[pos + 2] | (swf[pos + 3] << 8));
                else if (code == 28 && len >= 2)
                    list.Remove(swf[pos] | (swf[pos + 1] << 8));
                else if (code == 4 || code == 26 || code == 70)
                {
                    if (TryReadPlace(swf, pos, len, code, out var depth, out var cid, out var matrix, out var mask))
                    {
                        if (cid == null)
                        {
                            if (matrix != null && list.TryGetValue(depth, out var cur))
                                list[depth] = (cur.Cid, matrix.Value, cur.Mask);
                        }
                        else
                            list[depth] = (cid.Value, matrix ?? Mx.Identity, mask);
                    }
                }
                pos += len;
            }

            foreach (var item in list.Values)
            {
                if (item.Mask)
                    continue;
                var local = place.Mul(item.Matrix);
                if (bitmaps.ContainsKey(item.Cid))
                    AddBitmap(swf, bitmaps[item.Cid], local, new Mx(20, 20, 0, 0), layers);
                else if (shapes.ContainsKey(item.Cid))
                    AddShape(swf, shapes[item.Cid], local, bitmaps, layers);
                else if (sprites.ContainsKey(item.Cid))
                    CollectSprite(swf, item.Cid, local, bitmaps, sprites, shapes, layers, stack);
            }
            stack.Remove(spriteId);
        }

        private static void AddShape(
            byte[] swf,
            (int Tag, int Pos, int Len) shape,
            Mx place,
            Dictionary<int, (int Tag, int Pos, int Len)> bitmaps,
            List<Layer> layers)
        {
            if (shape.Len < 4)
                return;
            var bits = new Bits(swf, shape.Pos + 2);
            if (!bits.ReadRect(out var bx0, out var bx1, out var by0, out var by1))
                return;
            var count = bits.U8();
            if (count == 0xFF)
                count = bits.U16();
            if (count > 64)
                count = 64;
            var rgba = shape.Tag == 32 || shape.Tag == 83;
            for (var i = 0; i < count; i++)
            {
                var kind = bits.U8();
                if (kind == 0)
                {
                    var r = (byte)bits.U8();
                    var g = (byte)bits.U8();
                    var b = (byte)bits.U8();
                    var a = rgba ? (byte)bits.U8() : (byte)255;
                    AddSolid(place, bx0, by0, bx1, by1, b, g, r, a, layers);
                }
                else if (kind == 0x10 || kind == 0x12 || kind == 0x13)
                {
                    if (!bits.ReadMatrix(out _))
                        return;
                    if (!bits.SkipGradient(rgba, kind == 0x13))
                        return;
                }
                else if (kind >= 0x40 && kind <= 0x43)
                {
                    var id = bits.U16();
                    if (!bits.ReadMatrix(out var fill))
                        return;
                    if (bitmaps.TryGetValue(id, out var bmp))
                        AddBitmap(swf, bmp, place, fill, layers);
                }
                else
                    return;
            }
        }

        private static void AddSolid(
            Mx place, int x0, int y0, int x1, int y1, byte b, byte g, byte r, byte a, List<Layer> layers)
        {
            if (a == 0)
                return;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var corner in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
            {
                var px = (place.Sx * corner.Item1 + place.Tx) / 20.0;
                var py = (place.Sy * corner.Item2 + place.Ty) / 20.0;
                minX = Math.Min(minX, px);
                minY = Math.Min(minY, py);
                maxX = Math.Max(maxX, px);
                maxY = Math.Max(maxY, py);
            }
            var w = maxX - minX;
            var h = maxY - minY;
            if (w < 1 || h < 1)
                return;
            var pix = new byte[16];
            for (var i = 0; i < 4; i++)
            {
                pix[i * 4] = b;
                pix[i * 4 + 1] = g;
                pix[i * 4 + 2] = r;
                pix[i * 4 + 3] = a;
            }
            layers.Add(new Layer(new StageBitmap(2, 2, pix), minX, minY, w, h));
        }

        private static void AddBitmap(
            byte[] swf,
            (int Tag, int Pos, int Len) src,
            Mx place,
            Mx fill,
            List<Layer> layers)
        {
            var bmp = ReadLossless(swf, src.Pos, src.Len, src.Tag);
            if (bmp == null)
                return;
            var x0 = place.Sx * fill.Tx + place.Tx;
            var y0 = place.Sy * fill.Ty + place.Ty;
            var x1 = place.Sx * (fill.Sx * bmp.Value.Width + fill.Tx) + place.Tx;
            var y1 = place.Sy * (fill.Sy * bmp.Value.Height + fill.Ty) + place.Ty;
            var left = Math.Min(x0, x1) / 20.0;
            var top = Math.Min(y0, y1) / 20.0;
            var w = Math.Abs(x1 - x0) / 20.0;
            var h = Math.Abs(y1 - y0) / 20.0;
            if (w < 1 || h < 1)
                return;
            layers.Add(new Layer(bmp.Value, left, top, w, h));
        }

        private static bool TryReadPlace(
            byte[] swf, int pos, int len, int code,
            out int depth, out int? cid, out Mx? matrix, out bool mask)
        {
            depth = 0;
            cid = null;
            matrix = null;
            mask = false;
            if (code == 4)
            {
                if (len < 6) return false;
                cid = swf[pos] | (swf[pos + 1] << 8);
                depth = swf[pos + 2] | (swf[pos + 3] << 8);
                var bits = new Bits(swf, pos + 4);
                if (!bits.ReadMatrix(out var mx)) return false;
                matrix = mx;
                return true;
            }
            if (len < 3) return false;
            var flags = swf[pos];
            var q = 1;
            var flags2 = 0;
            if (code == 70)
            {
                if (len < 4) return false;
                flags2 = swf[pos + 1];
                q = 2;
            }
            depth = swf[pos + q] | (swf[pos + q + 1] << 8);
            q += 2;
            mask = (flags & 0x40) != 0;
            if ((flags2 & 0x08) != 0 || ((flags2 & 0x10) != 0 && (flags & 0x02) != 0))
            {
                var end = pos + len;
                while (q < end && swf[pos + q] != 0) q++;
                q++;
            }
            if ((flags & 0x02) != 0)
            {
                if (q + 2 > len) return false;
                cid = swf[pos + q] | (swf[pos + q + 1] << 8);
                q += 2;
            }
            if ((flags & 0x04) != 0)
            {
                if (q >= len) return false;
                var bits = new Bits(swf, pos + q);
                if (!bits.ReadMatrix(out var mx)) return false;
                matrix = mx;
            }
            return true;
        }

        private static void Blit(byte[] canvas, int width, int height, Layer layer, double originX, double originY, double fit)
        {
            var bmp = layer.Bitmap;
            var x = (layer.X - originX) * fit;
            var y = (layer.Y - originY) * fit;
            var w = layer.W * fit;
            var h = layer.H * fit;
            var x0 = Math.Max(0, (int)Math.Floor(x));
            var y0 = Math.Max(0, (int)Math.Floor(y));
            var x1 = Math.Min(width, (int)Math.Ceiling(x + w));
            var y1 = Math.Min(height, (int)Math.Ceiling(y + h));
            for (var py = y0; py < y1; py++)
            {
                var v = ((py + 0.5) - y) / h;
                var sv = (int)(v * bmp.Height);
                if (sv < 0 || sv >= bmp.Height) continue;
                for (var px = x0; px < x1; px++)
                {
                    var u = ((px + 0.5) - x) / w;
                    var su = (int)(u * bmp.Width);
                    if (su < 0 || su >= bmp.Width) continue;
                    var si = (sv * bmp.Width + su) * 4;
                    var di = (py * width + px) * 4;
                    var sa = bmp.Bgra[si + 3];
                    if (sa == 0) continue;
                    if (sa == 255)
                    {
                        canvas[di] = bmp.Bgra[si];
                        canvas[di + 1] = bmp.Bgra[si + 1];
                        canvas[di + 2] = bmp.Bgra[si + 2];
                        canvas[di + 3] = 255;
                        continue;
                    }
                    var inv = 255 - sa;
                    canvas[di] = (byte)((bmp.Bgra[si] * sa + canvas[di] * inv) / 255);
                    canvas[di + 1] = (byte)((bmp.Bgra[si + 1] * sa + canvas[di + 1] * inv) / 255);
                    canvas[di + 2] = (byte)((bmp.Bgra[si + 2] * sa + canvas[di + 2] * inv) / 255);
                    canvas[di + 3] = (byte)(sa + (canvas[di + 3] * inv) / 255);
                }
            }
        }

        private static StageBitmap? ReadLossless(byte[] swf, int pos, int len, int tag)
        {
            if (len < 8 || pos + len > swf.Length) return null;
            var format = swf[pos + 2];
            var width = swf[pos + 3] | (swf[pos + 4] << 8);
            var height = swf[pos + 5] | (swf[pos + 6] << 8);
            if (width < 2 || height < 2 || width > 4096 || height > 4096)
                return null;
            var zlibAt = format == 3 ? 8 : 7;
            if (format != 3 && format != 5) return null;
            if (len <= zlibAt) return null;
            byte[] pix;
            try
            {
                using var input = new MemoryStream(swf, pos + zlibAt, len - zlibAt, writable: false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
                using var raw = new MemoryStream();
                zlib.CopyTo(raw);
                pix = raw.ToArray();
            }
            catch
            {
                return null;
            }

            var count = width * height;
            var bgra = new byte[count * 4];
            if (format == 5)
            {
                if (pix.Length < count * 4) return null;
                var keepAlpha = tag == 36;
                for (var i = 0; i < count; i++)
                {
                    var s = i * 4;
                    bgra[s] = pix[s + 3];
                    bgra[s + 1] = pix[s + 2];
                    bgra[s + 2] = pix[s + 1];
                    bgra[s + 3] = keepAlpha ? pix[s] : (byte)255;
                }
                return new StageBitmap(width, height, bgra);
            }

            var colors = swf[pos + 7] + 1;
            var entry = tag == 36 ? 4 : 3;
            var stride = (width + 3) & ~3;
            if (pix.Length < colors * entry + stride * height) return null;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = pix[colors * entry + y * stride + x];
                    var d = (y * width + x) * 4;
                    if (index >= colors)
                        continue;
                    var c = index * entry;
                    if (entry == 4)
                    {
                        bgra[d] = pix[c + 2];
                        bgra[d + 1] = pix[c + 1];
                        bgra[d + 2] = pix[c];
                        bgra[d + 3] = pix[c + 3];
                    }
                    else
                    {
                        bgra[d] = pix[c + 2];
                        bgra[d + 1] = pix[c + 1];
                        bgra[d + 2] = pix[c];
                        bgra[d + 3] = 255;
                    }
                }
            }
            return new StageBitmap(width, height, bgra);
        }

        private static bool WalkTags(
            byte[] swf,
            Dictionary<int, (int Tag, int Pos, int Len)> bitmaps,
            Dictionary<int, (int Pos, int Len)> sprites,
            Dictionary<int, (int Tag, int Pos, int Len)> shapes,
            Dictionary<int, string> symbols)
        {
            var pos = 8 + RectByteLength(swf, 8) + 4;
            while (pos + 2 <= swf.Length)
            {
                var codeLen = swf[pos] | (swf[pos + 1] << 8);
                pos += 2;
                var code = codeLen >> 6;
                var len = codeLen & 0x3F;
                if (len == 0x3F)
                {
                    if (pos + 4 > swf.Length) return false;
                    len = swf[pos] | (swf[pos + 1] << 8) | (swf[pos + 2] << 16) | (swf[pos + 3] << 24);
                    pos += 4;
                }
                if (code == 0) return true;
                if (len < 0 || pos + len > swf.Length) return false;
                if ((code == 20 || code == 36) && len >= 7)
                {
                    var id = swf[pos] | (swf[pos + 1] << 8);
                    bitmaps[id] = (code, pos, len);
                }
                else if (code == 39 && len >= 4)
                {
                    var id = swf[pos] | (swf[pos + 1] << 8);
                    sprites[id] = (pos, len);
                }
                else if ((code == 2 || code == 22 || code == 32 || code == 83) && len >= 2)
                {
                    var id = swf[pos] | (swf[pos + 1] << 8);
                    shapes[id] = (code, pos, len);
                }
                else if (code == 76 && len >= 2)
                    ReadSymbols(swf, pos, len, symbols);
                pos += len;
            }
            return true;
        }

        private static void ReadSymbols(byte[] swf, int pos, int len, Dictionary<int, string> symbols)
        {
            var count = swf[pos] | (swf[pos + 1] << 8);
            var p = pos + 2;
            var end = pos + len;
            for (var i = 0; i < count && p + 3 <= end; i++)
            {
                var id = swf[p] | (swf[p + 1] << 8);
                p += 2;
                var start = p;
                while (p < end && swf[p] != 0) p++;
                if (p >= end) return;
                symbols[id] = System.Text.Encoding.ASCII.GetString(swf, start, p - start);
                p++;
            }
        }

        private struct Bits
        {
            private readonly byte[] _data;
            private int _i;
            private int _n;
            private int _b;
            private readonly int _end;

            public Bits(byte[] data, int offset)
            {
                _data = data;
                _i = offset;
                _n = 0;
                _b = 0;
                _end = data.Length;
            }

            public int Ub(int n)
            {
                var v = 0;
                for (var k = 0; k < n; k++)
                {
                    if (_n == 0)
                    {
                        if (_i >= _end) return v;
                        _b = _data[_i++];
                        _n = 8;
                    }
                    _n--;
                    v = (v << 1) | ((_b >> _n) & 1);
                }
                return v;
            }

            public int Sb(int n)
            {
                var v = Ub(n);
                if (n > 0 && (v & (1 << (n - 1))) != 0)
                    v -= 1 << n;
                return v;
            }

            public void Align() => _n = 0;

            public int U8()
            {
                Align();
                if (_i >= _end) return 0;
                return _data[_i++];
            }

            public int U16()
            {
                Align();
                if (_i + 1 >= _end) return 0;
                var v = _data[_i] | (_data[_i + 1] << 8);
                _i += 2;
                return v;
            }

            public bool ReadRect(out int x0, out int x1, out int y0, out int y1)
            {
                x0 = x1 = y0 = y1 = 0;
                if (_i >= _end) return false;
                var nbits = Ub(5);
                x0 = Sb(nbits);
                x1 = Sb(nbits);
                y0 = Sb(nbits);
                y1 = Sb(nbits);
                Align();
                return true;
            }

            public bool ReadMatrix(out Mx mx)
            {
                mx = Mx.Identity;
                if (_i >= _end && _n == 0) return false;
                double sx = 1, sy = 1;
                if (Ub(1) == 1)
                {
                    var nb = Ub(5);
                    sx = Sb(nb) / 65536.0;
                    sy = Sb(nb) / 65536.0;
                }
                if (Ub(1) == 1)
                {
                    var nb = Ub(5);
                    Sb(nb);
                    Sb(nb);
                }
                var nt = Ub(5);
                var tx = Sb(nt);
                var ty = Sb(nt);
                Align();
                mx = new Mx(sx, sy, tx, ty);
                return true;
            }

            public bool SkipGradient(bool rgba, bool focal)
            {
                Ub(2);
                Ub(2);
                var n = Ub(4);
                Align();
                var color = rgba ? 4 : 3;
                if (_i + n * (1 + color) > _end) return false;
                _i += n * (1 + color);
                if (focal)
                {
                    if (_i + 2 > _end) return false;
                    _i += 2;
                }
                return true;
            }
        }

        private static int RectByteLength(byte[] swf, int offset)
        {
            if (offset >= swf.Length) return 1;
            var nbits = swf[offset] >> 3;
            var bits = 5 + nbits * 4;
            return (bits + 7) / 8;
        }
    }
}
