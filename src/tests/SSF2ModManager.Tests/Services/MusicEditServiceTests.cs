using System.Collections.Generic;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class MusicEditServiceTests
    {
        [Fact]
        public void ParseMusicRegister_ReadsUnquotedIdKeys()
        {
            const string asText = @"
register(""stage"", ""battlefield"");
register(""music"", [
				{id: ""bgm_battlefield""},
				{id: ""bgm_menu""}
			]);
register(""other"", 1);";
            MusicEditService.ParseMusicRegister(asText)
                .Should().Equal("bgm_battlefield", "bgm_menu");
        }

        [Fact]
        public void ParseMusicRegister_ReadsFfdecQuotedIdKeys()
        {
            const string asText = @"register(""music"",[{""id"":""bgm_battlefield""},{""id"":""bgm_multimansmash""},{""id"":""bgm_cruelsmash""}]);";
            MusicEditService.ParseMusicRegister(asText)
                .Should().Equal("bgm_battlefield", "bgm_multimansmash", "bgm_cruelsmash");
        }

        [Fact]
        public void ParseMusicRegister_IgnoresMusicStringRegister()
        {
            // Music SWFs use register("music", "bgm_x") — not a playlist.
            const string asText = @"register(""music"",""bgm_battlefield"");
register(""musicLoop"",8468);";
            MusicEditService.ParseMusicRegister(asText).Should().BeEmpty();
        }

        [Fact]
        public void RewriteMusicRegister_AppendsTrack_PreservingExisting()
        {
            const string asText = @"register(""music"",[{""id"":""bgm_battlefield""},{""id"":""bgm_multimansmash""}]);";
            var next = MusicEditService.RewriteMusicRegisterInText(
                asText,
                MusicEditService.BuildMusicArrayLiteral(new[]
                {
                    "bgm_battlefield", "bgm_multimansmash", "bgm_deadanimesong"
                }));
            MusicEditService.ParseMusicRegister(next)
                .Should().Equal("bgm_battlefield", "bgm_multimansmash", "bgm_deadanimesong");
        }

        [Fact]
        public void PatchMusicMetadata_UpdatesGuidLoopId3AndMusicId_FfdecStyle()
        {
            const string src = @"
register(""id"",""bgm_battlefield"");
         register(""guid"",""e4ea4051-6ca9-4bc1-9820-42705cf03717"");
         register(""music"",""bgm_battlefield"");
         register(""musicLoop"",8468);
         register(""id3"",{
            ""songName"":""Battlefield"",
            ""artist"":""Jacob Tjolsen""
         });";
            var next = MusicEditService.PatchMusicMetadataInText(
                src, "bgm_deadanimesong", "10cf18f4-7578-4cc5-9f46-6982e5b6a63a",
                4500, "Song of the Dead", "KANA-BOON");
            next.Should().Contain("register(\"id\",\"bgm_deadanimesong\")");
            next.Should().Contain("register(\"guid\",\"10cf18f4-7578-4cc5-9f46-6982e5b6a63a\")");
            next.Should().Contain("register(\"music\",\"bgm_deadanimesong\")");
            next.Should().Contain("register(\"musicLoop\",4500)");
            next.Should().Contain("\"songName\":\"Song of the Dead\"");
            next.Should().Contain("\"artist\":\"KANA-BOON\"");
            next.Should().NotContain("e4ea4051-6ca9-4bc1-9820-42705cf03717");
        }

        [Fact]
        public void BuildMusicArrayLiteral_EmptyIsEmptyArray()
        {
            MusicEditService.BuildMusicArrayLiteral(new List<string>())
                .Should().Be("[]");
        }
    }
}
