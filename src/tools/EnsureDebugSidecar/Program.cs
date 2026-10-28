using System;
using System.IO;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: EnsureDebugSidecar <ssf2-install-root> [--with-char-pool]");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var withPool = args.Any(a => a.Equals("--with-char-pool", StringComparison.OrdinalIgnoreCase));
var progress = new Progress<string>(m => Console.WriteLine(m));

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

var svc = new GamePrepareService();
await svc.EnsureDebugSidecarAsync(root, null, progress);

if (withPool)
{
    await svc.EnsureContentPackRuntimeAsync(
        root, null, progress,
        characterSlots: SlotPoolService.DefaultCharacterSlots,
        stageSlots: 0);
}

Console.WriteLine("Debug host present: " + File.Exists(Path.Combine(root, GamePrepareService.DebugHostExeName)));
var swfBytes = File.ReadAllBytes(Path.Combine(root, "SSF2.swf"));
var needle = System.Text.Encoding.ASCII.GetBytes(GamePrepareService.DebugHostSpawnMarker);
var hasSpawn = false;
for (var i = 0; i <= swfBytes.Length - needle.Length; i++)
{
    var ok = true;
    for (var j = 0; j < needle.Length; j++)
    {
        if (swfBytes[i + j] != needle[j]) { ok = false; break; }
    }
    if (ok) { hasSpawn = true; break; }
}
Console.WriteLine("Spawn marker in SWF: " + hasSpawn);
Console.WriteLine("ADL: " + (GamePrepareService.ResolveAdlPath() ?? "(not found)"));
Console.WriteLine("OK.");
return 0;
