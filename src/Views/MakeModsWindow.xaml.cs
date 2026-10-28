using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using FlowDirection = System.Windows.FlowDirection;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;

namespace SSF2ModManager.Views
{
    public partial class MakeModsWindow : Window
    {
        private readonly string _shell;
        private readonly ShellCharacterCloneService _characters = new();
        private readonly ShellStageCloneService _stages = new();
        private readonly MappingsEditService _mappings = new();
        private JObject? _doc;
        private bool _charLinkageEdited;
        private bool _stageLinkageEdited;
        private List<string> _importCharFiles = new();
        private List<string> _importStageFiles = new();
        private string? _stageBgFile;
        private string? _stageAssetFolder;
        private int _stageAssetPreview;
        private string _panel = "home";
        private bool _eventFilling;
        private readonly List<EventSlot> _eventSlots = new();
        private readonly List<System.Windows.Controls.CheckBox> _eventNeedBoxes = new();
        private readonly HashSet<string> _loadedBans = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _itemTouched = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(int Id, string Label)> _packageDeps = new();
        private int _depSearchGen;
        private string _eventPackDir = "";
        private string _eventPackId = "";
        private string _eventPreviewPath = "";
        private bool _eventPreviewCustom;
        private int _eventFighterShown = 1;
        private List<string> _eventExtras = new();
        private List<string> _eventCast = new();
        private string _eventStage = "";
        private string _eventMusic = "";
        private string _eventCloneSource = "";
        private EventRecipe? _eventCloneRecipe;
        private string _eventUnlock = "";
        private bool _eventRecordStock;
        private bool _eventRecordFps;

        private static readonly (string Id, string Label)[] WinRules =
        {
            ("KO_BASED", "KO clear"),
            ("TIMED", "Timed"),
            ("TIMED_KO_EVENT", "Timed KO"),
            ("SURVIVAL", "Survival")
        };

        private sealed class EventSlot
        {
            public System.Windows.Controls.ComboBox Character = null!;
            public System.Windows.Controls.ComboBox Stage = null!;
            public StackPanel? StageHost;
            public System.Windows.Controls.ComboBox Size = null!;
            public System.Windows.Controls.ComboBox Cpu = null!;
            public System.Windows.Controls.CheckBox Human = null!;
            public System.Windows.Controls.CheckBox Metal = null!;
            public System.Windows.Controls.CheckBox PlayerCostume = null!;
            public System.Windows.Controls.CheckBox Special = null!;
            public System.Windows.Controls.CheckBox Moves = null!;
            public System.Windows.Controls.TextBox Level = null!;
            public System.Windows.Controls.TextBox Lives = null!;
            public System.Windows.Controls.TextBox Costume = null!;
            public System.Windows.Controls.TextBox Team = null!;
            public System.Windows.Controls.TextBox Scale = null!;
            public System.Windows.Controls.TextBox Damage = null!;
            public System.Windows.Controls.TextBox Attack = null!;
            public StackPanel Dock = new();
            public StackPanel Row = new();
        }

        private static readonly (string Id, string Label)[] RankRules =
        {
            ("framesAtMost", "Clear time (frames or fewer)"),
            ("framesAtLeast", "Survive (frames or more)"),
            ("damageAtMost", "Least damage"),
            ("damageAtLeast", "Most damage"),
            ("countAtLeast", "Most KOs"),
            ("perMatch", "Frames per wave")
        };

        private static readonly (EventGoal Goal, string Label)[] GoalChoices =
        {
            (EventGoal.Stocks, "Stock clear"),
            (EventGoal.Tag, "Tag"),
            (EventGoal.Combo, "Combo"),
            (EventGoal.Bat, "Home-run bat"),
            (EventGoal.Bombs, "Bob-ombs"),
            (EventGoal.Bumpers, "Bumpers"),
            (EventGoal.Sheik, "Sheik"),
            (EventGoal.Saturns, "Mr. Saturns"),
            (EventGoal.Dirt, "Cleaning duty"),
            (EventGoal.Pokeball, "Poké Balls"),
            (EventGoal.Protoman, "Protoman"),
            (EventGoal.MotherBrain, "Mother Brain"),
            (EventGoal.MasterHand, "Master Hand"),
            (EventGoal.RandomItems, "Random items"),
            (EventGoal.LoopBackground, "Looping background")
        };
        public event Action? SectionChanged;

        public MakeModsWindow(string shellRoot)
        {
            _shell = shellRoot;
            InitializeComponent();
        }

        private void Show(string panel, string title)
        {
            _panel = panel;
            TxtTitle.Text = title;
            BtnBack.Visibility = Visibility.Collapsed;
            if (panel != "music")
                HideTrail = false;
            foreach (var name in new[]
                     {
                         "PanelHome", "PanelEvents", "PanelNewEvent", "PanelCompile",
                         "PanelStage", "PanelMakeStage", "PanelStageBg", "PanelStageAsset", "PanelImportStage",
                         "PanelCharacter", "PanelMakeCharacter", "PanelImportCharacter",
                         "PanelCostume", "PanelAlts", "PanelIntros", "PanelMusic",
                         "PanelPackageHome", "PanelPackageEdit", "PanelPackageZip",
                         "PanelN2f", "PanelDecompile", "PanelCompileFolder", "PanelPressor"
                     })
            {
                if (FindName(name) is FrameworkElement el)
                    el.Visibility = Visibility.Collapsed;
            }
            var showLog = panel is "newEvent" or "compile" or "makeStage" or "stageBg" or "stageAsset" or "makeCharacter" or "importStage" or "importCharacter" or "packageEdit" or "packageZip" or "intros" or "decompile" or "compileFolder" or "pressor" or "alts";
            TxtLog.Visibility = showLog ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("Panel" + char.ToUpperInvariant(panel[0]) + panel[1..]) is FrameworkElement current)
                current.Visibility = Visibility.Visible;
            else if (panel == "home" && FindName("PanelHome") is FrameworkElement home)
                home.Visibility = Visibility.Visible;
            if (panel == "music")
                LoadMusicEditor();
            if (FindName("FormScroll") is FrameworkElement form)
                form.Visibility = panel == "home" ? Visibility.Collapsed : Visibility.Visible;
            if (panel == "packageEdit")
                EnsurePackageForm();
            if (panel == "packageZip")
                EnsurePackageZip();
            if (panel == "costume")
                EnsureCostumeCharacters();
            if (panel == "alts")
                EnsureAltCharacters();
            if (panel == "intros")
                ReloadIntros();
            if (panel == "decompile")
                EnsureDecompileSwfs();
            if (panel == "compileFolder")
                EnsureCompileFolders();
            SectionChanged?.Invoke();
        }

        public void OpenPanel(string panel)
        {
            var title = panel switch
            {
                "home" => "Make Mods",
                "events" => "Events",
                "newEvent" => "New event",
                "compile" => "Compile",
                "stage" => "Stage",
                "makeStage" => "Clone a stage",
                "stageBg" => "Create a stage background",
                "importStage" => "Import a stage",
                "character" => "Character",
                "makeCharacter" => "Clone a character",
                "importCharacter" => "Import a character",
                "costume" => "Costume",
                "alts" => "Character Alts",
                "intros" => "Intros",
                "music" => "Music",
                "packageHome" => "Package mods",
                "packageEdit" => "Edit mod info",
                "packageZip" => "Package mods",
                "n2f" => "Modding Tools",
                "decompile" => "Convert to FLA",
                "compileFolder" => "Compile folder",
                "pressor" => "Pressor",
                _ => "Make Mods"
            };
            Show(panel, title);
        }

        public bool HideTrail { get; private set; }

        public System.Collections.Generic.IReadOnlyList<(string Id, string Label)> GetTrail()
        {
            var trail = new System.Collections.Generic.List<(string Id, string Label)>
            {
                ("home", "Make Mods")
            };
            switch (_panel)
            {
                case "events":
                case "newEvent":
                case "compile":
                    trail.Add(("events", "Events"));
                    break;
                case "stage":
                case "makeStage":
                case "stageBg":
                case "importStage":
                    trail.Add(("stage", "Stage"));
                    break;
                case "character":
                case "makeCharacter":
                case "importCharacter":
                    trail.Add(("character", "Character"));
                    break;
                case "costume":
                    trail.Add(("costume", "Costume"));
                    break;
                case "alts":
                    trail.Add(("alts", "Character Alts"));
                    break;
                case "intros":
                    trail.Add(("intros", "Intros"));
                    break;
                case "music":
                    trail.Add(("music", "Music"));
                    break;
                case "packageHome":
                case "packageEdit":
                case "packageZip":
                    trail.Add(("packageHome", "Package mods"));
                    break;
                case "n2f":
                    trail.Add(("n2f", "Modding Tools"));
                    break;
                case "decompile":
                    trail.Add(("n2f", "Modding Tools"));
                    trail.Add(("decompile", "Convert to FLA"));
                    break;
                case "compileFolder":
                    trail.Add(("n2f", "Modding Tools"));
                    trail.Add(("compileFolder", "Compile folder"));
                    break;
                case "pressor":
                    trail.Add(("n2f", "Modding Tools"));
                    trail.Add(("pressor", "Pressor"));
                    break;
            }
            switch (_panel)
            {
                case "newEvent":
                    trail.Add(("newEvent", "New event"));
                    break;
                case "compile":
                    trail.Add(("compile", "Compile"));
                    break;
                case "makeStage":
                    trail.Add(("makeStage", "Clone a stage"));
                    break;
                case "stageBg":
                    trail.Add(("stageBg", "Create a stage background"));
                    break;
                case "importStage":
                    trail.Add(("importStage", "Import a stage"));
                    break;
                case "makeCharacter":
                    trail.Add(("makeCharacter", "Clone a character"));
                    break;
                case "importCharacter":
                    trail.Add(("importCharacter", "Import a character"));
                    break;
                case "packageEdit":
                    trail.Add(("packageEdit", "Edit info"));
                    break;
                case "packageZip":
                    trail.Add(("packageZip", "Package"));
                    break;
            }
            return trail;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            switch (_panel)
            {
                case "events":
                case "stage":
                case "character":
                case "costume":
                case "intros":
                case "packageHome":
                case "n2f":
                    Show("home", "Make Mods");
                    break;
                case "decompile":
                case "compileFolder":
                case "pressor":
                    Show("n2f", "Modding Tools");
                    break;
                case "packageEdit":
                case "packageZip":
                    Show("packageHome", "Package mods");
                    break;
                case "newEvent":
                case "compile":
                    Show("events", "Events");
                    break;
                case "makeStage":
                case "stageBg":
                case "importStage":
                    Show("stage", "Stage");
                    break;
                case "makeCharacter":
                case "importCharacter":
                    Show("character", "Character");
                    break;
                default:
                    Show("home", "Make Mods");
                    break;
            }
        }

        private void BtnEvents_Click(object sender, RoutedEventArgs e) => Show("events", "Events");

        private void BtnPackage_Click(object sender, RoutedEventArgs e) => Show("packageHome", "Package mods");
        private void BtnN2f_Click(object sender, RoutedEventArgs e) => Show("n2f", "Modding Tools");

        private void BtnDecompileSection_Click(object sender, RoutedEventArgs e) => Show("decompile", "Convert to FLA");

        private void BtnCompileFolder_Click(object sender, RoutedEventArgs e) => Show("compileFolder", "Compile folder");

        private void BtnPressor_Click(object sender, RoutedEventArgs e) => Show("pressor", "Pressor");

        private bool PressorGarbageHeader() => ChkPressorGarbage.IsChecked == true;

        private string PressorStartDirectory()
        {
            var data = Path.Combine(_shell, "data");
            if (Directory.Exists(data))
                return data;
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private async void BtnPressorUncompressFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select files to uncompress",
                Filter = "SSF files|*.ssf;*.ssfrec|All files|*.*",
                Multiselect = true,
                InitialDirectory = PressorStartDirectory()
            };
            if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0)
                return;
            var header = PressorGarbageHeader();
            await RunPressorAsync("Uncompress files", (svc, progress) =>
                svc.UncompressFilesAsync(dlg.FileNames, progress, useGarbageHeader: header));
        }

        private async void BtnPressorUncompressFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder to uncompress",
                ShowNewFolderButton = false,
                SelectedPath = PressorStartDirectory()
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
            var header = PressorGarbageHeader();
            await RunPressorAsync("Uncompress folder", (svc, progress) =>
                svc.UncompressFolderAsync(dlg.SelectedPath, progress, useGarbageHeader: header));
        }

        private async void BtnPressorCompressFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select files to compress",
                Filter = "SWF files|*.swf|All files|*.*",
                Multiselect = true,
                InitialDirectory = PressorStartDirectory()
            };
            if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0)
                return;
            var header = PressorGarbageHeader();
            await RunPressorAsync("Compress files", (svc, progress) =>
                svc.CompressFilesAsync(dlg.FileNames, header, progress));
        }

        private async void BtnPressorCompressFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder to compress",
                ShowNewFolderButton = false,
                SelectedPath = PressorStartDirectory()
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
            var header = PressorGarbageHeader();
            await RunPressorAsync("Compress folder", (svc, progress) =>
                svc.CompressFolderAsync(dlg.SelectedPath, header, progress));
        }

        private async void BtnPressorModdable_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a Super Smash Flash 2 Beta folder",
                ShowNewFolderButton = false,
                SelectedPath = PressorStartDirectory()
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
            var header = PressorGarbageHeader();
            var launcher = System.Windows.Application.Current?.Windows.OfType<LauncherWindow>().FirstOrDefault();
            launcher?.LockForWork(true, "Create moddable build…");
            try
            {
                Log("Create moddable build…");
                var result = await new PressorService().CreateModdableBuildAsync(
                    dlg.SelectedPath, header, LogProgress());
                var line = "Moddable build " + result.OutputRoot
                    + $" — {result.Files.SuccessCount} ok, {result.Files.FailCount} failed.";
                Log(line);
                foreach (var file in result.Files.Files.Where(f => !f.Success))
                    Log(Path.GetFileName(file.SourcePath) + ": " + file.Error);
                MessageBox.Show(line, "Create moddable build", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "Create moddable build", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                launcher?.LockForWork(false, "");
            }
        }

        private async Task RunPressorAsync(string title, Func<PressorService, IProgress<string>, Task<PressorBatchResult>> action)
        {
            var launcher = System.Windows.Application.Current?.Windows.OfType<LauncherWindow>().FirstOrDefault();
            launcher?.LockForWork(true, title + "…");
            try
            {
                Log(title + "…");
                var result = await action(new PressorService(), LogProgress());
                var line = $"Finished in {result.ElapsedSeconds:0.00}s — {result.SuccessCount} ok, {result.FailCount} failed.";
                Log(line);
                foreach (var file in result.Files.Where(f => !f.Success))
                    Log(Path.GetFileName(file.SourcePath) + ": " + file.Error);
                MessageBox.Show(line, title, MessageBoxButton.OK,
                    result.FailCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                launcher?.LockForWork(false, "");
            }
        }

        private sealed class DataSwfItem
        {
            public string Label { get; init; } = "";
            public string Relative { get; init; } = "";
            public string FullPath { get; init; } = "";
        }

        private void EnsureDecompileSwfs()
        {
            var folders = FolderCompileService.ListDataFolders(_shell)
                .Select(folder => folder == "." ? "data" : folder)
                .ToList();
            CmbDecompileFolder.ItemsSource = folders;
            if (folders.Count > 0 && CmbDecompileFolder.SelectedIndex < 0)
                CmbDecompileFolder.SelectedIndex = 0;
            else
                FillDecompileFiles();
        }

        private void CmbDecompileFolder_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            FillDecompileFiles();

        private string SelectedDecompileFolder()
        {
            var label = CmbDecompileFolder.SelectedItem as string ?? "";
            return label == "data" ? "." : label;
        }

        private void FillDecompileFiles()
        {
            var folder = SelectedDecompileFolder();
            if (string.IsNullOrEmpty(folder))
            {
                CmbDecompileSwf.ItemsSource = null;
                return;
            }
            var items = FolderCompileService.ListSwfsInFolder(_shell, folder)
                .Select(x => new DataSwfItem
                {
                    Label = x.Relative,
                    Relative = x.Relative,
                    FullPath = x.FullPath
                })
                .ToList();
            CmbDecompileSwf.ItemsSource = items;
            if (items.Count > 0)
                CmbDecompileSwf.SelectedIndex = 0;
        }

        private async void BtnRunDecompile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbDecompileSwf.SelectedItem is not DataSwfItem swf)
            {
                MessageBox.Show("Pick a folder, then a file.", "Convert to FLA",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-fla-src-" + Guid.NewGuid().ToString("N"));
            string plain;
            try
            {
                plain = SwfPackage.EnsurePlainSwf(swf.FullPath, work);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                MessageBox.Show(ex.Message, "Convert to FLA",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                if (FolderCompileService.IsEngineFolder(SelectedDecompileFolder()))
                {
                    var dest = FolderCompileService.EngineDecompileDirectory(_shell, swf.FullPath);
                    await RunLogged("Convert to FLA", () =>
                        FolderCompileService.DecompileSwfAsync(plain, dest, LogProgress()));
                    return;
                }
                var flaDest = JpexsCliService.DecompiledOutputDirectory(_shell, swf.Relative);
                var jpexs = new JpexsCliService();
                if (!jpexs.IsAvailable(null))
                {
                    MessageBox.Show(
                        "Next2Flash could not be found. Open the manager once so it can download Next2Flash.",
                        "Convert to FLA", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var characterPretty = swf.Relative.StartsWith("character/", StringComparison.OrdinalIgnoreCase);
                var stageLibrary = JpexsCliService.IsStageDataSwf(swf.Relative);
                await RunLogged("Convert to FLA", () => jpexs.ConvertSwfToFlaAsync(
                    plain, flaDest, LogProgress(), default, characterPretty, stageLibrary));
            }
            finally
            {
                if (Directory.Exists(work))
                {
                    try { Directory.Delete(work, recursive: true); }
                    catch { /* the convert already finished */ }
                }
            }
        }

        private void EnsureCompileFolders()
        {
            var items = FolderCompileService.ListCompileTargets(_shell);
            CmbCompileFolder.ItemsSource = items;
            if (items.Count > 0 && CmbCompileFolder.SelectedIndex < 0)
                CmbCompileFolder.SelectedIndex = 0;
        }

        private async void BtnRunCompileFolder_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCompileFolder.SelectedItem is not FolderCompileService.CompileTarget target)
            {
                MessageBox.Show(
                    "Decompile engine2 first, or add a mod with a src folder under expansions/mods.",
                    "Compile folder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await RunLogged("Compile folder", () =>
                FolderCompileService.CompileAsync(_shell, target, LogProgress()));
        }

        private void BtnPackageEdit_Click(object sender, RoutedEventArgs e) => Show("packageEdit", "Edit mod info");

        private void BtnPackageZipSection_Click(object sender, RoutedEventArgs e) => Show("packageZip", "Package mods");

        private void BtnCostumeSection_Click(object sender, RoutedEventArgs e) => Show("costume", "Costume");
        private void BtnAltsSection_Click(object sender, RoutedEventArgs e) => Show("alts", "Character Alts");

        private void BtnIntros_Click(object sender, RoutedEventArgs e) => Show("intros", "Intros");

        private void EnsureCostumeCharacters()
        {
            if (CmbCostumeCharacter.Items.Count > 0)
                return;
            foreach (var id in ShellConfigureLaunchService.ListCharacters(_shell))
                CmbCostumeCharacter.Items.Add(id);
            if (CmbCostumeCharacter.Items.Count > 0)
                CmbCostumeCharacter.SelectedIndex = 0;
        }

        private DispatcherTimer? _altPickTimer;
        private long _altPickAt;

        private void EnsureAltCharacters()
        {
            if (CmbAltCharacter.Items.Count > 0)
                return;
            foreach (var id in ShellConfigureLaunchService.ListCharacters(_shell))
                CmbAltCharacter.Items.Add(id);
            if (CmbAltCharacter.Items.Count > 0)
                CmbAltCharacter.SelectedIndex = 0;
            RadAltScope_Changed(this, new RoutedEventArgs());
        }

        private void RadAltScope_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelAltCostumePick == null) return;
            PanelAltCostumePick.Visibility = RadAltCostume?.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BtnAltPickCostume_Click(object sender, RoutedEventArgs e)
        {
            var character = CmbAltCharacter.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(character))
            {
                MessageBox.Show("Pick a character.", "Character Alts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var pickPaths = new[]
                {
                    Path.Combine(ShellWorkspaceService.StateDirectory(_shell), "alt-costume-pick.json"),
                    Path.Combine(ShellWorkspaceService.UserStateDirectory(_shell), "alt-costume-pick.json")
                };
                foreach (var p in pickPaths)
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                _altPickAt = 0;
                ShellDirectLaunchService.Launch(_shell, ShellDirectLaunchService.ScreenCss,
                    new Dictionary<string, object?>
                    {
                        ["altCostumePick"] = true,
                        ["altCostumePickChar"] = character,
                        ["match"] = new Dictionary<string, object?>
                        {
                            ["players"] = new object[]
                            {
                                new Dictionary<string, object?> { ["character"] = character }
                            }
                        }
                    });
                TxtAltWork.Text = "Opened Character Select with " + character
                    + " — click costumes to update the index, then Install.";
                _altPickTimer?.Stop();
                _altPickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                var started = DateTime.UtcNow;
                _altPickTimer.Tick += (_, _) =>
                {
                    if (DateTime.UtcNow - started > TimeSpan.FromMinutes(15))
                    {
                        _altPickTimer.Stop();
                        TxtAltWork.Text = "Costume pick timed out. Type the index (−1 = base) yourself.";
                        return;
                    }
                    var pickPath = pickPaths.FirstOrDefault(File.Exists);
                    if (pickPath == null) return;
                    try
                    {
                        var jo = JObject.Parse(File.ReadAllText(pickPath));
                        if (jo["costume"] == null) return;
                        var at = jo["at"]?.Value<long>() ?? 0;
                        if (at != 0 && at == _altPickAt) return;
                        _altPickAt = at;
                        TxtAltCostume.Text = jo["costume"]!.ToString();
                        var pickName = ((string?)jo["name"] ?? "").Trim();
                        if (pickName.Length > 0)
                            TxtAltName.Text = pickName;
                        TxtAltWork.Text = "Costume " + TxtAltCostume.Text
                            + (pickName.Length > 0 ? " (" + pickName + ")" : "")
                            + " — keep clicking to change; then Install.";
                    }
                    catch { }
                };
                _altPickTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pick costume", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnAltInstall_Click(object sender, RoutedEventArgs e)
        {
            var character = CmbAltCharacter.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(character))
            {
                MessageBox.Show("Pick a character.", "Character Alts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            int? costume = null;
            if (RadAltCostume?.IsChecked == true)
            {
                if (!int.TryParse(TxtAltCostume.Text.Trim(), out var c))
                {
                    MessageBox.Show("Costume index must be an integer (−1 = base).", "Character Alts",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                costume = c;
            }
            try
            {
                TxtAltWork.Text = "Extracting sounds…";
                await LoadRosterAsync();
                var swf = MusicEditService.ResolveCharacterSwfPath(_shell, _doc ?? new JObject(), character);
                if (string.IsNullOrWhiteSpace(swf) || !File.Exists(swf))
                    throw new FileNotFoundException("Character SWF not found for " + character + ".");
                var name = string.IsNullOrWhiteSpace(TxtAltName.Text) ? null : TxtAltName.Text.Trim();
                var folderName = SafeAltFolder(name, character);
                // Sound-only alt: export into sounds/, no character SWF copy — stock SWF stays loaded in-game.
                var dest = Path.Combine(CharacterAltsService.AltsRoot(_shell), "local",
                    CharacterAltsService.SafeFolder(character), folderName);
                Directory.CreateDirectory(dest);
                var sounds = Path.Combine(dest, "sounds");
                if (Directory.Exists(sounds))
                    Directory.Delete(sounds, recursive: true);
                Directory.CreateDirectory(sounds);
                var work = Path.Combine(Path.GetTempPath(), "ssf2mm-alt-export-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    var plain = SwfPackage.EnsurePlainSwf(swf, work);
                    var jpexs = new JpexsCliService();
                    await jpexs.ExportAsync("", plain, sounds, "sound", default);
                }
                finally
                {
                    try { Directory.Delete(work, recursive: true); } catch { }
                }
                await AltSoundEditService.ConvertNonAirSoundsToWavAsync(sounds);
                var links = AltSoundEditService.WriteLinkagesJson(sounds);
                var count = Directory.GetFiles(sounds, "*.*", SearchOption.TopDirectoryOnly)
                    .Count(f =>
                    {
                        var e = Path.GetExtension(f).ToLowerInvariant();
                        return e is ".wav" or ".mp3";
                    });
                if (count == 0)
                    throw new InvalidOperationException("No sounds were exported from " + Path.GetFileName(swf) + ".");
                // Remove any leftover character SWF from older installs.
                foreach (var leftover in Directory.GetFiles(dest, "*.swf"))
                    try { File.Delete(leftover); } catch { }
                TxtAltWork.Text = "Extracted " + count + " sound file(s) (" + links + " linkages). Registering…";
                var folder = CharacterAltsService.InstallSounds(_shell, character, name, folderName, costume);
                _altPickTimer?.Stop();
                var scopeNote = costume.HasValue ? "costume " + costume : "all costumes";
                TxtAltWork.Text = "Installed " + folder + " (" + scopeNote + ") sounds-only — stock SWF + sounds\\.";
                AppendLog("Installed alt " + (name ?? character) + " → " + folder + " mode=sounds " + scopeNote
                    + " sounds=" + count);
                try { System.Diagnostics.Process.Start("explorer.exe", sounds); } catch { }
                var msg = "Alt installed (" + scopeNote + ").\n\nSounds only — no character SWF copy.\n"
                    + sounds
                    + "\n\nThe game keeps the stock character and plays these files when the alt is active.";
                if (costume.HasValue)
                    msg += "\n\nJump cycles to this alt and selects costume " + costume + ".";
                else
                    msg += "\n\nOn Character Select, put a token on " + character + " and press jump to cycle to it.";
                MessageBox.Show(msg, "Character Alts", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Install alt", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtAltWork.Text = ex.Message;
            }
        }

        private static string SafeAltFolder(string? name, string character)
        {
            var raw = string.IsNullOrWhiteSpace(name) ? character + "-alt" : name.Trim();
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return clean.Length == 0 ? "alt" : clean;
        }

        private void AppendLog(string line)
        {
            if (TxtLog.Visibility != Visibility.Visible)
                TxtLog.Visibility = Visibility.Visible;
            TxtLog.AppendText(line + Environment.NewLine);
            TxtLog.ScrollToEnd();
        }

        private void ReloadIntros()
        {
            LstIntros.ItemsSource = ShellConfigureLaunchService.ListIntroFiles(_shell);
        }

        private void BtnAddMakeIntro_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Custom intro",
                Filter = "Video|*.mp4;*.flv;*.mov;*.webm;*.mkv|All files|*.*"
            };
            if (dlg.ShowDialog() != true)
                return;
            try
            {
                var intro = ShellConfigureLaunchService.AddIntroFile(_shell, dlg.FileName);
                TxtLog.Text = "Converted " + intro.Label + " to " + intro.Path;
                ReloadIntros();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Intros", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnRemoveMakeIntro_Click(object sender, RoutedEventArgs e)
        {
            if (LstIntros.SelectedItem is not LaunchCustomIntro hit)
            {
                MessageBox.Show("Pick an intro to remove.", "Intros", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShellConfigureLaunchService.RemoveIntroFile(_shell, hit.Id);
            TxtLog.Text = "Removed " + hit.Id;
            ReloadIntros();
        }

        private void EnsurePackageZip()
        {
            var mods = ModPackageService.List(_shell);
            LstPackageZip.ItemsSource = mods;
            TxtPackageZipEmpty.Visibility = mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void EnsurePackageForm()
        {
            if (CmbPackageType.Items.Count == 0)
            {
                foreach (var type in ModPackageService.Types)
                    CmbPackageType.Items.Add(type);
            }
            ReloadPackageList();
        }

        private void ReloadPackageList()
        {
            var mods = ModPackageService.List(_shell);
            LstPackageMods.ItemsSource = mods;
            TxtPackageEmpty.Visibility = mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (mods.Count > 0)
                LstPackageMods.SelectedIndex = 0;
            else
                ClearPackageFields();
        }

        private void LstPackageMods_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstPackageMods.SelectedItem is not PackageModEntry entry)
                return;
            TxtPackageName.Text = entry.Name;
            TxtPackageCreator.Text = entry.Creator;
            TxtPackageVersion.Text = entry.Version;
            TxtPackageDesc.Text = entry.Description;
            ShowPackageDeps(entry.Dependencies);
            if (!CmbPackageType.Items.Contains(entry.Type))
                CmbPackageType.Items.Add(entry.Type);
            CmbPackageType.SelectedItem = entry.Type;
        }

        private void ClearPackageFields()
        {
            TxtPackageName.Text = "";
            TxtPackageCreator.Text = "";
            TxtPackageVersion.Text = "";
            TxtPackageDesc.Text = "";
            ShowPackageDeps("");
            if (CmbPackageType.Items.Count > 0)
                CmbPackageType.SelectedIndex = 0;
        }

        private PackageModEntry? ReadPackageForm()
        {
            if (LstPackageMods.SelectedItem is not PackageModEntry entry)
            {
                MessageBox.Show("Pick a mod to package.", "Package mods", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            entry.Name = TxtPackageName.Text.Trim();
            entry.Creator = TxtPackageCreator.Text.Trim();
            entry.Type = CmbPackageType.SelectedItem as string ?? entry.Type;
            entry.Version = TxtPackageVersion.Text.Trim();
            entry.Description = TxtPackageDesc.Text.Trim();
            entry.Dependencies = ModDependencies.Format(_packageDeps.Select(d => d.Id).ToArray());
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                MessageBox.Show("Name is required.", "Package mods", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return entry;
        }

        private void ShowPackageDeps(string? text)
        {
            _packageDeps.Clear();
            foreach (var id in ModDependencies.Parse(text))
                _packageDeps.Add((id, id.ToString()));
            PaintPackageDeps();
        }

        private void AddPackageDep(int id, string label)
        {
            if (id <= 0 || _packageDeps.Any(d => d.Id == id))
                return;
            _packageDeps.Add((id, string.IsNullOrWhiteSpace(label) ? id.ToString() : label));
            PaintPackageDeps();
        }

        private void PaintPackageDeps()
        {
            PnlPackageDeps.Children.Clear();
            foreach (var dep in _packageDeps.ToList())
            {
                var chip = new System.Windows.Controls.Button
                {
                    Content = dep.Label + "  ×",
                    Margin = new Thickness(0, 0, 6, 4),
                    Padding = new Thickness(8, 2, 8, 2),
                    Tag = dep.Id
                };
                chip.Click += (_, _) =>
                {
                    _packageDeps.RemoveAll(d => d.Id == (int)chip.Tag);
                    PaintPackageDeps();
                };
                PnlPackageDeps.Children.Add(chip);
            }
        }

        private async void TxtDepSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var gen = ++_depSearchGen;
            var query = TxtDepSearch.Text.Trim();
            if (query.Length < 2)
            {
                LstDepHits.ItemsSource = null;
                return;
            }
            await Task.Delay(300);
            if (gen != _depSearchGen)
                return;
            try
            {
                using var client = new GameBananaApiClient();
                var (mods, _, _) = await client.SearchModsAsync(query, 1, 12);
                if (gen != _depSearchGen)
                    return;
                LstDepHits.ItemsSource = mods
                    .Select(m => new EventFormPick(m.Id.ToString(), m.Name + " — " + m.Id))
                    .ToList();
            }
            catch (Exception ex)
            {
                if (gen == _depSearchGen)
                    Log(ex.Message);
            }
        }

        private void TxtDepSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;
            foreach (var id in ModDependencies.Parse(TxtDepSearch.Text))
                AddPackageDep(id, id.ToString());
            if (ModDependencies.Parse(TxtDepSearch.Text).Length > 0)
                TxtDepSearch.Text = "";
        }

        private void LstDepHits_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (LstDepHits.SelectedItem is not EventFormPick pick || !int.TryParse(pick.Id, out var id))
                return;
            AddPackageDep(id, pick.Label);
        }

        private void BtnPackageSave_Click(object sender, RoutedEventArgs e)
        {
            var entry = ReadPackageForm();
            if (entry == null)
                return;
            try
            {
                ModPackageService.SaveInfo(entry);
                TxtLog.Text = "Saved info.json in " + entry.FolderPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Package mods", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnPackageZip_Click(object sender, RoutedEventArgs e)
        {
            var picked = PackageZipSelection();
            if (picked == null)
                return;
            var safe = string.Join("_", picked[0].Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (picked.Count > 1)
                safe = "mods";
            if (string.IsNullOrWhiteSpace(safe))
                safe = "mod";
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Package mods",
                FileName = safe + ".ssfe",
                Filter = "Supported package (*.ssfe)|*.ssfe|Zip archive (*.zip)|*.zip",
                AddExtension = true,
                DefaultExt = ".ssfe"
            };
            if (dlg.ShowDialog() != true)
                return;
            try
            {
                if (dlg.FileName.EndsWith(".ssfe", StringComparison.OrdinalIgnoreCase))
                {
                    if (picked.Count == 1)
                    {
                        SsfeSeal.Package(picked[0].FolderPath, dlg.FileName);
                        TxtLog.Text = "Sealed " + picked[0].Name + " into " + dlg.FileName
                            + "\nSource, fixtures, and rebuild scripts are left out.";
                    }
                    else
                    {
                        if (!AskPackInfo(out var packName, out var packDesc))
                            return;
                        ModPackageService.PackageSsfe(picked, packName, packDesc, dlg.FileName);
                        TxtLog.Text = "Sealed " + picked.Count + " mods into " + dlg.FileName
                            + " as " + packName + ".";
                    }
                }
                else
                {
                    ModPackageService.PackageZip(picked, dlg.FileName);
                    TxtLog.Text = "Packaged " + picked.Count + " mod" + (picked.Count == 1 ? "" : "s") + " into " + dlg.FileName
                        + "\nSource and build.bat are left out of the zip.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Package mods", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnPackageInstall_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Install a mod archive",
                Filter = "Mod archive (*.ssfe;*.zip;*.rar;*.7z)|*.ssfe;*.zip;*.rar;*.7z"
            };
            if (dlg.ShowDialog() != true)
                return;
            try
            {
                if (dlg.FileName.EndsWith(".ssfe", StringComparison.OrdinalIgnoreCase)
                    && !SsfeSeal.Accept(dlg.FileName))
                {
                    MessageBox.Show("That .ssfe package did not check out.", "Package mods",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var placed = ModPackageService.InstallArchive(_shell, dlg.FileName);
                if (placed.Count == 0)
                {
                    MessageBox.Show("No mods with an info.json were found in that archive.", "Package mods",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                TxtLog.Text = "Installed " + placed.Count + " mod" + (placed.Count == 1 ? "" : "s") + ":\n"
                    + string.Join("\n", placed.Select(p => p.Kind + "  " + p.Dest));
                ReloadPackageList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Package mods", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private List<PackageModEntry>? PackageZipSelection()
        {
            if (LstPackageZip.ItemsSource is not IEnumerable<PackageModEntry> all)
            {
                MessageBox.Show("Pick a mod to package.", "Package mods", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            var checkedMods = all.Where(m => m.Include).ToList();
            if (checkedMods.Count == 0)
            {
                MessageBox.Show("Check at least one mod.", "Package mods", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return checkedMods;
        }

        private bool AskPackInfo(out string name, out string description)
        {
            name = "";
            description = "";
            var nameBox = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 8) };
            var descBox = new System.Windows.Controls.TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 72,
                Margin = new Thickness(0, 0, 0, 12)
            };
            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var ok = new System.Windows.Controls.Button { Content = "Package", Padding = new Thickness(14, 6, 14, 6), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Cancel", Padding = new Thickness(14, 6, 14, 6), IsCancel = true };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var form = new StackPanel { Margin = new Thickness(16) };
            form.Children.Add(new TextBlock { Text = "Name", Margin = new Thickness(0, 0, 0, 4) });
            form.Children.Add(nameBox);
            form.Children.Add(new TextBlock { Text = "Description", Margin = new Thickness(0, 0, 0, 4) });
            form.Children.Add(descBox);
            form.Children.Add(buttons);
            var win = new Window
            {
                Title = "Package mods",
                Width = 420,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = form,
                ResizeMode = ResizeMode.NoResize
            };
            var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.IsLoaded);
            if (owner != null)
                win.Owner = owner;
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(descBox.Text))
                {
                    MessageBox.Show(win, "Name and description are required.", "Package mods", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                win.DialogResult = true;
            };
            if (win.ShowDialog() != true)
                return false;
            name = nameBox.Text.Trim();
            description = descBox.Text.Trim();
            return true;
        }

        private void BtnStage_Click(object sender, RoutedEventArgs e) => Show("stage", "Stage");
        private void BtnCharacter_Click(object sender, RoutedEventArgs e) => Show("character", "Character");

        private void BtnCostume_Click(object sender, RoutedEventArgs e)
        {
            var character = CmbCostumeCharacter.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(character))
            {
                MessageBox.Show("Pick a character.", "Costume", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                ShellDirectLaunchService.Launch(_shell, ShellDirectLaunchService.ScreenVaultCostumes,
                    new Dictionary<string, object?>
                    {
                        ["vaultAction"] = "costumes",
                        ["vaultPaletteChar"] = character
                    });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Costume", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnMusic_Click(object sender, RoutedEventArgs e) => Show("music", "Music");

        public Action<IReadOnlyList<CachedSongInfo>, int>? PlayInManager { get; set; }

        private readonly MusicLibraryCacheService _musicCache = new();
        private MusicLibraryCache? _musicLib;
        private bool _musicLoading;
        private string _listKind = "custom";

        private sealed class TrackRow
        {
            public string Id { get; init; } = "";
            public string Label { get; init; } = "";
            public override string ToString() => Label;
        }

        private void LoadMusicEditor()
        {
            _musicLib = _musicCache.EnsureFastLibrary(_shell);
            var songs = _musicLib.Songs ?? new List<CachedSongInfo>();
            _musicLoading = true;
            CmbStageAddSong.ItemsSource = songs;
            CmbUserAddSong.ItemsSource = songs;
            if (CmbStageAddSong.Items.Count > 0 && CmbStageAddSong.SelectedIndex < 0)
                CmbStageAddSong.SelectedIndex = 0;
            if (CmbUserAddSong.Items.Count > 0 && CmbUserAddSong.SelectedIndex < 0)
                CmbUserAddSong.SelectedIndex = 0;

            var stages = _musicLib.StagePlaylists ?? new List<CachedStagePlaylist>();
            foreach (var stage in stages)
            {
                if (string.IsNullOrWhiteSpace(stage.StageName))
                    stage.StageName = stage.StageId;
            }
            CmbMusicStage.ItemsSource = null;
            CmbMusicStage.ItemsSource = stages;
            CmbMusicStage.DisplayMemberPath = "StageName";
            if (CmbMusicStage.Items.Count > 0 && CmbMusicStage.SelectedIndex < 0)
                CmbMusicStage.SelectedIndex = 0;

            _musicLib.UserPlaylists ??= new List<UserMusicPlaylist>();
            BindUserLists();
            var nowLists = new List<UserMusicPlaylist>();
            nowLists.AddRange(_musicLib.UserPlaylists);
            foreach (var stage in stages)
            {
                nowLists.Add(new UserMusicPlaylist
                {
                    Id = "stage:" + stage.StageId,
                    Name = "Stage: " + stage.StageName,
                    TrackIds = stage.TrackIds ?? new List<string>()
                });
            }
            CmbNowSong.ItemsSource = songs;
            if (CmbNowSong.Items.Count > 0 && CmbNowSong.SelectedIndex < 0)
                CmbNowSong.SelectedIndex = 0;
            CmbNowPlaylist.ItemsSource = nowLists;
            CmbNowPlaylist.DisplayMemberPath = "Name";
            if (CmbNowPlaylist.Items.Count > 0 && CmbNowPlaylist.SelectedIndex < 0)
                CmbNowPlaylist.SelectedIndex = 0;
            _musicLoading = false;
            FillStageTracks();
            FillUserTracks();
            ShowMusicMenu();
        }

        private void BindUserLists()
        {
            if (_musicLib == null) return;
            var lists = _musicLib.UserPlaylists
                .Where(p => string.Equals(string.IsNullOrWhiteSpace(p.Kind) ? "custom" : p.Kind, _listKind, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _musicLoading = true;
            CmbUserPlaylist.ItemsSource = null;
            CmbUserPlaylist.ItemsSource = lists;
            CmbUserPlaylist.DisplayMemberPath = "Name";
            if (CmbUserPlaylist.Items.Count > 0)
                CmbUserPlaylist.SelectedIndex = 0;
            _musicLoading = false;
        }

        private void ShowMusicMenu()
        {
            HideTrail = false;
            MusicMenu.Visibility = Visibility.Visible;
            MusicStagePage.Visibility = Visibility.Collapsed;
            MusicListPage.Visibility = Visibility.Collapsed;
            MusicUploadPage.Visibility = Visibility.Collapsed;
            MusicNowPage.Visibility = Visibility.Collapsed;
            SectionChanged?.Invoke();
        }

        private void OpenMusicSub()
        {
            HideTrail = true;
            MusicMenu.Visibility = Visibility.Collapsed;
            SectionChanged?.Invoke();
        }

        private void BtnMusicMenu_Click(object sender, RoutedEventArgs e) => ShowMusicMenu();

        private void BtnMusicStage_Click(object sender, RoutedEventArgs e)
        {
            OpenMusicSub();
            MusicStagePage.Visibility = Visibility.Visible;
        }

        private void BtnMusicSeries_Click(object sender, RoutedEventArgs e) => OpenListPage("series", "Series playlists");

        private void BtnMusicCustom_Click(object sender, RoutedEventArgs e) => OpenListPage("custom", "Custom playlists");

        private void OpenListPage(string kind, string title)
        {
            _listKind = kind;
            TxtMusicListTitle.Text = title;
            BindUserLists();
            FillUserTracks();
            OpenMusicSub();
            MusicListPage.Visibility = Visibility.Visible;
        }

        private void BtnMusicUpload_Click(object sender, RoutedEventArgs e)
        {
            OpenMusicSub();
            MusicUploadPage.Visibility = Visibility.Visible;
        }

        private void BtnMusicNow_Click(object sender, RoutedEventArgs e)
        {
            OpenMusicSub();
            MusicNowPage.Visibility = Visibility.Visible;
        }

        private void BtnNowSong_Click(object sender, RoutedEventArgs e)
        {
            if (CmbNowSong.SelectedItem is not CachedSongInfo song) return;
            PlayIds(new[] { song.Id }, 0);
        }

        private void BtnNowPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (CmbNowPlaylist.SelectedItem is not UserMusicPlaylist list) return;
            PlayIds(list.TrackIds, 0);
        }

        private void BtnUploadBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "MP3|*.mp3" };
            if (dlg.ShowDialog() == true)
                TxtUploadFile.Text = dlg.FileName;
        }

        private async void BtnUploadSong_Click(object sender, RoutedEventArgs e)
        {
            var file = TxtUploadFile.Text?.Trim() ?? "";
            var id = TxtUploadId.Text?.Trim() ?? "";
            if (!File.Exists(file) || string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show("Pick an MP3 and enter a track id.", "Upload", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var mappings = new MappingsEditService();
                var doc = await mappings.LoadDocumentAsync(_shell, exportIfMissing: false);
                await new MusicEditService().CreateMusicFromMp3Async(
                    _shell, file, id, 0, TxtUploadName.Text?.Trim() ?? id, TxtUploadArtist.Text?.Trim() ?? "", doc);
                await mappings.SaveDocumentAsync(_shell, doc);
                LoadMusicEditor();
                MessageBox.Show("Song uploaded.", "Upload", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Upload", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string SongLabel(string id)
        {
            var song = _musicLib?.Songs?.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            return song == null ? id : song.DisplayLabel;
        }

        private void FillStageTracks()
        {
            LstStageTracks.Items.Clear();
            if (CmbMusicStage.SelectedItem is not CachedStagePlaylist stage)
                return;
            foreach (var id in stage.TrackIds ?? new List<string>())
                LstStageTracks.Items.Add(new TrackRow { Id = id, Label = SongLabel(id) });
        }

        private void FillUserTracks()
        {
            LstUserTracks.Items.Clear();
            if (CmbUserPlaylist.SelectedItem is not UserMusicPlaylist list)
                return;
            foreach (var id in list.TrackIds ?? new List<string>())
                LstUserTracks.Items.Add(new TrackRow { Id = id, Label = SongLabel(id) });
        }

        private void CmbMusicStage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_musicLoading) return;
            FillStageTracks();
        }

        private void CmbUserPlaylist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_musicLoading) return;
            FillUserTracks();
        }

        private void BtnStageAdd_Click(object sender, RoutedEventArgs e)
        {
            if (CmbMusicStage.SelectedItem is not CachedStagePlaylist stage)
                return;
            if (CmbStageAddSong.SelectedItem is not CachedSongInfo song || string.IsNullOrWhiteSpace(song.Id))
                return;
            stage.TrackIds ??= new List<string>();
            if (!stage.TrackIds.Any(id => string.Equals(id, song.Id, StringComparison.OrdinalIgnoreCase)))
                stage.TrackIds.Add(song.Id);
            FillStageTracks();
        }

        private void BtnStageRemove_Click(object sender, RoutedEventArgs e)
        {
            if (CmbMusicStage.SelectedItem is not CachedStagePlaylist stage)
                return;
            if (LstStageTracks.SelectedItem is not TrackRow row)
                return;
            stage.TrackIds?.RemoveAll(id => string.Equals(id, row.Id, StringComparison.OrdinalIgnoreCase));
            FillStageTracks();
        }

        private async void BtnStageSave_Click(object sender, RoutedEventArgs e)
        {
            if (_musicLib == null || CmbMusicStage.SelectedItem is not CachedStagePlaylist stage)
                return;
            try
            {
                _musicCache.Save(_musicLib);
                var swf = MenuBackgroundStore.FindStageFile(_shell, stage.StageId);
                if (swf == null)
                {
                    MessageBox.Show("Saved in the mod manager. The stage file was not found, so the game playlist was left as it is.",
                        "Stage playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                await new MusicEditService().WritePlaylistAsync(swf, stage.TrackIds ?? new List<string>());
                MessageBox.Show("Stage playlist saved.", "Stage playlist", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Stage playlist", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnStagePlay_Click(object sender, RoutedEventArgs e)
        {
            if (CmbMusicStage.SelectedItem is not CachedStagePlaylist stage)
                return;
            PlayIds(stage.TrackIds, LstStageTracks.SelectedIndex);
        }

        private void BtnUserNew_Click(object sender, RoutedEventArgs e)
        {
            if (_musicLib == null) return;
            var name = "Playlist " + (_musicLib.UserPlaylists.Count + 1);
            var created = new UserMusicPlaylist
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Kind = _listKind,
                TrackIds = new List<string>()
            };
            _musicLib.UserPlaylists.Add(created);
            _musicCache.Save(_musicLib);
            BindUserLists();
            CmbUserPlaylist.SelectedItem = created;
            FillUserTracks();
        }

        private void BtnUserAdd_Click(object sender, RoutedEventArgs e)
        {
            if (CmbUserPlaylist.SelectedItem is not UserMusicPlaylist list)
                return;
            if (CmbUserAddSong.SelectedItem is not CachedSongInfo song || string.IsNullOrWhiteSpace(song.Id))
                return;
            list.TrackIds ??= new List<string>();
            if (!list.TrackIds.Any(id => string.Equals(id, song.Id, StringComparison.OrdinalIgnoreCase)))
                list.TrackIds.Add(song.Id);
            SaveUserLibrary();
            FillUserTracks();
        }

        private void BtnUserRemove_Click(object sender, RoutedEventArgs e)
        {
            if (CmbUserPlaylist.SelectedItem is not UserMusicPlaylist list)
                return;
            if (LstUserTracks.SelectedItem is not TrackRow row)
                return;
            list.TrackIds?.RemoveAll(id => string.Equals(id, row.Id, StringComparison.OrdinalIgnoreCase));
            SaveUserLibrary();
            FillUserTracks();
        }

        private void BtnUserDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_musicLib == null || CmbUserPlaylist.SelectedItem is not UserMusicPlaylist list)
                return;
            _musicLib.UserPlaylists.Remove(list);
            SaveUserLibrary();
            BindUserLists();
            FillUserTracks();
        }

        private void BtnUserPlay_Click(object sender, RoutedEventArgs e)
        {
            if (CmbUserPlaylist.SelectedItem is not UserMusicPlaylist list)
                return;
            PlayIds(list.TrackIds, LstUserTracks.SelectedIndex);
        }

        private void PlayIds(IReadOnlyList<string>? ids, int selectedIndex)
        {
            if (PlayInManager == null || _musicLib?.Songs == null || ids == null || ids.Count == 0)
                return;
            var songs = new List<CachedSongInfo>();
            foreach (var id in ids)
            {
                var song = _musicLib.Songs.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
                if (song != null)
                    songs.Add(song);
            }
            if (songs.Count == 0)
                return;
            var index = selectedIndex >= 0 && selectedIndex < songs.Count ? selectedIndex : 0;
            PlayInManager(songs, index);
        }

        private void SaveUserLibrary()
        {
            if (_musicLib != null)
                _musicCache.Save(_musicLib);
        }

        private void BtnOpenJukebox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShellDirectLaunchService.Launch(_shell, ShellDirectLaunchService.ScreenJukebox);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Music", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnManageCss_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShellDirectLaunchService.Launch(_shell, ShellDirectLaunchService.ScreenCssEdit);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Character select", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnNewEvent_Click(object sender, RoutedEventArgs e)
        {
            await LoadRosterAsync();
            EnsureEventForm();
            SetEventPackMode("");
            TxtEventName.Text = "";
            TxtEventDesc.Text = "";
            _eventPreviewPath = "";
            _eventPreviewCustom = false;
            ShowFighterCount(1);
            ShowDefaultPreview();
            _eventFilling = true;
            CmbEventSource.ItemsSource = EventAuthorService.ListSources(_shell);
            _eventFilling = false;
            if (CmbEventSource.Items.Count > 0)
                CmbEventSource.SelectedIndex = 0;
            Show("newEvent", "Make a new event");
        }

        private void EnsureEventForm()
        {
            FillEventPicks();
            if (_eventSlots.Count > 0)
                return;
            var chars = ShellConfigureLaunchService.ListCharacters(_shell);
            for (var i = 0; i < 4; i++)
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                var line = new WrapPanel();
                var slot = new EventSlot
                {
                    Character = new System.Windows.Controls.ComboBox { Width = 140, ItemsSource = chars, IsEditable = true },
                    Human = new System.Windows.Controls.CheckBox { VerticalAlignment = VerticalAlignment.Center },
                    Level = new System.Windows.Controls.TextBox { Width = 36 },
                    Lives = new System.Windows.Controls.TextBox { Width = 36 },
                    Costume = new System.Windows.Controls.TextBox { Width = 48, Text = "-1" }
                };
                slot.Human.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
                line.Children.Add(Field("Fighter", slot.Character));
                line.Children.Add(Field("Costume", slot.Costume));
                line.Children.Add(Field("Human", slot.Human));
                line.Children.Add(Field("Level", slot.Level));
                line.Children.Add(Field("Stocks", slot.Lives));
                row.Children.Add(line);
                slot.Row = row;
                row.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
                EventFighterHost.Children.Add(row);
                _eventSlots.Add(slot);
            }
            FillEventPicks();
        }

        private static StackPanel Field(string label, UIElement control)
        {
            var host = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var text = new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            host.Children.Add(text);
            host.Children.Add(control);
            return host;
        }

        private static void Park(StackPanel dock, string label, UIElement control)
        {
            dock.Children.Add(Field(label, control));
        }

        private System.Windows.Controls.CheckBox ItemBox(string id)
        {
            var box = new System.Windows.Controls.CheckBox
            {
                Content = id,
                Tag = id,
                Margin = new Thickness(0, 0, 10, 4)
            };
            box.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
            box.Checked += (_, _) => { if (!_eventFilling) _itemTouched.Add(id); };
            box.Unchecked += (_, _) => { if (!_eventFilling) _itemTouched.Add(id); };
            _eventNeedBoxes.Add(box);
            return box;
        }

        private void FillEventPicks()
        {
            var stages = StagePicks();
            var selectedStage = PickId(CmbEventStage);
            var selectedBg = PickId(CmbEventStageBg);
            var selectedMusic = PickId(CmbEventMusic);
            CmbEventStage.ItemsSource = stages.Where(s => s.Id.Length > 0).ToList();
            var backgrounds = new List<EventFormPick> { new("", "(stage picture)") };
            backgrounds.AddRange(stages.Where(s => s.Id.Length > 0));
            foreach (var pack in StageBackgroundStore.List(_shell))
                backgrounds.Add(new EventFormPick("pack:" + pack.Id, "Background pack: " + pack.Label));
            CmbEventStageBg.ItemsSource = backgrounds;
            CmbEventMusic.ItemsSource = MusicPicks();
            ChoosePick(CmbEventStage, selectedStage);
            ChoosePick(CmbEventStageBg, selectedBg);
            ChoosePick(CmbEventMusic, selectedMusic);
        }

        private List<EventFormPick> StagePicks()
        {
            var list = new List<EventFormPick>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_doc != null)
            {
                foreach (var stage in ShellStageCloneService.ListPlayableStages(_shell, _doc))
                {
                    if (!seen.Add(stage.Id)) continue;
                    list.Add(new EventFormPick(stage.Id, stage.DisplayName));
                }
            }
            if (list.Count == 0)
            {
                foreach (var id in ShellConfigureLaunchService.ListStages(_shell))
                {
                    if (seen.Add(id))
                        list.Add(new EventFormPick(id, id));
                }
            }
            var extra = ShellWorkspaceService.StageExpansionsDirectory(_shell);
            if (Directory.Exists(extra))
            {
                foreach (var dir in Directory.GetDirectories(extra))
                {
                    var swf = Directory.GetFiles(dir, "*.swf").FirstOrDefault();
                    if (swf == null) continue;
                    var id = Path.GetFileNameWithoutExtension(swf);
                    if (!seen.Add(id)) continue;
                    list.Add(new EventFormPick(id, id));
                }
            }
            return list.OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private List<EventFormPick> MusicPicks()
        {
            var list = new List<EventFormPick> { new("", "(stage default)") };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                _musicLib ??= _musicCache.EnsureFastLibrary(_shell);
            }
            catch
            {
                /* names fall back to ids */
            }
            if (_musicLib?.Songs != null)
            {
                foreach (var song in _musicLib.Songs.OrderBy(s => s.DisplayLabel, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(song.Id) || !seen.Add(song.Id)) continue;
                    list.Add(new EventFormPick(song.Id, song.DisplayLabel));
                }
            }
            foreach (var id in ShellConfigureLaunchService.ListMusic(_shell))
            {
                if (seen.Add(id))
                    list.Add(new EventFormPick(id, id));
            }
            return list;
        }

        private void OpenFighterMore(EventSlot slot)
        {
            try
            {
                var rows = slot.Dock.Children.Cast<UIElement>().ToList();
                foreach (var row in rows)
                    slot.Dock.Children.Remove(row);
                var stack = new StackPanel { Margin = new Thickness(16) };
                foreach (var row in rows)
                    stack.Children.Add(row);
                var win = new Window
                {
                    Title = "Fighter",
                    Width = 420,
                    Height = 560,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new ScrollViewer { Content = stack }
                };
                win.SetResourceReference(Window.BackgroundProperty, "WindowBackgroundBrush");
                var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.IsLoaded);
                if (owner != null)
                    win.Owner = owner;
                win.Closed += (_, _) =>
                {
                    var back = stack.Children.Cast<UIElement>().ToList();
                    foreach (var row in back)
                    {
                        stack.Children.Remove(row);
                        slot.Dock.Children.Add(row);
                    }
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "Fighter", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void CmbEventSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_eventFilling || CmbEventSource.SelectedItem is not EventSourceChoice source)
                return;
            if (string.IsNullOrWhiteSpace(source.ClassName))
                return;
            EventLoadOverlay.Visibility = Visibility.Visible;
            try
            {
                await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Render);
                EnsureEventForm();
                Log("Reading " + source.ClassName + "…");
                var path = string.IsNullOrWhiteSpace(source.Path) || !source.Path.EndsWith(".as", StringComparison.OrdinalIgnoreCase)
                    ? await EventAuthorService.ExportVanillaSourceAsync(_shell, source.ClassName)
                    : source.Path;
                var text = File.ReadAllText(path);
                if (path != source.Path)
                    try { File.Delete(path); } catch { /* temp script */ }
                RememberClone(text);
                ApplyEventRecipe(_eventCloneRecipe!, source.Label);
                TxtEventDesc.Text = EventMenuText.Description(_shell, source);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
            finally
            {
                EventLoadOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void RememberClone(string text)
        {
            _eventCloneSource = text;
            _eventCloneRecipe = EventRecipe.Parse(text);
        }

        private string CloneScript(EventInfoDraft draft)
        {
            if (string.IsNullOrEmpty(_eventCloneSource) || _eventCloneRecipe == null)
                throw new InvalidOperationException("Pick an event to clone.");
            var after = EventRecipe.Parse(_eventCloneSource);
            after.Stage = PickId(CmbEventStage);
            after.StageBackground = StageBackgroundId();
            after.Music = PickId(CmbEventMusic);
            after.UsingLives = ChkEventStocks.IsChecked == true;
            after.UsingTime = ChkEventTime.IsChecked == true;
            after.Hazards = ChkEventHazards.IsChecked == true;
            after.TeamDamage = ChkEventTeam.IsChecked == true;
            after.Lives = int.TryParse(TxtEventLives.Text, out var lives) ? lives : after.Lives;
            after.Minutes = int.TryParse(TxtEventMinutes.Text, out var minutes) ? minutes : after.Minutes;
            for (var i = 0; i < _eventSlots.Count && i < after.Fighters.Count && i < _eventCloneRecipe.Fighters.Count; i++)
            {
                if (_eventSlots[i].Row.Visibility != Visibility.Visible)
                    continue;
                var who = _eventSlots[i].Character.Text?.Trim() ?? "";
                if (who.Length == 0)
                    continue;
                var fighter = after.Fighters[i];
                fighter.CharacterFromPlayer = who == "(player)";
                fighter.Character = fighter.CharacterFromPlayer ? "(player)" : who;
                fighter.Human = _eventSlots[i].Human.IsChecked == true;
                if (int.TryParse(_eventSlots[i].Level.Text, out var level))
                    fighter.Level = level;
                if (int.TryParse(_eventSlots[i].Lives.Text, out var stocks))
                    fighter.Lives = stocks;
                if (int.TryParse(_eventSlots[i].Costume.Text, out var costume))
                    fighter.Costume = costume;
            }
            return EventCloneEdit.Apply(_eventCloneSource, _eventCloneRecipe, after);
        }

        private void ApplyEventRecipe(EventRecipe recipe, string label)
        {
            _eventFilling = true;
            try
            {
                ChoosePick(CmbEventStage, recipe.Stage);
                if (recipe.StageBackground.Length > 0)
                    ChoosePick(CmbEventStageBg, recipe.StageBackground);
                ChoosePick(CmbEventMusic, recipe.Music);
                ChkEventStocks.IsChecked = recipe.UsingLives;
                TxtEventLives.Text = recipe.Lives.ToString();
                ChkEventTime.IsChecked = recipe.UsingTime;
                TxtEventMinutes.Text = recipe.Minutes.ToString();
                ChkEventHazards.IsChecked = recipe.Hazards;
                ChkEventTeam.IsChecked = recipe.TeamDamage;
                if (recipe.Kind == EventKind.MultiMan && recipe.StockOverride > 0)
                    TxtEventLives.Text = recipe.StockOverride.ToString();
                _eventCast = recipe.Fighters.Select(f => f.Character).ToList();
                _eventStage = recipe.Stage;
                _eventMusic = recipe.Music;
                var shown = Math.Clamp(recipe.Fighters.Count, 1, _eventSlots.Count);
                for (var i = 0; i < _eventSlots.Count; i++)
                {
                    var slot = _eventSlots[i];
                    slot.Row.Visibility = i < shown ? Visibility.Visible : Visibility.Collapsed;
                    if (i < recipe.Fighters.Count && i < shown)
                        ShowFighter(slot, recipe.Fighters[i]);
                    else
                        ClearFighter(slot);
                }
                _eventFighterShown = shown;
            }
            finally
            {
                _eventFilling = false;
            }
        }

        private static void ChooseCombo(System.Windows.Controls.ComboBox box, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                box.SelectedIndex = -1;
                box.Text = "";
                return;
            }
            if (box.ItemsSource is IEnumerable<string> items && !items.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                var list = items.ToList();
                list.Add(value);
                box.ItemsSource = list;
            }
            box.SelectedItem = value;
            if (box.SelectedItem == null)
                box.Text = value;
        }

        private static string PickId(System.Windows.Controls.ComboBox box)
        {
            if (box.SelectedItem is EventFormPick pick)
                return pick.Id;
            return box.Text ?? "";
        }

        private string StageBackgroundId()
        {
            var id = PickId(CmbEventStageBg).Trim();
            return id.StartsWith("pack:", StringComparison.Ordinal) ? "" : id;
        }

        private static void ChoosePick(System.Windows.Controls.ComboBox box, string value)
        {
            if (box.ItemsSource is not IEnumerable<EventFormPick> items)
            {
                ChooseCombo(box, value);
                return;
            }
            var list = items.ToList();
            var hit = list.FirstOrDefault(p => string.Equals(p.Id, value ?? "", StringComparison.OrdinalIgnoreCase));
            if (hit == null && !string.IsNullOrEmpty(value))
            {
                hit = new EventFormPick(value, value);
                list.Add(hit);
                box.ItemsSource = list;
            }
            box.SelectedItem = hit ?? list.FirstOrDefault();
        }


        private List<string> RewriteExtras()
        {
            var currentCast = new List<string>();
            foreach (var slot in _eventSlots)
            {
                var who = slot.Character.Text?.Trim() ?? "";
                if (who.Length > 0) currentCast.Add(who);
            }
            var stage = PickId(CmbEventStage);
            var music = PickId(CmbEventMusic);
            var list = new List<string>();
            foreach (var method in _eventExtras)
            {
                var text = method;
                if (_eventStage.Length > 0 && stage.Length > 0)
                    text = text.Replace("\"" + _eventStage + "\"", "\"" + stage + "\"", StringComparison.Ordinal);
                if (_eventMusic.Length > 0 && music.Length > 0)
                    text = text.Replace("\"" + _eventMusic + "\"", "\"" + music + "\"", StringComparison.Ordinal);
                for (var i = 0; i < _eventCast.Count && i < currentCast.Count; i++)
                {
                    var from = _eventCast[i];
                    var to = currentCast[i];
                    if (from.Length == 0 || to.Length == 0 || from == "(player)" || to == "(player)") continue;
                    if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) continue;
                    text = text.Replace("\"" + from + "\"", "\"" + to + "\"", StringComparison.Ordinal);
                }
                list.Add(text);
            }
            return list;
        }


        private static void ShowFighter(EventSlot slot, EventFighter fighter)
        {
            ChooseCombo(slot.Character, fighter.CharacterFromPlayer ? "(player)" : fighter.Character);
            slot.Human.IsChecked = fighter.Human;
            slot.Level.Text = fighter.Level.ToString();
            slot.Lives.Text = fighter.Lives.ToString();
            slot.Costume.Text = fighter.CostumeFromPlayer ? "-1" : fighter.Costume.ToString();
        }

        private static void ClearFighter(EventSlot slot)
        {
            slot.Character.Text = "";
            slot.Human.IsChecked = false;
            slot.Level.Text = "";
            slot.Lives.Text = "";
            slot.Costume.Text = "-1";
        }

        private EventFighter ReadFighter(EventSlot slot, string who, int matchLives)
        {
            var fromPlayer = who == "(player)";
            return new EventFighter
            {
                Character = fromPlayer ? "(player)" : who,
                CharacterFromPlayer = fromPlayer,
                Human = slot.Human.IsChecked == true,
                Level = int.TryParse(slot.Level.Text, out var level) ? level : 9,
                Lives = int.TryParse(slot.Lives.Text, out var lives) ? lives : matchLives,
                Costume = int.TryParse(slot.Costume.Text, out var costume) ? costume : -1
            };
        }

        private void SetEventPackMode(string? dir)
        {
            _eventPackDir = dir ?? "";
            if (_eventPackDir.Length == 0)
                _eventPackId = "";
            var editing = _eventPackDir.Length > 0;
            BtnCreateEvent.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            BtnCreateTestEvent.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            BtnSaveEventPack.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            BtnTestEvent.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        private EventInfoDraft EventDraft()
        {
            var draft = new EventInfoDraft
            {
                Name = TxtEventName.Text,
                Description = TxtEventDesc.Text,
                Creator = new SettingsService().GetEventCreator(),
                ChooseCharacter = ChkEventChoose.IsChecked == true,
                StageBackground = PickId(CmbEventStageBg),
                Id = _eventPackDir.Length > 0 ? _eventPackId : "",
                PreviewPath = _eventPreviewPath
            };
            if (_eventSlots.Count > 0)
            {
                var slot = _eventSlots[0];
                var who = slot.Character.Text?.Trim() ?? "";
                if (who.Length > 0 && !who.Equals("(player)", StringComparison.OrdinalIgnoreCase))
                {
                    draft.PreviewCharacter = who;
                    draft.PreviewCostume = int.TryParse(slot.Costume.Text, out var costume) ? costume : -1;
                }
            }
            return draft;
        }


        private void ShowFighterCount(int count)
        {
            if (_eventSlots.Count == 0)
                return;
            count = Math.Clamp(count, 1, _eventSlots.Count);
            _eventFighterShown = count;
            for (var i = 0; i < _eventSlots.Count; i++)
            {
                var show = i < count;
                _eventSlots[i].Row.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show)
                    ClearFighter(_eventSlots[i]);
            }
        }

        private void TxtEventName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_eventPreviewCustom || !IsLoaded)
                return;
            ShowDefaultPreview();
        }

        private void ShowDefaultPreview()
        {
            var title = string.IsNullOrWhiteSpace(TxtEventName.Text) ? "Event" : TxtEventName.Text.Trim();
            ImgEventPreview.Source = RenderPreview(title);
        }

        private void BtnEventMusicPreview_Click(object sender, RoutedEventArgs e)
        {
            var id = PickId(CmbEventMusic);
            if (string.IsNullOrEmpty(id))
            {
                MessageBox.Show("Pick a track.", "Music");
                return;
            }
            try
            {
                _musicLib ??= _musicCache.EnsureFastLibrary(_shell);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Music", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var song = _musicLib?.Songs?.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (song == null)
            {
                MessageBox.Show("That track has no preview.", "Music");
                return;
            }
            if (PlayInManager == null)
            {
                MessageBox.Show("The mini player is not open.", "Music");
                return;
            }
            PlayInManager(new[] { song }, 0);
        }

        private void BtnCompileEvent_Click(object sender, RoutedEventArgs e)
        {
            _eventFilling = true;
            CmbCompilePack.ItemsSource = EventAuthorService.ListPacks(_shell);
            CmbCompilePack.SelectedIndex = -1;
            _eventFilling = false;
            Show("compile", "Compile an event");
        }

        private async void BtnCreateEvent_Click(object sender, RoutedEventArgs e) =>
            await CreateEventAsync(test: false);

        private async void BtnCreateTestEvent_Click(object sender, RoutedEventArgs e) =>
            await CreateEventAsync(test: true);

        private async Task CreateEventAsync(bool test)
        {
            try
            {
                if (CmbEventSource.SelectedItem is not EventSourceChoice source)
                {
                    MessageBox.Show("Pick an event.", "Events");
                    return;
                }
                var eventName = TxtEventName.Text.Trim();
                if (string.IsNullOrEmpty(EventAuthorService.SuggestId(eventName)))
                {
                    MessageBox.Show("Event name needs letters or numbers.", "Events");
                    return;
                }
                if (string.IsNullOrWhiteSpace(TxtEventDesc.Text))
                {
                    MessageBox.Show("Description is required.", "Events");
                    return;
                }
                if (EventNames.IsTaken(TakenEventNames(), eventName))
                {
                    MessageBox.Show("An event already uses that name.", "Events");
                    return;
                }
                if (string.IsNullOrWhiteSpace(_eventPreviewPath))
                    _eventPreviewPath = WriteNamedPreview(eventName);
                var draft = EventDraft();
                var script = CloneScript(draft);
                var scriptPath = Path.Combine(Path.GetTempPath(), "ssf2mm-event-" + Guid.NewGuid().ToString("N") + ".as");
                File.WriteAllText(scriptPath, script);
                string dir;
                try
                {
                    dir = EventAuthorService.CreateEvent(_shell, scriptPath, "", draft);
                }
                finally
                {
                    try { File.Delete(scriptPath); } catch { /* temp script */ }
                }
                Log("Created " + dir);
                Log("Compiling…");
                var (ok, log) = await EventAuthorService.CompileAsync(dir);
                Log(log);
                if (!ok)
                {
                    MessageBox.Show("The event was written, and the compile failed.\n\n" + log,
                        "Events", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!test)
                {
                    MessageBox.Show("Created " + dir, "Events");
                    return;
                }
                var id = EventAuthorService.PackEventId(dir);
                if (string.IsNullOrWhiteSpace(id))
                {
                    MessageBox.Show("The pack has no event id.", "Events");
                    return;
                }
                Log("Starting event " + id);
                ShellConfigureLaunchService.Launch(_shell, new LaunchConfigurePrefs
                {
                    StartingMenu = "events",
                    EventId = id
                });
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "Events", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnEventPreview_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PNG|*.png",
                Title = "Event preview"
            };
            if (dlg.ShowDialog() != true)
                return;
            _eventPreviewPath = dlg.FileName;
            _eventPreviewCustom = true;
            ImgEventPreview.Source = new BitmapImage(new Uri(_eventPreviewPath));
        }

        private static BitmapSource RenderPreview(string title)
        {
            const int w = 320;
            const int h = 180;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 28)), null, new Rect(0, 0, w, h));
                var text = new FormattedText(
                    string.IsNullOrWhiteSpace(title) ? "Event" : title,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    28,
                    Brushes.White,
                    1);
                dc.DrawText(text, new Point(16, (h - text.Height) / 2));
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        private string WriteNamedPreview(string title)
        {
            var bmp = RenderPreview(title);
            var path = Path.Combine(Path.GetTempPath(), "ssf2mm-event-preview-" + Guid.NewGuid().ToString("N") + ".png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var stream = File.Create(path))
                enc.Save(stream);
            ImgEventPreview.Source = new BitmapImage(new Uri(path));
            return path;
        }

        private IEnumerable<string> TakenEventNames()
        {
            foreach (var ev in ShellConfigureLaunchService.ListEvents(_shell))
            {
                yield return ev.Label;
                yield return ev.Id;
            }
            var root = Path.Combine(_shell, "expansions", "events");
            if (!Directory.Exists(root))
                yield break;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var folder = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(folder) || folder.StartsWith('_') || folder.StartsWith('.'))
                    continue;
                yield return folder;
                var infoPath = Path.Combine(dir, "info.json");
                if (!File.Exists(infoPath))
                    continue;
                JObject info;
                try { info = JObject.Parse(File.ReadAllText(infoPath)); }
                catch { continue; }
                var name = info["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    yield return name;
                var id = info["id"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    yield return id;
            }
        }

        private async Task OpenEventPack(EventSourceChoice pack)
        {
            await LoadRosterAsync();
            Show("newEvent", "Edit event");
            EventLoadOverlay.Visibility = Visibility.Visible;
            try
            {
                await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Render);
                EnsureEventForm();
                var script = EventAuthorService.PackScript(pack.Path);
                if (script == null)
                    throw new FileNotFoundException("That pack has no event script.", pack.Path);
                Log("Reading " + pack.Label + "…");
                var packText = File.ReadAllText(script);
                RememberClone(packText);
                ApplyEventRecipe(_eventCloneRecipe!, pack.Label);
                var infoPath = Path.Combine(pack.Path, "info.json");
                if (File.Exists(infoPath))
                {
                    var info = JObject.Parse(File.ReadAllText(infoPath));
                    var name = info["name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        TxtEventName.Text = name;
                    var description = info["description"]?.ToString();
                    if (description != null)
                        TxtEventDesc.Text = description;
                    ChoosePick(CmbEventStageBg, info["stageBackground"]?.ToString() ?? "");
                }
                _eventPackId = EventAuthorService.PackEventId(pack.Path);
                SetEventPackMode(pack.Path);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "Events", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                EventLoadOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<(bool Ok, string Log)> SaveOpenPackAsync()
        {
            if (string.IsNullOrWhiteSpace(_eventPackDir))
                return (false, "Pick an event pack.");
            var script = EventAuthorService.PackScript(_eventPackDir);
            if (script == null)
                return (false, "That pack has no event script.");
            var draft = EventDraft();
            var className = Path.GetFileNameWithoutExtension(script);
            var source = CloneScript(draft);
            if (!source.Contains("class " + className))
            {
                var oldClass = System.Text.RegularExpressions.Regex.Match(source, @"class\s+(EventMode[A-Za-z0-9_]*)");
                if (oldClass.Success)
                    source = source.Replace(oldClass.Groups[1].Value, className);
            }
            EventAuthorService.UpdatePack(_eventPackDir, source, draft);
            Log("Compiling " + _eventPackDir + "…");
            return await EventAuthorService.CompileAsync(_eventPackDir);
        }

        private async void BtnSaveEventPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (ok, log) = await SaveOpenPackAsync();
                Log(log);
                MessageBox.Show(ok ? "Compiled successfully." : "Compile failed. See the log.\n\n" + log,
                    "Events", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "Events", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnTestEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (ok, log) = await SaveOpenPackAsync();
                Log(log);
                if (!ok)
                {
                    MessageBox.Show("Compile failed. See the log.\n\n" + log, "Events", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var id = EventAuthorService.PackEventId(_eventPackDir);
                if (string.IsNullOrWhiteSpace(id))
                {
                    MessageBox.Show("The pack has no event id.", "Events");
                    return;
                }
                Log("Starting event " + id);
                var prefs = new LaunchConfigurePrefs
                {
                    StartingMenu = "events",
                    EventId = id
                };
                ShellConfigureLaunchService.Launch(_shell, prefs);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "Events", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnCompileTest_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCompilePack.SelectedItem is not EventSourceChoice pack)
            {
                MessageBox.Show("Pick an event pack.", "Compile");
                return;
            }
            Log("Compiling " + pack.Label + "…");
            try
            {
                var (ok, log) = await EventAuthorService.CompileAsync(pack.Path);
                Log(log);
                if (!ok)
                {
                    MessageBox.Show("Compile failed. See the log.", "Compile", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var id = EventAuthorService.PackEventId(pack.Path);
                if (string.IsNullOrWhiteSpace(id))
                {
                    MessageBox.Show("The pack has no event id.", "Compile");
                    return;
                }
                Log("Starting event " + id);
                ShellConfigureLaunchService.Launch(_shell, new LaunchConfigurePrefs
                {
                    StartingMenu = "events",
                    EventId = id
                });
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "Compile", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnRunCompile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCompilePack.SelectedItem is not EventSourceChoice pack)
            {
                MessageBox.Show("Pick an event pack.", "Compile");
                return;
            }
            Log("Compiling " + pack.Label + "…");
            try
            {
                var (ok, log) = await EventAuthorService.CompileAsync(pack.Path);
                Log(log);
                MessageBox.Show(ok ? "Compiled successfully." : "Compile failed. See the log.",
                    "Compile", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        private async void BtnMakeStage_Click(object sender, RoutedEventArgs e)
        {
            await LoadRosterAsync();
            CmbStageSource.ItemsSource = ShellStageCloneService.ListPlayableStages(_shell, _doc ?? new JObject());
            if (CmbStageSource.Items.Count > 0) CmbStageSource.SelectedIndex = 0;
            _stageLinkageEdited = false;
            Show("makeStage", "Clone a stage");
            RefreshStageNameClash();
        }

        private async void BtnStageBackground_Click(object sender, RoutedEventArgs e)
        {
            await LoadRosterAsync();
            CmbStageBgDefault.ItemsSource = ShellStageCloneService.ListPlayableStages(_shell, _doc ?? new JObject());
            if (CmbStageBgDefault.Items.Count > 0)
                CmbStageBgDefault.SelectedIndex = 0;
            _stageBgFile = null;
            TxtStageBgFile.Text = "";
            TxtStageBgName.Text = "";
            ImgStageBg.Source = null;
            Show("stageBg", "Create a stage background");
        }

        private void BtnBrowseStageBg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Stage background",
                Filter = "Stage background (*.png;*.gif;*.mp4)|*.png;*.gif;*.mp4"
            };
            if (dlg.ShowDialog() != true)
                return;
            _stageBgFile = dlg.FileName;
            TxtStageBgFile.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(TxtStageBgName.Text))
            {
                var stem = Path.GetFileNameWithoutExtension(dlg.FileName);
                TxtStageBgName.Text = stem;
            }
            ShowStageBgPreview(dlg.FileName);
        }

        private void ShowStageBgPreview(string path)
        {
            try
            {
                var preview = Path.Combine(Path.GetTempPath(), "ssf2mm-stagebg-preview.png");
                StageBackgroundStore.WritePreview(path, preview);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(preview);
                image.EndInit();
                image.Freeze();
                ImgStageBg.Source = image;
            }
            catch (Exception ex)
            {
                ImgStageBg.Source = null;
                Log(ex.Message);
            }
        }

        private async void BtnRunStageBackground_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_stageBgFile) || !File.Exists(_stageBgFile))
            {
                MessageBox.Show("Pick a png, gif, or mp4.", "Create a stage background", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (CmbStageBgDefault.SelectedItem is not ShellRosterCharacter stage)
            {
                MessageBox.Show("Pick the default stage.", "Create a stage background", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var name = TxtStageBgName.Text;
            var file = _stageBgFile;
            await RunLogged("Create a stage background", () =>
                Task.FromResult(StageBackgroundStore.Create(_shell, name, file!, stage.Id)));
        }

        private async void BtnStageAsset_Click(object sender, RoutedEventArgs e)
        {
            await LoadRosterAsync();
            TxtStageAssetName.Text = "";
            LstStageAssets.ItemsSource = null;
            _stageAssetFolder = null;
            Show("stageAsset", "Stage asset swap");
            CmbStageAssetSource.ItemsSource = ShellStageCloneService.ListPlayableStages(_shell, _doc ?? new JObject());
            CmbStageAssetSource.SelectedIndex = -1;
            if (CmbStageAssetSource.Items.Count > 0)
                CmbStageAssetSource.SelectedIndex = 0;
        }

        private async void CmbStageAssetSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_panel != "stageAsset" || CmbStageAssetSource.SelectedItem is not ShellRosterCharacter stage
                || string.IsNullOrWhiteSpace(stage.SwfPath) || !File.Exists(stage.SwfPath))
                return;
            if (string.IsNullOrWhiteSpace(TxtStageAssetName.Text))
                TxtStageAssetName.Text = string.IsNullOrWhiteSpace(stage.DisplayName) ? stage.Id : stage.DisplayName;
            var token = ++_stageAssetPreview;
            var path = stage.SwfPath;
            var images = await Task.Run(() => StageAssetSwap.List(File.ReadAllBytes(path)));
            if (token != _stageAssetPreview)
                return;
            LstStageAssets.ItemsSource = images.Select(image => new StageAssetRow(
                image.CharacterId + "  " + image.Width + "×" + image.Height,
                StageAssetPixels(image.Bgra, image.Width, image.Height))).ToList();
        }

        private async void BtnRunStageAsset_Click(object sender, RoutedEventArgs e)
        {
            if (CmbStageAssetSource.SelectedItem is not ShellRosterCharacter stage
                || string.IsNullOrWhiteSpace(stage.SwfPath))
            {
                MessageBox.Show("Pick the source stage.", "Stage asset swap", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var name = string.IsNullOrWhiteSpace(TxtStageAssetName.Text) ? stage.DisplayName : TxtStageAssetName.Text;
            var id = stage.Id;
            var swf = stage.SwfPath;
            string? folder = null;
            await RunLogged("Stage asset swap", async () =>
            {
                folder = await StageAssetSwapStore.CreateFromFileAsync(_shell, name, id, swf);
                return folder;
            });
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                ShowStageAssetPack(folder);
        }

        private void BtnOpenStageAssetFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = _stageAssetFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("Create the pack first.", "Stage asset swap", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var assets = Path.Combine(folder, "assets");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Directory.Exists(assets) ? assets : folder,
                UseShellExecute = true
            });
        }

        private void ShowStageAssetPack(string folder)
        {
            _stageAssetFolder = folder;
            _stageAssetPreview++;
            var items = new List<StageAssetRow>();
            var jsonPath = Path.Combine(folder, "assets.json");
            if (File.Exists(jsonPath))
            {
                var doc = JObject.Parse(File.ReadAllText(jsonPath));
                foreach (var row in doc["images"] as JArray ?? new JArray())
                {
                    var file = Path.Combine(folder, "assets", row["file"]?.ToString() ?? "");
                    var id = row["characterId"]?.ToString() ?? "";
                    var width = row["width"]?.ToString() ?? "";
                    var height = row["height"]?.ToString() ?? "";
                    items.Add(new StageAssetRow(id + "  " + width + "×" + height, StageAssetFile(file)));
                }
            }
            LstStageAssets.ItemsSource = items;
        }

        private static ImageSource? StageAssetPixels(byte[]? bgra, int width, int height)
        {
            if (bgra == null || width <= 0 || height <= 0 || bgra.Length < width * height * 4)
                return null;
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
            image.Freeze();
            return image;
        }

        private static ImageSource? StageAssetFile(string path)
        {
            if (!File.Exists(path))
                return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.DecodePixelHeight = 72;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private async void BtnMakeCharacter_Click(object sender, RoutedEventArgs e)
        {
            await LoadRosterAsync();
            CmbCharSource.ItemsSource = ShellCharacterCloneService.ListPlayableCharacters(_shell, _doc ?? new JObject());
            if (CmbCharSource.Items.Count > 0) CmbCharSource.SelectedIndex = 0;
            _charLinkageEdited = false;
            Show("makeCharacter", "Clone a character");
            RefreshCharNameClash();
        }

        private async void BtnRunMakeStage_Click(object sender, RoutedEventArgs e)
        {
            if (CmbStageSource.SelectedItem is not ShellRosterCharacter stage) return;
            RefreshStageNameClash();
            var clash = StageNameClash();
            if (!string.IsNullOrWhiteSpace(clash))
            {
                MessageBox.Show(clash, "Clone a stage", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var keepPreview = ChkStageKeepOldAssets.IsChecked == true;
            var preview = keepPreview ? null : WritePreviewPng(TxtStageLinkage.Text);
            await RunLogged("Clone a stage", () => _stages.CloneIntoShellAsync(
                _shell, stage.Id, TxtStageDisplay.Text, TxtStageLinkage.Text, preview, LogProgress(),
                default, ChkStageSkipFla.IsChecked == true, keepPreview));
        }

        private async void BtnRunMakeCharacter_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCharSource.SelectedItem is not ShellRosterCharacter fighter) return;
            RefreshCharNameClash();
            var clash = CharNameClash();
            if (!string.IsNullOrWhiteSpace(clash))
            {
                MessageBox.Show(clash, "Clone a character", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await RunLogged("Clone a character", async () =>
            {
                var result = await _characters.CloneIntoShellAsync(
                    _shell, fighter.Id, TxtCharLinkage.Text, TxtCharDisplay.Text,
                    null, LogProgress(), default, TxtCharLinkage.Text, ChkCharSkipFla.IsChecked == true,
                    ChkCharKeepOldAssets.IsChecked == true);
                return result.ExpansionFolder;
            });
        }

        private void BtnOpenStageEditor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShellDirectLaunchService.Launch(_shell, ShellDirectLaunchService.ScreenSssEdit);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Stage select", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnImportStage_Click(object sender, RoutedEventArgs e)
        {
            Show("importStage", "Import a stage");
        }

        private void BtnImportCharacter_Click(object sender, RoutedEventArgs e)
        {
            Show("importCharacter", "Import a character");
        }

        private void BtnBrowseStageFile_Click(object sender, RoutedEventArgs e)
        {
            var paths = PickSwfs("Stage SWF");
            if (paths == null)
                return;
            _importStageFiles = ShellCharacterCloneService.SelectedImportPaths(paths);
            TxtImportStageFile.Text = string.Join(Environment.NewLine, _importStageFiles);
        }

        private void BtnBrowseCharacterFile_Click(object sender, RoutedEventArgs e)
        {
            var paths = PickSwfs("Character SWF");
            if (paths == null)
                return;
            _importCharFiles = ShellCharacterCloneService.SelectedImportPaths(paths);
            TxtImportCharFile.Text = string.Join(Environment.NewLine, _importCharFiles);
        }

        private static string? PickSwf(string title)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = "Flash files (*.swf;*.ssf)|*.swf;*.ssf"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private static string[]? PickSwfs(string title)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = "Flash files (*.swf;*.ssf)|*.swf;*.ssf",
                Multiselect = true
            };
            return dlg.ShowDialog() == true ? dlg.FileNames : null;
        }

        private async void BtnRunImportStage_Click(object sender, RoutedEventArgs e)
        {
            var files = ShellCharacterCloneService.SelectedImportPaths(_importStageFiles)
                .Where(File.Exists)
                .ToList();
            if (files.Count == 0)
            {
                MessageBox.Show("Pick one or more stage files (.swf or .ssf).", "Import stage", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await LoadRosterAsync();
            var taken = ShellStageCloneService.TakenStageIds(_shell, _doc ?? new JObject());
            var pending = new List<ImportCloneAssetChoice>();
            foreach (var path in files)
            {
                try
                {
                    var bytes = SwfPackage.ToSwf(await File.ReadAllBytesAsync(path));
                    var (linkage, display) = ShellStageCloneService.ReadStageImportIdentity(bytes);
                    if (string.IsNullOrWhiteSpace(linkage))
                        continue;
                    pending.Add(new ImportCloneAssetChoice
                    {
                        Path = path,
                        FileName = Path.GetFileName(path),
                        DisplayName = display ?? "",
                        SourceId = linkage
                    });
                }
                catch
                {
                    // Import reports a file that is not a stage.
                }
            }
            var clones = ShellCharacterCloneService.CloneAssetChoices(pending, taken);
            var keep = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (clones.Count > 0)
            {
                var dlg = new KeepCloneAssetsWindow(
                    clones,
                    "These files are clones. Check the ones that should keep the original stage's preview.");
                var owner = KeepCloneAssetsWindow.PickOwner(this, System.Windows.Application.Current?.Windows);
                if (owner != null)
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if (dlg.ShowDialog() != true)
                    return;
                foreach (var choice in clones)
                    keep[choice.Path] = choice.KeepOldAssets;
            }
            await RunLogged("Import stage", async () =>
            {
                var folders = new List<string>();
                for (var i = 0; i < files.Count; i++)
                {
                    Log($"Importing {i + 1}/{files.Count}: {Path.GetFileName(files[i])}");
                    var useOld = keep.TryGetValue(files[i], out var yes) && yes;
                    string? preview = null;
                    if (!useOld)
                    {
                        var bytes = SwfPackage.ToSwf(await File.ReadAllBytesAsync(files[i]));
                        var (_, display) = ShellStageCloneService.ReadStageImportIdentity(bytes);
                        preview = WritePreviewPng(string.IsNullOrWhiteSpace(display) ? "Stage" : display);
                    }
                    var folder = await _stages.ImportIntoShellAsync(
                        _shell, files[i], preview, LogProgress(), default,
                        ChkImportStageSkipFla.IsChecked == true, useOld);
                    folders.Add(folder);
                }
                return string.Join(Environment.NewLine, folders);
            });
        }

        private async void BtnRunImportCharacter_Click(object sender, RoutedEventArgs e)
        {
            var files = ShellCharacterCloneService.SelectedImportPaths(_importCharFiles)
                .Where(File.Exists)
                .ToList();
            if (files.Count == 0)
            {
                MessageBox.Show("Pick one or more character files (.swf or .ssf).", "Import character", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await LoadRosterAsync();
            var taken = ShellCharacterCloneService.TakenLinkageIds(_shell, _doc ?? new JObject());
            var pending = new List<ImportCloneAssetChoice>();
            foreach (var path in files)
            {
                try
                {
                    var bytes = SwfPackage.ToSwf(await File.ReadAllBytesAsync(path));
                    var (linkage, display) = CharacterCloneService.ReadImportIdentity(bytes);
                    if (string.IsNullOrWhiteSpace(linkage))
                        continue;
                    pending.Add(new ImportCloneAssetChoice
                    {
                        Path = path,
                        FileName = Path.GetFileName(path),
                        DisplayName = display ?? "",
                        SourceId = linkage
                    });
                }
                catch
                {
                    // Import reports a file that is not a character.
                }
            }
            var clones = ShellCharacterCloneService.CloneAssetChoices(pending, taken);
            var keep = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (clones.Count > 0)
            {
                var dlg = new KeepCloneAssetsWindow(clones);
                var owner = KeepCloneAssetsWindow.PickOwner(this, System.Windows.Application.Current?.Windows);
                if (owner != null)
                    dlg.Owner = owner;
                else
                    dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if (dlg.ShowDialog() != true)
                    return;
                foreach (var choice in clones)
                    keep[choice.Path] = choice.KeepOldAssets;
            }
            await RunLogged("Import character", async () =>
            {
                var folders = new List<string>();
                for (var i = 0; i < files.Count; i++)
                {
                    Log($"Importing {i + 1}/{files.Count}: {Path.GetFileName(files[i])}");
                    var useOld = keep.TryGetValue(files[i], out var yes) && yes;
                    var result = await _characters.ImportIntoShellAsync(
                        _shell, files[i],
                        LogProgress(), default, ChkImportCharSkipFla.IsChecked == true, useOld);
                    folders.Add(result.ExpansionFolder);
                }
                return string.Join(Environment.NewLine, folders);
            });
        }

        private void TxtStageDisplay_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_stageLinkageEdited)
                TxtStageLinkage.Text = SuggestLinkage(TxtStageDisplay.Text);
            RefreshStageNameClash();
        }

        private void TxtStageLinkage_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtStageLinkage.IsKeyboardFocusWithin)
                _stageLinkageEdited = true;
            RefreshStageNameClash();
        }

        private void TxtCharDisplay_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_charLinkageEdited)
                TxtCharLinkage.Text = SuggestLinkage(TxtCharDisplay.Text);
            RefreshCharNameClash();
        }

        private void TxtCharLinkage_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtCharLinkage.IsKeyboardFocusWithin)
                _charLinkageEdited = true;
            RefreshCharNameClash();
        }

        private string? StageNameClash()
        {
            var roster = CmbStageSource.ItemsSource as System.Collections.Generic.IEnumerable<ShellRosterCharacter>;
            return ShellStageCloneService.TakenNameMessage(
                roster ?? System.Array.Empty<ShellRosterCharacter>(),
                TxtStageDisplay.Text, TxtStageLinkage.Text);
        }

        private string? CharNameClash()
        {
            var roster = CmbCharSource.ItemsSource as System.Collections.Generic.IEnumerable<ShellRosterCharacter>;
            return ShellCharacterCloneService.TakenNameMessage(
                roster ?? System.Array.Empty<ShellRosterCharacter>(),
                TxtCharDisplay.Text, TxtCharLinkage.Text);
        }

        private void RefreshStageNameClash()
        {
            if (TxtStageNameError == null || BtnMakeStageGo == null) return;
            var msg = StageNameClash();
            TxtStageNameError.Text = msg ?? "";
            TxtStageNameError.Visibility = string.IsNullOrWhiteSpace(msg) ? Visibility.Collapsed : Visibility.Visible;
            BtnMakeStageGo.IsEnabled = string.IsNullOrWhiteSpace(msg);
        }

        private void RefreshCharNameClash()
        {
            if (TxtCharNameError == null || BtnMakeCharacterGo == null) return;
            var msg = CharNameClash();
            TxtCharNameError.Text = msg ?? "";
            TxtCharNameError.Visibility = string.IsNullOrWhiteSpace(msg) ? Visibility.Collapsed : Visibility.Visible;
            BtnMakeCharacterGo.IsEnabled = string.IsNullOrWhiteSpace(msg);
        }

        private static string SuggestLinkage(string display)
        {
            var id = Regex.Replace(display ?? "", @"[^A-Za-z0-9]+", "");
            if (string.IsNullOrEmpty(id)) return "";
            if (char.IsDigit(id[0])) id = "s" + id;
            return id;
        }

        private async Task LoadRosterAsync()
        {
            try
            {
                _doc ??= await _mappings.LoadDocumentAsync(_shell, exportIfMissing: false);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                _doc ??= new JObject();
            }
        }

        private async Task RunLogged(string title, Func<Task<string>> work)
        {
            var launcher = System.Windows.Application.Current?.Windows.OfType<LauncherWindow>().FirstOrDefault();
            launcher?.LockForWork(true, title + "…");
            try
            {
                Log(title + "…");
                var path = await work();
                Log(path);
                MessageBox.Show(path, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                launcher?.LockForWork(false, "");
            }
        }

        private IProgress<string> LogProgress() => new Progress<string>(Log);

        private void Log(string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(line));
                return;
            }
            if (TxtLog.Text.Length > 0)
                TxtLog.AppendText(Environment.NewLine);
            TxtLog.AppendText(line);
            TxtLog.ScrollToEnd();
        }

        private static string WritePreviewPng(string label)
        {
            var path = Path.Combine(Path.GetTempPath(), "ssf2mm-stagepreview.png");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 48, 72)), null, new Rect(0, 0, 96, 72));
                var text = new FormattedText(
                    string.IsNullOrWhiteSpace(label) ? "Stage" : label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    12,
                    Brushes.White,
                    1.0);
                dc.DrawText(text, new Point(6, 28));
            }
            var bmp = new RenderTargetBitmap(96, 72, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
            return path;
        }
    }

    public sealed class StageAssetRow
    {
        public StageAssetRow(string label, ImageSource? image)
        {
            Label = label;
            Image = image;
        }

        public string Label { get; }
        public ImageSource? Image { get; }
    }
}
