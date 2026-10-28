using System.IO;
using System.IO.Compression;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class StageBackdropTests
    {
        [Fact]
        public void DecodeLargest_ReadsOpaqueRgbFromLossless()
        {
            var swf = BuildLosslessSwf(2, 2, new byte[]
            {
                255, 10, 20, 30,
                255, 1, 2, 3,
                255, 4, 5, 6,
                255, 7, 8, 9
            });
            var bmp = StageBackdrop.DecodeLargest(swf);
            bmp.Should().NotBeNull();
            bmp!.Value.Width.Should().Be(2);
            bmp.Value.Height.Should().Be(2);
            bmp.Value.Bgra[0].Should().Be(30);
            bmp.Value.Bgra[1].Should().Be(20);
            bmp.Value.Bgra[2].Should().Be(10);
            bmp.Value.Bgra[3].Should().Be(255);
        }

        [Fact]
        public void DecodeLargest_PicksTheBiggerBitmap()
        {
            var small = LosslessBody(1, 1, 1, new byte[] { 255, 1, 2, 3 });
            var big = LosslessBody(2, 2, 2, new byte[]
            {
                255, 9, 8, 7, 255, 9, 8, 7, 255, 9, 8, 7, 255, 9, 8, 7
            });
            var swf = WrapTags(small, big);
            var bmp = StageBackdrop.DecodeLargest(swf);
            bmp!.Value.Width.Should().Be(2);
            bmp.Value.Height.Should().Be(2);
        }

        [Fact]
        public void ComposeBackground_StacksBgLayersAndSkipsTheLargestBitmap()
        {
            var red = LosslessBody(1, 8, 8, Solid(8, 8, 255, 255, 0, 0));
            var blue = LosslessBody(2, 2, 2, Solid(2, 2, 255, 0, 0, 255));
            var green = LosslessBody(3, 2, 2, Solid(2, 2, 255, 0, 255, 0));
            var sprite = SpriteBody(10, new[]
            {
                Place(depth: 1, character: 2, matrix: null),
                Place(depth: 2, character: 3, matrix: TranslateX(20))
            });
            var swf = WrapRaw(Tag(20, red), Tag(20, blue), Tag(20, green), Tag(39, sprite), Tag(76, Symbol("demo_bg", 10)));
            var bmp = StageBackdrop.ComposeBackground(swf, "demo");
            bmp.Should().NotBeNull();
            bmp!.Value.Width.Should().Be(3);
            bmp.Value.Height.Should().Be(2);
            Pixel(bmp.Value, 0, 0).Should().Be((255, 0, 0, 255));
            Pixel(bmp.Value, 1, 0).Should().Be((0, 255, 0, 255));
        }

        [Fact]
        public void ComposeBackground_UsesTheBitmapFillInsideTheBgShape()
        {
            var red = LosslessBody(1, 8, 8, Solid(8, 8, 255, 255, 0, 0));
            var blue = LosslessBody(2, 2, 2, Solid(2, 2, 255, 0, 0, 255));
            var shape = ShapeBody(4, bitmapId: 2);
            var sprite = SpriteBody(10, new[] { Place(depth: 1, character: 4, matrix: null) });
            var swf = WrapRaw(Tag(20, red), Tag(20, blue), Tag(2, shape), Tag(39, sprite), Tag(76, Symbol("demo_bg", 10)));
            var bmp = StageBackdrop.ComposeBackground(swf, "demo");
            bmp.Should().NotBeNull();
            bmp!.Value.Width.Should().Be(2);
            bmp.Value.Height.Should().Be(2);
            Pixel(bmp.Value, 0, 0).Should().Be((255, 0, 0, 255));
        }

        [Fact]
        public void ComposeBackground_ReadsAColormapLayer()
        {
            var indexed = ColormapBody(2, 2, 2, new byte[] { 0, 0, 255 }, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
            var sprite = SpriteBody(10, new[] { Place(depth: 1, character: 2, matrix: null) });
            var swf = WrapRaw(Tag(20, indexed), Tag(39, sprite), Tag(76, Symbol("demo_bg", 10)));
            var bmp = StageBackdrop.ComposeBackground(swf, "demo");
            bmp.Should().NotBeNull();
            Pixel(bmp!.Value, 0, 0).Should().Be((255, 0, 0, 255));
        }

        [Fact]
        public void ComposeBackground_PaintsASolidFillInsteadOfTheLargestBitmap()
        {
            var red = LosslessBody(1, 8, 8, Solid(8, 8, 255, 255, 0, 0));
            var shape = SolidShape(4, 0, 0, 255);
            var sprite = SpriteBody(10, new[] { Place(depth: 1, character: 4, matrix: null) });
            var swf = WrapRaw(Tag(20, red), Tag(2, shape), Tag(39, sprite), Tag(76, Symbol("demo_bg", 10)));
            var bmp = StageBackdrop.ComposeBackground(swf, "demo");
            bmp.Should().NotBeNull();
            bmp!.Value.Width.Should().Be(2);
            bmp.Value.Height.Should().Be(2);
            Pixel(bmp.Value, 0, 0).Should().Be((255, 0, 0, 255));
        }

        [Fact]
        public void ComposeBackground_DrawsANestedSprite()
        {
            var red = LosslessBody(1, 8, 8, Solid(8, 8, 255, 255, 0, 0));
            var blue = LosslessBody(2, 2, 2, Solid(2, 2, 255, 0, 0, 255));
            var child = SpriteBody(9, new[] { Place(depth: 1, character: 2, matrix: null) });
            var parent = SpriteBody(10, new[] { Place(depth: 1, character: 9, matrix: null) });
            var swf = WrapRaw(Tag(20, red), Tag(20, blue), Tag(39, child), Tag(39, parent), Tag(76, Symbol("demo_bg", 10)));
            var bmp = StageBackdrop.ComposeBackground(swf, "demo");
            bmp.Should().NotBeNull();
            bmp!.Value.Width.Should().Be(2);
            Pixel(bmp.Value, 0, 0).Should().Be((255, 0, 0, 255));
        }

        private static (byte B, byte G, byte R, byte A) Pixel(StageBitmap bmp, int x, int y)
        {
            var i = (y * bmp.Width + x) * 4;
            return (bmp.Bgra[i], bmp.Bgra[i + 1], bmp.Bgra[i + 2], bmp.Bgra[i + 3]);
        }

        private static byte[] Solid(int w, int h, byte a, byte r, byte g, byte b)
        {
            var pix = new byte[w * h * 4];
            for (var i = 0; i < w * h; i++)
            {
                pix[i * 4] = a;
                pix[i * 4 + 1] = r;
                pix[i * 4 + 2] = g;
                pix[i * 4 + 3] = b;
            }
            return pix;
        }

        private static byte[] Place(int depth, int character, byte[]? matrix)
        {
            var flags = matrix == null ? 0x02 : 0x06;
            var body = new byte[3 + 2 + (matrix?.Length ?? 0)];
            body[0] = (byte)flags;
            body[1] = (byte)depth;
            body[2] = (byte)(depth >> 8);
            body[3] = (byte)character;
            body[4] = (byte)(character >> 8);
            if (matrix != null)
                Buffer.BlockCopy(matrix, 0, body, 5, matrix.Length);
            return Tag(26, body);
        }

        private static byte[] TranslateX(int twips)
        {
            var bw = new BitWriter();
            bw.Ub(0, 1);
            bw.Ub(0, 1);
            bw.Ub(6, 5);
            bw.Sb(twips, 6);
            bw.Sb(0, 6);
            return bw.ToArray();
        }

        private static byte[] BitmapFillMatrix()
        {
            const int scale = 20 * 65536;
            var bw = new BitWriter();
            bw.Ub(1, 1);
            bw.Ub(22, 5);
            bw.Sb(scale, 22);
            bw.Sb(scale, 22);
            bw.Ub(0, 1);
            bw.Ub(0, 5);
            return bw.ToArray();
        }

        private static byte[] SolidShape(int id, byte r, byte g, byte b)
        {
            var rect = new BitWriter();
            rect.Ub(7, 5);
            rect.Sb(0, 7);
            rect.Sb(40, 7);
            rect.Sb(0, 7);
            rect.Sb(40, 7);
            var bounds = rect.ToArray();
            var body = new byte[2 + bounds.Length + 5];
            body[0] = (byte)id;
            body[1] = (byte)(id >> 8);
            Buffer.BlockCopy(bounds, 0, body, 2, bounds.Length);
            var p = 2 + bounds.Length;
            body[p] = 1;
            body[p + 1] = 0;
            body[p + 2] = r;
            body[p + 3] = g;
            body[p + 4] = b;
            return body;
        }

        private static byte[] ShapeBody(int id, int bitmapId)
        {
            var matrix = BitmapFillMatrix();
            var body = new byte[2 + 1 + 1 + 1 + 2 + matrix.Length];
            body[0] = (byte)id;
            body[1] = (byte)(id >> 8);
            body[2] = 0;
            body[3] = 1;
            body[4] = 0x41;
            body[5] = (byte)bitmapId;
            body[6] = (byte)(bitmapId >> 8);
            Buffer.BlockCopy(matrix, 0, body, 7, matrix.Length);
            return body;
        }

        private static byte[] SpriteBody(int id, byte[][] places)
        {
            var size = 4;
            foreach (var place in places)
                size += place.Length;
            size += 2;
            var body = new byte[size];
            body[0] = (byte)id;
            body[1] = (byte)(id >> 8);
            body[2] = 1;
            var p = 4;
            foreach (var place in places)
            {
                Buffer.BlockCopy(place, 0, body, p, place.Length);
                p += place.Length;
            }
            return body;
        }

        private static byte[] Symbol(string name, int id)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(name);
            var body = new byte[2 + 2 + bytes.Length + 1];
            body[0] = 1;
            body[2] = (byte)id;
            body[3] = (byte)(id >> 8);
            Buffer.BlockCopy(bytes, 0, body, 4, bytes.Length);
            return body;
        }

        private static byte[] ColormapBody(int id, int w, int h, byte[] rgb, byte[] indices)
        {
            var raw = new byte[rgb.Length + indices.Length];
            Buffer.BlockCopy(rgb, 0, raw, 0, rgb.Length);
            Buffer.BlockCopy(indices, 0, raw, rgb.Length, indices.Length);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(raw, 0, raw.Length);
            var z = compressed.ToArray();
            var body = new byte[8 + z.Length];
            body[0] = (byte)id;
            body[1] = (byte)(id >> 8);
            body[2] = 3;
            body[3] = (byte)w;
            body[4] = (byte)(w >> 8);
            body[5] = (byte)h;
            body[6] = (byte)(h >> 8);
            body[7] = 0;
            Buffer.BlockCopy(z, 0, body, 8, z.Length);
            return body;
        }

        private static byte[] Tag(int code, byte[] body)
        {
            using var ms = new MemoryStream();
            if (body.Length < 0x3F)
            {
                var codeLen = (code << 6) | body.Length;
                ms.WriteByte((byte)codeLen);
                ms.WriteByte((byte)(codeLen >> 8));
            }
            else
            {
                var codeLen = (code << 6) | 0x3F;
                ms.WriteByte((byte)codeLen);
                ms.WriteByte((byte)(codeLen >> 8));
                ms.Write(BitConverter.GetBytes(body.Length), 0, 4);
            }
            ms.Write(body, 0, body.Length);
            return ms.ToArray();
        }

        private sealed class BitWriter
        {
            private readonly MemoryStream _ms = new();
            private int _acc;
            private int _n;

            public void Ub(int value, int bits)
            {
                for (var i = bits - 1; i >= 0; i--)
                {
                    _acc = (_acc << 1) | ((value >> i) & 1);
                    _n++;
                    if (_n == 8)
                    {
                        _ms.WriteByte((byte)_acc);
                        _acc = 0;
                        _n = 0;
                    }
                }
            }

            public void Sb(int value, int bits) => Ub(value, bits);

            public byte[] ToArray()
            {
                if (_n > 0)
                {
                    _acc <<= 8 - _n;
                    _ms.WriteByte((byte)_acc);
                    _acc = 0;
                    _n = 0;
                }
                return _ms.ToArray();
            }
        }

        private static byte[] BuildLosslessSwf(int w, int h, byte[] pix) =>
            WrapTags(LosslessBody(1, w, h, pix));

        private static byte[] LosslessBody(int id, int w, int h, byte[] pix)
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(pix, 0, pix.Length);
            var z = compressed.ToArray();
            var body = new byte[7 + z.Length];
            body[0] = (byte)id;
            body[1] = (byte)(id >> 8);
            body[2] = 5;
            body[3] = (byte)w;
            body[4] = (byte)(w >> 8);
            body[5] = (byte)h;
            body[6] = (byte)(h >> 8);
            Buffer.BlockCopy(z, 0, body, 7, z.Length);
            return body;
        }

        private static byte[] WrapRaw(params byte[][] tags)
        {
            using var ms = new MemoryStream();
            ms.Write(new byte[] { (byte)'F', (byte)'W', (byte)'S', 10, 0, 0, 0, 0 });
            ms.Write(new byte[] { 0x08, 0x00 });
            ms.Write(new byte[] { 0x00, 0x18, 0x01, 0x00 });
            foreach (var tag in tags)
                ms.Write(tag, 0, tag.Length);
            ms.Write(new byte[] { 0, 0 });
            var swf = ms.ToArray();
            var len = swf.Length;
            swf[4] = (byte)len;
            swf[5] = (byte)(len >> 8);
            swf[6] = (byte)(len >> 16);
            swf[7] = (byte)(len >> 24);
            return swf;
        }

        private static byte[] WrapTags(params byte[][] bodies)
        {
            using var ms = new MemoryStream();
            ms.Write(new byte[] { (byte)'F', (byte)'W', (byte)'S', 10, 0, 0, 0, 0 });
            // RECT nbits=1, all zeros. 9 bits → 2 bytes.
            ms.Write(new byte[] { 0x08, 0x00 });
            ms.Write(new byte[] { 0x00, 0x18, 0x01, 0x00 });
            foreach (var body in bodies)
            {
                var codeLen = (20 << 6) | body.Length;
                ms.WriteByte((byte)codeLen);
                ms.WriteByte((byte)(codeLen >> 8));
                ms.Write(body, 0, body.Length);
            }
            ms.Write(new byte[] { 0, 0 });
            var swf = ms.ToArray();
            var len = swf.Length;
            swf[4] = (byte)len;
            swf[5] = (byte)(len >> 8);
            swf[6] = (byte)(len >> 16);
            swf[7] = (byte)(len >> 24);
            return swf;
        }
    }
}
