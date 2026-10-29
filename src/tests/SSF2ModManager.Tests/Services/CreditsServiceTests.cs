using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class CreditsServiceTests
    {
        [Fact]
        public void StandingCredits_AddsIdkAndOriginalPaletteMaker()
        {
            var notes = CreditsService.StandingCredits("");
            notes.Should().Contain("IDK — David (stariwinkle)");
            notes.Should().Contain("Palette Maker (original) — VAL");
            notes.Should().Contain("Discord Presence — Jotardo");
            var page = CreditsService.GeneralCreditsText();
            page.Should().Contain("Created by MasterWex");
            page.Should().Contain("Discord Presence — Jotardo");
            page.Should().Contain("Palette Maker (original) — VAL");
        }

        [Fact]
        public void StandingCredits_DoesNotDuplicate()
        {
            var once = CreditsService.StandingCredits("# Add freeform credits below this line.");
            var twice = CreditsService.StandingCredits(once);
            twice.Should().Be(once);
        }
    }
}
