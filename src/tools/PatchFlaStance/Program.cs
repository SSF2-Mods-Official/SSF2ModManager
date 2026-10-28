using System.IO;
using System.IO.Compression;
using SSF2ModManager.Services;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PatchFlaStance <path-to.fla> <baseName> [scriptRoot]");
    return 1;
}

var fla = args[0];
var baseName = args[1];
var scriptRoot = args.Length > 2 ? args[2] : Path.GetDirectoryName(fla)!;

if (!File.Exists(fla))
{
    Console.Error.WriteLine("FLA not found: " + fla);
    return 2;
}

Console.WriteLine("FLA: " + fla);
Console.WriteLine("base: " + baseName);
Console.WriteLine("scripts: " + scriptRoot);

var rebuilt = JpexsCliService.RebuildTimelineFlaAnimations(fla, baseName, scriptRoot);
var stripped = JpexsCliService.StripAllTimelineFlaExportLinkage(fla, baseName);
var sanitized = JpexsCliService.SanitizeFlaTimelineSelfBindings(fla);
Console.WriteLine($"rebuilt={rebuilt} stripped={stripped} sanitized={sanitized}");

var flaLink = 0;
var other = 0;
using (var zip = ZipFile.OpenRead(fla))
{
    foreach (var entry in zip.Entries)
    {
        if (!entry.FullName.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
            !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            continue;
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();
        var m = System.Text.RegularExpressions.Regex.Match(xml, @"linkageClassName=""([^""]+)""");
        if (!m.Success || xml.IndexOf("linkageExportForAS=\"true\"", StringComparison.Ordinal) < 0)
            continue;
        if (m.Groups[1].Value.Contains("_fla.", StringComparison.Ordinal))
            flaLink++;
        else
            other++;
    }
}

Console.WriteLine($"linkage remaining: _fla={flaLink} other={other}");
return flaLink == 0 ? 0 : 3;
