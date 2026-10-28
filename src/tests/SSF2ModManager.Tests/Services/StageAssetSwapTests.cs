using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class StageAssetSwapTests
    {
        [Fact]
        public void Create_WritesBothPngsAndNoSwf()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-asset-swap-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var swf = Swf(Red(), Green());
                var folder = StageAssetSwapStore.Create(root, "Battlefield Clone", "battlefield", swf);
                var info = JObject.Parse(File.ReadAllText(Path.Combine(folder, "info.json")));
                info["type"]!.ToString().Should().Be("StageAssetSwap");
                info["sourceStage"]!.ToString().Should().Be("battlefield");
                var assets = JObject.Parse(File.ReadAllText(Path.Combine(folder, "assets.json")));
                var images = (JArray)assets["images"]!;
                images.Should().HaveCount(2);
                images[0]!["characterId"]!.Value<int>().Should().Be(1);
                images[1]!["characterId"]!.Value<int>().Should().Be(2);
                File.Exists(Path.Combine(folder, "assets", "1_bitmap.png")).Should().BeTrue();
                File.Exists(Path.Combine(folder, "assets", "2_bitmap.png")).Should().BeTrue();
                File.Exists(Path.Combine(folder, "stagepreview.png")).Should().BeTrue();
                Directory.GetFiles(folder, "*.swf", SearchOption.AllDirectories).Should().BeEmpty();
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        [Fact]
        public void Swap_ReplacesTheMatchingPixel()
        {
            var swf = Swf(Red(), Green());
            var blue = new StageImageTag(1, 1, 1, new byte[] { 255, 0, 0, 255 });
            var patched = StageAssetSwap.Swap(swf, new Dictionary<int, StageImageTag> { [1] = blue });
            var first = StageAssetSwap.Find(patched, 1);
            var second = StageAssetSwap.Find(patched, 2);
            first.Should().NotBeNull();
            first!.Bgra.Should().NotBeNull();
            first.Bgra![0].Should().Be(255);
            first.Bgra[1].Should().Be(0);
            first.Bgra[2].Should().Be(0);
            second!.Bgra![1].Should().Be(255);
        }

        private static byte[] Red() => new byte[] { 0, 0, 255, 255 };

        private static byte[] Green() => new byte[] { 0, 255, 0, 255 };

        private static byte[] Swf(byte[] first, byte[] second)
        {
            using var output = new MemoryStream();
            output.Write(new byte[]
            {
                (byte)'F', (byte)'W', (byte)'S', 10,
                0, 0, 0, 0,
                0,
                0, 0x18,
                1, 0
            });
            WriteLossless(output, 1, first);
            WriteLossless(output, 2, second);
            output.Write(new byte[] { 0, 0 });
            var bytes = output.ToArray();
            var length = bytes.Length;
            bytes[4] = (byte)length;
            bytes[5] = (byte)(length >> 8);
            bytes[6] = (byte)(length >> 16);
            bytes[7] = (byte)(length >> 24);
            return bytes;
        }

        private static void WriteLossless(Stream output, int id, byte[] bgra)
        {
            var argb = new byte[] { bgra[3], bgra[2], bgra[1], bgra[0] };
            using var raw = new MemoryStream();
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(argb, 0, argb.Length);
            var compressed = raw.ToArray();
            var body = new byte[7 + compressed.Length];
            body[0] = (byte)id;
            body[1] = 0;
            body[2] = 5;
            body[3] = 1;
            body[4] = 0;
            body[5] = 1;
            body[6] = 0;
            compressed.CopyTo(body, 7);
            var len = body.Length;
            if (len < 63)
            {
                var codeLen = (36 << 6) | len;
                output.WriteByte((byte)codeLen);
                output.WriteByte((byte)(codeLen >> 8));
            }
            else
            {
                var codeLen = (36 << 6) | 0x3F;
                output.WriteByte((byte)codeLen);
                output.WriteByte((byte)(codeLen >> 8));
                output.WriteByte((byte)len);
                output.WriteByte((byte)(len >> 8));
                output.WriteByte((byte)(len >> 16));
                output.WriteByte((byte)(len >> 24));
            }
            output.Write(body, 0, body.Length);
        }
    }
}
