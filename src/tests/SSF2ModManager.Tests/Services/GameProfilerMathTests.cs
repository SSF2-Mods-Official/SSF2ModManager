using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class GameProfilerMathTests
    {
        [Fact]
        public void Fps_CountsDisplayedFramesOverTheTick()
        {
            GameProfilerMath.Fps(1000, 1030, 500).Should().Be(60);
            GameProfilerMath.Fps(1000, 1010, 500).Should().Be(20);
            GameProfilerMath.Fps(1000, 999, 500).Should().BeNull();
            GameProfilerMath.Fps(1000, 1060, 0).Should().BeNull();
        }

        [Fact]
        public void FrameMs_AndBand_MatchTheStrip()
        {
            GameProfilerMath.FrameMs(60).Should().Be(17);
            GameProfilerMath.FrameMs(0).Should().Be(0);
            GameProfilerMath.Band(34).Should().Be("ok");
            GameProfilerMath.Band(35).Should().Be("slow");
            GameProfilerMath.Band(68).Should().Be("slow");
            GameProfilerMath.Band(69).Should().Be("spike");
            GameProfilerMath.Band(0).Should().Be("none");
        }

        [Fact]
        public void WindowStart_KeepsTheSelectedFrameInView()
        {
            GameProfilerMath.WindowStart(400, 399, 160).Should().Be(240);
            GameProfilerMath.WindowStart(400, 10, 160).Should().Be(0);
            GameProfilerMath.WindowStart(400, 200, 160).Should().Be(120);
            GameProfilerMath.WindowStart(40, 10, 160).Should().Be(0);
        }
    }
}
