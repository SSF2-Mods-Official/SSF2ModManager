using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: PackE2E <ssf2-install-root> [--launch]");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var launch = args.Any(a => a.Equals("--launch", StringComparison.OrdinalIgnoreCase));
if (!Directory.Exists(root))
{
    Console.Error.WriteLine("Install root not found: " + root);
    return 1;
}

var progress = new Progress<string>(m => Console.WriteLine(m));
var prep = new GamePrepareService();

Console.WriteLine("=== Ensure content-pack runtime (Prepare inject-once) ===");
if (!GamePrepareService.IsPrepared(root))
{
    Console.WriteLine("Install not prepared — running Prepare…");
    await prep.PrepareAsync(root, null, progress);
}

await prep.EnsureContentPackRuntimeAsync(
    root, null, progress,
    characterSlots: SlotPoolService.DefaultCharacterSlots,
    stageSlots: 0);

if (!SlotPoolService.IsReady(root))
{
    Console.Error.WriteLine("Slot pool not ready after Ensure.");
    return 2;
}

static string Sha1(string path)
{
    using var fs = File.OpenRead(path);
    return Convert.ToHexString(SHA1.HashData(fs));
}

var menuChars = MenuSlotService.FindMenuCharacters(root)!;
var misc2 = MenuSlotService.FindMisc2(root)!;
var misc = Path.Combine(root, "data", "misc", "misc.swf");
if (!File.Exists(misc)) misc = Path.Combine(root, "data", "misc", "misc.ssf");

var hashBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["menu_characters"] = Sha1(menuChars),
    ["misc2"] = Sha1(misc2),
};
if (File.Exists(misc)) hashBefore["misc"] = Sha1(misc);

var charId = "mm_e2e_char";
var packDir = ContentPackService.PackDir(root, charId);
if (Directory.Exists(packDir))
{
    Console.WriteLine("Removing previous pack " + charId + "…");
    try { Directory.Delete(packDir, true); } catch { /* ignore */ }
}

var index = ContentPackService.LoadIndex(root);
index.CharacterSlots = index.CharacterSlots
    .Where(kv => !kv.Value.Equals(charId, StringComparison.OrdinalIgnoreCase))
    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
index.Packs = index.Packs.Where(p => !p.Equals(charId, StringComparison.OrdinalIgnoreCase)).ToList();
ContentPackService.SaveIndex(root, index);

var mappings = new MappingsEditService();
var doc = await mappings.LoadDocumentAsync(root, exportIfMissing: true, jpexsPath: null, progress: progress);
if (doc["character"] is Newtonsoft.Json.Linq.JObject chars)
{
    chars.Remove(charId);
    chars.Remove(SlotPoolService.CharacterSlotId(0));
}

var mario = Path.Combine(root, "data", "character", "mario.swf");
if (!File.Exists(mario))
{
    Console.Error.WriteLine("Missing donor SWF (mario). Did Prepare decompress data?");
    return 2;
}

Console.WriteLine("=== Add character via pack (files only) ===");
var wizard = new RosterWizardService();
var charResult = await wizard.RunCharacterWizardAsync(root, doc, new CharacterWizardRequest
{
    Mode = CharacterWizardMode.Clone,
    SourceSwfPath = mario,
    DonorId = "mario",
    Id = charId,
    DisplayName = "MM E2E Char",
    WritePackageFolder = false,
    ExportFla = false,
    AddToRandom = false,
    CloneIfExists = true
}, jpexsPath: null, progress: progress);
Console.WriteLine(charResult.Success ? "OK: " + charResult.Message : "FAIL: " + charResult.Message);
if (!charResult.Success) return 3;

Console.WriteLine("=== Host SWF mutation check (must be unchanged) ===");
var hostOk = true;
foreach (var kv in hashBefore)
{
    var path = kv.Key switch
    {
        "menu_characters" => menuChars,
        "misc2" => misc2,
        "misc" => misc,
        _ => null
    };
    if (path == null || !File.Exists(path)) continue;
    var after = Sha1(path);
    var same = after.Equals(kv.Value, StringComparison.OrdinalIgnoreCase);
    Console.WriteLine($"  {kv.Key}: {(same ? "UNCHANGED" : "MUTATED!")}");
    if (!same) hostOk = false;
}
if (!hostOk)
{
    Console.Error.WriteLine("FAIL: pack add mutated host SWFs (violates inject-once).");
    return 4;
}

var assets = Path.Combine(packDir, "assets");
Console.WriteLine("Pack assets:");
if (Directory.Exists(assets))
{
    foreach (var f in Directory.GetFiles(assets))
        Console.WriteLine("  " + Path.GetFileName(f) + " " + new FileInfo(f).Length + " bytes");
}
else Console.WriteLine("  (none)");

var costumes = Path.Combine(packDir, "costumes.json");
Console.WriteLine("costumes.json: " + (File.Exists(costumes) ? new FileInfo(costumes).Length + " bytes" : "MISSING"));
Console.WriteLine("live.json: " + File.Exists(Path.Combine(ContentPackService.RootDir(root), "live.json")));
Console.WriteLine("Content: " + File.Exists(ContentPackService.ContentFullPath(root, charId)));
Console.WriteLine("Resource pack path (ssf2mm/): " +
    GamePrepareService.SwfContainsAscii(Path.Combine(root, "SSF2.swf"), "ssf2mm/"));
// Comment markers are stripped by FFDec; durable check is a live helper name.
Console.WriteLine("Live API (ssf2mmFindPackForUiLink): " +
    GamePrepareService.SwfContainsAscii(Path.Combine(root, "SSF2.swf"), "ssf2mmFindPackForUiLink"));

if (launch)
    Console.WriteLine("Launch SSF2-Debug.bat yourself and test CSS + match.");

Console.WriteLine("PASS: inject-once pack character rebuilt.");
return 0;
