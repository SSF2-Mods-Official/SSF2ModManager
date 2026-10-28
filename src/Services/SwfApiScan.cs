using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Reads a mod SWF and refuses network calls, a second SWF, and a file write.
    /// A file that is not a readable SWF is refused. A banned name split across two
    /// nearby strings is refused too.
    /// </summary>
    public static class SwfApiScan
    {
        public static readonly string[] Refused =
        [
            "URLLoader",
            "URLRequest",
            "URLStream",
            "navigateToURL",
            "Socket",
            "ExternalInterface",
            "FileReference",
            "getDefinitionByName",
            "ApplicationDomain",
            "loadBytes",
            "FileStream",
            "FileMode",
            "flash.filesystem",
            "fromCharCode"
        ];

        /// <summary>Changes when the refused names change, so an old pass is scanned again.</summary>
        public static readonly string RulesId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", Refused)))).ToLowerInvariant();

        public static IReadOnlyList<string> RefusedIn(string path) =>
            RefusedIn(File.ReadAllBytes(path));

        public static IReadOnlyList<string> RefusedIn(byte[] bytes)
        {
            var found = new List<string>();
            Scan(bytes, found, 0);
            return found.Distinct(StringComparer.Ordinal).ToList();
        }

        public static byte[] Decompress(byte[] bytes)
        {
            if (!TryPlain(bytes, out var plain))
                return bytes;
            return plain;
        }

        private static void Scan(byte[] bytes, List<string> found, int depth)
        {
            if (!TryPlain(bytes, out var plain))
            {
                found.Add("unreadable");
                return;
            }
            Note(plain, found);
            if (depth >= 2)
                return;
            for (var i = 8; i + 3 < plain.Length; i++)
            {
                if (!IsSwfMark(plain, i))
                    continue;
                var nested = new byte[plain.Length - i];
                Buffer.BlockCopy(plain, i, nested, 0, nested.Length);
                var before = found.Count;
                Scan(nested, found, depth + 1);
                if (found.Count > before)
                    return;
            }
        }

        private static void Note(byte[] plain, List<string> found)
        {
            var text = Encoding.UTF8.GetString(plain);
            foreach (var name in Refused)
            {
                if (text.Contains(name, StringComparison.Ordinal))
                    found.Add(name);
            }
            var runs = Runs(plain);
            for (var i = 0; i < runs.Count; i++)
            {
                for (var j = i + 1; j < runs.Count && runs[j].Start - runs[i].End <= 8; j++)
                {
                    var joined = runs[i].Text + runs[j].Text;
                    foreach (var name in Refused)
                    {
                        if (joined.Contains(name, StringComparison.Ordinal)
                            && !runs[i].Text.Contains(name, StringComparison.Ordinal)
                            && !runs[j].Text.Contains(name, StringComparison.Ordinal))
                            found.Add(name);
                    }
                }
            }
        }

        private static bool TryPlain(byte[] bytes, out byte[] plain)
        {
            plain = bytes;
            if (bytes.Length < 3)
                return false;
            var kind = Encoding.ASCII.GetString(bytes, 0, 3);
            if (kind == "FWS")
                return true;
            if (kind == "ZWS" || bytes.Length < 8 || kind != "CWS")
                return false;
            try
            {
                using var input = new MemoryStream(bytes, 8, bytes.Length - 8);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                plain = output.ToArray();
                return plain.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSwfMark(byte[] bytes, int i) =>
            (bytes[i] == (byte)'F' || bytes[i] == (byte)'C' || bytes[i] == (byte)'Z')
            && bytes[i + 1] == (byte)'W' && bytes[i + 2] == (byte)'S';

        private readonly struct Run
        {
            public Run(int start, int end, string text)
            {
                Start = start;
                End = end;
                Text = text;
            }

            public int Start { get; }
            public int End { get; }
            public string Text { get; }
        }

        private static List<Run> Runs(byte[] bytes)
        {
            var runs = new List<Run>();
            var i = 0;
            while (i < bytes.Length)
            {
                if (bytes[i] < 32 || bytes[i] > 126)
                {
                    i++;
                    continue;
                }
                var start = i;
                while (i < bytes.Length && bytes[i] >= 32 && bytes[i] <= 126)
                    i++;
                if (i - start >= 2 && i - start <= 80)
                    runs.Add(new Run(start, i, Encoding.ASCII.GetString(bytes, start, i - start)));
            }
            return runs;
        }
    }
}
