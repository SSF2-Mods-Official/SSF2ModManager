using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// A stage asset swap pack: pngs for the source stage's bitmap tags. The stage SWF is not copied.
    /// </summary>
    public static class StageAssetSwapStore
    {
        public const string TypeName = "StageAssetSwap";

        public static string Root(string installRoot) =>
            Path.Combine(ShellWorkspaceService.ExpansionsRoot(installRoot), "stage_assets");

        public static string MakeId(string displayName)
        {
            var folder = CharacterCloneService.SanitizeExpansionFolderName(displayName ?? "");
            return ShellCharacterCloneService.ExpansionGameId(folder);
        }

        /// <summary>Writes the pack from stage-file bytes. No <c>.swf</c> is written.</summary>
        public static string Create(string installRoot, string displayName, string sourceStage, byte[] swfBytes)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                throw new DirectoryNotFoundException("SSF2 Shell build folder was not found.");
            sourceStage = (sourceStage ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sourceStage))
                throw new ArgumentException("Pick the stage these assets come from.");
            var images = StageAssetSwap.List(swfBytes);
            return Write(installRoot, displayName, sourceStage, images, new Dictionary<int, string>());
        }

        /// <summary>
        /// Writes the pack from a stage file. Images this decoder cannot read are filled from FFDec <c>-export image</c>.
        /// </summary>
        public static async Task<string> CreateFromFileAsync(
            string installRoot,
            string displayName,
            string sourceStage,
            string swfPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath))
                throw new FileNotFoundException("Stage file was not found.", swfPath);
            var raw = await File.ReadAllBytesAsync(swfPath, cancellationToken);
            var images = StageAssetSwap.List(raw);
            var exportDir = "";
            var extras = new Dictionary<int, string>();
            try
            {
                if (images.Any(image => image.Bgra == null))
                {
                    exportDir = Path.Combine(Path.GetTempPath(), "ssf2mm-stage-assets-" + Guid.NewGuid().ToString("N"));
                    extras = await ExportMissingAsync(swfPath, exportDir, images, cancellationToken);
                }
                return Write(installRoot, displayName, sourceStage, images, extras);
            }
            finally
            {
                if (exportDir.Length > 0)
                {
                    try { Directory.Delete(exportDir, recursive: true); } catch { }
                }
            }
        }

        private static string Write(
            string installRoot,
            string displayName,
            string sourceStage,
            IReadOnlyList<StageImageTag> images,
            IReadOnlyDictionary<int, string> extras)
        {
            var id = MakeId(displayName);
            var name = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
            var dest = Path.Combine(Root(installRoot), id);
            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            var assets = Path.Combine(dest, "assets");
            Directory.CreateDirectory(assets);

            var rows = new JArray();
            string? largest = null;
            var largestArea = 0;
            foreach (var image in images)
            {
                var fileName = image.CharacterId + "_bitmap.png";
                var full = Path.Combine(assets, fileName);
                var width = image.Width;
                var height = image.Height;
                if (image.Bgra != null && width > 0 && height > 0)
                    SavePng(image.Bgra, width, height, full);
                else if (extras.TryGetValue(image.CharacterId, out var exported) && File.Exists(exported))
                {
                    if (!TryCopyPng(exported, full, out width, out height))
                        continue;
                }
                else
                    continue;
                rows.Add(new JObject
                {
                    ["characterId"] = image.CharacterId,
                    ["file"] = fileName,
                    ["width"] = width,
                    ["height"] = height
                });
                var area = width * height;
                if (area >= largestArea)
                {
                    largestArea = area;
                    largest = full;
                }
            }

            if (largest != null)
                File.Copy(largest, Path.Combine(dest, "stagepreview.png"), overwrite: true);

            File.WriteAllText(Path.Combine(dest, "assets.json"), new JObject { ["images"] = rows }.ToString(Formatting.Indented));
            var info = new JObject
            {
                ["name"] = name,
                ["id"] = id,
                ["type"] = TypeName,
                ["sourceStage"] = sourceStage,
                ["enabled"] = true
            };
            File.WriteAllText(Path.Combine(dest, "info.json"), info.ToString(Formatting.Indented));
            return dest;
        }

        private static bool TryCopyPng(string source, string dest, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                using var stream = File.OpenRead(source);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                    return false;
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var bgra = new byte[width * height * 4];
                converted.CopyPixels(bgra, width * 4, 0);
                SavePng(bgra, width, height, dest);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<Dictionary<int, string>> ExportMissingAsync(
            string swfPath,
            string exportDir,
            IReadOnlyList<StageImageTag> images,
            CancellationToken cancellationToken)
        {
            var found = new Dictionary<int, string>();
            var missing = new HashSet<int>(images.Where(image => image.Bgra == null).Select(image => image.CharacterId));
            if (missing.Count == 0)
                return found;
            Directory.CreateDirectory(exportDir);
            try
            {
                var jpexs = new JpexsCliService();
                await jpexs.ExportAsync("", swfPath, exportDir, "image", cancellationToken);
                foreach (var file in Directory.GetFiles(exportDir, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var match = Regex.Match(Path.GetFileName(file), @"^(\d+)");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id) || !missing.Contains(id))
                        continue;
                    found[id] = file;
                }
            }
            catch
            {
                return found;
            }
            return found;
        }

        public static void SavePng(byte[] bgra, int width, int height, string path)
        {
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
