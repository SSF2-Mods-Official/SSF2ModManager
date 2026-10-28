using System;
using System.IO;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: PrepareInstall <ssf2-install-root>");
    return 1;
}

var root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root))
{
    Console.Error.WriteLine("Install root not found: " + root);
    return 1;
}

Console.WriteLine("Preparing: " + root);
var svc = new GamePrepareService();
var progress = new Progress<string>(m => Console.WriteLine(m));

try
{
    if (GamePrepareService.IsPrepared(root))
    {
        Console.WriteLine("Already prepared — ensuring content-pack runtime…");
        await svc.EnsureContentPackRuntimeAsync(
            root, jpexsPath: null, progress,
            characterSlots: SlotPoolService.DefaultCharacterSlots,
            stageSlots: 0);
        Console.WriteLine("OK: content pack runtime ensured.");
        return 0;
    }

    var result = await svc.PrepareAsync(root, jpexsPath: null, progress);
    Console.WriteLine(result.Success ? "OK: " + result.Message : "FAIL: " + result.Message);
    if (result.Errors is { Count: > 0 })
    {
        foreach (var e in result.Errors)
            Console.WriteLine("  error: " + e);
    }

    Console.WriteLine("Prepared marker: " + GamePrepareService.IsPrepared(root));
    Console.WriteLine("Slot pool ready: " + SlotPoolService.IsReady(root));
    Console.WriteLine("Debug console: " + GamePrepareService.GetDebugConsolePatchStatus(root));
    return result.Success ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 3;
}
