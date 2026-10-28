using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class LogoBounceMathTests
    {
        [Fact]
        public void Step_RightEdge_FlipsAndCounts()
        {
            var hit = LogoBounceMath.Step(90, 10, 40, 20, 100, 200, 1);
            Assert.Equal(100, hit.X);
            Assert.True(hit.Vx < 0);
            Assert.Equal(1, hit.Hits);
        }

        [Fact]
        public void Step_Corner_CountsBothEdges()
        {
            var hit = LogoBounceMath.Step(-5, 210, -30, 40, 100, 200, 1);
            Assert.Equal(0, hit.X);
            Assert.Equal(200, hit.Y);
            Assert.True(hit.Vx > 0);
            Assert.True(hit.Vy < 0);
            Assert.Equal(2, hit.Hits);
        }

        [Fact]
        public void IsCornerHit_CountsANearMissAndASecondCorner()
        {
            Assert.True(LogoBounceMath.IsCornerHit(200, 3, 200, 100, 1));
            Assert.False(LogoBounceMath.IsCornerHit(200, 40, 200, 100, 1));
            Assert.True(LogoBounceMath.IsCornerHit(0, 0, 200, 100, 2));
            Assert.False(LogoBounceMath.IsCornerHit(0, 0, 200, 100, 0));
        }

        [Fact]
        public void AimAtCorner_NextHitIsACorner()
        {
            var (vx, vy) = LogoBounceMath.AimAtCorner(0, 40, 100, 10, 200, 100);
            double x = 0, y = 40;
            var hits = 0;
            for (var i = 0; i < 80 && hits == 0; i++)
            {
                var step = LogoBounceMath.Step(x, y, vx, vy, 200, 100, 0.05);
                x = step.X;
                y = step.Y;
                vx = step.Vx;
                vy = step.Vy;
                hits = step.Hits;
            }
            Assert.Equal(2, hits);
            Assert.Equal(200, x);
            Assert.Equal(100, y);
        }

        [Fact]
        public void ShiftHue_RedMoves_BlackStays()
        {
            var red = LogoBounceMath.ShiftHue(220, 16, 16, 120);
            Assert.NotEqual(220, red.R);
            var black = LogoBounceMath.ShiftHue(8, 8, 8, 140);
            Assert.Equal((byte)8, black.R);
            Assert.Equal((byte)8, black.G);
            Assert.Equal((byte)8, black.B);
        }
    }
}
