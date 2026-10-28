using System;
using System.IO;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: CloneCharacterOnly <ssf2-install-root> [new-id] [display-name] [donor-id]");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var newId = args.Length > 1 ? MenuSlotService.SanitizeId(args[1]) : "mm_clone_char";
var displayName = args.Length > 2 ? args[2] : "MM Clone";
var donorId = args.Length > 3 ? MenuSlotService.SanitizeId(args[3]) : "mario";
var progress = new Progress<string>(m => Console.WriteLine(m));

if (!Directory.Exists(root))
{
    Console.Error.WriteLine("Install not found: " + root);
    return 1;
}

if (SlotPoolService.IsReady(root))
{
    Console.Error.WriteLine(
        "Slot pool is ready on this install — refusing to run (character-clone-only mode). " +
        "Use a fresh install without content-pack runtime.");
    return 2;
}

if (!GamePrepareService.IsPrepared(root))
{
    Console.WriteLine("Preparing install (no content-pack runtime)…");
    var prep = new GamePrepareService();
    var prepResult = await prep.PrepareAsync(root, null, progress);
    Console.WriteLine(prepResult.Success ? "Prepare OK: " + prepResult.Message : "Prepare FAIL: " + prepResult.Message);
    if (!prepResult.Success) return 3;
}

if (SlotPoolService.IsReady(root))
{
    Console.Error.WriteLine("Prepare unexpectedly built a slot pool. Aborting.");
    return 2;
}

var donorSwf = Path.Combine(root, "data", "character", donorId + ".swf");
if (!File.Exists(donorSwf))
{
    Console.Error.WriteLine("Donor SWF missing: " + donorSwf);
    return 4;
}

Console.WriteLine($"Cloning {donorId} → {newId} (\"{displayName}\") via classic wizard…");
var mappings = new MappingsEditService();
var doc = await mappings.LoadDocumentAsync(root, true, null, progress);
var wizard = new RosterWizardService();
var result = await wizard.RunCharacterWizardAsync(root, doc, new CharacterWizardRequest
{
    Mode = CharacterWizardMode.Clone,
    SourceSwfPath = donorSwf,
    DonorId = donorId,
    Id = newId,
    DisplayName = displayName,
    WritePackageFolder = true,
    ExportFla = false,
    AddToRandom = false,
    CloneIfExists = true
}, null, progress);

Console.WriteLine(result.Success ? "OK: " + result.Message : "FAIL: " + result.Message);
if (!string.IsNullOrWhiteSpace(result.Warning))
    Console.WriteLine("Warning: " + result.Warning);

Console.WriteLine("Slot pool ready: " + SlotPoolService.IsReady(root));
return result.Success ? 0 : 5;
