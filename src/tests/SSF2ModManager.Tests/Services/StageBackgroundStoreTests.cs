using System.IO;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class StageBackgroundStoreTests
    {
        [Fact]
        public void Create_PngWritesAssetPreviewAndNoSwf()
        {
            var root = TempRoot();
            try
            {
                var png = Path.Combine(root, "src.png");
                File.WriteAllBytes(png, Png1x1());
                var folder = StageBackgroundStore.Create(root, "Red Sky", png, "battlefield");
                var info = JObject.Parse(File.ReadAllText(Path.Combine(folder, "info.json")));
                info["type"]!.ToString().Should().Be("StageBackground");
                info["defaultStage"]!.ToString().Should().Be("battlefield");
                info["asset"]!.ToString().Should().Be("background.png");
                File.Exists(Path.Combine(folder, "background.png")).Should().BeTrue();
                File.ReadAllBytes(Path.Combine(folder, "stagepreview.png")).Take(8)
                    .Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                Directory.GetFiles(folder, "*.swf").Should().BeEmpty();
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Create_GifUsesFirstFrameAndNoSwf()
        {
            var root = TempRoot();
            try
            {
                var gif = Path.Combine(root, "src.gif");
                File.WriteAllBytes(gif, Gif1x1Red());
                var folder = StageBackgroundStore.Create(root, "Gif Sky", gif, "finaldestination", "gifsky");
                var info = JObject.Parse(File.ReadAllText(Path.Combine(folder, "info.json")));
                info["id"]!.ToString().Should().Be("gifsky");
                info["defaultStage"]!.ToString().Should().Be("finaldestination");
                info["asset"]!.ToString().Should().Be("background.gif");
                File.Exists(Path.Combine(folder, "background.gif")).Should().BeTrue();
                var preview = File.ReadAllBytes(Path.Combine(folder, "stagepreview.png"));
                preview.Take(8).Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                preview.Length.Should().BeGreaterThan(8);
                Directory.GetFiles(folder, "*.swf").Should().BeEmpty();
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static string TempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-stagebg-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDelete(string root)
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        private static byte[] Png1x1()
        {
            return Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        }

        /// <summary>1×1 red GIF89a.</summary>
        private static byte[] Gif1x1Red() => new byte[]
        {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x21, 0xF9, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B
        };
    }
}
