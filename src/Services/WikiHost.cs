using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Save, history, and tutorial calls for the wiki window. The page script talks to this object.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public sealed class WikiHost
    {
        private static readonly HashSet<string> DocExt = new(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".json", ".txt"
        };

        private readonly string _docs;
        private readonly Window _window;

        public WikiHost(string docsRoot, Window window)
        {
            _docs = docsRoot;
            _window = window;
        }

        public string Author()
        {
            var file = Path.Combine(_docs, "_history", "author.txt");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        }

        public string History(string file)
        {
            var record = ReadRecord(file);
            return record.ToString(Newtonsoft.Json.Formatting.None);
        }

        public string Images()
        {
            var dir = Path.Combine(_docs, "_images");
            if (!Directory.Exists(dir))
                return "[]";
            var names = Directory.GetFiles(dir)
                .Select(Path.GetFileName)
                .Where(n => n != null && RejectRel(n) == null)
                .Cast<string>()
                .ToList();
            return new JArray(names).ToString(Newtonsoft.Json.Formatting.None);
        }

        public string PickImage()
        {
            return _window.Dispatcher.Invoke(PickImageCore);
        }

        public string SavePage(string file, string body, string author, string note)
        {
            author = (author ?? "").Trim();
            note = (note ?? "").Trim();
            if (author.Length == 0)
                throw new InvalidOperationException("Author name is required.");
            if (note.Length == 0)
                throw new InvalidOperationException("Say what changed before saving.");
            var abs = PageInside(file) ?? throw new InvalidOperationException("That page cannot be saved.");
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, body ?? "", Encoding.UTF8);
            Directory.CreateDirectory(Path.Combine(_docs, "_history"));
            File.WriteAllText(Path.Combine(_docs, "_history", "author.txt"), author, Encoding.UTF8);
            var prior = ReadRecord(file);
            var entries = (JArray)prior["entries"]!;
            entries.Insert(0, new JObject
            {
                ["at"] = DateTime.UtcNow.ToString("o"),
                ["author"] = author,
                ["note"] = note
            });
            WriteRecord(file, prior);
            Rebuild();
            return "";
        }

        public string DeletePage(string file)
        {
            var abs = PageInside(file) ?? throw new InvalidOperationException("That page cannot be deleted.");
            if (ReadRecord(file)["userMade"]?.Value<bool>() != true)
                throw new InvalidOperationException("Only pages you created can be deleted.");
            File.Delete(abs);
            var hist = HistoryPath(file);
            if (File.Exists(hist))
                File.Delete(hist);
            var dir = Path.GetDirectoryName(abs)!;
            var tutorials = Path.GetFullPath(Path.Combine(_docs, "Dev Wiki", "Tutorials"));
            if (dir.StartsWith(tutorials + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !Directory.EnumerateFiles(dir, "*.md").Any())
                Directory.Delete(dir, true);
            Rebuild();
            return "";
        }

        public string PackagePage(string file, string author)
        {
            var abs = PageInside(file) ?? throw new InvalidOperationException("That page cannot be packaged.");
            if (ReadRecord(file)["userMade"]?.Value<bool>() != true)
                throw new InvalidOperationException("Only tutorials you created can be packaged.");
            var dest = _window.Dispatcher.Invoke(() =>
            {
                var dlg = new SaveFileDialog
                {
                    FileName = Path.GetFileNameWithoutExtension(file) + "-tutorial.zip",
                    Filter = "Zip|*.zip"
                };
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : "";
            });
            if (string.IsNullOrEmpty(dest))
                return "";
            var tmp = Path.Combine(Path.GetTempPath(), "ssf2-tutorial-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                File.Copy(abs, Path.Combine(tmp, "tutorial.md"));
                var info = new JObject
                {
                    ["name"] = Path.GetFileNameWithoutExtension(file),
                    ["creator"] = string.IsNullOrWhiteSpace(author) ? Author() : author.Trim(),
                    ["type"] = "Tutorial",
                    ["description"] = "Wiki tutorial exported from the Dev Wiki."
                };
                File.WriteAllText(Path.Combine(tmp, "info.json"), info.ToString(), Encoding.UTF8);
                var text = File.ReadAllText(abs);
                foreach (Match match in Regex.Matches(text, @"!\[[^\]]*\]\(([^)]+)\)"))
                {
                    var rel = match.Groups[1].Value.Trim();
                    if (RejectRel(Path.GetFileName(rel)) != null)
                        continue;
                    var fromPage = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(abs)!, rel));
                    var root = Path.GetFullPath(_docs) + Path.DirectorySeparatorChar;
                    if (fromPage.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(fromPage))
                    {
                        var copy = Path.Combine(tmp, Path.GetFileName(fromPage));
                        File.Copy(fromPage, copy, true);
                        RejectWritten(copy);
                    }
                }
                if (File.Exists(dest))
                    File.Delete(dest);
                ZipFile.CreateFromDirectory(tmp, dest);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
            return "";
        }

        public string DownloadTutorial(string idRaw)
        {
            return Task.Run(() => DownloadCore(idRaw)).GetAwaiter().GetResult();
        }

        public string CreateTutorial(string titleRaw, string bodyRaw)
        {
            var title = (titleRaw ?? "").Trim();
            if (title.Length == 0)
                throw new InvalidOperationException("A title is required.");
            var slug = Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (slug.Length == 0)
                slug = "tutorial";
            var rel = "Dev Wiki/Tutorials/" + slug + "/tutorial.md";
            var n = 2;
            while (File.Exists(Path.Combine(_docs, rel.Replace('/', Path.DirectorySeparatorChar))))
            {
                rel = "Dev Wiki/Tutorials/" + slug + "-" + n + "/tutorial.md";
                n++;
            }
            var abs = PageInside(rel) ?? throw new InvalidOperationException("That title cannot be used.");
            var safe = title.Replace("\r", " ").Replace("\n", " ");
            var body = string.IsNullOrWhiteSpace(bodyRaw)
                ? "---\ntitle: " + safe + "\nsummary: One line on what this page is for.\norder: 10\n---\n\nOne sentence on what the reader does here.\n\n## First step\n\nWrite the step.\n"
                : bodyRaw.Replace("\0", "");
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, body, Encoding.UTF8);
            WriteRecord(rel, new JObject { ["userMade"] = true, ["entries"] = new JArray() });
            Rebuild();
            return rel;
        }

        private string PickImageCore()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrEmpty(dlg.FileName))
                return "";
            var name = Path.GetFileName(dlg.FileName);
            var reason = RejectRel(name);
            if (reason != null)
                throw new InvalidOperationException(reason);
            var dir = Path.Combine(_docs, "_images");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, name);
            File.Copy(dlg.FileName, dest, true);
            RejectWritten(dest);
            return name;
        }

        private string DownloadCore(string idRaw)
        {
            var id = Regex.Match(idRaw ?? "", @"\d+");
            if (!id.Success)
                throw new InvalidOperationException("Enter a GameBanana mod id.");
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SSF2ModManager-Wiki");
            var profile = http.GetStringAsync("https://gamebanana.com/apiv11/Mod/" + id.Value + "?_csvProperties=_aFiles,_sName")
                .GetAwaiter().GetResult();
            var mod = JObject.Parse(profile);
            var files = mod["_aFiles"] as JArray ?? new JArray();
            string? url = null;
            foreach (var token in files)
            {
                var name = token?["_sFile"]?.ToString() ?? token?["_sDownloadUrl"]?.ToString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    url = token?["_sDownloadUrl"]?.ToString();
                    break;
                }
            }
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("That mod has no zip file.");
            var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            if (bytes.Length > 400L * 1024 * 1024)
                throw new InvalidOperationException("That archive is over 400 MB.");
            var tmp = Path.Combine(Path.GetTempPath(), "ssf2-dl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                var zipPath = Path.Combine(tmp, "tutorial.zip");
                File.WriteAllBytes(zipPath, bytes);
                var expanded = Path.Combine(tmp, "out");
                Directory.CreateDirectory(expanded);
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    if (zip.Entries.Count > 2000)
                        throw new InvalidOperationException("That archive has too many files.");
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;
                        if (entry.Length > 80L * 1024 * 1024)
                            throw new InvalidOperationException("A file in that archive is over 80 MB.");
                        var rel = entry.FullName.Replace('\\', '/');
                        if (rel.Split('/').Contains("..") || Path.IsPathRooted(entry.FullName))
                            throw new InvalidOperationException("Path escapes the folder: " + entry.FullName);
                        var target = Path.GetFullPath(Path.Combine(expanded, entry.FullName));
                        var root = Path.GetFullPath(expanded) + Path.DirectorySeparatorChar;
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Path escapes the folder: " + entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, true);
                    }
                }
                var modName = (mod["_sName"]?.ToString() ?? ("tutorial-" + id.Value));
                foreach (var ch in Path.GetInvalidFileNameChars())
                    modName = modName.Replace(ch, '_');
                modName = modName.Trim();
                if (modName.Length == 0)
                    modName = "tutorial-" + id.Value;
                var dest = Path.Combine(_docs, "Dev Wiki", "Tutorials", modName);
                var copied = CopyAllowed(expanded, dest);
                if (copied == 0)
                    throw new InvalidOperationException("The zip had no allowed tutorial files.");
                ScanFolder(dest);
                Rebuild();
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
            return "";
        }

        private int CopyAllowed(string fromDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            var copied = 0;
            foreach (var file in Directory.EnumerateFiles(fromDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(fromDir, file);
                if (RejectRel(rel) != null)
                    continue;
                var target = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
                RejectWritten(target);
                copied++;
            }
            return copied;
        }

        private static void ScanFolder(string folder)
        {
            var mp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
            if (!File.Exists(mp))
                return;
            using var proc = Process.Start(new ProcessStartInfo(mp, "-Scan -ScanType 3 -File \"" + folder + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (proc == null)
                return;
            if (!proc.WaitForExit(30000))
            {
                try { proc.Kill(); } catch { }
                return;
            }
            if (proc.ExitCode == 2)
            {
                try { Directory.Delete(folder, true); } catch { }
                throw new InvalidOperationException("Windows Defender reported a threat in the download.");
            }
        }

        private void Rebuild()
        {
            var script = Path.Combine(_docs, "_static", "build-docs.ps1");
            var psi = new ProcessStartInfo("powershell", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
            {
                WorkingDirectory = _docs,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Docs rebuild failed");
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "Docs rebuild failed" : err.Trim());
        }

        private JObject ReadRecord(string file)
        {
            var path = HistoryPath(file);
            if (!File.Exists(path))
                return new JObject { ["userMade"] = false, ["entries"] = new JArray() };
            try
            {
                var data = JToken.Parse(File.ReadAllText(path));
                if (data is JArray list)
                    return new JObject { ["userMade"] = false, ["entries"] = list };
                return new JObject
                {
                    ["userMade"] = data["userMade"]?.Value<bool>() == true,
                    ["entries"] = data["entries"] as JArray ?? new JArray()
                };
            }
            catch
            {
                return new JObject { ["userMade"] = false, ["entries"] = new JArray() };
            }
        }

        private void WriteRecord(string file, JObject record)
        {
            Directory.CreateDirectory(Path.Combine(_docs, "_history"));
            File.WriteAllText(HistoryPath(file), record.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
        }

        private string HistoryPath(string file)
        {
            return Path.Combine(_docs, "_history", Uri.EscapeDataString((file ?? "").Replace('\\', '/')) + ".json");
        }

        private string? PageInside(string file)
        {
            var rel = (file ?? "").Replace('\\', '/');
            if (!rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || rel.Split('/').Contains("..") || rel.StartsWith('_'))
                return null;
            var abs = Path.GetFullPath(Path.Combine(_docs, rel));
            var root = Path.GetFullPath(_docs) + Path.DirectorySeparatorChar;
            return abs.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? abs : null;
        }

        private static string? RejectRel(string? rel)
        {
            var name = (rel ?? "").Replace('\\', '/');
            if (name.Length == 0 || name.StartsWith('/') || name.Contains('\0') || name.Split('/').Contains(".."))
                return "Path escapes the folder: " + rel;
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (!DocExt.Contains(ext))
                return "File type is not allowed: " + rel;
            return null;
        }

        private static void RejectWritten(string filePath)
        {
            var buf = new byte[2];
            using var stream = File.OpenRead(filePath);
            var n = stream.Read(buf, 0, 2);
            if (n == 2 && buf[0] == 0x4D && buf[1] == 0x5A)
            {
                stream.Dispose();
                File.Delete(filePath);
                throw new InvalidOperationException("Blocked a Windows executable disguised as " + Path.GetFileName(filePath));
            }
        }
    }
}
