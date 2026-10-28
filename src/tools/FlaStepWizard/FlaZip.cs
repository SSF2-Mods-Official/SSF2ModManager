using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FlaStepWizard;

internal static class FlaZip
{
    public static Dictionary<string, byte[]> Read(string flaPath)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(flaPath);
        foreach (var entry in zip.Entries)
        {
            var key = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(entry.Name))
            {
                map[key] = Array.Empty<byte>();
                continue;
            }
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            map[key] = ms.ToArray();
        }
        return map;
    }

    public static void Write(string flaPath, Dictionary<string, byte[]> entries)
    {
        var tmp = flaPath + ".tmp";
        if (File.Exists(tmp))
            File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var kv in entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                var created = zip.CreateEntry(kv.Key.Replace('\\', '/'), CompressionLevel.Fastest);
                using var dest = created.Open();
                dest.Write(kv.Value, 0, kv.Value.Length);
            }
        }
        File.Copy(tmp, flaPath, overwrite: true);
        try { File.Delete(tmp); } catch { /* ignore */ }
    }

    public static string Utf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);
    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    public static XDocument? Parse(string xml)
    {
        try { return XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
        catch { return null; }
    }

    public static string Save(XDocument doc) =>
        doc.Declaration == null
            ? doc.ToString(SaveOptions.DisableFormatting)
            : doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
}
