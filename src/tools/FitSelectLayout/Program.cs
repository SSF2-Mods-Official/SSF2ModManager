using System;
using System.IO;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: FitSelectLayout <prepared-install-root>");
    return 1;
}

var root = args[0];
if (!Directory.Exists(root))
{
    Console.Error.WriteLine("Install root not found: " + root);
    return 1;
}

var svc = new MappingsEditService();
Console.WriteLine("Loading mappings…");
var doc = await svc.LoadDocumentAsync(root, exportIfMissing: true);
var cssRows = MappingsEditService.GetCssRows(doc);
var sssRows = MappingsEditService.GetSssNormalRows(doc);

var cssMsg = MappingsEditService.FitSelectLayout(doc, isCss: true, cssRows);
var sssMsg = MappingsEditService.FitSelectLayout(doc, isCss: false, sssRows);

Console.WriteLine(cssMsg ?? "CSS: already fits (no change).");
Console.WriteLine(sssMsg ?? "SSS: already fits (no change).");

Console.WriteLine("Saving + applying to mappings.swf…");
await svc.CommitAsync(root, doc, null, new Progress<string>(m => Console.WriteLine("  " + m)));
Console.WriteLine("Done.");
return 0;
