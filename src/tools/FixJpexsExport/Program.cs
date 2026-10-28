using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

return FixJpexsExportProgram.Run(args);

static class FixJpexsExportProgram
{
    private static readonly string[] FlashUtilsTypes =
    {
        "ByteArray", "Timer", "Endian", "Proxy", "IDataInput", "IDataOutput",
        "IExternalizable", "CompressionAlgorithm",
    };

    private static readonly string[] FlashUtilsFuncs =
    {
        "getTimer", "getDefinitionByName", "getQualifiedClassName", "getQualifiedSuperclassName",
        "describeType", "setTimeout", "clearTimeout", "setInterval", "clearInterval",
        "escapeMultiByte", "unescapeMultiByte", "getAliasName",
    };

    private static readonly Regex ReWhileTrue = new(@"while\s*\(\s*true\s*\)", RegexOptions.Compiled);
    private static readonly Regex ReOrEq = new(@"^(\s*)([^/\n]*?)\|\|=", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ReIndex = new(@"(?<![\w.])((?:this\.)?[A-Za-z_][\w.]*)\[([A-Za-z_]\w*)\]", RegexOptions.Compiled);
    private static readonly Regex ReEmbed = new(@"\[Embed\s*\(\s*source\s*=\s*(['""])([^'""]+)\1", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReImportUtilsStar = new(@"^(\s*)import\s+flash\.utils\.\*\s*;\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ReNewFlashDict = new(@"\bnew\s+Dictionary\s*\(\s*(true|false)\s*\)", RegexOptions.Compiled);
    private static readonly Regex ReNewEmptyDict = new(@"\bnew\s+Dictionary\s*\(\s*\)", RegexOptions.Compiled);
    private static readonly Regex ReAsDict = new(@"\bas\s+Dictionary\b", RegexOptions.Compiled);

    private const string SsfToSwf =
        "         if(!Main.ENCRYPTED && this.m_fileName != null" +
        " && this.m_fileName.toLowerCase().indexOf(\".ssf\")" +
        " == this.m_fileName.length - 4)\n" +
        "         {\n" +
        "            this.m_fileName = this.m_fileName.substr(" +
        "0,this.m_fileName.length - 4) + \".swf\";\n" +
        "         }\n";

    private const string SsfToSwfOverride =
        "         if(!Main.ENCRYPTED && this.m_urlOverride != null" +
        " && this.m_urlOverride.toLowerCase().lastIndexOf(\".ssf\")" +
        " == this.m_urlOverride.length - 4)\n" +
        "         {\n" +
        "            this.m_urlOverride = this.m_urlOverride.substr(" +
        "0,this.m_urlOverride.length - 4) + \".swf\";\n" +
        "         }\n";

    private const string ModHostSsfFallback =
        "         if(!asset.exists && fileName.length >= 4" +
        " && fileName.substr(fileName.length - 4).toLowerCase() == \".ssf\")\n" +
        "         {\n" +
        "            fileName = fileName.substr(0,fileName.length - 4) + \".swf\";\n" +
        "            asset = folder.resolvePath(fileName);\n" +
        "         }\n";

    public static int Run(string[] args)
    {
        bool decompile = false;
        bool dryRun = false;
        string? exportDirArg = null;
        string? swfArg = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--decompile":
                    decompile = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--swf":
                    if (++i >= args.Length)
                    {
                        Console.Error.WriteLine("--swf requires a path argument");
                        return 1;
                    }
                    swfArg = args[i];
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine("unknown argument: " + arg);
                        return 1;
                    }
                    if (exportDirArg != null)
                    {
                        Console.Error.WriteLine("unexpected extra argument: " + arg);
                        return 1;
                    }
                    exportDirArg = arg;
                    break;
            }
        }

        if (decompile)
        {
            var engineDir = FindEngineDir();
            var swf = Path.GetFullPath(swfArg ?? Path.Combine(engineDir, "engine2.swf"));
            var exportDir = Path.GetFullPath(exportDirArg ?? Path.Combine(engineDir, "decompiled"));
            var code = DecompileEngine2(swf, exportDir);
            if (code != 0)
                return code;
            return ApplyFixes(exportDir, dryRun);
        }

        if (exportDirArg == null)
        {
            Console.Error.WriteLine("export_dir is required unless --decompile is set");
            return 1;
        }

        var exportDirResolved = Path.GetFullPath(exportDirArg);
        if (!Directory.Exists(exportDirResolved))
        {
            Console.Error.WriteLine("not found: " + exportDirResolved);
            return 1;
        }

        return ApplyFixes(exportDirResolved, dryRun);
    }

    private static string FindEngineDir()
    {
        var exeDir = Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dir = exeDir;
        while (!string.IsNullOrEmpty(dir))
        {
            var dataEngine = Path.Combine(dir, "data", "engine", "engine2.swf");
            if (File.Exists(dataEngine))
                return Path.GetDirectoryName(dataEngine)!;

            var shellData = Path.Combine(dir, "SSF2 Shell", "data", "engine", "engine2.swf");
            if (File.Exists(shellData))
                return Path.GetDirectoryName(shellData)!;

            var expansions = Path.Combine(dir, "expansions", "engine", "engine2.swf");
            if (File.Exists(expansions))
                return Path.GetDirectoryName(expansions)!;

            var shell = Path.Combine(dir, "SSF2 Shell", "expansions", "engine", "engine2.swf");
            if (File.Exists(shell))
                return Path.GetDirectoryName(shell)!;

            dir = Path.GetDirectoryName(dir)!;
        }

        return Path.GetFullPath(Path.Combine(exeDir, "..", "data", "engine"));
    }

    private static string? FindNext2FlashRoot()
    {
        var env = Environment.GetEnvironmentVariable("NEXT2FLASH_HOME");
        if (IsNext2Flash(env))
            return Path.GetFullPath(env!);

        var dir = Path.GetFullPath(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && !string.IsNullOrWhiteSpace(dir); i++)
        {
            var parent = Directory.GetParent(dir)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                var sibling = Path.Combine(parent, "Next2Flash");
                if (IsNext2Flash(sibling))
                    return Path.GetFullPath(sibling);
            }
            if (string.IsNullOrWhiteSpace(parent) || parent == dir)
                break;
            dir = parent;
        }

        var docs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GitHub",
            "Next2Flash");
        return IsNext2Flash(docs) ? Path.GetFullPath(docs) : null;
    }

    private static bool IsNext2Flash(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(Path.Combine(path, "toolchain", "ffdec", "ffdec.jar"));

    private static string FindJava()
    {
        var n2f = FindNext2FlashRoot();
        var candidates = new List<string>();
        if (n2f != null)
        {
            candidates.Add(Path.Combine(n2f, "toolchain", "jre", "bin", "java.exe"));
            candidates.Add(Path.Combine(n2f, "toolchain", "flex_sdk", "jre", "bin", "java.exe"));
        }

        var exeDir = Path.GetFullPath(AppContext.BaseDirectory);
        foreach (var rel in new[]
                 {
                     Path.Combine("..", "..", "..", "runtime", "jre", "bin", "java.exe"),
                     Path.Combine("..", "..", "runtime", "jre", "bin", "java.exe"),
                     Path.Combine("..", "..", "jre", "bin", "java.exe")
                 })
        {
            candidates.Add(Path.GetFullPath(Path.Combine(exeDir, rel)));
        }

        foreach (var bundled in candidates)
        {
            if (File.Exists(bundled))
                return bundled;
        }

        var ssf2Java = Environment.GetEnvironmentVariable("SSF2_JAVA");
        if (!string.IsNullOrWhiteSpace(ssf2Java) && File.Exists(ssf2Java))
            return Path.GetFullPath(ssf2Java);

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var javaName = OperatingSystem.IsWindows() ? "java.exe" : "java";
            var candidate = Path.Combine(javaHome, "bin", javaName);
            if (File.Exists(candidate))
                return candidate;
        }

        return "java";
    }

    private static string FindFfdecJar()
    {
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable("FFDEC_JAR");
        if (!string.IsNullOrWhiteSpace(env))
            candidates.Add(env);

        var exeDir = Path.GetFullPath(AppContext.BaseDirectory);
        var n2f = FindNext2FlashRoot();
        if (n2f != null)
            candidates.Add(Path.Combine(n2f, "toolchain", "ffdec", "ffdec.jar"));
        candidates.Add(Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "runtime", "ffdec", "ffdec.jar")));
        candidates.Add(Path.GetFullPath(Path.Combine(exeDir, "..", "..", "runtime", "ffdec", "ffdec.jar")));
        candidates.Add(Path.GetFullPath(Path.Combine(exeDir, "..", "..", "ffdec", "ffdec.jar")));
        candidates.Add(@"C:\Program Files (x86)\FFDec\ffdec.jar");
        candidates.Add(@"C:\Program Files\FFDec\ffdec.jar");

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException("ffdec.jar not found. Install JPEXS FFDec or set FFDEC_JAR.");
    }

    private static int DecompileEngine2(string swf, string outDir)
    {
        if (!File.Exists(swf))
        {
            Console.Error.WriteLine("missing SWF: " + swf);
            return 1;
        }

        string ffdec;
        try
        {
            ffdec = FindFfdecJar();
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var java = FindJava();
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);

        Console.WriteLine("JPEXS " + ffdec);
        Console.WriteLine("SWF    " + swf);
        Console.WriteLine("OUT    " + outDir);

        var psi = new ProcessStartInfo
        {
            FileName = java,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-Xmx4096m");
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(ffdec);
        psi.ArgumentList.Add("-config");
        psi.ArgumentList.Add("autoDeobfuscate=0,parallelSpeedUp=1");
        psi.ArgumentList.Add("-export");
        psi.ArgumentList.Add("script,binaryData");
        psi.ArgumentList.Add(outDir);
        psi.ArgumentList.Add(swf);

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            Console.Error.WriteLine("failed to start JPEXS export process");
            return 1;
        }

        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine("JPEXS export failed (exit " + proc.ExitCode + ")");
            return proc.ExitCode;
        }

        return 0;
    }

    private static int ApplyFixes(string exportDir, bool dryRun)
    {
        string scriptsRoot;
        try
        {
            scriptsRoot = FindScriptsRoot(exportDir);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var files = Directory.EnumerateFiles(scriptsRoot, "*.as", SearchOption.AllDirectories).ToList();
        var totals = new Dictionary<string, int>
        {
            ["utils"] = 0,
            ["oreq"] = 0,
            ["while"] = 0,
            ["changed"] = 0,
        };

        foreach (var path in files)
        {
            var stats = ProcessFile(path, dryRun);
            totals["utils"] += stats["utils"];
            totals["oreq"] += stats["oreq"];
            totals["while"] += stats["while"];
            totals["changed"] += stats["changed"];
        }

        var copied = dryRun ? 0 : FixEmbeds(exportDir, scriptsRoot);
        Console.WriteLine("scripts root: " + scriptsRoot);
        Console.WriteLine(
            "files=" + files.Count +
            " changed=" + totals["changed"] +
            "  flash.utils*=" + totals["utils"] +
            "  ||=" + totals["oreq"] +
            "  while-bounds=" + totals["while"] +
            "  embed-bins=" + copied +
            (dryRun ? "  [dry-run]" : ""));

        return 0;
    }

    private static string FindScriptsRoot(string exportDir)
    {
        var root = Path.GetFullPath(exportDir);
        if (Directory.Exists(Path.Combine(root, "com")))
            return root;

        var nested = Directory.EnumerateDirectories(root, "com", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(p =>
            {
                var parts = p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var hasSsf2 = Directory.Exists(Path.Combine(p, "mcleodgaming", "ssf2"));
                var hasScripts = parts.Contains("scripts", StringComparer.OrdinalIgnoreCase);
                return (hasSsf2 ? 0 : 1, hasScripts ? 0 : 1, parts.Length);
            });

        foreach (var comDir in nested)
        {
            if (Path.GetFileName(comDir) == "com")
                return Path.GetDirectoryName(comDir)!;
        }

        throw new FileNotFoundException(
            "Could not find a com/ package under " + root + ". Pass the JPEXS export folder " +
            "(the one that contains scripts/ or com/).");
    }

    private static int? SkipStringComment(string text, int i)
    {
        var n = text.Length;
        if (i >= n)
            return null;

        var c = text[i];
        if (c == '/' && i + 1 < n)
        {
            var nxt = text[i + 1];
            if (nxt == '/')
            {
                var j = text.IndexOf('\n', i + 2);
                return j < 0 ? n : j + 1;
            }
            if (nxt == '*')
            {
                var j = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                return j < 0 ? n : j + 2;
            }
        }

        if (c is '\'' or '"')
        {
            var quote = c;
            var j = i + 1;
            while (j < n)
            {
                if (text[j] == '\\')
                {
                    j += 2;
                    continue;
                }
                if (text[j] == quote)
                    return j + 1;
                j++;
            }
            return n;
        }

        return null;
    }

    private static int MatchingBrace(string text, int openIdx)
    {
        var depth = 0;
        var i = openIdx;
        var n = text.Length;
        while (i < n)
        {
            var skipped = SkipStringComment(text, i);
            if (skipped.HasValue)
            {
                i = skipped.Value;
                continue;
            }

            var ch = text[i];
            if (ch == '{')
                depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
            i++;
        }

        throw new InvalidOperationException("unbalanced brace at " + openIdx);
    }

    private static string CodeWithoutStrings(string text)
    {
        var outChars = text.ToCharArray();
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            var skipped = SkipStringComment(text, i);
            if (skipped.HasValue)
            {
                for (var k = i; k < skipped.Value; k++)
                    outChars[k] = text[k] == '\n' ? '\n' : ' ';
                i = skipped.Value;
                continue;
            }
            i++;
        }
        return new string(outChars);
    }

    private static IEnumerable<(int KwStart, int BraceOpen, int BraceClose)> IterWhileTrue(string text)
    {
        foreach (Match m in ReWhileTrue.Matches(text))
        {
            var i = m.Index + m.Length;
            var n = text.Length;
            while (i < n && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= n || text[i] != '{')
                continue;
            var close = MatchingBrace(text, i);
            yield return (m.Index, i, close);
        }
    }

    private static (string Arr, string Idx)? FirstUnsafeIndex(string body)
    {
        var masked = CodeWithoutStrings(body);
        var incNames = new HashSet<string>();
        foreach (Match m in Regex.Matches(masked, @"\b([A-Za-z_]\w*)\s*(?:\+\+|\+=\s*1)|(?:\+\+)\s*([A-Za-z_]\w*)"))
        {
            if (m.Groups[1].Success)
                incNames.Add(m.Groups[1].Value);
            if (m.Groups[2].Success)
                incNames.Add(m.Groups[2].Value);
        }

        if (incNames.Count == 0)
            return null;

        foreach (Match m in ReIndex.Matches(masked))
        {
            var arr = m.Groups[1].Value;
            var idx = m.Groups[2].Value;
            if (!incNames.Contains(idx))
                continue;

            var prefix = masked[..m.Index];
            var boundPattern = Regex.Escape(idx) + @"\s*(?:<|>=)\s*" + Regex.Escape(arr) + @"\.length|" +
                               Regex.Escape(arr) + @"\.length\s*(?:>|<=)\s*" + Regex.Escape(idx);
            if (Regex.IsMatch(prefix, boundPattern))
                continue;

            return (arr, idx);
        }

        return null;
    }

    private static (string Text, int Count) FixWhileTrueBounds(string text)
    {
        var edits = new List<(int Pos, string Guard)>();
        foreach (var (_, braceOpen, braceClose) in IterWhileTrue(text))
        {
            var body = text[(braceOpen + 1)..braceClose];
            var hit = FirstUnsafeIndex(body);
            if (hit == null)
                continue;

            var (arr, idx) = hit.Value;
            var guardPattern = @"if\s*\(\s*" + Regex.Escape(idx) + @"\s*>=\s*" + Regex.Escape(arr) + @"\.length\s*\)";
            var bodyPrefix = body.Length > 200 ? body[..200] : body;
            if (Regex.IsMatch(bodyPrefix, guardPattern))
                continue;

            var nl = body.IndexOf('\n');
            var indent = "         ";
            if (nl >= 0)
            {
                var rest = body[(nl + 1)..];
                var trimmed = rest.TrimStart(' ', '\t');
                var leading = rest.Length - trimmed.Length;
                if (leading > 0)
                    indent = rest[..leading];
            }

            var guard =
                "\n" + indent + "if(" + idx + " >= " + arr + ".length)\n" +
                indent + "{\n" +
                indent + "   break;\n" +
                indent + "}\n";
            edits.Add((braceOpen + 1, guard));
        }

        if (edits.Count == 0)
            return (text, 0);

        var outText = text;
        foreach (var (pos, guard) in edits.OrderByDescending(e => e.Pos))
            outText = outText[..pos] + guard + outText[pos..];

        return (outText, edits.Count);
    }

    private static (string Text, int Count) FixOrAssign(string text)
    {
        var count = 0;
        var lines = SplitLinesKeepEnds(text);
        var outLines = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var stripped = line.TrimStart();
            if (stripped.StartsWith("//", StringComparison.Ordinal) || !line.Contains("||=", StringComparison.Ordinal))
            {
                outLines.Add(line);
                continue;
            }

            if (!CodeWithoutStrings(line).Contains("||=", StringComparison.Ordinal))
            {
                outLines.Add(line);
                continue;
            }

            var m = ReOrEq.Match(line);
            if (!m.Success)
            {
                outLines.Add(line);
                continue;
            }

            var indent = m.Groups[1].Value;
            var lhs = m.Groups[2].Value.TrimEnd();
            var replaced = line[..m.Index] + indent + lhs + " = " + lhs + " ||" + line[(m.Index + m.Length)..];
            count++;
            outLines.Add(replaced);
        }

        return (string.Concat(outLines), count);
    }

    private static List<string> SplitLinesKeepEnds(string text)
    {
        var lines = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var j = text.IndexOf('\n', i);
            if (j < 0)
            {
                lines.Add(text[i..]);
                break;
            }
            lines.Add(text[i..(j + 1)]);
            i = j + 1;
        }
        return lines;
    }

    private static List<string> UsedFlashUtils(string text)
    {
        var masked = CodeWithoutStrings(text);
        masked = ReImportUtilsStar.Replace(masked, "");
        var found = new List<string>();
        var seen = new HashSet<string>();
        foreach (var name in FlashUtilsTypes.Concat(FlashUtilsFuncs))
        {
            if (Regex.IsMatch(masked, @"\b" + Regex.Escape(name) + @"\b") && seen.Add(name))
                found.Add(name);
        }
        return found;
    }

    private static bool CustomDictionaryInScope(string text)
    {
        var pkg = Regex.Match(text, @"package\s+([\w.]+)");
        var package = pkg.Success ? pkg.Groups[1].Value : "";
        if (package == "com.mcleodgaming.ssf2.util" || package.EndsWith(".util", StringComparison.Ordinal))
            return true;
        if (Regex.IsMatch(text, @"import\s+com\.mcleodgaming\.ssf2\.util\.\*\s*;"))
            return true;
        if (Regex.IsMatch(text, @"import\s+com\.mcleodgaming\.ssf2\.util\.Dictionary\s*;"))
            return true;
        return false;
    }

    private static (string Text, int Count) FixFlashUtilsStar(string text)
    {
        var m = ReImportUtilsStar.Match(text);
        if (!m.Success)
            return (text, 0);

        var indent = m.Groups[1].Value;
        var names = UsedFlashUtils(text);
        var collide = CustomDictionaryInScope(text);
        string replacement;
        if (names.Count == 0)
            replacement = "";
        else
            replacement = string.Concat(names.Select(name => indent + "import flash.utils." + name + ";\n"));

        text = ReImportUtilsStar.Replace(text, replacement, 1);
        var extra = 0;
        if (collide)
        {
            text = ReNewFlashDict.Replace(text, m =>
            {
                extra++;
                return "new flash.utils.Dictionary(" + m.Groups[1].Value + ")";
            });
            text = ReNewEmptyDict.Replace(text, m =>
            {
                extra++;
                return "new flash.utils.Dictionary()";
            });
            if (text.Contains("flash.utils.Dictionary", StringComparison.Ordinal))
            {
                text = ReAsDict.Replace(text, m =>
                {
                    extra++;
                    return "as flash.utils.Dictionary";
                });
            }
        }

        return (text, 1 + extra);
    }

    private static List<string> CollectEmbedNames(string scriptsRoot)
    {
        var names = new List<string>();
        foreach (var path in Directory.EnumerateFiles(scriptsRoot, "*.as", SearchOption.AllDirectories))
        {
            var text = ReadText(path);
            foreach (Match m in ReEmbed.Matches(text))
            {
                var src = m.Groups[2].Value.Replace('\\', '/');
                names.Add(src.Split('/')[^1]);
            }
        }
        return names;
    }

    private static int FixEmbeds(string exportDir, string scriptsRoot)
    {
        var names = CollectEmbedNames(scriptsRoot);
        if (names.Count == 0)
            return 0;

        var assets = Path.Combine(scriptsRoot, "_assets");
        Directory.CreateDirectory(assets);
        var copied = 0;
        var searchRoots = new[]
        {
            exportDir,
            scriptsRoot,
            Path.GetDirectoryName(scriptsRoot)!,
            Path.GetDirectoryName(exportDir)!,
        };

        var bins = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var p in Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories))
            {
                var resolved = Path.GetFullPath(p);
                if (!seen.Add(resolved))
                    continue;
                bins.Add(resolved);
            }
        }

        var byName = bins.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var dest = Path.Combine(assets, name);
            byName.TryGetValue(name, out var src);
            if (src == null)
            {
                foreach (var cand in bins)
                {
                    var candName = Path.GetFileName(cand)!;
                    if (candName.EndsWith(name, StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(candName, StringComparison.OrdinalIgnoreCase))
                    {
                        src = cand;
                        break;
                    }
                }
            }

            if (src == null)
            {
                Console.WriteLine("warn: missing embed binary " + name);
                continue;
            }

            var destResolved = Path.GetFullPath(dest);
            var srcResolved = Path.GetFullPath(src);
            if (!destResolved.Equals(srcResolved, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(dest) && new FileInfo(dest).Length == new FileInfo(src).Length)
                    continue;
                File.Copy(src, dest, overwrite: true);
                copied++;
            }
        }

        return copied;
    }

    private static (string Text, int Count) FixStageAlign(string fileName, string text)
    {
        if (!fileName.Equals("Main.as", StringComparison.OrdinalIgnoreCase))
            return (text, 0);
        if (text.Contains("stage.align = \"\";", StringComparison.Ordinal))
            return (text, 0);
        const string needle = "stage.align = StageAlign.TOP_LEFT;";
        if (!text.Contains(needle, StringComparison.Ordinal))
            return (text, 0);
        return (text.Replace(needle, "stage.align = \"\";", StringComparison.Ordinal), 1);
    }

    private static (string Text, int Count) FixSsfExtension(string fileName, string text)
    {
        var n = 0;
        if (fileName.Equals("Resource.as", StringComparison.OrdinalIgnoreCase))
        {
            if (!text.Contains("this.m_fileName.length - 4) + \".swf\"", StringComparison.Ordinal))
            {
                const string needle = "this.m_fileName = file;";
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    text = text.Replace(needle, needle + "\n" + SsfToSwf, StringComparison.Ordinal);
                    n++;
                }
            }
            if (!text.Contains("this.m_urlOverride.length - 4) + \".swf\"", StringComparison.Ordinal))
            {
                const string needle = "this.m_urlOverride = value;";
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    text = text.Replace(needle, needle + "\n" + SsfToSwfOverride, StringComparison.Ordinal);
                    n++;
                }
            }
            return (text, n);
        }

        if (fileName.Equals("ModHost.as", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("fileName.substr(fileName.length - 4).toLowerCase() == \".ssf\"", StringComparison.Ordinal))
                return (text, 0);
            const string needle = "         asset = folder.resolvePath(fileName);";
            if (!text.Contains(needle, StringComparison.Ordinal))
                return (text, 0);
            text = text.Replace(needle, needle + "\n" + ModHostSsfFallback, StringComparison.Ordinal);
            return (text, 1);
        }

        return (text, 0);
    }

    private static Dictionary<string, int> ProcessFile(string path, bool dryRun)
    {
        var original = ReadText(path);
        var text = original;
        var stats = new Dictionary<string, int>
        {
            ["utils"] = 0,
            ["oreq"] = 0,
            ["while"] = 0,
            ["ssf"] = 0,
            ["align"] = 0,
            ["changed"] = 0,
        };

        (text, stats["utils"]) = FixFlashUtilsStar(text);
        (text, stats["oreq"]) = FixOrAssign(text);
        (text, stats["while"]) = FixWhileTrueBounds(text);
        (text, stats["ssf"]) = FixSsfExtension(Path.GetFileName(path), text);
        (text, stats["align"]) = FixStageAlign(Path.GetFileName(path), text);

        if (text != original && !dryRun)
            WriteText(path, text);

        stats["changed"] = text != original ? 1 : 0;
        return stats;
    }

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteText(string path, string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
