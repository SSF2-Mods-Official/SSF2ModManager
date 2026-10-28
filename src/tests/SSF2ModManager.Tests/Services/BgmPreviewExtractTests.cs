using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class BgmPreviewExtractTests
    {
        /// <summary>Minimal FWS with one DefineSound(MP3) tag holding fake MPEG sync frames.</summary>
        public static byte[] BuildTinyMusicSwf(byte[] mp3Payload)
        {
            // Rect: nbits=1 → 1 byte of bits after nbits nibble packing; keep simple 0 rect.
            // Frame: rate 0, count 1, then tags.
            using var ms = new MemoryStream();
            // Placeholder header — length rewritten at end.
            ms.Write(new byte[] { (byte)'F', (byte)'W', (byte)'S', 9, 0, 0, 0, 0 });
            // RECT with nBits=15 → (5+60+7)/8 = 9 bytes (common empty-ish)
            ms.WriteByte(0x78); // nbits=15
            ms.Write(new byte[8]); // padded rect zeros
            ms.WriteByte(0); // frameRate low
            ms.WriteByte(12); // frameRate high (~12 fps)
            ms.WriteByte(1);
            ms.WriteByte(0); // frameCount = 1

            // DefineSound tag 14, long length form
            var soundBody = new byte[7 + mp3Payload.Length];
            // soundId u16 = 1
            soundBody[0] = 1;
            soundBody[1] = 0;
            // format=2 (MP3) in high nibble of byte 2; rate/size/type zeros
            soundBody[2] = 0x20;
            // sampleCount u32 = 1
            soundBody[3] = 1;
            soundBody[4] = 0;
            soundBody[5] = 0;
            soundBody[6] = 0;
            // Seek samples often precede MP3 in Flash; FindDefineSoundMp3 skips first 7 bytes after
            // the 2-byte soundId+flags… actually it uses length-9 from tag data start:
            // body[i+2] is format, mp3 at i+9. So we need 9 header bytes before mp3.
            // Rebuild: tag data = soundId(2) + flags(1) + sampleCount(4) + seekSamples(2) + mp3
            soundBody = new byte[9 + mp3Payload.Length];
            soundBody[0] = 1;
            soundBody[1] = 0;
            soundBody[2] = 0x20; // MP3
            soundBody[3] = 1;
            soundBody[4] = 0;
            soundBody[5] = 0;
            soundBody[6] = 0;
            soundBody[7] = 0; // seekSamples
            soundBody[8] = 0;
            Buffer.BlockCopy(mp3Payload, 0, soundBody, 9, mp3Payload.Length);

            var tagHeader = (ushort)((14 << 6) | 0x3f);
            ms.WriteByte((byte)(tagHeader & 0xff));
            ms.WriteByte((byte)(tagHeader >> 8));
            var len = soundBody.Length;
            ms.WriteByte((byte)len);
            ms.WriteByte((byte)(len >> 8));
            ms.WriteByte((byte)(len >> 16));
            ms.WriteByte((byte)(len >> 24));
            ms.Write(soundBody);

            // End tag
            ms.WriteByte(0);
            ms.WriteByte(0);

            var all = ms.ToArray();
            var fileLen = all.Length;
            all[4] = (byte)fileLen;
            all[5] = (byte)(fileLen >> 8);
            all[6] = (byte)(fileLen >> 16);
            all[7] = (byte)(fileLen >> 24);
            return all;
        }

        [Fact]
        public void ExtractMp3_PullsDefineSoundWithoutFfdec()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-bgm-ext-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                // Fake MP3: ID3 header + sync-ish payload long enough for the size check.
                var payload = new byte[128];
                payload[0] = (byte)'I';
                payload[1] = (byte)'D';
                payload[2] = (byte)'3';
                for (var i = 3; i < payload.Length; i++) payload[i] = (byte)(i & 0xff);

                var swf = Path.Combine(root, "bgm_test.swf");
                File.WriteAllBytes(swf, BuildTinyMusicSwf(payload));
                var outMp3 = Path.Combine(root, "out.mp3");

                BgmPreviewService.ExtractMp3(swf, outMp3);

                File.Exists(outMp3).Should().BeTrue();
                var got = File.ReadAllBytes(outMp3);
                got.Length.Should().BeGreaterThan(64);
                got[0].Should().Be((byte)'I');
                got[1].Should().Be((byte)'D');
                got[2].Should().Be((byte)'3');
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task EnsurePreviewMp3_UsesFastExtractOnPlainSwf()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-prev-fast-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            var prevOverride = MusicLibraryCacheService.CacheFilePathOverride;
            try
            {
                MusicLibraryCacheService.CacheFilePathOverride = Path.Combine(root, "cache.json");
                var payload = new byte[96];
                payload[0] = 0xff;
                payload[1] = 0xfb;
                for (var i = 2; i < payload.Length; i++) payload[i] = 0x11;

                var swf = Path.Combine(root, "bgm_fastprev.swf");
                File.WriteAllBytes(swf, BuildTinyMusicSwf(payload));
                var song = new CachedSongInfo
                {
                    Id = "bgm_fastprev_" + Guid.NewGuid().ToString("N")[..8],
                    Name = "Fast Prev",
                    SwfPath = swf
                };

                var path = await new MusicLibraryCacheService().EnsurePreviewMp3Async(song);

                path.Should().NotBeNullOrWhiteSpace();
                File.Exists(path!).Should().BeTrue();
                new FileInfo(path!).Length.Should().BeGreaterThan(64);
                song.PreviewMp3Path.Should().Be(path);
            }
            finally
            {
                MusicLibraryCacheService.CacheFilePathOverride = prevOverride;
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }
    }
}
