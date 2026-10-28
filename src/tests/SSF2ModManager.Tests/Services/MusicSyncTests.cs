using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class MusicSyncTests
    {
        [Fact]
        public void Find_MatchesResourceIdAndStrippedName()
        {
            var songs = new[]
            {
                new CachedSongInfo { Id = "bgm_huecomundo", Name = "Hueco Mundo" },
                new CachedSongInfo { Id = "bgm_crateria", Name = "Crateria" }
            };
            MusicSync.Find(songs, "bgm_huecomundo", null)!.Id.Should().Be("bgm_huecomundo");
            MusicSync.Find(songs, "crateria", "nope")!.Id.Should().Be("bgm_crateria");
            MusicSync.Find(songs, "missing", "Hueco Mundo")!.Id.Should().Be("bgm_huecomundo");
            MusicSync.Find(songs, "", "").Should().BeNull();
        }

        [Fact]
        public void ShouldPlay_FollowsTheGameWithoutRepeatingTheSameState()
        {
            MusicSync.ShouldPlay(gamePaused: false, miniPlaying: false).Should().BeTrue();
            MusicSync.ShouldPlay(gamePaused: true, miniPlaying: true).Should().BeFalse();
            MusicSync.ShouldPlay(gamePaused: false, miniPlaying: true).Should().BeNull();
            MusicSync.ShouldPlay(gamePaused: true, miniPlaying: false).Should().BeNull();
        }
    }
}
