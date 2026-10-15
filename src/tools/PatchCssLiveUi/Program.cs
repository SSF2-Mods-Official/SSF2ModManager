using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: PatchCssLiveUi <ssf2-install-root>");
    return 1;
}

var root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root))
{
    Console.Error.WriteLine("Install not found: " + root);
    return 1;
}

if (!GamePrepareService.IsPrepared(root))
{
    Console.Error.WriteLine("Install is not prepared.");
    return 2;
}

var progress = new Progress<string>(m => Console.WriteLine(m));
var menu = MenuSlotService.FindMenuCharacters(root);
if (menu == null)
{
    Console.Error.WriteLine("menu_characters.swf not found.");
    return 4;
}

var slots = SlotPoolService.DefaultCharacterSlots;
var readyPath = SlotPoolService.ReadyPath(root);
if (File.Exists(readyPath))
{
    try
    {
        var jo = JObject.Parse(File.ReadAllText(readyPath));
        slots = jo["characterSlots"]?.Value<int>() ?? slots;
    }
    catch { /* keep default */ }
}

var menuSlots = new MenuSlotService();
await menuSlots.EnsurePoolSelectEditableLabelsAsync(menu, slots, null, progress);

// Mark pool at current UI version so Prepare won't thrash hosts again.
if (File.Exists(readyPath))
{
    try
    {
        var jo = JObject.Parse(File.ReadAllText(readyPath));
        jo["uiPoolVersion"] = SlotPoolService.UiPoolVersion;
        jo["editableLabelsAt"] = DateTimeOffset.UtcNow.ToString("o");
        await File.WriteAllTextAsync(readyPath, jo.ToString(Newtonsoft.Json.Formatting.Indented));
        Console.WriteLine($"Updated {readyPath} → uiPoolVersion={SlotPoolService.UiPoolVersion}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Warning: could not bump slot-pool-ready.json: " + ex.Message);
    }
}

var svc = new GamePrepareService();
var result = await svc.PatchContentPackRuntimeOnInstallAsync(root, null, progress);
Console.WriteLine(result.Success ? ("OK: " + result.Message) : ("FAIL: " + result.Message));
return result.Success ? 0 : 3;
