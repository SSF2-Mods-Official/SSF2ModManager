using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class LogRunSliceTests
    {
        [Fact]
        public void Latest_KeepsTheLastMarkerAndTheLinesAfterIt()
        {
            var body = "old line\n[Bootstrap] loading engine1\nfirst\n[Bootstrap] loading engine1\nvegeta\n";
            LogRunSlice.Latest(body, "[Bootstrap] loading engine1").Should().Be("[Bootstrap] loading engine1\nvegeta\n");
        }

        [Fact]
        public void Latest_KeepsTheWholeFileWhenTheMarkerIsMissing()
        {
            LogRunSlice.Latest("no marker\n", "[Bootstrap] loading engine1").Should().Be("no marker\n");
            LogRunSlice.Latest(null, "[Bootstrap] loading engine1").Should().Be("");
        }
    }
}
