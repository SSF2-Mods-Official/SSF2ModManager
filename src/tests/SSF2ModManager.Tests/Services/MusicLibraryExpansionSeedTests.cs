using System.IO;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class MusicLibraryExpansionSeedTests
    {
        [Fact]
        public void SeedFromMappings_IncludesMusicPackNotInMappings()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-music-exp-" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "data", "sound"));
                Directory.CreateDirectory(Path.Combine(root, "data", "misc"));
                File.WriteAllText(Path.Combine(root, "data", "sound", "bgm_expansionhit.swf"), "x");

                ContentPackService.EnsureRoot(root);
                var index = ContentPackService.LoadIndex(root);
                ContentPackService.RegisterMusicPack(index, "bgm_expansionhit");
                ContentPackService.SaveIndex(root, index);
                ContentPackService.SavePack(root, new ContentPackInfo
                {
                    PackId = "bgm_expansionhit",
                    Type = ContentPackType.Music,
                    Name = "Expansion Hit",
                    ContentRelativePath = "bgm_expansionhit.swf"
                });

                // Mappings only has stock — expansion is pack-only.
                var doc = JObject.Parse("""{"music":{"bgm_stock":{"file":"bgm_stock.swf","name":"Stock"}}}""");
                File.WriteAllText(Path.Combine(root, "data", "sound", "bgm_stock.swf"), "x");

                var cache = new MusicLibraryCacheService().SeedFromMappings(root, doc);
                cache.Songs.Select(s => s.Id).Should().Contain(new[] { "bgm_stock", "bgm_expansionhit" });
                cache.Songs.Single(s => s.Id == "bgm_expansionhit").Name.Should().Be("Expansion Hit");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }
    }
}
