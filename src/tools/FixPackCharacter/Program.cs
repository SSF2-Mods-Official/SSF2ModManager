using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: FixPackCharacter <ssf2-install-root> <pack-id>");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var packId = MenuSlotService.SanitizeId(args[1]);
var progress = new Progress<string>(m => Console.WriteLine(m));

var pack = ContentPackService.LoadPack(root, packId)
    ?? throw new FileNotFoundException("Pack not found: " + packId);
if (pack.Type != ContentPackType.Character || pack.Slot < 0)
{
    Console.Error.WriteLine("Not a claimed character pack.");
    return 2;
}

var rosterId = SlotPoolService.CharacterSlotId(pack.Slot);
var playable = string.IsNullOrWhiteSpace(pack.PlayableId) ? pack.PackId : pack.PlayableId;
var displayName = string.IsNullOrWhiteSpace(pack.Name) ? playable : pack.Name;
var chipLabel = MenuSlotService.BuildSingleLineChipLabel(displayName);
var donorId = "mario";

var menuChars = MenuSlotService.FindMenuCharacters(root)
    ?? throw new FileNotFoundException("menu_characters.swf not found.");
var menu = new MenuSlotService();
var assets = new CharacterAssetService();

Console.WriteLine($"Fixing {packId}: roster={rosterId} playable={playable} label=\"{chipLabel}\"");

await menu.CloneCssSelectJobsAsync(
    menuChars,
    new List<(string DonorId, string AliasId, string PlayableId, string ChipLabel)>
    {
        (donorId, rosterId, playable, chipLabel)
    },
    null, progress);

await assets.EnsureUiSlotsFromDonorAsync(
    root, playable, donorId, null, progress,
    displayName: displayName,
    seriesIcon: string.IsNullOrWhiteSpace(pack.SeriesIcon) ? "smash_symbol" : pack.SeriesIcon,
    skipCssSelect: true);

var mappings = new MappingsEditService();
var doc = await mappings.LoadDocumentAsync(root, true, null, progress);
var packs = new ContentPackService();
await packs.CommitPacksAsync(root, doc, null, progress);

Console.WriteLine("OK: character pack UI repaired.");
return 0;
