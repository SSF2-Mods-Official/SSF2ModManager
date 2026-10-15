using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: FixPoolLabels <ssf2-install-root>");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var menu = MenuSlotService.FindMenuCharacters(root)
    ?? throw new FileNotFoundException("menu_characters.swf not found");
var slots = SlotPoolService.DefaultCharacterSlots;
var progress = new Progress<string>(m => Console.WriteLine(m));
await new MenuSlotService().EnsurePoolSelectEditableLabelsAsync(menu, slots, null, progress);
return 0;
