using System.IO;
using System.Text;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class MusicLoopMetaTests
    {
        private static void WriteU8String(Stream s, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            s.WriteByte((byte)bytes.Length);
            s.Write(bytes);
        }

        [Fact]
        public void TryReadMusicMeta_ReadsMusicLoopMs()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-loopmeta-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "bgm_loop.swf");
                using (var ms = new MemoryStream())
                {
                    ms.Write(new byte[] { (byte)'F', (byte)'W', (byte)'S', 9, 0, 0, 0, 0 });
                    // Pad so we have a body, then plant length-prefixed strings.
                    ms.Write(new byte[64]);
                    WriteU8String(ms, "songName");
                    WriteU8String(ms, "Test Track");
                    WriteU8String(ms, "artist");
                    WriteU8String(ms, "Tester");
                    WriteU8String(ms, "musicLoop");
                    WriteU8String(ms, "12345");
                    var all = ms.ToArray();
                    all[4] = (byte)all.Length;
                    all[5] = (byte)(all.Length >> 8);
                    all[6] = (byte)(all.Length >> 16);
                    all[7] = (byte)(all.Length >> 24);
                    File.WriteAllBytes(path, all);
                }

                SwfQuickScan.TryReadMusicMeta(path, out var name, out var artist, out var loop)
                    .Should().BeTrue();
                name.Should().Be("Test Track");
                artist.Should().Be("Tester");
                loop.Should().Be(12345);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }
    }
}
