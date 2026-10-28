using System;
using System.Linq;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class LauncherMusicUiTests
    {
        [Fact]
        public void TrackLabel_UsesDisplayLabelWithArtist()
        {
            var song = new CachedSongInfo
            {
                Id = "bgm_alcatraz",
                Name = "Lost Planet Alcatraz",
                Artist = "194"
            };
            LauncherMusicUi.TrackLabel(song).Should().Be("Lost Planet Alcatraz — 194");
            LauncherMusicUi.TrackLabel(null).Should().Be("No track");
        }

        [Fact]
        public void FormatTrackTime_MinutesAndHours()
        {
            LauncherMusicUi.FormatTrackTime(TimeSpan.FromSeconds(65)).Should().Be("1:05");
            LauncherMusicUi.FormatTrackTime(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3))
                .Should().Be("1:02:03");
        }

        [Fact]
        public void SeekRatio_AndBack()
        {
            var dur = TimeSpan.FromSeconds(100);
            LauncherMusicUi.SeekRatio(TimeSpan.FromSeconds(25), dur).Should().BeApproximately(0.25, 0.001);
            LauncherMusicUi.SeekFromRatio(0.5, dur).TotalSeconds.Should().BeApproximately(50, 0.01);
            LauncherMusicUi.SeekRatio(TimeSpan.Zero, TimeSpan.Zero).Should().Be(0);
        }

        [Fact]
        public void PickerSongs_SortedByDisplayLabel_SkipsEmptyIds()
        {
            var songs = new[]
            {
                new CachedSongInfo { Id = "bgm_z", Name = "Zulu" },
                new CachedSongInfo { Id = "", Name = "Nope" },
                new CachedSongInfo { Id = "bgm_a", Name = "Alpha", Artist = "A" },
            };
            var pick = LauncherMusicUi.PickerSongs(songs);
            pick.Select(s => s.Id).Should().Equal("bgm_a", "bgm_z");
            pick[0].DisplayLabel.Should().Be("Alpha — A");
        }

        [Fact]
        public void RememberLastSong_KeepsTheCurrentId()
        {
            var cache = new MusicLibraryCache();
            var current = new CachedSongInfo { Id = "bgm_b", Name = "Beta" };
            var queue = new[]
            {
                new CachedSongInfo { Id = "bgm_a", Name = "Alpha" },
                current
            };
            LauncherMusicUi.RememberLastSong(cache, current, queue);
            cache.LastPlayedSongId.Should().Be("bgm_b");
            cache.LastPlayedQueueIds.Should().Equal("bgm_a", "bgm_b");
        }

        [Fact]
        public void PreviewQueue_KeepsTheSongList()
        {
            var library = new[]
            {
                new CachedSongInfo { Id = "bgm_a", Name = "Alpha" },
                new CachedSongInfo { Id = "bgm_b", Name = "Beta" },
                new CachedSongInfo { Id = "bgm_c", Name = "Gamma" }
            };
            var current = LauncherMusicUi.PickerSongs(library);
            var preview = new[] { library[2] };
            var (queue, index) = LauncherMusicUi.PreviewQueue(current, library, preview, 0);
            queue.Select(s => s.Id).Should().Equal("bgm_a", "bgm_b", "bgm_c");
            index.Should().Be(2);

            var shrunk = new[] { library[0] };
            var (restored, at) = LauncherMusicUi.PreviewQueue(shrunk, library, preview, 0);
            restored.Select(s => s.Id).Should().Equal("bgm_a", "bgm_b", "bgm_c");
            at.Should().Be(2);
        }

        [Fact]
        public void PreviewQueue_PlaylistReplacesTheList()
        {
            var current = new[]
            {
                new CachedSongInfo { Id = "bgm_a", Name = "Alpha" },
                new CachedSongInfo { Id = "bgm_b", Name = "Beta" }
            };
            var playlist = new[]
            {
                new CachedSongInfo { Id = "bgm_x", Name = "X" },
                new CachedSongInfo { Id = "bgm_y", Name = "Y" }
            };
            var (queue, index) = LauncherMusicUi.PreviewQueue(current, current, playlist, 1);
            queue.Select(s => s.Id).Should().Equal("bgm_x", "bgm_y");
            index.Should().Be(1);
        }

        [Fact]
        public void PreviewIdleText_JoinsNameAndVersion()
        {
            LauncherMusicUi.PreviewIdleText("SSF2 Beta Mod Manager", "v1.1.0")
                .Should().Be("SSF2 Beta Mod Manager v1.1.0");
        }
    }
}
