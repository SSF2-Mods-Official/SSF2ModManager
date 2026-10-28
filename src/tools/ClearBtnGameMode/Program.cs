using System;
using System.IO;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: ClearBtnGameMode <prepared-install-root>");
    return 1;
}

var root = args[0];
if (!Directory.Exists(root))
{
    Console.Error.WriteLine("Install root not found: " + root);
    return 1;
}

var n = await new MenuSlotService().ClearBtnGameModeTextsAsync(
    root,
    jpexsPath: null,
    progress: new Progress<string>(Console.WriteLine));
Console.WriteLine(n > 0 ? $"OK: cleared {n} tag(s)." : "OK: nothing to clear.");
return 0;
