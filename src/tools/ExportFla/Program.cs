using System.IO;
using SSF2ModManager.Services;

if (args.Length >= 2 && args[0] == "--sanitize")
{
    JpexsCliService.ApplyFlaVanillaExportPatches(args[1], Path.GetFileNameWithoutExtension(args[1]) ?? "");
    var n = JpexsCliService.SanitizeFlaTimelineSelfBindings(args[1]);
    Console.WriteLine("patched symbols: " + n);
    return 0;
}

if (args.Length >= 2 && args[0] == "--blueprint")
{
    var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(args[1]);
    var dest = args.Length >= 3
        ? args[2]
        : Path.Combine(Path.GetDirectoryName(args[1]) ?? "", "tools", "ssf2mm_organize.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
    File.WriteAllText(dest, tsv);
    Console.WriteLine("wrote " + dest + " (" + tsv.Split('\n').Length + " rows)");
    return 0;
}

if (args.Length >= 2 && args[0] == "--pcm")
{
    var n = JpexsCliService.ConvertFlaSoundBinsToPcm(args[1]);
    JpexsCliService.ApplyFlaVanillaExportPatches(args[1], Path.GetFileNameWithoutExtension(args[1]) ?? "");
    Console.WriteLine("pcm sounds: " + n);
    return n > 0 ? 0 : 5;
}

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: ExportFla <ffdec.jar> <in.swf> <outDir> [baseName]");
    Console.Error.WriteLine("       ExportFla --sanitize <file.fla>");
    Console.Error.WriteLine("       ExportFla --pcm <file.fla>");
    return 1;
}

var ffdec = args[0];
var swf = args[1];
var outDir = args[2];
var baseName = args.Length > 3 ? args[3] : Path.GetFileNameWithoutExtension(swf);

if (!File.Exists(ffdec))
{
    Console.Error.WriteLine("FFDec not found: " + ffdec);
    return 2;
}
if (!File.Exists(swf))
{
    Console.Error.WriteLine("SWF not found: " + swf);
    return 3;
}

Directory.CreateDirectory(outDir);
Console.WriteLine("SWF: " + swf);
Console.WriteLine("out: " + outDir);
Console.WriteLine("base: " + baseName);

var progress = new Progress<string>(msg => Console.WriteLine(msg));
await new JpexsCliService().ExportFlaAsync(ffdec, swf, outDir, "cs6", CancellationToken.None, baseName, progress);

var fla = Path.Combine(outDir, baseName + ".fla");
var wizard = Path.Combine(outDir, "FlaStepWizard.exe");
Console.WriteLine(File.Exists(fla) ? "FLA OK " + fla : "FLA MISSING");
Console.WriteLine(File.Exists(wizard) ? "wizard OK " + wizard : "wizard MISSING");
return File.Exists(fla) ? 0 : 4;
