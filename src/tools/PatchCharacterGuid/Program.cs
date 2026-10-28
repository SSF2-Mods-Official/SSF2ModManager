using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PatchCharacterGuid <character.swf> <guid>");
    Console.Error.WriteLine("   or: PatchCharacterGuid <install-root> <characterId>");
    return 1;
}

var a0 = args[0];
var a1 = args[1];
string swf;
string guid;

if (File.Exists(a0) && a0.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
{
    swf = a0;
    guid = a1;
}
else
{
    var root = a0;
    var id = a1;
    swf = Path.Combine(root, "data", "character", id + ".swf");
    if (!File.Exists(swf))
    {
        Console.Error.WriteLine("SWF not found: " + swf);
        return 1;
    }
    var jsonPath = Path.Combine(root, "data", "misc", "mappings.json");
    if (!File.Exists(jsonPath))
    {
        Console.Error.WriteLine("mappings.json not found: " + jsonPath);
        return 1;
    }
    var doc = JObject.Parse(await File.ReadAllTextAsync(jsonPath));
    guid = doc["character"]?[id]?["guid"]?.ToString()
        ?? throw new InvalidOperationException($"No guid for character.{id} in mappings.");
}

Console.WriteLine($"SWF:  {swf}");
Console.WriteLine($"GUID: {guid}");
await new CharacterCloneService().PatchGuidInSwfAsync(
    swf, guid, jpexsPath: null, progress: new Progress<string>(Console.WriteLine));
Console.WriteLine("OK");
return 0;
