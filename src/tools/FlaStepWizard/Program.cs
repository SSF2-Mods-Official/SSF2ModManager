using FlaStepWizard;

if (HasFlag(args, "--diff-inventory"))
{
    var files = args.Where(a => !a.StartsWith("-", StringComparison.Ordinal)).ToList();
    if (files.Count < 2)
    {
        Console.WriteLine("FAIL");
        Console.WriteLine("Usage: --diff-inventory before.json after.json");
        return 2;
    }
    var before = FlaInventory.FromJson(File.ReadAllText(files[0]));
    var after = FlaInventory.FromJson(File.ReadAllText(files[1]));
    var issues = FlaInventory.Diff(before, after);
    if (issues.Count == 0)
    {
        Console.WriteLine("SUCCESS");
        Console.WriteLine("No missing, extra, or misplaced libs, box names, families, or box placements.");
        return 0;
    }
    Console.WriteLine("FAIL");
    Console.WriteLine(issues.Count + " inventory regression(s):");
    foreach (var issue in issues)
        Console.WriteLine("  " + issue);
    return 1;
}

var fla = ResolveFla(args);
if (fla == null)
{
    Console.WriteLine("FAIL");
    Console.WriteLine("No .fla next to this exe (skipped RECOVER_*.fla). Pass a path if needed.");
    Pause();
    return 2;
}

if (HasFlag(args, "--run-organize"))
{
    var once = FlaCleanupSteps.Organize(fla);
    Console.WriteLine(once.Ok ? "SUCCESS" : "FAIL");
    Console.WriteLine(once.Message);
    return once.Ok ? 0 : 1;
}

if (HasFlag(args, "--run-host"))
{
    var once = FlaCleanupSteps.RenameHost(fla);
    Console.WriteLine(once.Ok ? "SUCCESS" : "FAIL");
    Console.WriteLine(once.Message);
    return once.Ok ? 0 : 1;
}

if (HasFlag(args, "--run-names"))
{
    var once = FlaCleanupSteps.NamesAndOutlines(fla);
    Console.WriteLine(once.Ok ? "SUCCESS" : "FAIL");
    Console.WriteLine(once.Message);
    return once.Ok ? 0 : 1;
}

if (HasFlag(args, "--run-folders"))
{
    var once = FlaCleanupSteps.FoldersAndLock(fla);
    Console.WriteLine(once.Ok ? "SUCCESS" : "FAIL");
    Console.WriteLine(once.Message);
    return once.Ok ? 0 : 1;
}

if (HasFlag(args, "--run-merge"))
{
    var once = FlaCleanupSteps.Merge(fla);
    Console.WriteLine(once.Ok ? "SUCCESS" : "FAIL");
    Console.WriteLine(once.Message);
    return once.Ok ? 0 : 1;
}

if (HasFlag(args, "--inventory"))
{
    var dest = args.SkipWhile(a => !a.Equals("--inventory", StringComparison.OrdinalIgnoreCase))
        .Skip(1).FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
    if (string.IsNullOrWhiteSpace(dest) || dest.Equals(fla, StringComparison.OrdinalIgnoreCase) ||
        dest.EndsWith(".fla", StringComparison.OrdinalIgnoreCase))
        dest = Path.ChangeExtension(fla, ".inventory.json");
    var report = FlaInventory.FromFla(fla);
    File.WriteAllText(dest, FlaInventory.ToJson(report));
    Console.WriteLine("SUCCESS");
    Console.WriteLine("Wrote " + dest + " (" + report.Clips.Count + " clip(s)).");
    return 0;
}

Console.WriteLine("FLA: " + fla);
Console.WriteLine("Close Animate before each step. Q quits at any prompt.");
Console.WriteLine();

if (!File.Exists(fla + ".bak"))
{
    try { File.Copy(fla, fla + ".bak", overwrite: false); }
    catch (Exception ex)
    {
        Console.WriteLine("Could not write vanilla .bak: " + ex.Message);
    }
}

var steps = new (string Id, string Title, Func<string, FlaCleanupSteps.Result> Run)[]
{
    ("organize", "Organize library folders / names", FlaCleanupSteps.Organize),
    ("names", "Rename layers + outlines", FlaCleanupSteps.NamesAndOutlines),
    ("folders", "Display name / Ground / Effects / lock", FlaCleanupSteps.FoldersAndLock),
    ("merge", "Merge box layers", FlaCleanupSteps.Merge),
};

var start = ParseStartStep(args, steps.Length);
if (start > 0)
    Console.WriteLine("Starting at step " + (start + 1) + " (" + steps[start].Title + ").");

for (var i = start; i < steps.Length; i++)
{
    var step = steps[i];
    Console.WriteLine("────────────────────────────────────────");
    Console.WriteLine("[" + (i + 1) + "/" + steps.Length + "] " + step.Title);
    Console.Write("Enter = run, Q = quit: ");
    if (Quit(ReadKey()))
        return 0;

    if (IsLocked(fla))
    {
        Console.WriteLine("FAIL  File is locked. Close Animate and run this step again.");
        Console.Write("Enter = retry, Q = quit: ");
        if (Quit(ReadKey()))
            return 0;
        i--;
        continue;
    }

    var before = Path.Combine(Path.GetDirectoryName(fla)!, Path.GetFileNameWithoutExtension(fla) + ".before-" + step.Id + ".bak");
    try { File.Copy(fla, before, overwrite: true); }
    catch (Exception ex)
    {
        Console.WriteLine("FAIL  Could not write " + Path.GetFileName(before) + ": " + ex.Message);
        Console.Write("Enter = continue anyway, Q = quit: ");
        if (Quit(ReadKey()))
            return 1;
    }

    FlaCleanupSteps.Result result;
    try { result = step.Run(fla); }
    catch (Exception ex)
    {
        result = new FlaCleanupSteps.Result { Ok = false, Message = ex.Message };
    }

    Console.WriteLine(result.Ok ? "SUCCESS" : "FAIL");
    Console.WriteLine(result.Message);
    if (!result.Ok)
    {
        Console.WriteLine("Left FLA as written. Backup: " + before);
        Console.WriteLine("Fix with me, then re-run this exe (start from this step).");
        Pause();
        return 1;
    }

    Console.WriteLine("Open the FLA in Animate and check. Close Animate when done.");
    if (i == steps.Length - 1)
    {
        Console.WriteLine("All steps done.");
        Pause();
        return 0;
    }
    Console.Write("Enter = next step, Q = quit: ");
    if (Quit(ReadKey()))
        return 0;
}

return 0;

static bool HasFlag(string[] args, string flag) =>
    args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

static int ParseStartStep(string[] args, int count)
{
    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        string? raw = null;
        if (a.StartsWith("--from=", StringComparison.OrdinalIgnoreCase))
            raw = a[7..];
        else if (a.Equals("--from", StringComparison.OrdinalIgnoreCase) ||
                 a.Equals("-from", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < args.Length)
                raw = args[i + 1];
        }
        if (raw == null || !int.TryParse(raw, out var n))
            continue;
        return Math.Clamp(n - 1, 0, Math.Max(0, count - 1));
    }
    return 0;
}

static string? ResolveFla(string[] args)
{
    foreach (var a in args)
    {
        if (string.IsNullOrWhiteSpace(a) || a.StartsWith("-", StringComparison.Ordinal))
            continue;
        if (int.TryParse(a, out _))
            continue;
        if (a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            continue;
        var p = Path.GetFullPath(a);
        if (Directory.Exists(p))
            return PickFla(p);
        return p;
    }
    var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return PickFla(dir) ?? PickFla(Directory.GetCurrentDirectory());
}

static string? PickFla(string dir)
{
    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        return null;
    string[] files;
    try { files = Directory.GetFiles(dir, "*.fla"); }
    catch { return null; }
    var usable = files.Where(f =>
        !Path.GetFileName(f).StartsWith("RECOVER_", StringComparison.OrdinalIgnoreCase)).ToList();
    return usable.Count == 1 ? usable[0] : usable.FirstOrDefault();
}

static bool IsLocked(string path)
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

static string ReadKey()
{
    try
    {
        var line = Console.ReadLine();
        return line ?? "";
    }
    catch { return ""; }
}

static bool Quit(string s) =>
    s.Trim().Equals("q", StringComparison.OrdinalIgnoreCase) ||
    s.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase);

static void Pause()
{
    Console.WriteLine();
    Console.WriteLine("Press Enter to close");
    try { Console.ReadLine(); } catch { /* ignore */ }
}
