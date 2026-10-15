using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace SSF2ModManager.Services;

internal static class DebugAgentLog
{
    private const string SessionId = "968d48";
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ssf2mm-debug.log");

    public static void Write(string hypothesisId, string location, string message, object? data)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = SessionId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["hypothesisId"] = hypothesisId,
                ["location"] = location,
                ["message"] = message,
                ["data"] = data,
                ["runId"] = "clone-fla"
            };
            File.AppendAllText(LogPath, JsonConvert.SerializeObject(payload) + "\n", Encoding.UTF8);
        }
        catch
        {
            /* ignore */
        }
    }

    public static object ScanSwf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new { path, exists = false };
        var bytes = File.ReadAllBytes(path);
        var ascii = Encoding.ASCII.GetString(bytes);
        return new
        {
            path,
            exists = true,
            size = bytes.Length,
            lastWriteUtc = File.GetLastWriteTimeUtc(path).ToString("o"),
            jabCombo_25 = ascii.Contains("JabCombo_25", StringComparison.Ordinal),
            symbol1545_25 = ascii.Contains("Symbol1545_25", StringComparison.Ordinal),
            checkForGoToJab2 = ascii.Contains("checkForGoToJab2", StringComparison.Ordinal),
            checkControls = ascii.Contains("checkControls", StringComparison.Ordinal),
            xSpeedDecay = ascii.Contains("xSpeedDecay", StringComparison.Ordinal)
        };
    }

    public static object ScanFlaAndLeftovers(string flaPath, string flaDir)
    {
        var leftoverHits = 0;
        var leftoverFiles = 0;
        if (Directory.Exists(flaDir))
        {
            foreach (var pkg in Directory.GetDirectories(flaDir, "*_fla", SearchOption.AllDirectories))
            {
                foreach (var asFile in Directory.GetFiles(pkg, "*.as"))
                {
                    leftoverFiles++;
                    var text = File.ReadAllText(asFile);
                    if (text.Contains("function checkControls", StringComparison.Ordinal) ||
                        text.Contains("function checkForGoToJab2", StringComparison.Ordinal))
                        leftoverHits++;
                }
            }
        }

        var libHasFnCheckControls = false;
        var libHasCreateTimerCheckControls = false;
        if (File.Exists(flaPath))
        {
            using var zip = ZipFile.OpenRead(flaPath);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var reader = new StreamReader(entry.Open());
                var xml = reader.ReadToEnd();
                if (xml.Contains("function checkControls", StringComparison.Ordinal))
                    libHasFnCheckControls = true;
                if (xml.Contains("checkControls", StringComparison.Ordinal) &&
                    xml.Contains("createTimer", StringComparison.Ordinal))
                    libHasCreateTimerCheckControls = true;
            }
        }

        return new
        {
            flaPath,
            flaExists = File.Exists(flaPath),
            leftoverAsFiles = leftoverFiles,
            leftoverAsWithJabHelpers = leftoverHits,
            flaLibraryHasFunctionCheckControls = libHasFnCheckControls,
            flaLibraryHasCreateTimerCheckControls = libHasCreateTimerCheckControls
        };
    }
}
