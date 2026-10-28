using System;

namespace SSF2ModManager.Services
{
    /// <summary>DVD-style bounce for the home-screen logo. Each edge hit is one count.</summary>
    public static class LogoBounceMath
    {
        public const string ExitPreview = "Click to exit";
        public const string CornerPreview = "whoooo it hit the corner!";

        public readonly record struct StepResult(double X, double Y, double Vx, double Vy, int Hits);

        public static StepResult Step(double x, double y, double vx, double vy, double maxX, double maxY, double dt)
        {
            x += vx * dt;
            y += vy * dt;
            var hits = 0;
            if (x <= 0)
            {
                x = 0;
                vx = Math.Abs(vx);
                hits++;
            }
            else if (x >= maxX)
            {
                x = maxX;
                vx = -Math.Abs(vx);
                hits++;
            }
            if (y <= 0)
            {
                y = 0;
                vy = Math.Abs(vy);
                hits++;
            }
            else if (y >= maxY)
            {
                y = maxY;
                vy = -Math.Abs(vy);
                hits++;
            }
            return new StepResult(x, y, vx, vy, hits);
        }

        /// <summary>A real corner, or a wall hit that lands within <paramref name="slop"/> pixels of the other wall.</summary>
        public static bool IsCornerHit(double x, double y, double maxX, double maxY, int hits, double slop = 8)
        {
            if (hits >= 2)
                return true;
            if (hits != 1)
                return false;
            var nearX = x <= slop || maxX - x <= slop;
            var nearY = y <= slop || maxY - y <= slop;
            return nearX && nearY;
        }

        /// <summary>
        /// After one wall hit, change the free speed so the next wall is a corner.
        /// Signs stay. A position already in a corner is left alone.
        /// </summary>
        public static (double Vx, double Vy) AimAtCorner(double x, double y, double vx, double vy, double maxX, double maxY)
        {
            var onX = x <= 0.5 || x >= maxX - 0.5;
            var onY = y <= 0.5 || y >= maxY - 0.5;
            if (onX && onY)
                return (vx, vy);
            if (Math.Abs(vx) < 1)
                vx = 92;
            if (Math.Abs(vy) < 1)
                vy = 64;
            if (onX)
            {
                var travel = vx > 0 ? maxX - x : x;
                var tx = travel / Math.Abs(vx);
                if (tx < 0.001)
                    tx = 0.001;
                var targetY = vy >= 0 ? maxY : 0;
                if (Math.Abs(targetY - y) < 0.5)
                    targetY = targetY <= 0 ? maxY : 0;
                vy = (targetY - y) / tx;
                return (vx, vy);
            }
            if (onY)
            {
                var travel = vy > 0 ? maxY - y : y;
                var ty = travel / Math.Abs(vy);
                if (ty < 0.001)
                    ty = 0.001;
                var targetX = vx >= 0 ? maxX : 0;
                if (Math.Abs(targetX - x) < 0.5)
                    targetX = targetX <= 0 ? maxX : 0;
                vx = (targetX - x) / ty;
                return (vx, vy);
            }
            return (vx, vy);
        }

        /// <summary>Rotates saturated color. Near-black and gray stay put so the sword outline does not wash out.</summary>
        public static (byte R, byte G, byte B) ShiftHue(byte r, byte g, byte b, double degrees)
        {
            RgbToHsv(r, g, b, out var h, out var s, out var v);
            if (s < 0.12 || v < 0.12)
                return (r, g, b);
            h = (h + degrees) % 360;
            if (h < 0)
                h += 360;
            HsvToRgb(h, s, v, out var rr, out var gg, out var bb);
            return (rr, gg, bb);
        }

        private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            var rd = r / 255.0;
            var gd = g / 255.0;
            var bd = b / 255.0;
            var max = Math.Max(rd, Math.Max(gd, bd));
            var min = Math.Min(rd, Math.Min(gd, bd));
            var d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0)
                h = 0;
            else if (max == rd)
                h = 60 * (((gd - bd) / d) % 6);
            else if (max == gd)
                h = 60 * (((bd - rd) / d) + 2);
            else
                h = 60 * (((rd - gd) / d) + 4);
            if (h < 0)
                h += 360;
        }

        private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            var c = v * s;
            var hp = h / 60;
            var x = c * (1 - Math.Abs(hp % 2 - 1));
            double rd, gd, bd;
            if (hp < 1) { rd = c; gd = x; bd = 0; }
            else if (hp < 2) { rd = x; gd = c; bd = 0; }
            else if (hp < 3) { rd = 0; gd = c; bd = x; }
            else if (hp < 4) { rd = 0; gd = x; bd = c; }
            else if (hp < 5) { rd = x; gd = 0; bd = c; }
            else { rd = c; gd = 0; bd = x; }
            var m = v - c;
            r = (byte)Math.Clamp((int)Math.Round((rd + m) * 255), 0, 255);
            g = (byte)Math.Clamp((int)Math.Round((gd + m) * 255), 0, 255);
            b = (byte)Math.Clamp((int)Math.Round((bd + m) * 255), 0, 255);
        }
    }
}
