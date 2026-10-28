using MergeFlaLayers;

var interactive = args.Length == 0;
try
{
    var fla = ResolveFla(args);
    if (fla == null)
    {
        Fail("No .fla found next to this exe (skipped RECOVER_*.fla). Pass a path if needed.", interactive);
        return 2;
    }

    Console.WriteLine("FLA: " + fla);
    if (IsLikelyLocked(fla))
    {
        Fail("File looks locked. Close Animate (and any RECOVER dialog) then run again.", interactive);
        return 3;
    }

    var result = FlaBoxLayerMerge.Run(fla);
    if (!result.Ok)
    {
        Fail(result.Message, interactive);
        return 1;
    }

    Console.WriteLine("SUCCESS");
    Console.WriteLine(result.Message);
    if (!string.IsNullOrEmpty(result.BackupPath))
        Console.WriteLine("Backup: " + result.BackupPath);
    PauseIfInteractive(interactive);
    return 0;
}
catch (Exception ex)
{
    Fail(ex.Message, interactive);
    return 1;
}

static string? ResolveFla(string[] args)
{
    if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        return Path.GetFullPath(args[0]);

    var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return PickFlaIn(dir) ?? PickFlaIn(Directory.GetCurrentDirectory());
}

static string? PickFlaIn(string dir)
{
    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        return null;
    string[] files;
    try { files = Directory.GetFiles(dir, "*.fla"); }
    catch { return null; }
    var usable = files
        .Where(f => !Path.GetFileName(f).StartsWith("RECOVER_", StringComparison.OrdinalIgnoreCase))
        .ToList();
    var named = usable.FirstOrDefault(f =>
        Path.GetFileName(f).Equals("Gah_Oku.fla", StringComparison.OrdinalIgnoreCase));
    return named ?? usable.FirstOrDefault();
}

static bool IsLikelyLocked(string path)
{
    try
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        return false;
    }
    catch (IOException)
    {
        return true;
    }
}

static void Fail(string message, bool interactive)
{
    Console.WriteLine("FAIL");
    Console.WriteLine(message);
    PauseIfInteractive(interactive);
}

static void PauseIfInteractive(bool interactive)
{
    if (!interactive)
        return;
    Console.WriteLine();
    Console.WriteLine("Press Enter to close");
    try { Console.ReadLine(); } catch { /* redirected */ }
}
