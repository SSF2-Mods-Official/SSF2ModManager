using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// A stage background pack: one gif, png, or mp4, plus the stage that loads when the slot is picked.
    /// The stage SWF is not copied.
    /// </summary>
    public static class StageBackgroundStore
    {
        public const string TypeName = "StageBackground";

        public static string Root(string installRoot) =>
            Path.Combine(ShellWorkspaceService.ExpansionsRoot(installRoot), "stage_backgrounds");

        /// <summary>Folder id from the display name, using the same sanitize rules as a stage clone.</summary>
        public static string MakeId(string displayName, string? linkage = null)
        {
            var raw = string.IsNullOrWhiteSpace(linkage) ? displayName : linkage;
            var folder = CharacterCloneService.SanitizeExpansionFolderName(raw ?? "");
            return ShellCharacterCloneService.ExpansionGameId(folder);
        }

        /// <summary>
        /// Writes <c>expansions/stage_backgrounds/&lt;id&gt;</c> with the asset, <c>info.json</c>, and <c>stagepreview.png</c>.
        /// </summary>
        public static string Create(string installRoot, string displayName, string sourceFile, string defaultStage, string? linkage = null)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                throw new DirectoryNotFoundException("SSF2 Shell build folder was not found.");
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
                throw new FileNotFoundException("Background file was not found.", sourceFile);
            defaultStage = (defaultStage ?? "").Trim();
            if (string.IsNullOrWhiteSpace(defaultStage))
                throw new ArgumentException("Pick the stage this background uses.");

            var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
            if (ext is not ".png" and not ".gif" and not ".mp4")
                throw new ArgumentException("Use a png, gif, or mp4.");

            var id = MakeId(displayName, linkage);
            var name = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
            var dest = Path.Combine(Root(installRoot), id);
            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            Directory.CreateDirectory(dest);

            var assetName = "background" + ext;
            var assetPath = Path.Combine(dest, assetName);
            File.Copy(sourceFile, assetPath, overwrite: true);
            WritePreview(sourceFile, Path.Combine(dest, "stagepreview.png"));

            var info = new JObject
            {
                ["name"] = name,
                ["id"] = id,
                ["type"] = TypeName,
                ["defaultStage"] = defaultStage,
                ["asset"] = assetName,
                ["enabled"] = true
            };
            File.WriteAllText(Path.Combine(dest, "info.json"), info.ToString(Formatting.Indented));
            return dest;
        }

        public sealed class Choice
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public string Label => string.IsNullOrEmpty(Id) ? "(stage default)" : Name;
        }

        public static List<Choice> List(string installRoot)
        {
            var list = new List<Choice>();
            var root = Root(installRoot);
            if (!Directory.Exists(root))
                return list;
            foreach (var dir in Directory.GetDirectories(root).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(dir);
                if (id.StartsWith('_') || id.StartsWith('.'))
                    continue;
                var name = id;
                var infoPath = Path.Combine(dir, "info.json");
                if (File.Exists(infoPath))
                {
                    try
                    {
                        var info = JObject.Parse(File.ReadAllText(infoPath));
                        if (!string.IsNullOrWhiteSpace(info["name"]?.ToString()))
                            name = info["name"]!.ToString();
                        if (!string.IsNullOrWhiteSpace(info["id"]?.ToString()))
                            id = info["id"]!.ToString();
                    }
                    catch
                    {
                    }
                }
                list.Add(new Choice { Id = id, Name = name });
            }
            return list;
        }

        public static void WritePreview(string sourceFile, string destPng)
        {
            var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
            BitmapSource? frame = ext switch
            {
                ".gif" => FirstGifFrame(sourceFile),
                ".mp4" => VideoFrame(sourceFile) ?? LabeledStill(Path.GetFileName(sourceFile)),
                _ => Still(sourceFile)
            };
            if (frame == null)
                frame = LabeledStill(Path.GetFileName(sourceFile));
            SavePng(frame, destPng);
        }

        private static BitmapSource? Still(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                    return null;
                var frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource? FirstGifFrame(string path)
        {
            try
            {
                var frames = ManagerBackgroundMedia.DecodeGif(path);
                if (frames.Count == 0)
                    return Still(path);
                var image = frames[0].Image;
                if (image.CanFreeze && !image.IsFrozen)
                    image.Freeze();
                return image;
            }
            catch
            {
                return Still(path);
            }
        }

        private static BitmapSource? VideoFrame(string path)
        {
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
                factory.GetImage(new ShellSize { Cx = 320, Cy = 180 }, 0, out var hbmp);
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(hbmp);
                }
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource LabeledStill(string label)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 48, 72)), null, new Rect(0, 0, 160, 90));
                var text = new FormattedText(
                    string.IsNullOrWhiteSpace(label) ? "Background" : label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    14,
                    System.Windows.Media.Brushes.White,
                    1.0);
                dc.DrawText(text, new System.Windows.Point(8, 36));
            }
            var bmp = new RenderTargetBitmap(160, 90, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        private static void SavePng(BitmapSource source, string dest)
        {
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(source));
            using var fs = File.Create(dest);
            enc.Save(fs);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShellSize
        {
            public int Cx;
            public int Cy;
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(ShellSize size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bind,
            ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
