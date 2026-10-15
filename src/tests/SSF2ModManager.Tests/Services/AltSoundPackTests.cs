using System.IO;
using System.Text;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class AltSoundPackTests
    {
        [Fact]
        public void StripDisplayTags_DropsBitmapKeepsAbc()
        {
            var swf = BuildToySwf();
            var outBytes = AltSoundPackService.StripDisplayTags(swf);
            Assert.NotNull(outBytes);
            Assert.True(outBytes!.Length < swf.Length, $"expected shrink {swf.Length} -> {outBytes.Length}");
            Assert.True(IndexOf(outBytes, "register"u8) >= 0);
            Assert.True(IndexOf(outBytes, "FAKEJPEG"u8) < 0);
        }

        [Fact]
        public void Install_ScopeCostume_WritesJson()
        {
            var shell = Path.Combine(Path.GetTempPath(), "ssf2mm-altscope-" + Path.GetRandomFileName());
            var src = Path.Combine(shell, "_src");
            Directory.CreateDirectory(src);
            try
            {
                var a = Path.Combine(src, "DAT7.swf");
                File.WriteAllBytes(a, BuildToySwf());
                CharacterAltsService.Install(shell, 0, "wario", "Red Wario", new[] { ("wario", a) }, "Red", costume: 2);
                var entry = Assert.Single(CharacterAltsService.List(shell));
                Assert.Equal("costume", entry.Scope);
                Assert.Equal(2, entry.Costume);
                var json = File.ReadAllText(CharacterAltsService.IndexPath(shell));
                Assert.Contains("\"scope\": \"costume\"", json);
                Assert.Contains("\"costume\": 2", json);
            }
            finally
            {
                Directory.Delete(shell, true);
            }
        }

        [Fact]
        public void Load_AlwaysIncludesCharactersSection()
        {
            var shell = Path.Combine(Path.GetTempPath(), "ssf2mm-chars-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(shell, "expansions"));
            try
            {
                var sections = ExpansionModsService.Load(shell);
                Assert.Contains(sections, s => s.Section == "Characters");
            }
            finally
            {
                Directory.Delete(shell, true);
            }
        }

        /// <summary>Minimal FWS: empty rect, DoABC with "register", and a fat DefineBitsJPEG2 to strip.</summary>
        private static byte[] BuildToySwf()
        {
            using var body = new MemoryStream();
            // RECT: nBits=1 → 5+4 bits = 9 → 2 bytes
            body.WriteByte(0x08); // nBits=1 in top 5 bits of first byte roughly; use simple 0x00 empty-ish
            body.WriteByte(0x00);
            // frame rate 12.0, frame count 1
            body.WriteByte(0x00);
            body.WriteByte(0x0C);
            body.WriteByte(0x01);
            body.WriteByte(0x00);

            // FileAttributes tag 69, length 4, value 8 (AS3)
            WriteTag(body, 69, new byte[] { 0x08, 0x00, 0x00, 0x00 });

            // DoABC tag 82: flags(4) + name\0 + abc bytes containing "register"
            var abc = Encoding.ASCII.GetBytes("xxxxregisteryyyy");
            var doAbc = new byte[4 + 1 + abc.Length];
            doAbc[0] = 1; // flags
            // name empty string at offset 4
            Buffer.BlockCopy(abc, 0, doAbc, 5, abc.Length);
            WriteTag(body, 82, doAbc);

            // Fat image tag 21 DefineBitsJPEG2: id + "FAKEJPEG" + padding
            var jpeg = new byte[2 + 8 + 2000];
            jpeg[0] = 1;
            jpeg[1] = 0;
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("FAKEJPEG"), 0, jpeg, 2, 8);
            WriteTag(body, 21, jpeg);

            // End
            body.WriteByte(0);
            body.WriteByte(0);

            var payload = body.ToArray();
            var swf = new byte[8 + payload.Length];
            swf[0] = (byte)'F';
            swf[1] = (byte)'W';
            swf[2] = (byte)'S';
            swf[3] = 10;
            var len = swf.Length;
            swf[4] = (byte)len;
            swf[5] = (byte)(len >> 8);
            swf[6] = (byte)(len >> 16);
            swf[7] = (byte)(len >> 24);
            Buffer.BlockCopy(payload, 0, swf, 8, payload.Length);
            return swf;
        }

        private static int IndexOf(byte[] hay, ReadOnlySpan<byte> needle)
        {
            for (var i = 0; i <= hay.Length - needle.Length; i++)
            {
                var ok = true;
                for (var j = 0; j < needle.Length; j++)
                    if (hay[i + j] != needle[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        private static void WriteTag(MemoryStream ms, int type, byte[] data)
        {
            var length = data.Length;
            if (length < 0x3F)
            {
                var code = (type << 6) | length;
                ms.WriteByte((byte)code);
                ms.WriteByte((byte)(code >> 8));
            }
            else
            {
                var code = (type << 6) | 0x3F;
                ms.WriteByte((byte)code);
                ms.WriteByte((byte)(code >> 8));
                ms.WriteByte((byte)length);
                ms.WriteByte((byte)(length >> 8));
                ms.WriteByte((byte)(length >> 16));
                ms.WriteByte((byte)(length >> 24));
            }
            ms.Write(data, 0, data.Length);
        }
    }
}
