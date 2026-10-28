using SSF2ModManager.Services;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: RecloneSsf <source.ssf> <oldName> <newName> <outputDir> [ffdecDir] [shellRoot]");
    return 1;
}

var source = args[0];
var oldName = args[1];
var newName = args[2];
var outDir = args[3];
var ffdec = args.Length > 4 ? args[4] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "ffdec"));
var shellRoot = args.Length > 5 ? args[5] : null;

Directory.CreateDirectory(outDir);
var progress = new Progress<string>(m => Console.WriteLine(m));
var svc = new CharacterCloneService();
var result = await svc.CloneAsync(new CharacterCloneRequest
{
    SourcePath = source,
    OldName = oldName,
    NewName = newName,
    OutputDirectory = outDir,
    DisplayName = null,
    JpexsPath = Directory.Exists(ffdec) ? ffdec : null,
    TakenNames = string.IsNullOrWhiteSpace(shellRoot)
        ? null
        : ShellCharacterCloneService.CollectTakenSymbolNames(shellRoot)
}, progress);

Console.WriteLine("OUTPUT=" + result.OutputSwfPath);
Console.WriteLine("HITS=" + result.XmlReplacements);
foreach (var note in result.Notes)
    Console.WriteLine("NOTE=" + note);
return 0;
