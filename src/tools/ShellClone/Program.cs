using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SSF2ModManager.Services;

static string FindRepo()
{
    static string? Scan(DirectoryInfo? dir)
    {
        for (var i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "SSF2 Mod Manager")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    return Scan(new DirectoryInfo(Directory.GetCurrentDirectory()))
        ?? Scan(new DirectoryInfo(AppContext.BaseDirectory))
        ?? throw new DirectoryNotFoundException("Could not find the SSF2 Mod Manager folder.");
}

var skipFla = args.Any(a => a.Equals("--skip-fla", StringComparison.OrdinalIgnoreCase));
var rootFlag = Array.FindIndex(args, a => a.Equals("--root", StringComparison.OrdinalIgnoreCase));
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
if (rootFlag >= 0 && rootFlag + 1 < args.Length)
    positional = positional.Where(a => !a.Equals(args[rootFlag + 1], StringComparison.Ordinal)).ToArray();
var repo = FindRepo();
var mm = Path.Combine(repo, "SSF2 Mod Manager");
var build = rootFlag >= 0 && rootFlag + 1 < args.Length
    ? args[rootFlag + 1]
    : Path.Combine(mm, "SSF2 Shell", "build");
var ffdec = Path.Combine(mm, "ffdec", "ffdec.jar");
if (!File.Exists(ffdec))
{
    var bundled = AppPaths.FfdecDirectory;
    if (!string.IsNullOrWhiteSpace(bundled))
        ffdec = Path.Combine(bundled, "ffdec.jar");
}
var sourceId = positional.Length > 0 ? positional[0] : "bandanadee";
var folderName = positional.Length > 1 ? positional[1] : "Clone Dee";

if (!Directory.Exists(build))
{
    Console.Error.WriteLine("Shell build not found: " + build);
    return 1;
}
if (!File.Exists(ffdec))
{
    Console.Error.WriteLine("FFDec not found: " + ffdec);
    return 1;
}

var dest = Path.Combine(ShellWorkspaceService.ExpansionsDirectory(build), folderName);
if (Directory.Exists(dest))
{
    Console.WriteLine("Removing previous folder: " + dest);
    ShellCharacterCloneService.TryDeleteDirectory(dest);
}

Console.WriteLine($"Cloning {sourceId} → folder \"{folderName}\" (id {ShellCharacterCloneService.ExpansionGameId(folderName)})");
Console.WriteLine("Install: " + build);
Console.WriteLine("FFDec: " + ffdec);
Console.WriteLine(skipFla ? "FLA: skipped" : "FLA: always (CS6 XFL)");

var progress = new Progress<string>(m => Console.WriteLine("  " + m));
var svc = new ShellCharacterCloneService();
try
{
    var result = await svc.CloneIntoShellAsync(
        build, sourceId, folderName, folderName, ffdec, progress, skipFla: skipFla);
    Console.WriteLine("OK folder: " + result.ExpansionFolder);
    Console.WriteLine("OK swf: " + result.FighterSwfPath);
    foreach (var note in result.Notes)
        Console.WriteLine("  note: " + note);
    return File.Exists(result.FighterSwfPath) ? 0 : 6;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 5;
}
