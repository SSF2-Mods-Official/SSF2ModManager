using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SSF2ModManager.Services
{
    public enum ManagerBackgroundKind
    {
        Still,
        Gif,
        Video
    }

    public readonly record struct GifFrame(BitmapSource Image, int DelayMs);

    /// <summary>Custom Mod Manager background: file kind, a copy under AppData, and composited gif frames.</summary>
    public static class ManagerBackgroundMedia
    {
        public static ManagerBackgroundKind KindFor(string? path)
        {
            var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            if (ext == ".gif")
                return ManagerBackgroundKind.Gif;
            if (ext is ".mp4" or ".m4v" or ".wmv" or ".avi" or ".mov" or ".mkv" or ".webm" or ".mpg" or ".mpeg")
                return ManagerBackgroundKind.Video;
            return ManagerBackgroundKind.Still;
        }

        /// <summary>Copies <paramref name="source"/> to <c>manager-background.ext</c> in <paramref name="directory"/>.</summary>
        public static string Install(string source, string directory)
        {
            Directory.CreateDirectory(directory);
            var ext = Path.GetExtension(source);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".bin";
            var dest = Path.Combine(directory, "manager-background-" + Guid.NewGuid().ToString("N") + ext.ToLowerInvariant());
            File.Copy(source, dest, overwrite: false);
            DeleteOthers(directory, dest);
            return dest;
        }

        /// <summary>Removes older <c>manager-background*</c> copies. A file still open in the player is left for the next try.</summary>
        public static void DeleteOthers(string directory, string keepPath)
        {
            if (!Directory.Exists(directory))
                return;
            var keep = Path.GetFullPath(keepPath);
            foreach (var old in Directory.GetFiles(directory, "manager-background*"))
            {
                if (string.Equals(Path.GetFullPath(old), keep, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(old); } catch { /* the player may still have the previous file open */ }
            }
        }

        public static List<GifFrame> DecodeGif(string path)
        {
            var frames = new List<GifFrame>();
            try
            {
                using var stream = File.OpenRead(path);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                    return frames;
                var screen = decoder.Metadata as BitmapMetadata;
                var width = QueryInt(screen, "/logscrdesc/Width");
                var height = QueryInt(screen, "/logscrdesc/Height");
                if (width <= 0) width = decoder.Frames[0].PixelWidth;
                if (height <= 0) height = decoder.Frames[0].PixelHeight;
                BitmapSource? canvas = null;
                BitmapSource? restore = null;
                var prevDisposal = 0;
                var prevRect = new Rect();
                foreach (BitmapFrame raw in decoder.Frames)
                {
                    var meta = raw.Metadata as BitmapMetadata;
                    var left = QueryInt(meta, "/imgdesc/Left");
                    var top = QueryInt(meta, "/imgdesc/Top");
                    var disposal = QueryInt(meta, "/grctlext/Disposal");
                    var delay = QueryInt(meta, "/grctlext/Delay");
                    var delayMs = delay <= 1 ? 100 : delay * 10;
                    var rect = new Rect(left, top, raw.PixelWidth, raw.PixelHeight);
                    if (prevDisposal == 2)
                        canvas = ClearRect(canvas, width, height, prevRect);
                    else if (prevDisposal == 3)
                        canvas = restore;
                    if (disposal == 3)
                        restore = canvas;
                    canvas = DrawFrame(canvas, width, height, raw, rect);
                    frames.Add(new GifFrame(canvas, delayMs));
                    prevDisposal = disposal;
                    prevRect = rect;
                }
            }
            catch
            {
                frames.Clear();
            }
            return frames;
        }

        private static int QueryInt(BitmapMetadata? meta, string query)
        {
            try
            {
                if (meta == null || !meta.ContainsQuery(query))
                    return 0;
                return Convert.ToInt32(meta.GetQuery(query));
            }
            catch
            {
                return 0;
            }
        }

        private static BitmapSource DrawFrame(BitmapSource? canvas, int width, int height, BitmapSource frame, Rect rect)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (canvas != null)
                    dc.DrawImage(canvas, new Rect(0, 0, width, height));
                dc.DrawImage(frame, rect);
            }
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        private static BitmapSource ClearRect(BitmapSource? canvas, int width, int height, Rect rect)
        {
            if (canvas == null)
            {
                var blank = new RenderTargetBitmap(Math.Max(width, 1), Math.Max(height, 1), 96, 96, PixelFormats.Pbgra32);
                blank.Freeze();
                return blank;
            }
            var converted = new FormatConvertedBitmap(canvas, PixelFormats.Pbgra32, null, 0);
            var wb = new WriteableBitmap(converted);
            var x0 = Math.Clamp((int)rect.X, 0, width);
            var y0 = Math.Clamp((int)rect.Y, 0, height);
            var x1 = Math.Clamp((int)Math.Ceiling(rect.X + rect.Width), 0, width);
            var y1 = Math.Clamp((int)Math.Ceiling(rect.Y + rect.Height), 0, height);
            if (x1 > x0 && y1 > y0)
            {
                var zeros = new byte[(x1 - x0) * 4];
                for (var y = y0; y < y1; y++)
                    wb.WritePixels(new Int32Rect(x0, y, x1 - x0, 1), zeros, zeros.Length, 0);
            }
            wb.Freeze();
            return wb;
        }
    }
}
