using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Writes the same <c>expansions/state/boot.json</c> and <c>launch.json</c>
    /// payloads as the SSF2 Stream extension, then starts the Shell exe.
    /// </summary>
    public static class ShellConfigureLaunchService
    {
        public static readonly (string Id, string Label)[] Menus =
        {
            ("home", "Home"),
            ("group", "Group"),
            ("online", "Online"),
            ("data", "Data"),
            ("options", "Options"),
            ("controls", "Controls"),
            ("vault", "Vault"),
            ("solo", "Solo"),
            ("events", "Events"),
            ("versus_css", "Versus"),
            ("training_css", "Training"),
            ("classic", "Classic"),
            ("allstar", "All-Star"),
            ("btt", "Break the Targets"),
            ("hrc", "Home-Run Contest"),
            ("multiman", "Multi-Man"),
            ("crystal", "Crystal Smash"),
            ("rttf", "Race to the Finish"),
            ("arena", "Arena"),
            ("build_info", "Build Info")
        };

        public static readonly (string Id, string Label)[] BootIntros =
        {
            ("default", "Default"),
            ("skip", "Skip"),
            ("intro1", "Intro 1"),
            ("intro2", "Intro 2"),
            ("intro3", "Intro 3"),
            ("random", "Random")
        };

        public static readonly (string Id, string Label)[] Difficulties =
        {
            ("EASY", "Easy"),
            ("NORMAL", "Normal"),
            ("HARD", "Hard"),
            ("INSANE", "Insane")
        };

        public static readonly (string Id, string Label, int Bit)[] SpecialModes =
        {
            ("MINI", "Mini", 1 << 0),
            ("MEGA", "Mega", 1 << 1),
            ("SLOW", "Slow", 1 << 2),
            ("LIGHTNING", "Lightning", 1 << 3),
            ("VAMPIRE", "Vampire", 1 << 4),
            ("VENGEANCE", "Vengeance", 1 << 5),
            ("FREEZE", "Freeze", 1 << 6),
            ("EGG", "Egg", 1 << 7),
            ("DRAMATIC", "Dramatic", 1 << 8),
            ("TURBO", "Turbo", 1 << 9),
            ("INVISIBLE", "Invisible", 1 << 10),
            ("METAL", "Metal", 1 << 11),
            ("LIGHT", "Light", 1 << 12),
            ("HEAVY", "Heavy", 1 << 13),
            ("SSF1", "SSF1", 1 << 14)
        };

        private static readonly HashSet<string> MatchScreens = new(StringComparer.OrdinalIgnoreCase)
        {
            "versus_css", "training_css", "classic", "allstar", "btt", "hrc",
            "multiman", "crystal", "rttf", "arena"
        };

        public static bool IsMatchScreen(string? screen) =>
            !string.IsNullOrWhiteSpace(screen) && MatchScreens.Contains(screen);

        public static List<string> ListIds(string? folder, string pattern, Func<string, bool>? keep = null)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return ids;
            foreach (var file in Directory.GetFiles(folder, pattern))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (keep != null && !keep(id)) continue;
                ids.Add(id);
            }
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        public static List<string> ListCharacters(string installRoot)
        {
            var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ListIds(ShellWorkspaceService.CharacterDirectory(installRoot), "*.swf"))
                ids.Add(id);
            var exp = ShellWorkspaceService.CharacterExpansionsDirectory(installRoot);
            if (Directory.Exists(exp))
            {
                foreach (var dir in Directory.GetDirectories(exp))
                {
                    foreach (var id in ListIds(dir, "*.swf", n => !n.Equals("assets", StringComparison.OrdinalIgnoreCase)))
                        ids.Add(id);
                }
            }
            return ids.ToList();
        }

        /// <summary>Stage ids under <c>data/stage</c> from both <c>.swf</c> and <c>.ssf</c> (menu backgrounds still ship as .ssf).</summary>
        public static List<string> ListStages(string installRoot)
        {
            var folder = Path.Combine(installRoot, "data", "stage");
            var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ListIds(folder, "*.swf"))
                ids.Add(id);
            foreach (var id in ListIds(folder, "*.ssf"))
                ids.Add(id);
            return ids.ToList();
        }

        public static List<string> ListBttStages(string installRoot)
        {
            var list = new List<string> { "Level 1", "Level 2", "Level 3", "Level 4" };
            foreach (var st in ListStages(installRoot))
            {
                if (!st.StartsWith("targettest_", StringComparison.OrdinalIgnoreCase))
                    continue;
                var charId = st["targettest_".Length..];
                if (string.IsNullOrWhiteSpace(charId) || charId is "2" or "3" or "4")
                    continue;
                list.Add(st);
            }
            return list;
        }

        public static bool IsGameRunning()
        {
            foreach (var exe in ShellWorkspaceService.GameExeNames)
            {
                var name = Path.GetFileNameWithoutExtension(exe);
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0)
                        return true;
                }
                catch { }
            }
            try
            {
                return Process.GetProcessesByName("adl").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void WriteCmd(string installRoot, JObject payload)
        {
            var dir = MmDir(installRoot);
            payload["cmdId"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N")[..6];
            payload["writtenUtc"] = DateTime.UtcNow.ToString("o");
            var path = Path.Combine(dir, "cmd.json");
            File.WriteAllText(path + ".tmp", payload.ToString(Newtonsoft.Json.Formatting.Indented));
            File.Move(path + ".tmp", path, overwrite: true);
        }

        public static void ApplyToRunningGame(string installRoot, LaunchConfigurePrefs prefs, bool gotoMenu)
        {
            if (!IsGameRunning())
                throw new InvalidOperationException("SSF2 is not running. Launch the game first.");
            prefs.EnsureDefaults();
            var screen = (prefs.StartingMenu ?? "home").Trim().ToLowerInvariant();
            var match = BuildMatch(installRoot, prefs) as JObject;
            if (match != null && gotoMenu)
                match["immediate"] = false;

            var bootIntro = "skip";
            var bootIntroPath = "";
            if (screen == "vault")
            {
                if (string.Equals(prefs.VaultAction, "intro", StringComparison.OrdinalIgnoreCase))
                {
                    var resolved = ResolveIntro(installRoot, prefs, prefs.VaultIntro);
                    bootIntro = resolved.Intro;
                    bootIntroPath = resolved.Path;
                }
            }
            else if (string.IsNullOrWhiteSpace(screen) || screen == "home")
            {
                var resolved = ResolveIntro(installRoot, prefs, prefs.BootIntro);
                bootIntro = resolved.Intro;
                bootIntroPath = resolved.Path;
            }

            var payload = new JObject
            {
                ["op"] = "applyLaunch",
                ["screen"] = screen,
                ["match"] = (JToken?)match ?? JValue.CreateNull(),
                ["bootIntro"] = bootIntro,
                ["fullscreen"] = prefs.OpenFullscreen,
                ["logFileAbs"] = ShellWorkspaceService.GameLogPath(installRoot).Replace('\\', '/')
            };
            if (!string.IsNullOrWhiteSpace(bootIntroPath))
                payload["bootIntroPath"] = bootIntroPath;
            if (screen == "vault")
            {
                payload["vaultAction"] = prefs.VaultAction;
                if (string.Equals(prefs.VaultAction, "costumes", StringComparison.OrdinalIgnoreCase))
                    payload["vaultPaletteChar"] = prefs.VaultPaletteChar;
            }
            AppendEventPrefs(payload, installRoot, prefs, screen);
            AppendMusicPrefs(payload, prefs);
            WriteCmd(installRoot, payload);
            FocusGameWindow();
        }

        public static void FocusGameWindow()
        {
            try
            {
                foreach (var name in new[] { "adl", "Super Smash Flash 2 Beta" })
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        var hwnd = proc.MainWindowHandle;
                        if (hwnd == IntPtr.Zero)
                            continue;
                        ShowWindow(hwnd, 9);
                        var foreground = GetForegroundWindow();
                        var fgThread = GetWindowThreadProcessId(foreground, out _);
                        var targetThread = GetWindowThreadProcessId(hwnd, out _);
                        AttachThreadInput(fgThread, targetThread, true);
                        SetForegroundWindow(hwnd);
                        AttachThreadInput(fgThread, targetThread, false);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        public static void SendLiveOp(string installRoot, string op, JObject? extra = null)
        {
            if (!IsGameRunning())
                throw new InvalidOperationException("SSF2 is not running. Launch the game first.");
            var payload = extra ?? new JObject();
            payload["op"] = op;
            WriteCmd(installRoot, payload);
        }

        public static List<string> ListMusic(string installRoot) =>
            ListIds(Path.Combine(installRoot, "data", "sound"), "*.swf",
                id => id.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase));

        public static List<(string Id, string Label, bool ChooseCharacter)> ListEvents(string installRoot)
        {
            var path = Path.Combine(installRoot, "data", "modes", "event_mode.swf");
            var found = new List<(string Id, string Label, int Order, bool Choose)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<(string Id, string Label, bool ChooseCharacter)>();
            if (File.Exists(path))
            {
            var data = File.ReadAllBytes(path);
            var strings = new List<string>();
            var i = 0;
            while (i < data.Length - 2)
            {
                var n = data[i];
                if (n is >= 3 and <= 90 && i + 1 + n <= data.Length)
                {
                    var ok = true;
                    for (var j = 0; j < n; j++)
                    {
                        var b = data[i + 1 + j];
                        if (b < 32 || b > 126)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok)
                    {
                        strings.Add(System.Text.Encoding.ASCII.GetString(data, i + 1, n));
                        i += 1 + n;
                        continue;
                    }
                }
                i++;
            }
            for (var s = 1; s < strings.Count; s++)
            {
                var cur = strings[s];
                var prev = strings[s - 1];
                if (cur.Length > 3 && char.IsDigit(cur[0]) && cur.Contains(". ") && IsEventId(prev))
                {
                    var dot = cur.IndexOf('.');
                    var order = int.TryParse(cur[..dot], out var num) ? num : 1000;
                    var choose = s >= 2 && strings[s - 2] == "chooseCharacter";
                    found.Add((prev, cur, order, choose));
                }
                else if (prev.StartsWith("AllStarBattle", StringComparison.Ordinal) && cur.StartsWith("All-Star", StringComparison.Ordinal))
                {
                    found.Add((prev, cur, 2000 + found.Count, false));
                }
            }
            foreach (var item in found.OrderBy(x => x.Order).ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
            {
                if (!seen.Add(item.Id))
                    continue;
                var label = item.Id.Equals("ACelebration", StringComparison.OrdinalIgnoreCase)
                    ? "51. A Celebration"
                    : item.Label;
                list.Add((item.Id, label, item.Choose));
            }
            }
            AppendExpansionEvents(installRoot, list, seen);
            return list;
        }

        private static void AppendExpansionEvents(
            string installRoot,
            List<(string Id, string Label, bool ChooseCharacter)> list,
            HashSet<string> seen)
        {
            var root = Path.Combine(installRoot, "expansions", "events");
            if (!Directory.Exists(root))
                return;
            var extra = new List<(string Id, string Label, bool ChooseCharacter)>();
            foreach (var folder in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(folder);
                if (string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith('.') || folderName.StartsWith('_'))
                    continue;
                var infoPath = Path.Combine(folder, "info.json");
                if (!File.Exists(infoPath))
                {
                    if (Directory.GetFiles(folder, "*.swf").Length == 0 || !seen.Add(folderName))
                        continue;
                    extra.Add((folderName, folderName, false));
                    continue;
                }
                JObject info;
                try { info = JObject.Parse(File.ReadAllText(infoPath)); }
                catch { continue; }
                if (info["enabled"]?.Type == JTokenType.Boolean && info["enabled"]!.Value<bool>() == false)
                    continue;
                var id = info["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id))
                    id = folderName;
                if (!seen.Add(id))
                    continue;
                var label = info["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(label))
                    label = id;
                var choose = info["chooseCharacter"]?.Value<bool?>() ?? false;
                extra.Add((id, label, choose));
            }
            foreach (var item in extra.OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase))
                list.Add(item);
        }

        /// <summary>
        /// Mode option pairs stored after the <c>mode</c> token in a mode SWF.
        /// Crystal Smash is Level 1 / Level 2. Home-Run Contest is Barrier / No Barrier.
        /// </summary>
        public static List<(string Id, string Label)> ListModeOptions(string installRoot, string swfFileName)
        {
            var path = Path.Combine(installRoot, "data", "modes", swfFileName);
            var list = new List<(string Id, string Label)>();
            if (!File.Exists(path))
                return list;
            var strings = ReadPrintableStrings(File.ReadAllBytes(path));
            var modeAt = -1;
            for (var i = 0; i < strings.Count; i++)
            {
                if (strings[i] == "mode")
                    modeAt = i;
            }
            if (modeAt < 0)
                return list;
            var tail = new List<string>();
            for (var i = modeAt + 1; i < strings.Count; i++)
            {
                if (!IsModeToken(strings[i]))
                    break;
                tail.Add(strings[i]);
            }
            var index = 0;
            while (index < tail.Count)
            {
                var cur = tail[index];
                if (cur.Contains(' '))
                {
                    list.Add((cur, cur));
                    index++;
                    continue;
                }
                if (index + 1 < tail.Count && !IsModeId(tail[index + 1]))
                {
                    list.Add((cur, tail[index + 1]));
                    index += 2;
                    continue;
                }
                list.Add((cur, cur));
                index++;
            }
            return list;
        }

        private static List<string> ReadPrintableStrings(byte[] data)
        {
            var strings = new List<string>();
            var i = 0;
            while (i < data.Length - 2)
            {
                var n = data[i];
                if (n is >= 3 and <= 90 && i + 1 + n <= data.Length)
                {
                    var ok = true;
                    for (var j = 0; j < n; j++)
                    {
                        var b = data[i + 1 + j];
                        if (b < 32 || b > 126)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok)
                    {
                        strings.Add(System.Text.Encoding.ASCII.GetString(data, i + 1, n));
                        i += 1 + n;
                        continue;
                    }
                }
                i++;
            }
            return strings;
        }

        private static bool IsModeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 40)
                return false;
            foreach (var ch in value)
            {
                if (!char.IsLetterOrDigit(ch) && ch != ' ')
                    return false;
            }
            return true;
        }

        private static bool IsModeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains(' ') || value.Length < 4 || !char.IsUpper(value[0]))
                return false;
            if (value.StartsWith("HRC", StringComparison.Ordinal))
                return true;
            foreach (var ch in value)
            {
                if (char.IsDigit(ch))
                    return true;
            }
            return value.Length >= 10;
        }

        private static bool IsEventId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 40 || !char.IsLetter(value[0]))
                return false;
            foreach (var ch in value)
            {
                if (!char.IsLetterOrDigit(ch))
                    return false;
            }
            return true;
        }

        private static void AppendEventPrefs(JObject payload, string installRoot, LaunchConfigurePrefs prefs, string screen)
        {
            if (screen != "events" || string.IsNullOrWhiteSpace(prefs.EventId))
                return;
            payload["eventId"] = prefs.EventId;
            if (!prefs.EventOverride || string.IsNullOrWhiteSpace(prefs.EventCharacter))
                return;
            var choose = ListEvents(installRoot).Any(e =>
                e.Id.Equals(prefs.EventId, StringComparison.OrdinalIgnoreCase) && e.ChooseCharacter);
            if (!choose)
                payload["eventCharacter"] = prefs.EventCharacter;
        }

        public static void WriteMenuMusic(string installRoot, string? musicId, IReadOnlyList<string>? playlist = null, bool? enabled = null)
        {
            var dir = ShellWorkspaceService.UserStateDirectory(installRoot);
            Directory.CreateDirectory(dir);
            var source = ShellWorkspaceService.UserStateFile(installRoot, "menu-music.json");
            var path = Path.Combine(dir, "menu-music.json");
            var songs = new List<string>();
            var wasEnabled = true;
            try
            {
                if (File.Exists(source))
                {
                    var existing = JObject.Parse(File.ReadAllText(source));
                    if (existing["enabled"] != null && existing["enabled"]!.Type != JTokenType.Null)
                        wasEnabled = existing["enabled"]!.Value<bool>();
                    // When playlist is omitted, keep the songs already on disk (disable-only writes).
                    if (playlist == null && existing["playlist"] is JArray keep)
                    {
                        foreach (var t in keep)
                        {
                            var s = (t?.ToString() ?? "").Trim();
                            if (s.Length > 0 && !songs.Contains(s, StringComparer.OrdinalIgnoreCase))
                                songs.Add(s);
                        }
                        if (songs.Count == 0)
                        {
                            var keepId = existing["id"]?.ToString()?.Trim() ?? "";
                            if (keepId.Length > 0)
                                songs.Add(keepId);
                        }
                    }
                }
            }
            catch
            {
                // ignore corrupt file; rewrite below
            }
            if (playlist != null)
            {
                songs.Clear();
                foreach (var id in playlist)
                {
                    var trimmed = (id ?? "").Trim();
                    if (trimmed.Length == 0 || songs.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                        continue;
                    songs.Add(trimmed);
                }
            }
            if (songs.Count == 0 && !string.IsNullOrWhiteSpace(musicId))
                songs.Add(musicId.Trim());
            var current = songs.FirstOrDefault() ?? (musicId ?? "").Trim();
            var name = songs.Count switch
            {
                0 => "",
                1 => PrettyMusic(current),
                _ => string.Join(", ", songs.Select(PrettyMusic))
            };
            var isEnabled = enabled ?? wasEnabled;
            if (songs.Count == 0)
                isEnabled = false;
            var obj = new JObject
            {
                ["id"] = current,
                ["name"] = name,
                ["loop"] = 0,
                ["enabled"] = isEnabled,
                ["playlist"] = new JArray(songs)
            };
            File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public static List<string> ReadMenuPlaylist(string installRoot)
        {
            var path = ShellWorkspaceService.UserStateFile(installRoot, "menu-music.json");
            try
            {
                if (!File.Exists(path))
                    return new List<string>();
                var obj = JObject.Parse(File.ReadAllText(path));
                if (obj["playlist"] is JArray arr)
                {
                    return arr.Select(t => t?.ToString() ?? "")
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                var id = obj["id"]?.ToString() ?? "";
                return string.IsNullOrWhiteSpace(id) ? new List<string>() : new List<string> { id };
            }
            catch
            {
                return new List<string>();
            }
        }

        public static bool ReadMenuPlaylistEnabled(string installRoot)
        {
            var path = ShellWorkspaceService.UserStateFile(installRoot, "menu-music.json");
            try
            {
                if (!File.Exists(path))
                    return false;
                var obj = JObject.Parse(File.ReadAllText(path));
                if (obj["enabled"] != null && obj["enabled"]!.Type != JTokenType.Null)
                    return obj["enabled"]!.Value<bool>();
                // Legacy files without the flag: enabled whenever a playlist exists.
                if (obj["playlist"] is JArray arr && arr.Count > 0)
                    return true;
                var id = obj["id"]?.ToString() ?? "";
                return !string.IsNullOrWhiteSpace(id);
            }
            catch
            {
                return false;
            }
        }

        private static string PrettyMusic(string id) =>
            id.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase) ? id[4..] : id;

        private static void AppendMusicPrefs(JObject payload, LaunchConfigurePrefs prefs)
        {
            payload["musicEverywhere"] = prefs.UseMusicEverywhere;
            if ((prefs.UseMusicOnLaunch || prefs.UseMusicEverywhere)
                && !string.IsNullOrWhiteSpace(prefs.MatchMusic))
                payload["musicId"] = prefs.MatchMusic;
            else
                payload["musicId"] = "";
        }

        public static bool TryImportStreamIntros(string installRoot, LaunchConfigurePrefs prefs)
        {
            prefs.EnsureDefaults();
            if (prefs.BootCustomIntros.Count > 0)
                return false;

            JObject? root = null;
            foreach (var app in new[] { "Cursor", "Code" })
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    app, "User", "settings.json");
                if (!File.Exists(path))
                    continue;
                try
                {
                    using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(File.ReadAllText(path)));
                    root = JObject.Load(reader, new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore
                    });
                }
                catch
                {
                    continue;
                }
                if (root?["ssf2Stream.bootCustomIntros"] is JArray)
                    break;
                root = null;
            }
            if (root?["ssf2Stream.bootCustomIntros"] is not JArray customs || customs.Count == 0)
                return false;

            prefs.BootCustomIntros = new List<LaunchCustomIntro>();
            foreach (var item in customs.OfType<JObject>())
            {
                var id = item["id"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var stored = item["path"]?.ToString() ?? "";
                var local = Path.Combine(IntrosDirectory(installRoot), id + ".flv");
                var path = File.Exists(local) ? local : (File.Exists(stored) ? stored : local);
                prefs.BootCustomIntros.Add(new LaunchCustomIntro
                {
                    Id = id,
                    Label = string.IsNullOrWhiteSpace(item["label"]?.ToString()) ? id : item["label"]!.ToString(),
                    Path = path
                });
            }
            if (prefs.BootCustomIntros.Count == 0)
                return false;

            var boot = root["ssf2Stream.bootIntro"]?.ToString();
            if (!string.IsNullOrWhiteSpace(boot))
                prefs.BootIntro = boot;
            var pool = root["ssf2Stream.bootRandomPool"] as JArray;
            if (pool != null)
                prefs.BootRandomPool = pool.Select(t => t?.ToString() ?? "").Where(s => s.Length > 0).ToList();
            var vaultIntro = root["ssf2Stream.vaultIntro"]?.ToString();
            if (!string.IsNullOrWhiteSpace(vaultIntro))
                prefs.VaultIntro = vaultIntro;
            var vaultAction = root["ssf2Stream.vaultAction"]?.ToString();
            if (!string.IsNullOrWhiteSpace(vaultAction))
                prefs.VaultAction = vaultAction;
            var palette = root["ssf2Stream.vaultPaletteChar"]?.ToString();
            if (!string.IsNullOrWhiteSpace(palette))
                prefs.VaultPaletteChar = palette;
            prefs.EnsureDefaults();
            return true;
        }

        public static void Launch(string installRoot, LaunchConfigurePrefs prefs)
        {
            ShellSwfGuard.Ensure(installRoot);
            if (string.IsNullOrWhiteSpace(installRoot))
                throw new ArgumentException("Install root is required.", nameof(installRoot));
            prefs.EnsureDefaults();
            WriteBoot(installRoot, prefs);
            WriteLaunch(installRoot, prefs);
            if (IsGameRunning())
            {
                ApplyToRunningGame(installRoot, prefs, true);
                return;
            }

            var exe = ShellWorkspaceService.FindGameExecutable(installRoot)
                      ?? throw new FileNotFoundException(
                          "Could not find Super Smash Flash 2 Beta.exe in the SSF2 Shell folder.",
                          installRoot);
            try { CreditsService.Update(installRoot); } catch { }
            ShellDirectLaunchService.RaiseGameStarting();
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? installRoot
            });
        }

        public static void WriteBoot(string installRoot, LaunchConfigurePrefs prefs)
        {
            prefs.EnsureDefaults();
            var dir = MmDir(installRoot);
            var bootIntro = (prefs.BootIntro ?? "default").Trim().ToLowerInvariant();
            var customs = NormalizeCustoms(installRoot, prefs);
            string bootIntroPath = "";
            string bootIntroLabel = "";
            var effective = bootIntro;
            // Always write randomPool so attract can rotate customs + stock intros
            // even when this launch's bootIntro is a fixed custom/default.
            var randomPool = BuildRandomPool(installRoot, prefs, customs);

            if (bootIntro.StartsWith("custom:", StringComparison.Ordinal))
            {
                ResolveCustom(installRoot, bootIntro, customs, out bootIntroPath, out bootIntroLabel, out effective);
            }

            var payload = new JObject
            {
                ["bootIntro"] = effective,
                ["fullscreen"] = prefs.OpenFullscreen,
                ["vaultAction"] = prefs.VaultAction,
                ["vaultIntro"] = prefs.VaultIntro,
                ["vaultPaletteChar"] = prefs.VaultPaletteChar,
                ["customs"] = CustomsArray(customs),
                ["randomPoolKeys"] = new JArray(PoolKeys(prefs, customs)),
                ["randomPool"] = randomPool,
                ["writtenUtc"] = DateTime.UtcNow.ToString("o")
            };
            if (!string.IsNullOrWhiteSpace(bootIntroPath))
            {
                payload["bootIntroPath"] = bootIntroPath;
                payload["bootIntroLabel"] = bootIntroLabel;
            }
            File.WriteAllText(Path.Combine(dir, "boot.json"), payload.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public static void WriteLaunch(string installRoot, LaunchConfigurePrefs prefs)
        {
            prefs.EnsureDefaults();
            var dir = MmDir(installRoot);
            var logPath = ShellWorkspaceService.GameLogPath(installRoot);
            var tracePath = Path.Combine(dir, "air_trace.txt");
            try { File.WriteAllText(logPath, $"[ssf2mm] launch {DateTime.UtcNow:o}\n"); } catch { }
            try { File.WriteAllText(tracePath, ""); } catch { }
            try
            {
                var cmd = Path.Combine(dir, "cmd.json");
                if (File.Exists(cmd)) File.Delete(cmd);
            }
            catch { }

            var screen = (prefs.StartingMenu ?? "home").Trim().ToLowerInvariant();
            if (screen == "build_info")
                screen = "home";
            var introKey = IntroKeyForScreen(screen, prefs);
            var resolved = ResolveIntro(installRoot, prefs, introKey);

            var payload = new JObject
            {
                ["screen"] = screen,
                ["match"] = BuildMatch(installRoot, prefs) ?? JValue.CreateNull(),
                ["bootIntro"] = resolved.Intro,
                ["fullscreen"] = prefs.OpenFullscreen,
                ["logFile"] = "expansions/state/game.log",
                ["logFileAbs"] = logPath.Replace('\\', '/'),
                ["traceFileAbs"] = tracePath.Replace('\\', '/'),
                ["writtenUtc"] = DateTime.UtcNow.ToString("o")
            };
            if (screen == "vault")
            {
                payload["vaultAction"] = prefs.VaultAction;
                if (string.Equals(prefs.VaultAction, "costumes", StringComparison.OrdinalIgnoreCase))
                    payload["vaultPaletteChar"] = prefs.VaultPaletteChar;
            }
            AppendEventPrefs(payload, installRoot, prefs, screen);
            AppendMusicPrefs(payload, prefs);
            if (resolved.Pool != null)
                payload["randomPool"] = resolved.Pool;
            if (!string.IsNullOrWhiteSpace(resolved.Path))
            {
                payload["bootIntroPath"] = resolved.Path;
                payload["bootIntroLabel"] = resolved.Label;
            }
            File.WriteAllText(Path.Combine(dir, "launch.json"), payload.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private static string MmDir(string installRoot)
        {
            ShellWorkspaceService.EnsureRuntimeState(installRoot);
            return ShellWorkspaceService.StateDirectory(installRoot);
        }

        private static string? IntroKeyForScreen(string screen, LaunchConfigurePrefs prefs)
        {
            if (screen == "vault")
            {
                if (string.Equals(prefs.VaultAction, "costumes", StringComparison.OrdinalIgnoreCase))
                    return "skip";
                return prefs.VaultIntro;
            }
            if (string.IsNullOrWhiteSpace(screen) || screen == "home")
                return null;
            return "skip";
        }

        private sealed class ResolvedIntro
        {
            public string Intro { get; init; } = "default";
            public string Path { get; init; } = "";
            public string Label { get; init; } = "";
            public JArray? Pool { get; init; }
        }

        private static ResolvedIntro ResolveIntro(string installRoot, LaunchConfigurePrefs prefs, string? introKey)
        {
            var customs = NormalizeCustoms(installRoot, prefs);
            var raw = (introKey ?? prefs.BootIntro ?? "default").Trim().ToLowerInvariant();
            if (raw == "random")
            {
                return new ResolvedIntro
                {
                    Intro = "random",
                    Pool = BuildRandomPool(installRoot, prefs, customs)
                };
            }
            if (raw.StartsWith("custom:", StringComparison.Ordinal))
            {
                ResolveCustom(installRoot, raw, customs, out var path, out var label, out var effective);
                var intro = effective == "random"
                    ? "random"
                    : !string.IsNullOrWhiteSpace(path)
                        ? effective
                        : effective.StartsWith("custom:", StringComparison.Ordinal) ? "skip" : effective;
                return new ResolvedIntro { Intro = intro, Path = path, Label = label };
            }
            return new ResolvedIntro { Intro = string.IsNullOrWhiteSpace(raw) ? "default" : raw };
        }

        private static void ResolveCustom(
            string installRoot,
            string key,
            List<LaunchCustomIntro> customs,
            out string path,
            out string label,
            out string effective)
        {
            path = "";
            label = "";
            var id = key["custom:".Length..];
            var hit = customs.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (hit == null || string.IsNullOrWhiteSpace(hit.Path))
            {
                var onDisk = Path.Combine(IntrosDirectory(installRoot), id + ".flv");
                if (File.Exists(onDisk))
                {
                    path = onDisk;
                    label = id;
                    effective = "custom:" + id;
                    return;
                }
                effective = "default";
                return;
            }
            path = EnsureBootIntroAsset(installRoot, hit.Id, hit.Path);
            label = hit.Label;
            if (string.IsNullOrWhiteSpace(path))
            {
                effective = "skip";
                label = "";
                return;
            }
            hit.Path = path;
            effective = "custom:" + hit.Id;
        }

        private static List<LaunchCustomIntro> NormalizeCustoms(string installRoot, LaunchConfigurePrefs prefs)
        {
            var list = (prefs.BootCustomIntros ?? new List<LaunchCustomIntro>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Path))
                .Select(c => new LaunchCustomIntro
                {
                    Id = c.Id.Trim(),
                    Label = string.IsNullOrWhiteSpace(c.Label) ? c.Id.Trim() : c.Label.Trim(),
                    Path = c.Path.Trim()
                })
                .ToList();
            foreach (var file in ListIntroFiles(installRoot))
            {
                if (list.Any(c => c.Id.Equals(file.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(file);
            }
            return list;
        }

        private static JArray CustomsArray(List<LaunchCustomIntro> customs)
        {
            var arr = new JArray();
            foreach (var c in customs)
            {
                arr.Add(new JObject
                {
                    ["id"] = c.Id,
                    ["label"] = c.Label,
                    ["path"] = c.Path
                });
            }
            return arr;
        }

        private static List<string> PoolKeys(LaunchConfigurePrefs prefs, List<LaunchCustomIntro> customs)
        {
            var all = new List<string> { "default", "intro1", "intro2", "intro3" };
            foreach (var c in customs)
                all.Add("custom:" + c.Id.ToLowerInvariant());
            var raw = (prefs.BootRandomPool ?? new List<string>())
                .Select(k => (k ?? "").Trim().ToLowerInvariant())
                .Where(k => all.Contains(k, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return raw.Count > 0 ? raw : all;
        }

        private static JArray BuildRandomPool(string installRoot, LaunchConfigurePrefs prefs, List<LaunchCustomIntro> customs)
        {
            var arr = new JArray();
            foreach (var key in PoolKeys(prefs, customs))
            {
                if (key is "default" or "intro1" or "intro2" or "intro3")
                {
                    arr.Add(new JObject { ["bootIntro"] = key });
                    continue;
                }
                if (!key.StartsWith("custom:", StringComparison.Ordinal))
                    continue;
                ResolveCustom(installRoot, key, customs, out var path, out var label, out var effective);
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                arr.Add(new JObject
                {
                    ["bootIntro"] = effective,
                    ["bootIntroPath"] = path,
                    ["bootIntroLabel"] = label
                });
            }
            return arr;
        }

        private static JToken? BuildMatch(string installRoot, LaunchConfigurePrefs prefs)
        {
            var screen = (prefs.StartingMenu ?? "home").Trim().ToLowerInvariant();
            if (!IsMatchScreen(screen))
                return null;

            var versus = screen == "versus_css";
            var training = screen == "training_css";
            var arena = screen == "arena";
            var classicLike = screen is "classic" or "allstar";
            var solo = classicLike || screen is "btt" or "hrc" or "crystal" or "multiman" or "arena" or "rttf";
            var modeStage = screen is "btt" or "hrc" or "crystal" or "multiman";
            var modeId = ResolveModeId(installRoot, screen, prefs.MatchCustomModeId);
            var stage = versus || training || screen == "rttf"
                ? SanitizeStage(prefs.MatchStage)
                : modeStage
                    ? (string.IsNullOrWhiteSpace(modeId) ? SanitizeStage(prefs.MatchStage) : modeId)
                    : SanitizeStage(prefs.MatchStage);

            var players = new JArray();
            var p = prefs.Players;
            // Always emit P1–P4 by index so empty P3/P4 stay empty slots (exist:false).
            players.Add(PlayerObject(p[0], arena));
            if (versus || training || arena)
            {
                players.Add(PlayerObject(p[1], arena, forceCpu: training));
                players.Add(PlayerObject(p[2], arena));
                players.Add(PlayerObject(p[3], arena));
            }

            var match = new JObject
            {
                ["immediate"] = (versus || training || solo) && prefs.LoadMatchImmediate,
                ["stage"] = modeStage ? (string.IsNullOrWhiteSpace(modeId) ? stage : modeId) : stage,
                ["players"] = players
            };
            if (!string.IsNullOrWhiteSpace(prefs.MatchMusic)
                && (prefs.UseMusicOnLaunch || prefs.UseMusicEverywhere))
                match["music"] = prefs.MatchMusic;
            if (versus || training)
                match["hazards"] = prefs.MatchHazards;
            if (versus)
            {
                match["usingTime"] = prefs.MatchUsingTime;
                match["usingLives"] = prefs.MatchUsingLives;
                match["time"] = Math.Clamp(prefs.MatchTime, 1, 99);
                if (prefs.MatchSpecialModes > 0)
                    match["specialModes"] = prefs.MatchSpecialModes;
            }
            if (versus || classicLike)
                match["lives"] = Math.Clamp(prefs.MatchLives <= 0 ? (screen == "allstar" ? 5 : 3) : prefs.MatchLives, 1, 99);
            if (classicLike)
                match["difficulty"] = string.IsNullOrWhiteSpace(prefs.MatchDifficulty) ? "NORMAL" : prefs.MatchDifficulty;
            if ((modeStage || arena) && !string.IsNullOrWhiteSpace(modeId))
                match["customModeID"] = modeId;
            if (arena)
                match["scoreLimit"] = Math.Clamp(prefs.MatchScoreLimit <= 0 ? 5 : prefs.MatchScoreLimit, 1, 99);
            if ((screen is "hrc" or "arena") && prefs.SandbagOverride
                && !string.IsNullOrWhiteSpace(prefs.SandbagCharacter))
                match["sandbagCharacter"] = prefs.SandbagCharacter;
            return match;
        }

        private static string ResolveModeId(string installRoot, string screen, string? raw)
        {
            raw ??= "";
            if (screen == "arena")
                return raw is "soccer" or "basketball" ? raw : "soccer";
            if (screen == "multiman")
            {
                var ok = new[] { "man10", "man100", "min3", "endless", "cruel" };
                return ok.Contains(raw) ? raw : "man10";
            }
            if (screen == "btt")
            {
                var id = string.IsNullOrWhiteSpace(raw) ? "Level 1" : raw;
                return id.Equals("character", StringComparison.OrdinalIgnoreCase) ? "Level 1" : id;
            }
            if (screen == "crystal")
                return PickModeId(ListModeOptions(installRoot, "crystals_mode.swf"), raw,
                    ("Level 1", "Level 1"), ("Level 2", "Level 2"));
            if (screen == "hrc")
                return PickModeId(ListModeOptions(installRoot, "homeruncontest_mode.swf"), raw,
                    ("HRCBarrier", "Barrier"), ("HRCNoBarrier", "No Barrier"));
            return raw;
        }

        private static string PickModeId(
            List<(string Id, string Label)> parsed,
            string? raw,
            params (string Id, string Label)[] fallback)
        {
            var options = parsed.Count > 0 ? parsed : fallback.ToList();
            if (options.Count == 0)
                return raw ?? "";
            var hit = options.FirstOrDefault(o => o.Id.Equals(raw ?? "", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(hit.Id) ? options[0].Id : hit.Id;
        }

        private static string SanitizeStage(string? stage)
        {
            var s = (stage ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s) || s is "soccer" or "basketball" or "man10" or "man100")
                return "battlefield";
            return s;
        }

        private static bool IsEmptyPlayerSlot(LaunchPlayerPref? p)
        {
            if (p == null)
                return true;
            var c = (p.Character ?? "").Trim();
            return c.Length == 0
                   || c.Equals("none", StringComparison.OrdinalIgnoreCase)
                   || c.Equals("(none)", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject PlayerObject(LaunchPlayerPref p, bool arena, bool forceCpu = false)
        {
            if (IsEmptyPlayerSlot(p))
            {
                return new JObject
                {
                    ["character"] = "",
                    ["exist"] = false,
                    ["human"] = false,
                    ["level"] = 1
                };
            }

            var human = !forceCpu && p.Human;
            var obj = new JObject
            {
                ["character"] = p.Character ?? "",
                ["exist"] = true,
                ["human"] = human,
                ["level"] = Math.Clamp(p.Level <= 0 ? 1 : p.Level, 1, 9)
            };
            if (arena)
                obj["team"] = Math.Clamp(p.Team <= 0 ? 1 : p.Team, 1, 3);
            if (human && !string.IsNullOrWhiteSpace(p.Tag))
            {
                var tag = ControllerProfilesSyncService.NormalizeName(p.Tag);
                if (tag.Length > 0)
                    obj["name"] = tag;
            }
            return obj;
        }

        public static string IntrosDirectory(string installRoot)
        {
            var dir = Path.Combine(ShellWorkspaceService.ExpansionsRoot(installRoot), "intros");
            Directory.CreateDirectory(dir);
            MigrateLegacyIntros(installRoot, dir);
            return dir;
        }

        private static void MigrateLegacyIntros(string installRoot, string destDir)
        {
            var legacy = Path.Combine(ShellWorkspaceService.Ssf2mmDirectory(installRoot), "intros");
            if (!Directory.Exists(legacy))
                return;
            foreach (var file in Directory.GetFiles(legacy))
            {
                var name = Path.GetFileName(file);
                var dest = Path.Combine(destDir, name);
                if (File.Exists(dest))
                    continue;
                try { File.Move(file, dest); }
                catch { /* leave the old copy if the move fails */ }
            }
        }

        public static List<LaunchCustomIntro> ListIntroFiles(string installRoot)
        {
            var dir = IntrosDirectory(installRoot);
            var list = new List<LaunchCustomIntro>();
            var labels = LoadIntroLabels(installRoot);
            if (!Directory.Exists(dir))
                return list;
            foreach (var file in Directory.GetFiles(dir, "*.flv").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var label = id;
                if (labels.TryGetValue(id, out var named) && !string.IsNullOrWhiteSpace(named))
                    label = named;
                list.Add(new LaunchCustomIntro { Id = id, Label = label, Path = file });
            }
            return list;
        }

        private static Dictionary<string, string> LoadIntroLabels(string installRoot)
        {
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Remember(string? id, string? label)
            {
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label))
                    return;
                var named = label.Trim();
                if (labels.TryGetValue(id, out var existing)
                    && !existing.Equals(id, StringComparison.OrdinalIgnoreCase)
                    && named.Equals(id, StringComparison.OrdinalIgnoreCase))
                    return;
                labels[id] = named;
            }

            void ReadArray(JToken? array)
            {
                if (array is not JArray items)
                    return;
                foreach (var item in items)
                {
                    Remember(item?["id"]?.ToString() ?? item?["Id"]?.ToString(),
                        item?["label"]?.ToString() ?? item?["Label"]?.ToString());
                }
            }

            try
            {
                var bootPath = Path.Combine(ShellWorkspaceService.StateDirectory(installRoot), "boot.json");
                if (File.Exists(bootPath))
                    ReadArray(JObject.Parse(File.ReadAllText(bootPath))["customs"]);
            }
            catch { /* keep whatever labels we already have */ }

            try
            {
                var settingsPath = AppPaths.SettingsFile;
                if (File.Exists(settingsPath))
                {
                    var root = JObject.Parse(File.ReadAllText(settingsPath));
                    ReadArray(root["LaunchConfigure"]?["BootCustomIntros"]
                              ?? root["launchConfigure"]?["bootCustomIntros"]);
                }
            }
            catch { /* file labels still apply */ }

            return labels;
        }

        public static LaunchCustomIntro AddIntroFile(string installRoot, string sourcePath)
        {
            var id = Path.GetFileNameWithoutExtension(sourcePath);
            id = new string(id.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            if (string.IsNullOrWhiteSpace(id))
                id = "intro";
            var n = 1;
            var baseId = id;
            var existing = ListIntroFiles(installRoot);
            while (existing.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                id = baseId + (++n);
            var dest = EnsureBootIntroAsset(installRoot, id, sourcePath);
            if (string.IsNullOrWhiteSpace(dest))
                throw new InvalidOperationException("Could not convert that video. ffmpeg needs to be next to the manager.");
            return new LaunchCustomIntro { Id = id, Label = Path.GetFileNameWithoutExtension(sourcePath), Path = dest };
        }

        public static void RemoveIntroFile(string installRoot, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;
            var dir = IntrosDirectory(installRoot);
            foreach (var ext in new[] { ".flv", ".flv.ok", ".mp4", ".ok" })
            {
                var path = Path.Combine(dir, id + ext);
                try { if (File.Exists(path)) File.Delete(path); }
                catch { /* ignore */ }
            }
        }

        /// <summary>Sorenson FLV under expansions/intros. Same ffmpeg settings as before.</summary>
        public static string EnsureBootIntroAsset(string installRoot, string id, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return "";
            var destDir = IntrosDirectory(installRoot);
            var dest = Path.Combine(destDir, id + ".flv");
            var ok = dest + ".ok";
            var encodeSrc = sourcePath;
            var normalized = sourcePath.Replace('\\', '/');
            if (sourcePath.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)
                && (normalized.Contains("/ssf2mm/intros/", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("/expansions/intros/", StringComparison.OrdinalIgnoreCase)))
            {
                var mp4 = Path.ChangeExtension(sourcePath, ".mp4");
                if (File.Exists(mp4))
                    encodeSrc = mp4;
            }

            try
            {
                if (File.Exists(dest) && File.Exists(ok) && new FileInfo(dest).Length > 0)
                {
                    var srcTime = File.GetLastWriteTimeUtc(encodeSrc);
                    var destTime = File.GetLastWriteTimeUtc(dest);
                    var okTime = File.GetLastWriteTimeUtc(ok);
                    if (destTime >= srcTime && okTime >= destTime.AddSeconds(-1))
                        return dest;
                }
            }
            catch { }

            try { if (File.Exists(ok)) File.Delete(ok); } catch { }

            var ffmpeg = AppPaths.FfmpegExe;
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
                return "";

            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-y", "-i", encodeSrc,
                    "-c:v", "flv", "-b:v", "1200k",
                    "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                    "-c:a", "libmp3lame", "-ar", "44100", "-ac", "2", "-b:a", "128k",
                    "-f", "flv", dest
                }
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return "";
            if (!proc.WaitForExit(300_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return "";
            }
            if (proc.ExitCode == 0 && File.Exists(dest) && new FileInfo(dest).Length > 0)
            {
                try { File.WriteAllText(ok, "sorenson\n" + DateTime.UtcNow.ToString("o") + "\n"); } catch { }
                return dest;
            }
            return "";
        }
    }
}
