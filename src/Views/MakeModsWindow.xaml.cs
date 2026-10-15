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
        private bool _eventClassEdited;
        private string _panel = "home";
        public event Action? SectionChanged;

        public MakeModsWindow(string shellRoot)
        {
            _shell = shellRoot;
            InitializeComponent();
            TxtEventClass.TextChanged += (_, _) =>
            {
                if (TxtEventClass.IsKeyboardFocusWithin)
                    _eventClassEdited = true;
            };
        }

        private void Show(string panel, string title)
        {
            _panel = panel;
            TxtTitle.Text = title;
            BtnBack.Visibility = panel == "home" ? Visibility.Collapsed : Visibility.Visible;
            foreach (var name in new[]
                     {
                         "PanelHome", "PanelEvents", "PanelNewEvent", "PanelCompile",
                         "PanelStage", "PanelMakeStage", "PanelImportStage",
                         "PanelCharacter", "PanelMakeCharacter", "PanelImportCharacter",
                         "PanelCostume", "PanelAlts", "PanelIntros",
                         "PanelPackageHome", "PanelPackageEdit", "PanelPackageZip",
                         "PanelN2f", "PanelDecompile", "PanelCompileFolder", "PanelPressor"
                     })
            {
                if (FindName(name) is StackPanel sp)
                    sp.Visibility = Visibility.Collapsed;
            }
            var showLog = panel is "newEvent" or "compile" or "makeStage" or "makeCharacter" or "importStage" or "importCharacter" or "packageEdit" or "packageZip" or "intros" or "decompile" or "compileFolder" or "pressor" or "alts";
            TxtLog.Visibility = showLog ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("Panel" + char.ToUpperInvariant(panel[0]) + panel[1..]) is StackPanel current)
                current.Visibility = Visibility.Visible;
            else if (panel == "home" && FindName("PanelHome") is StackPanel home)
                home.Visibility = Visibility.Visible;
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
                "importStage" => "Import a stage",
                "character" => "Character",
                "makeCharacter" => "Clone a character",
                "importCharacter" => "Import a character",
                "costume" => "Costume",
                "alts" => "Character Alts",
                "intros" => "Intros",
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

        private string? _altWorkFolder;
        private string? _altSourceSwf;
        private DispatcherTimer? _altPickTimer;

        private void EnsureCostumeCharacters()
        {
            if (CmbCostumeCharacter.Items.Count > 0)
                return;
            foreach (var id in ShellConfigureLaunchService.ListCharacters(_shell))
                CmbCostumeCharacter.Items.Add(id);
            if (CmbCostumeCharacter.Items.Count > 0)
                CmbCostumeCharacter.SelectedIndex = 0;
        }

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

        private async void BtnAltExtract_Click(object sender, RoutedEventArgs e)
        {
            var character = CmbAltCharacter.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(character))
            {
                MessageBox.Show("Pick a character.", "Character Alts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                await LoadRosterAsync();
                var swf = MusicEditService.ResolveCharacterSwfPath(_shell, _doc ?? new JObject(), character);
                if (string.IsNullOrWhiteSpace(swf) || !File.Exists(swf))
                    throw new FileNotFoundException("Character SWF not found for " + character + ".");
                var work = Path.Combine(CharacterAltsService.AltsRoot(_shell), "_work", character,
                    SafeAltFolder(TxtAltName.Text, character));
                Directory.CreateDirectory(work);
                var sounds = Path.Combine(work, "sounds");
                Directory.CreateDirectory(sounds);
                var plain = SwfPackage.EnsurePlainSwf(swf, work);
                var copy = Path.Combine(work, character + ".swf");
                File.Copy(plain, copy, overwrite: true);
                _altSourceSwf = copy;
                _altWorkFolder = work;
                var jpexs = new JpexsCliService();
                await jpexs.ExportAsync("", copy, sounds, "sound", default);
                var count = Directory.Exists(sounds) ? Directory.GetFiles(sounds, "*", SearchOption.AllDirectories).Length : 0;
                TxtAltWork.Text = "Sounds (" + count + ") in " + sounds + ". Edit files there, then Install.";
                AppendLog("Extracted sounds for " + character + " → " + sounds);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Extract sounds", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
                ShellDirectLaunchService.Launch(_shell, ShellDirectLaunchService.ScreenCss,
                    new Dictionary<string, object?>
                    {
                        ["altCostumePick"] = true,
                        ["altCostumePickChar"] = character
                    });
                TxtAltWork.Text = "Waiting for costume pick in game…";
                _altPickTimer?.Stop();
                _altPickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                var started = DateTime.UtcNow;
                _altPickTimer.Tick += (_, _) =>
                {
                    if (DateTime.UtcNow - started > TimeSpan.FromMinutes(10))
                    {
                        _altPickTimer.Stop();
                        TxtAltWork.Text = "Costume pick timed out. Type the costume index (−1 = base) yourself.";
                        return;
                    }
                    var pickPath = pickPaths.FirstOrDefault(File.Exists);
                    if (pickPath == null) return;
                    try
                    {
                        var jo = JObject.Parse(File.ReadAllText(pickPath));
                        if (jo["costume"] == null) return;
                        TxtAltCostume.Text = jo["costume"]!.ToString();
                        TxtAltWork.Text = "Picked costume " + TxtAltCostume.Text + " for " + (jo["character"] ?? character) + ".";
                        _altPickTimer.Stop();
                        try { File.Delete(pickPath); } catch { }
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

        private void BtnAltInstall_Click(object sender, RoutedEventArgs e)
        {
            var character = CmbAltCharacter.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(character))
            {
                MessageBox.Show("Pick a character.", "Character Alts", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(_altSourceSwf) || !File.Exists(_altSourceSwf))
            {
                MessageBox.Show("Extract sounds first so there is a character file to install.", "Character Alts",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var name = string.IsNullOrWhiteSpace(TxtAltName.Text) ? null : TxtAltName.Text.Trim();
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
                var folder = CharacterAltsService.Install(_shell, 0, character, name,
                    new[] { (character, _altSourceSwf) }, name ?? character + " alt", costume);
                TxtAltWork.Text = "Installed " + folder + (costume.HasValue ? " (costume " + costume + ")" : " (all costumes)");
                AppendLog("Installed alt " + (name ?? character) + " → " + folder);
                MessageBox.Show("Alt installed. Restart or reopen Character Select to load it.", "Character Alts",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Install alt", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                MessageBox.Show("Name is required.", "Package mods", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return entry;
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
                FileName = safe + ".zip",
                Filter = "Zip archive (*.zip)|*.zip",
                AddExtension = true,
                DefaultExt = ".zip"
            };
            if (dlg.ShowDialog() != true)
                return;
            try
            {
                ModPackageService.PackageZip(picked, dlg.FileName);
                TxtLog.Text = "Packaged " + picked.Count + " mod" + (picked.Count == 1 ? "" : "s") + " into " + dlg.FileName
                    + "\nSource and build.bat are left out of the zip.";
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
                Filter = "Mod archive (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z"
            };
            if (dlg.ShowDialog() != true)
                return;
            try
            {
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

        private void BtnMusic_Click(object sender, RoutedEventArgs e)
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

        private void BtnNewEvent_Click(object sender, RoutedEventArgs e)
        {
            CmbEventSource.ItemsSource = EventAuthorService.ListSources(_shell);
            if (CmbEventSource.Items.Count > 0)
                CmbEventSource.SelectedIndex = 0;
            Show("newEvent", "Make a new event");
        }

        private void BtnCompileEvent_Click(object sender, RoutedEventArgs e)
        {
            CmbCompilePack.ItemsSource = EventAuthorService.ListPacks(_shell);
            if (CmbCompilePack.Items.Count > 0)
                CmbCompilePack.SelectedIndex = 0;
            Show("compile", "Compile an event");
        }

        private void TxtEventId_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_eventClassEdited) return;
            TxtEventClass.Text = EventAuthorService.SuggestClassName(TxtEventId.Text);
        }

        private void BtnCreateEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbEventSource.SelectedItem is not EventSourceChoice source)
                {
                    MessageBox.Show("Pick a template or an existing event.", "Events");
                    return;
                }
                var dir = EventAuthorService.CreateEvent(_shell, source.Path, TxtEventPack.Text, new EventInfoDraft
                {
                    Id = TxtEventId.Text,
                    Name = TxtEventName.Text,
                    Description = TxtEventDesc.Text,
                    Creator = TxtEventCreator.Text,
                    ClassName = TxtEventClass.Text,
                    ChooseCharacter = ChkEventChoose.IsChecked == true
                });
                Log("Created " + dir);
                Log("eventinfo was written into " + TxtEventClass.Text + " and the pack class.");
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "Events", MessageBoxButton.OK, MessageBoxImage.Warning);
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
}
