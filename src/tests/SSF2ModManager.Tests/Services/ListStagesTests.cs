using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ListStagesTests
    {
        [Fact]
        public void ListStages_IncludesSwfAndSsfIds()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-liststages-" + Path.GetRandomFileName());
            var stage = Path.Combine(root, "data", "stage");
            Directory.CreateDirectory(stage);
            try
            {
                File.WriteAllText(Path.Combine(stage, "battlefield.swf"), "x");
                File.WriteAllText(Path.Combine(stage, "finaldestination.ssf"), "x");
                File.WriteAllText(Path.Combine(stage, "readme.txt"), "x");

                var ids = ShellConfigureLaunchService.ListStages(root);

                ids.Should().BeEquivalentTo(new[] { "battlefield", "finaldestination" });
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ListCharacters_IncludesSwfAndSsfIds()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-listchars-" + Path.GetRandomFileName());
            var character = Path.Combine(root, "data", "character");
            Directory.CreateDirectory(character);
            try
            {
                File.WriteAllText(Path.Combine(character, "mario.swf"), "x");
                File.WriteAllText(Path.Combine(character, "sonic.ssf"), "x");
                File.WriteAllText(Path.Combine(character, "readme.txt"), "x");

                var ids = ShellConfigureLaunchService.ListCharacters(root);

                ids.Should().BeEquivalentTo(new[] { "mario", "sonic" });
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ListMusic_IncludesSwfAndSsfIds()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-listmusic-" + Path.GetRandomFileName());
            var sound = Path.Combine(root, "data", "sound");
            Directory.CreateDirectory(sound);
            try
            {
                File.WriteAllText(Path.Combine(sound, "bgm_battlefield.swf"), "x");
                File.WriteAllText(Path.Combine(sound, "bgm_finaldestination.ssf"), "x");
                File.WriteAllText(Path.Combine(sound, "sfx_hit.ssf"), "x");

                var ids = ShellConfigureLaunchService.ListMusic(root);

                ids.Should().BeEquivalentTo(new[] { "bgm_battlefield", "bgm_finaldestination" });
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ResolveModeFile_FallsBackToSsf()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-modefile-" + Path.GetRandomFileName());
            var modes = Path.Combine(root, "data", "modes");
            Directory.CreateDirectory(modes);
            try
            {
                File.WriteAllText(Path.Combine(modes, "event_mode.ssf"), "x");

                var path = ShellConfigureLaunchService.ResolveModeFile(root, "event_mode.swf");

                path.Should().Be(Path.Combine(modes, "event_mode.ssf"));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void LiveShellBuild_ListsStockSsfAssetsWhenPresent()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "SSF2 Shell Base", "Build"));
            if (!Directory.Exists(Path.Combine(root, "data", "character")))
                return;

            ShellConfigureLaunchService.ListCharacters(root).Should().NotBeEmpty();
            ShellConfigureLaunchService.ListMusic(root).Should().NotBeEmpty();
            ShellConfigureLaunchService.ResolveModeFile(root, "event_mode.swf").Should().NotBeNullOrEmpty();
            ShellConfigureLaunchService.ListEvents(root).Should().NotBeEmpty();
        }

        [Fact]
        public void ListEvents_ReadsEventModeSsf()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-listevents-" + Path.GetRandomFileName());
            var modes = Path.Combine(root, "data", "modes");
            Directory.CreateDirectory(modes);
            try
            {
                // printable ASCII length-prefixed: ComboMaker then "13. Combo Maker"
                var bytes = new System.Collections.Generic.List<byte>();
                void Add(string s)
                {
                    bytes.Add((byte)s.Length);
                    bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(s));
                }
                Add("chooseCharacter");
                Add("ComboMaker");
                Add("13. Combo Maker");
                File.WriteAllBytes(Path.Combine(modes, "event_mode.ssf"), bytes.ToArray());

                var list = ShellConfigureLaunchService.ListEvents(root);

                list.Should().Contain(e => e.Id == "ComboMaker" && e.Label == "13. Combo Maker");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }
    }
}
