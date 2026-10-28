using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class MusicLibraryFastSeedTests : IDisposable
    {
        private readonly string _root;
        private readonly string _cachePath;

        public MusicLibraryFastSeedTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "ssf2mm-music-fast-" + Path.GetRandomFileName());
            _cachePath = Path.Combine(_root, "music-library-cache.json");
            MusicLibraryCacheService.CacheFilePathOverride = _cachePath;
        }

        public void Dispose()
        {
            MusicLibraryCacheService.CacheFilePathOverride = null;
            try { Directory.Delete(_root, true); } catch { /* ignore */ }
        }

        [Fact]
        public void EnsureFastLibrary_UsesShellListJson_WithoutMappings()
        {
            Directory.CreateDirectory(Path.Combine(_root, "data", "sound"));
            File.WriteAllText(Path.Combine(_root, "data", "sound", "bgm_battlefield.swf"), "x");
            File.WriteAllText(Path.Combine(_root, "data", "sound", "bgm_menu.swf"), "x");

            ContentPackService.EnsureRoot(_root);
            var listDir = Path.Combine(ShellWorkspaceService.Ssf2mmDirectory(_root), "music_player");
            Directory.CreateDirectory(listDir);
            File.WriteAllText(Path.Combine(listDir, "list.json"), """
                {"tracks":{
                  "bgm_battlefield":{"title":"Battlefield","artist":"Jacob Tjolsen","loopMs":0},
                  "bgm_menu":{"title":"Menu 1","artist":"","loopMs":0},
                  "bgm_missing":{"title":"Gone","artist":"","loopMs":0}
                }}
                """);

            var svc = new MusicLibraryCacheService();
            var sw = Stopwatch.StartNew();
            var cache = svc.EnsureFastLibrary(_root);
            sw.Stop();

            cache.Songs.Select(s => s.Id).Should().BeEquivalentTo(new[] { "bgm_battlefield", "bgm_menu" });
            cache.Songs.Single(s => s.Id == "bgm_battlefield").Name.Should().Be("Battlefield");
            cache.EnrichmentComplete.Should().BeTrue();
            sw.ElapsedMilliseconds.Should().BeLessThan(2000);
            File.Exists(_cachePath).Should().BeTrue();

            var again = svc.EnsureFastLibrary(_root);
            again.Songs.Count.Should().Be(2);
        }

        [Fact]
        public void EnsureFastLibrary_RefreshesRawNamesFromStock()
        {
            Directory.CreateDirectory(Path.Combine(_root, "data", "sound"));
            File.WriteAllText(Path.Combine(_root, "data", "sound", "bgm_alcatraz.ssf"), "x");
            File.WriteAllText(Path.Combine(_root, "data", "sound", "bgm_alcatraz.swf"), "x");

            ContentPackService.EnsureRoot(_root);
            var listDir = Path.Combine(ShellWorkspaceService.Ssf2mmDirectory(_root), "music_player");
            Directory.CreateDirectory(listDir);
            File.WriteAllText(Path.Combine(listDir, "list.json"), """
                {"tracks":{"bgm_alcatraz":{"title":"Lost Planet Alcatraz","artist":"194","loopMs":0}}}
                """);

            // Stale AppData cache with raw id as the name and .ssf path.
            var stale = new MusicLibraryCache
            {
                InstallRoot = _root,
                EnrichmentComplete = true,
                SchemaVersion = MusicLibraryCache.CurrentSchema,
                Songs =
                {
                    new CachedSongInfo
                    {
                        Id = "bgm_alcatraz",
                        Name = "bgm_alcatraz",
                        SwfPath = Path.Combine(_root, "data", "sound", "bgm_alcatraz.ssf")
                    }
                }
            };
            File.WriteAllText(_cachePath, Newtonsoft.Json.JsonConvert.SerializeObject(stale));

            var cache = new MusicLibraryCacheService().EnsureFastLibrary(_root);
            var song = cache.Songs.Should().ContainSingle().Subject;
            song.Name.Should().Be("Lost Planet Alcatraz");
            song.Artist.Should().Be("194");
            song.DisplayLabel.Should().Be("Lost Planet Alcatraz — 194");
            song.SwfPath.Should().EndWith("bgm_alcatraz.swf");
        }

        [Fact]
        public void EnsureFastLibrary_DoesNotRereadACheckedLoop()
        {
            Directory.CreateDirectory(Path.Combine(_root, "data", "sound"));
            var swf = Path.Combine(_root, "data", "sound", "bgm_menu.swf");
            var bytes = new List<byte>();
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("FWS"));
            bytes.Add(10);
            bytes.AddRange(new byte[4]);
            bytes.Add(9);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("musicLoop"));
            bytes.Add(5);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("12345"));
            File.WriteAllBytes(swf, bytes.ToArray());
            ContentPackService.EnsureRoot(_root);
            var listDir = Path.Combine(ShellWorkspaceService.Ssf2mmDirectory(_root), "music_player");
            Directory.CreateDirectory(listDir);
            File.WriteAllText(Path.Combine(listDir, "list.json"), """
                {"tracks":{"bgm_menu":{"title":"Menu 1","artist":"","loopMs":0}}}
                """);

            var stale = new MusicLibraryCache
            {
                InstallRoot = _root,
                EnrichmentComplete = true,
                SchemaVersion = MusicLibraryCache.CurrentSchema,
                Songs =
                {
                    new CachedSongInfo
                    {
                        Id = "bgm_menu",
                        Name = "Menu 1",
                        SwfPath = swf,
                        LoopMs = 0,
                        LoopChecked = true
                    }
                }
            };
            File.WriteAllText(_cachePath, Newtonsoft.Json.JsonConvert.SerializeObject(stale));

            SwfQuickScan.TryReadMusicMeta(swf, out _, out _, out var loop).Should().BeTrue();
            loop.Should().Be(12345);
            var cache = new MusicLibraryCacheService().EnsureFastLibrary(_root);
            var song = cache.Songs.Should().ContainSingle(s => s.Id == "bgm_menu").Subject;
            song.LoopMs.Should().Be(0);
            song.LoopChecked.Should().BeTrue();
        }
    }
}
