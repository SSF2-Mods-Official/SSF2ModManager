using System;
using System.IO;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: EnsureMenuScript <menu_characters.swf|install-root> <donorId> <newId>");
    return 1;
}

var target = args[0];
var donorId = args[1];
var newId = args[2];
var swf = File.Exists(target)
    ? target
    : MenuSlotService.FindMenuCharacters(target)
      ?? throw new FileNotFoundException("menu_characters.swf not found: " + target);

Console.WriteLine("SWF: " + swf);
var result = await new MenuSlotService().EnsureLinkageScriptAsync(
    swf,
    donorId,
    newId,
    "_select",
    "characterID",
    jpexsPath: null,
    progress: new Progress<string>(Console.WriteLine));
Console.WriteLine(result.Success ? result.Message : ("FAILED: " + result.Message));
return result.Success ? 0 : 2;
