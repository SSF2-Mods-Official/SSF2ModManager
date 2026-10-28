using System.IO;
using System.Text;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class AltSoundEditTests
    {
        [Fact]
        public void ParseSoundId_FromPrefixedFileName()
        {
            Assert.Equal("10", AltSoundEditService.ParseSoundId("10_mario_star.flv"));
            Assert.Equal("-1", AltSoundEditService.ParseSoundId("-1.wav"));
            Assert.Null(AltSoundEditService.ParseSoundId("mario_star.flv"));
        }

        [Fact]
        public void ReplaceFileOnly_TracksAsteriskAndRestoreClears()
        {
            var alt = Path.Combine(Path.GetTempPath(), "ssf2mm-altsound-" + Path.GetRandomFileName());
            var sounds = Path.Combine(alt, "sounds");
            Directory.CreateDirectory(sounds);
            try
            {
                var name = "3_test.wav";
                var path = Path.Combine(sounds, name);
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes("ORIGWAV!!"));
                var replacement = Path.Combine(alt, "new.wav");
                File.WriteAllBytes(replacement, Encoding.ASCII.GetBytes("NEWWAV!!!"));

                AltSoundEditService.ReplaceFileOnly(alt, name, replacement);

                var listed = AltSoundEditService.ListSounds(alt);
                var item = Assert.Single(listed);
                Assert.True(item.Replaced);
                Assert.StartsWith("* ", item.DisplayLabel);
                Assert.Equal(1, AltSoundEditService.ReplacedCount(alt));
                Assert.Equal("NEWWAV!!!", Encoding.ASCII.GetString(File.ReadAllBytes(path)));
                Assert.True(File.Exists(Path.Combine(sounds, "_originals", name)));
                Assert.True(File.Exists(AltSoundEditService.ReplacementsPath(alt)));

                AltSoundEditService.RestoreFileOnly(alt, name);
                listed = AltSoundEditService.ListSounds(alt);
                item = Assert.Single(listed);
                Assert.False(item.Replaced);
                Assert.Equal(0, AltSoundEditService.ReplacedCount(alt));
                Assert.Equal("ORIGWAV!!", Encoding.ASCII.GetString(File.ReadAllBytes(path)));
            }
            finally
            {
                try { Directory.Delete(alt, true); } catch { }
            }
        }

        [Fact]
        public void ListSounds_SkipsOriginalsAndJson()
        {
            var alt = Path.Combine(Path.GetTempPath(), "ssf2mm-altsound2-" + Path.GetRandomFileName());
            var sounds = Path.Combine(alt, "sounds");
            var originals = Path.Combine(sounds, "_originals");
            Directory.CreateDirectory(originals);
            try
            {
                File.WriteAllText(Path.Combine(sounds, "1_a.wav"), "a");
                File.WriteAllText(Path.Combine(sounds, "replacements.json"), "{}");
                File.WriteAllText(Path.Combine(sounds, "linkages.json"), "{}");
                File.WriteAllText(Path.Combine(originals, "1_a.wav"), "old");
                var list = AltSoundEditService.ListSounds(alt);
                Assert.Single(list);
                Assert.Equal("1_a.wav", list[0].FileName);
            }
            finally
            {
                try { Directory.Delete(alt, true); } catch { }
            }
        }

        [Fact]
        public void WriteLinkagesJson_MapsPrefixedNames()
        {
            var sounds = Path.Combine(Path.GetTempPath(), "ssf2mm-link-" + Path.GetRandomFileName());
            Directory.CreateDirectory(sounds);
            try
            {
                File.WriteAllText(Path.Combine(sounds, "10_BM_Laugh.mp3"), "x");
                File.WriteAllText(Path.Combine(sounds, "11_iceshoot.wav"), "y");
                var n = AltSoundEditService.WriteLinkagesJson(sounds);
                Assert.Equal(2, n);
                var json = File.ReadAllText(Path.Combine(sounds, "linkages.json"));
                Assert.Contains("BM_Laugh", json);
                Assert.Contains("iceshoot", json);
            }
            finally
            {
                try { Directory.Delete(sounds, true); } catch { }
            }
        }

        [Fact]
        public void Peak_AndWriteWav_RoundTrip()
        {
            var samples = new short[] { 0, 1000, -2000, 500 };
            Assert.Equal(2000, AltSoundEditService.Peak(samples));
            var path = Path.Combine(Path.GetTempPath(), "ssf2mm-peak-" + Path.GetRandomFileName() + ".wav");
            try
            {
                AltSoundEditService.WritePcm16Wav(path, samples, 1, 22050);
                Assert.True(AltSoundEditService.TryReadPcm16(path, out var ch, out var rate, out var round));
                Assert.Equal(1, ch);
                Assert.Equal(22050, rate);
                Assert.Equal(samples, round);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
