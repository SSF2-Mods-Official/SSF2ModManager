using System.Diagnostics;
using System.IO;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Checks a downloaded archive before its entries are written.
    /// Allows game and doc files. Rejects executables, path escapes, and oversized entries.
    /// After extract, asks Windows Defender to scan the folder when MpCmdRun is present.
    /// </summary>
    public static class ArchiveExtractGuard
    {
        public const long MaxEntryBytes = 80L * 1024 * 1024;
        public const long MaxTotalBytes = 400L * 1024 * 1024;
        public const int MaxEntries = 2000;

        private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            ".swf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".json", ".xml", ".txt", ".md",
            ".mp3", ".wav", ".ogg", ".flv", ".dat", ".csv", ".as", ".fla", ".anm", ".svg"
        };

        public static string? Reject(string entryKey, long uncompressedBytes, ref long totalBytes, ref int count)
        {
            count++;
            if (count > MaxEntries)
                return "Archive has more than " + MaxEntries + " files.";
            if (uncompressedBytes < 0 || uncompressedBytes > MaxEntryBytes)
                return "Entry is larger than the allowed size: " + entryKey;
            totalBytes += uncompressedBytes;
            if (totalBytes > MaxTotalBytes)
                return "Archive uncompressed size is over the limit.";

            var name = entryKey.Replace('/', Path.DirectorySeparatorChar);
            if (name.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                name.StartsWith("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(name))
                return "Entry path escapes the folder: " + entryKey;

            var ext = Path.GetExtension(name);
            if (string.IsNullOrEmpty(ext) || !Allowed.Contains(ext))
                return "File type is not allowed: " + entryKey;
            return null;
        }

        public static void RejectWritten(string path)
        {
            if (!File.Exists(path))
                return;
            using var fs = File.OpenRead(path);
            var b = new byte[2];
            if (fs.Read(b, 0, 2) == 2 && b[0] == (byte)'M' && b[1] == (byte)'Z')
            {
                fs.Dispose();
                File.Delete(path);
                throw new InvalidOperationException("Blocked a Windows executable disguised as " + Path.GetFileName(path));
            }
        }

        public static void ScanFolder(string folder)
        {
            var mp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
            if (!File.Exists(mp) || !Directory.Exists(folder))
                return;
            var psi = new ProcessStartInfo(mp, "-Scan -ScanType 3 -File \"" + folder + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return;
            if (!proc.WaitForExit(30_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return;
            }
            if (proc.ExitCode == 2)
                throw new InvalidOperationException("Windows Defender reported a threat in the download.");
        }
    }
}
