using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class GameProfilerSessionTests
    {
        [Fact]
        public void SlowFrame_NamesThePaintSpan()
        {
            var events = new[]
            {
                new TelemetryEvent
                {
                    Name = "paint.raster",
                    Span = 70,
                    W = 349,
                    H = 248,
                    Hits = 39135
                },
                new TelemetryEvent { Name = ".enter", Delta = 84 },
                new TelemetryEvent { Name = ".mem.total", Value = 280 * 1024 },
                new TelemetryEvent { Name = ".mem.private", Value = 798 * 1024 }
            };
            var snap = GameProfilerSession.Read(events);
            snap.SpikeText.Should().Be("84 ms  paint.raster 70 ms  349x248 hits=39135");
            snap.MaxMs.Should().Be(84);
            snap.Fps.Should().Be(12);
            snap.FlashMb.Should().Be(280);
            snap.PrivateMb.Should().Be(798);
            GameProfilerSession.SampleLine(snap).Should().Be(
                "[profiler] sample fps=12 maxMs=84 flashMb=280 privateMb=798 spike=84 ms  paint.raster 70 ms  349x248 hits=39135");
        }

        [Fact]
        public void ParseLine_ReadsTheRasterObject()
        {
            var ok = GameProfilerSession.TryParseLine(
                "{\"name\":\"paint.raster\",\"span\":70,\"w\":349,\"h\":248,\"hits\":39135}",
                out var ev);
            ok.Should().BeTrue();
            ev.Name.Should().Be("paint.raster");
            ev.Span.Should().Be(70);
            ev.W.Should().Be(349);
            ev.H.Should().Be(248);
            ev.Hits.Should().Be(39135);
        }

        [Fact]
        public void SpikeText_AddsSpansThatShareAName()
        {
            var frame = new ProfilerFrame
            {
                DeltaMs = 99,
                Spans = new[]
                {
                    new TelemetryEvent { Name = "match", Span = 90 },
                    new TelemetryEvent { Name = "match.stage", Span = 4 },
                    new TelemetryEvent { Name = "match.chars", Span = 70 },
                    new TelemetryEvent { Name = "match.stage", Span = 8 },
                    new TelemetryEvent { Name = "match.hits", Span = 0 }
                }
            };
            GameProfilerSession.SpikeText(frame).Should().Be(
                "99 ms  match 90 ms  match.stage 12 ms  match.chars 70 ms");
        }
    }
}
