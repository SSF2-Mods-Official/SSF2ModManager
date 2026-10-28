using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ContentPackLocationTests
    {
        private const string ResourceSnippet =
            "         if(this.m_type == Resource.CHARACTER || this.m_type == Resource.CHARACTER_EXPANSION)\r\n" +
            "         {\r\n" +
            "            if(this.m_fileName != null && this.m_fileName.indexOf(\"ssf2mm/\") == 0)\r\n" +
            "            {\r\n" +
            "               return _loc1_ + \"data/\" + this.m_fileName;\r\n" +
            "            }\r\n" +
            "            if(this.m_fileName != null && this.m_fileName.indexOf(\"expansions/\") == 0)\r\n" +
            "            {\r\n" +
            "               return _loc1_ + \"expansions/character/\" + this.m_fileName.substring(\"expansions/\".length);\r\n" +
            "            }\r\n" +
            "            return _loc1_ + Resource.DIRECTORY_CHAR + this.m_fileName;\r\n" +
            "         }\r\n";

        [Fact]
        public void Packs_LiveUnderExpansions_RegistryStaysInSsf2mm()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-packloc-" + Guid.NewGuid().ToString("N"));
            ContentPackService.PackDir(root, "mario2")
                .Should().Be(Path.Combine(root, "expansions", "packs", "mario2"));
            ContentPackService.IndexPath(root)
                .Should().Be(Path.Combine(root, "data", "ssf2mm", "index.json"));
            ContentPackService.ContentFileRelativeFromData("mario2")
                .Should().Be("expansions/packs/mario2/content.swf");
        }

        [Fact]
        public void LegacyPacks_MoveToExpansions_AndFileFieldIsRewritten()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-packmig-" + Guid.NewGuid().ToString("N"));
            try
            {
                var legacy = Path.Combine(root, "data", "ssf2mm", "packs");
                Directory.CreateDirectory(Path.Combine(legacy, "mario2", "assets"));
                File.WriteAllText(Path.Combine(legacy, "mario2", "pack.json"),
                    "{\"PackId\":\"mario2\",\"Type\":0,\"ContentRelativePath\":\"ssf2mm/packs/mario2/content.swf\"}");
                Directory.CreateDirectory(Path.Combine(legacy, "bgm_song", "assets"));
                File.WriteAllText(Path.Combine(legacy, "bgm_song", "pack.json"),
                    "{\"PackId\":\"bgm_song\",\"Type\":2,\"ContentRelativePath\":\"bgm_song.swf\"}");

                ContentPackService.LoadIndex(root);

                Directory.Exists(legacy).Should().BeFalse();
                ContentPackService.LoadPack(root, "mario2")!.ContentRelativePath
                    .Should().Be("expansions/packs/mario2/content.swf");
                ContentPackService.LoadPack(root, "bgm_song")!.ContentRelativePath
                    .Should().Be("bgm_song.swf");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ResourcePatch_SendsExpansionPacksToGameFolder_BeforeExpansionsRule()
        {
            var patched = GamePrepareService.AddExpansionPacksBranch(ResourceSnippet);

            var packs = patched.IndexOf("indexOf(\"expansions/packs/\")", StringComparison.Ordinal);
            packs.Should().BeGreaterThan(0);
            packs.Should().BeLessThan(patched.IndexOf("indexOf(\"expansions/\")", StringComparison.Ordinal));
            patched.Should().Contain("return _loc1_ + this.m_fileName;");
            GamePrepareService.AddExpansionPacksBranch(patched).Should().Be(patched);
        }

        [Fact]
        public void ShippedEngineSource_HasExpansionPacksBranch()
        {
            var shell = ShellWorkspaceService.FindShellFolder();
            if (shell == null)
                return;
            var resource = Path.Combine(shell, "expansions", "decompiled", "engine2", "changed",
                "com", "mcleodgaming", "ssf2", "util", "Resource.as");
            if (!File.Exists(resource))
                return;
            var text = File.ReadAllText(resource);
            System.Text.RegularExpressions.Regex.Matches(text, "indexOf\\(\"expansions/packs/\"\\)")
                .Count.Should().Be(2, "character and stage branches");
            Directory.Exists(Path.Combine(shell, "data", "ssf2mm", "packs")).Should().BeFalse();
        }
    }
}
