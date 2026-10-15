using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace SSF2ModManager.Views
{
    public partial class ConfigureLaunchWindow : Window
    {
        private readonly SettingsService _settings;
        private readonly string _installRoot;
        private readonly List<string> _characters;
        private readonly List<(string Id, string Label, bool ChooseCharacter)> _events;
        private readonly ObservableCollection<PlayerRow> _players = new();
        private readonly List<System.Windows.Controls.CheckBox> _poolBoxes = new();
        private readonly List<System.Windows.Controls.CheckBox> _specialBoxes = new();
        private readonly ObservableCollection<string> _tagChoices = new();
        private readonly System.Windows.Threading.DispatcherTimer _liveTimer;
        private bool _gameLive;
        private const string NoTag = "(none)";
        private const string AddTag = "Add new…";
        private const string ManageTags = "Manage tags…";
        private const string NoneCharacter = "none";

        public ConfigureLaunchWindow(SettingsService settings, string installRoot)
        {
            _settings = settings;
            _installRoot = installRoot;
            InitializeComponent();
            _characters = ShellConfigureLaunchService.ListCharacters(installRoot);
            if (_characters.Count == 0)
                _characters.Add("mario");

            Fill(CmbMenu, ShellConfigureLaunchService.Menus);
            Fill(CmbBoot, ShellConfigureLaunchService.BootIntros);
            Fill(CmbVaultAction, new[] { ("intro", "Play intro"), ("costumes", "Palette Maker") });
            Fill(CmbVaultIntro, new[] { ("intro1", "Intro 1"), ("intro2", "Intro 2"), ("intro3", "Intro 3") });
            Fill(CmbDifficulty, ShellConfigureLaunchService.Difficulties);
            Fill(CmbPalette, _characters.Select(c => (c, c)));
            _events = ShellConfigureLaunchService.ListEvents(installRoot);
            Fill(CmbEvent, _events.Select(e => (e.Id, e.Label)));
            Fill(CmbEventChar, _characters.Select(c => (c, c)));
            Fill(CmbSandbag, _characters.Select(c => (c, c)));
            CmbStage.ItemsSource = ShellConfigureLaunchService.ListStages(installRoot);
            CmbMusic.ItemsSource = ShellConfigureLaunchService.ListMusic(installRoot);

            foreach (var mode in ShellConfigureLaunchService.SpecialModes)
            {
                var box = new System.Windows.Controls.CheckBox
                {
                    Content = mode.Label,
                    Tag = mode.Bit,
                    Margin = new Thickness(0, 0, 12, 6)
                };
                box.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
                _specialBoxes.Add(box);
                SpecialHost.Children.Add(box);
            }

            _tagChoices.Add(NoTag);
            _tagChoices.Add(AddTag);
            _tagChoices.Add(ManageTags);
            for (var i = 0; i < 4; i++)
            {
                // P3/P4 can be an empty slot ("none").
                var chars = i >= 2
                    ? new List<string> { NoneCharacter }.Concat(_characters).ToList()
                    : _characters;
                var row = new PlayerRow(chars, _tagChoices) { Slot = "P" + (i + 1) };
                row.AddTagRequested += OnAddTag;
                row.ManageTagsRequested += OnManageTags;
                _players.Add(row);
            }
            PlayerRows.ItemsSource = _players;

            var prefs = _settings.GetLaunchConfigure();
            if (ShellConfigureLaunchService.TryImportStreamIntros(_installRoot, prefs))
            {
                _settings.SetLaunchConfigure(prefs);
                TxtStatus.Text = "Imported intros from SSF2 Stream.";
            }
            ControllerProfilesSyncService.MergeIntoPrefs(_installRoot, prefs);
            ControllerProfilesSyncService.WriteFromPrefs(_installRoot, prefs);
            ChkMusicEverywhere.Checked += OnMusicEverywhereChanged;
            ChkMusicEverywhere.Unchecked += OnMusicEverywhereChanged;
            LoadPrefs(prefs);
            _liveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };
            _liveTimer.Tick += (_, _) => RefreshLiveUi();
            _liveTimer.Start();
            RefreshLiveUi();
        }

        private void LoadPrefs(LaunchConfigurePrefs prefs)
        {
            prefs.EnsureDefaults();
            foreach (var file in ShellConfigureLaunchService.ListIntroFiles(_installRoot))
            {
                if (prefs.BootCustomIntros.Any(c => c.Id.Equals(file.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                prefs.BootCustomIntros.Add(file);
            }
            Select(CmbMenu, prefs.StartingMenu);
            ChkImmediate.IsChecked = prefs.LoadMatchImmediate;
            RebuildBootChoices(prefs);
            Select(CmbBoot, prefs.BootIntro);
            Select(CmbVaultAction, prefs.VaultAction);
            Select(CmbVaultIntro, prefs.VaultIntro);
            Select(CmbPalette, prefs.VaultPaletteChar);
            ChkUseEveryLaunch.IsChecked = prefs.UseOnEveryLaunch;
            ChkKeepOpen.IsChecked = prefs.KeepConfigureOpen;
            ChkOpenFullscreen.IsChecked = prefs.OpenFullscreen;
            RebuildTagChoices(prefs);
            CmbStage.Text = prefs.MatchStage;
            if (CmbMusic.Items.Contains(prefs.MatchMusic))
                CmbMusic.SelectedItem = prefs.MatchMusic;
            else
                CmbMusic.Text = prefs.MatchMusic;
            ChkMusicOnLaunch.IsChecked = prefs.UseMusicOnLaunch;
            ChkMusicEverywhere.IsChecked = prefs.UseMusicEverywhere;
            Select(CmbEvent, prefs.EventId);
            ChkEventOverride.IsChecked = prefs.EventOverride;
            Select(CmbEventChar, string.IsNullOrWhiteSpace(prefs.EventCharacter) ? "mario" : prefs.EventCharacter);
            ChkSandbag.IsChecked = prefs.SandbagOverride;
            Select(CmbSandbag, string.IsNullOrWhiteSpace(prefs.SandbagCharacter) ? "mario" : prefs.SandbagCharacter);
            CmbMode.Text = prefs.MatchCustomModeId;
            Select(CmbDifficulty, prefs.MatchDifficulty);
            ChkHazards.IsChecked = prefs.MatchHazards;
            ChkUsingTime.IsChecked = prefs.MatchUsingTime;
            ChkUsingLives.IsChecked = prefs.MatchUsingLives;
            TxtTime.Text = prefs.MatchTime.ToString();
            TxtLives.Text = prefs.MatchLives.ToString();
            TxtScore.Text = prefs.MatchScoreLimit.ToString();
            foreach (var box in _specialBoxes)
                box.IsChecked = (prefs.MatchSpecialModes & (int)box.Tag!) != 0;
            LstCustoms.ItemsSource = prefs.BootCustomIntros.ToList();
            for (var i = 0; i < 4; i++)
            {
                var p = prefs.Players[i];
                var charId = (p.Character ?? "").Trim();
                if (i >= 2 && (charId.Length == 0
                    || charId.Equals(NoneCharacter, StringComparison.OrdinalIgnoreCase)))
                    _players[i].Character = NoneCharacter;
                else if (charId.Length == 0)
                    _players[i].Character = i == 0 ? "mario" : "fox";
                else
                    _players[i].Character = charId;
                _players[i].Human = p.Human;
                _players[i].Level = p.Level;
                _players[i].Team = p.Team;
                _players[i].Tag = p.Tag;
            }
            ApplyMenuVisibility();
        }

        private void RebuildTagChoices(LaunchConfigurePrefs prefs)
        {
            // Only tags from the synced profiles.json — wipe manager-only leftovers.
            var snap = ControllerProfilesSyncService.Load(_installRoot);
            prefs.PlayerTags = snap.Names.ToList();
            for (var i = 0; i < 4 && i < prefs.Players.Count; i++)
            {
                var tag = ControllerProfilesSyncService.NormalizeName(prefs.Players[i].Tag);
                if (tag.Length > 0 && snap.Names.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    prefs.Players[i].Tag = snap.Names.First(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
                else if (snap.Last.TryGetValue(i + 1, out var last) && last.Length > 0)
                    prefs.Players[i].Tag = last;
                else
                    prefs.Players[i].Tag = "";
            }
            _tagChoices.Clear();
            _tagChoices.Add(NoTag);
            foreach (var tag in snap.Names)
                _tagChoices.Add(tag);
            _tagChoices.Add(AddTag);
            _tagChoices.Add(ManageTags);
            foreach (var row in _players)
                row.RefreshTagSelection();
        }

        private void OnAddTag(object? sender, EventArgs e)
        {
            if (sender is not PlayerRow row)
                return;
            var dlg = new SSF2ModManager.Dialogs.InputDialog(
                "Add tag",
                $"Tag name (max {ControllerProfilesSyncService.NameMax} characters).",
                ControllerProfilesSyncService.NameMax)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true)
            {
                var name = ControllerProfilesSyncService.NormalizeName(dlg.InputText);
                if (name.Length > 0 && name is not NoTag and not AddTag and not ManageTags)
                {
                    ControllerProfilesSyncService.AddName(_installRoot, name);
                    RefreshTagsFromFile();
                    var existing = _tagChoices.FirstOrDefault(t => t.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                        row.Tag = existing;
                }
            }
            row.RefreshTagSelection();
        }

        private void OnManageTags(object? sender, EventArgs e)
        {
            var dlg = new SSF2ModManager.Dialogs.ManageTagsDialog(_installRoot) { Owner = this };
            dlg.ShowDialog();
            RefreshTagsFromFile();
        }

        private void RefreshTagsFromFile()
        {
            var prefs = Collect();
            ControllerProfilesSyncService.MergeIntoPrefs(_installRoot, prefs);
            RebuildTagChoices(prefs);
            for (var i = 0; i < 4 && i < prefs.Players.Count; i++)
                _players[i].Tag = prefs.Players[i].Tag;
        }

        private LaunchConfigurePrefs Collect()
        {
            var customs = (LstCustoms.ItemsSource as IEnumerable<LaunchCustomIntro>)?.ToList()
                          ?? new List<LaunchCustomIntro>();
            var bits = 0;
            foreach (var box in _specialBoxes)
            {
                if (box.IsChecked == true)
                    bits |= (int)box.Tag!;
            }
            var pool = _poolBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToList();
            var allPool = _poolBoxes.Select(b => (string)b.Tag!).ToList();
            if (pool.Count == allPool.Count)
                pool = new List<string>();

            var prefs = new LaunchConfigurePrefs
            {
                StartingMenu = SelectedId(CmbMenu, "home"),
                UseOnEveryLaunch = ChkUseEveryLaunch.IsChecked == true,
                KeepConfigureOpen = ChkKeepOpen.IsChecked == true,
                OpenFullscreen = ChkOpenFullscreen.IsChecked == true,
                LoadMatchImmediate = ChkImmediate.IsChecked == true,
                BootIntro = SelectedId(CmbBoot, "default"),
                VaultAction = SelectedId(CmbVaultAction, "intro"),
                VaultIntro = SelectedId(CmbVaultIntro, "intro3"),
                VaultPaletteChar = SelectedId(CmbPalette, "mario"),
                BootCustomIntros = customs,
                BootRandomPool = pool,
                MatchStage = CmbStage.Text.Trim(),
                MatchMusic = CmbMusic.Text.Trim(),
                UseMusicOnLaunch = ChkMusicOnLaunch.IsChecked == true,
                UseMusicEverywhere = ChkMusicEverywhere.IsChecked == true,
                EventId = SelectedId(CmbEvent, ""),
                EventOverride = ChkEventOverride.IsChecked == true,
                EventCharacter = SelectedId(CmbEventChar, "mario"),
                SandbagOverride = ChkSandbag.IsChecked == true,
                SandbagCharacter = SelectedId(CmbSandbag, "mario"),
                MenuMusicId = _settings.GetLaunchConfigure().MenuMusicId,
                MenuPlaylist = ShellConfigureLaunchService.ReadMenuPlaylist(_installRoot),
                MenuPlaylistEnabled = ShellConfigureLaunchService.ReadMenuPlaylistEnabled(_installRoot),
                MatchCustomModeId = SelectedId(CmbMode, CmbMode.Text.Trim()),
                MatchDifficulty = SelectedId(CmbDifficulty, "NORMAL"),
                MatchScoreLimit = ParseInt(TxtScore.Text, 5),
                MatchSpecialModes = bits,
                MatchHazards = ChkHazards.IsChecked == true,
                MatchUsingTime = ChkUsingTime.IsChecked == true,
                MatchUsingLives = ChkUsingLives.IsChecked == true,
                MatchLives = ParseInt(TxtLives.Text, 3),
                MatchTime = ParseInt(TxtTime.Text, 8),
                Players = _players.Select((p, i) => new LaunchPlayerPref
                {
                    Character = i >= 2 && IsNoneCharacter(p.Character)
                        ? NoneCharacter
                        : (p.Character?.Trim() ?? ""),
                    Human = p.Human,
                    Level = p.Level,
                    Team = p.Team,
                    Tag = ControllerProfilesSyncService.NormalizeName(p.Tag)
                }).ToList(),
                PlayerTags = _tagChoices
                    .Where(t => t is not NoTag and not AddTag and not ManageTags)
                    .Select(ControllerProfilesSyncService.NormalizeName)
                    .Where(t => t.Length > 0)
                    .ToList()
            };
            prefs.EnsureDefaults();
            return prefs;
        }

        private void PersistPrefs(LaunchConfigurePrefs prefs)
        {
            _settings.SetLaunchConfigure(prefs);
            ControllerProfilesSyncService.WriteFromPrefs(_installRoot, prefs);
        }

        private void RebuildBootChoices(LaunchConfigurePrefs prefs)
        {
            var choices = ShellConfigureLaunchService.BootIntros
                .Select(x => new Choice(x.Id, x.Label))
                .ToList();
            foreach (var c in prefs.BootCustomIntros.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
                choices.Add(new Choice("custom:" + c.Id, c.Label));
            var current = SelectedId(CmbBoot, prefs.BootIntro);
            CmbBoot.ItemsSource = choices;
            CmbBoot.DisplayMemberPath = nameof(Choice.Label);
            Select(CmbBoot, current);
            RebuildVaultChoices(prefs);

            PoolHost.Children.Clear();
            _poolBoxes.Clear();
            var selected = new HashSet<string>(
                prefs.BootRandomPool ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            var all = choices.Where(c => c.Id is not "skip" and not "random").ToList();
            var useAll = selected.Count == 0;
            foreach (var c in all)
            {
                var box = new System.Windows.Controls.CheckBox
                {
                    Content = c.Label,
                    Tag = c.Id,
                    IsChecked = useAll || selected.Contains(c.Id),
                    Margin = new Thickness(0, 0, 12, 6)
                };
                box.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
                _poolBoxes.Add(box);
                PoolHost.Children.Add(box);
            }
        }

        private void RebuildVaultChoices(LaunchConfigurePrefs prefs)
        {
            var choices = new List<Choice>
            {
                new("intro1", "Intro 1"),
                new("intro2", "Intro 2"),
                new("intro3", "Intro 3")
            };
            foreach (var c in prefs.BootCustomIntros.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
                choices.Add(new Choice("custom:" + c.Id, c.Label));
            var current = SelectedId(CmbVaultIntro, prefs.VaultIntro);
            CmbVaultIntro.ItemsSource = choices;
            CmbVaultIntro.DisplayMemberPath = nameof(Choice.Label);
            Select(CmbVaultIntro, string.IsNullOrWhiteSpace(current) ? prefs.VaultIntro : current);
        }

        private void CmbBoot_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ApplyMenuVisibility();

        private void CmbVaultAction_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ApplyMenuVisibility();

        private void CmbMenu_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ApplyMenuVisibility();

        private void ApplyMenuVisibility()
        {
            if (CmbMenu == null || BootPanel == null)
                return;
            var menu = SelectedId(CmbMenu, "home");
            var home = string.IsNullOrWhiteSpace(menu) || menu == "home";
            BootPanel.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            var boot = SelectedId(CmbBoot, "default");
            if (PoolPanel != null)
                PoolPanel.Visibility = home && boot == "random" ? Visibility.Visible : Visibility.Collapsed;

            var vault = menu == "vault";
            VaultPanel.Visibility = vault ? Visibility.Visible : Visibility.Collapsed;
            var vaultAction = SelectedId(CmbVaultAction, "intro");
            if (VaultIntroPanel != null)
                VaultIntroPanel.Visibility = vault && vaultAction == "intro" ? Visibility.Visible : Visibility.Collapsed;
            if (VaultCharPanel != null)
                VaultCharPanel.Visibility = vault && vaultAction == "costumes" ? Visibility.Visible : Visibility.Collapsed;

            var match = ShellConfigureLaunchService.IsMatchScreen(menu);
            MatchPanel.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (EventPanel != null)
                EventPanel.Visibility = menu == "events" ? Visibility.Visible : Visibility.Collapsed;
            UpdateEventOverrideUi();
            var versus = menu == "versus_css";
            var training = menu == "training_css";
            var classic = menu is "classic" or "allstar";
            var modeStage = menu is "btt" or "hrc" or "crystal" or "multiman";
            var arena = menu == "arena";
            var rttf = menu == "rttf";
            var solo = classic || modeStage || arena || rttf;
            var fighters = versus || training || arena;
            var p1Only = classic || modeStage || rttf;
            ChkImmediate.Visibility = versus || training || solo ? Visibility.Visible : Visibility.Collapsed;

            if (StagePanel != null)
                StagePanel.Visibility = versus || training ? Visibility.Visible : Visibility.Collapsed;
            if (ModePanel != null)
                ModePanel.Visibility = modeStage || arena ? Visibility.Visible : Visibility.Collapsed;
            if (SandbagPanel != null)
                SandbagPanel.Visibility = menu is "hrc" or "arena" ? Visibility.Visible : Visibility.Collapsed;
            if (CmbSandbag != null)
                CmbSandbag.IsEnabled = ChkSandbag.IsChecked == true;
            if (DifficultyPanel != null)
                DifficultyPanel.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;
            ChkHazards.Visibility = versus || training ? Visibility.Visible : Visibility.Collapsed;
            ChkUsingTime.Visibility = versus ? Visibility.Visible : Visibility.Collapsed;
            TxtTime.Visibility = versus ? Visibility.Visible : Visibility.Collapsed;
            ChkUsingLives.Visibility = versus ? Visibility.Visible : Visibility.Collapsed;
            if (TxtLivesCaption != null)
            {
                TxtLivesCaption.Text = classic ? "Lives" : "Stocks";
                TxtLivesCaption.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;
            }
            TxtLives.Visibility = versus || classic ? Visibility.Visible : Visibility.Collapsed;
            if (TxtScoreCaption != null)
                TxtScoreCaption.Visibility = arena ? Visibility.Visible : Visibility.Collapsed;
            TxtScore.Visibility = arena ? Visibility.Visible : Visibility.Collapsed;
            if (SpecialPanel != null)
                SpecialPanel.Visibility = versus ? Visibility.Visible : Visibility.Collapsed;
            if (TxtPlayers != null)
            {
                TxtPlayers.Visibility = fighters || p1Only ? Visibility.Visible : Visibility.Collapsed;
                TxtPlayers.Text = p1Only ? "Player" : "Players";
            }
            if (PlayerHeader != null)
                PlayerHeader.Visibility = fighters || p1Only ? Visibility.Visible : Visibility.Collapsed;
            if (TxtHumanHeader != null)
                TxtHumanHeader.Visibility = fighters ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCpuHeader != null)
                TxtCpuHeader.Visibility = fighters ? Visibility.Visible : Visibility.Collapsed;
            if (TxtTeamHeader != null)
                TxtTeamHeader.Visibility = arena ? Visibility.Visible : Visibility.Collapsed;
            if (TxtTagHeader != null)
                TxtTagHeader.Visibility = fighters || p1Only ? Visibility.Visible : Visibility.Collapsed;
            for (var i = 0; i < _players.Count; i++)
            {
                _players[i].RowVisible = i == 0 ? fighters || p1Only : fighters;
                _players[i].ShowHumanCpu = fighters;
                _players[i].ShowTeam = arena;
            }
            FillModeChoices(menu);
        }

        private void ChkSandbag_Changed(object sender, RoutedEventArgs e)
        {
            if (CmbSandbag != null)
                CmbSandbag.IsEnabled = ChkSandbag.IsChecked == true;
        }

        private void FillModeChoices(string menu)
        {
            var current = CmbMode.SelectedItem is Choice picked ? picked.Id : CmbMode.Text;
            string label;
            List<(string Id, string Label)> items;
            switch (menu)
            {
                case "arena":
                    label = "Arena mode";
                    items = new List<(string, string)> { ("soccer", "Soccer"), ("basketball", "Basketball") };
                    break;
                case "multiman":
                    label = "Multi-Man type";
                    items = new List<(string, string)>
                    {
                        ("man10", "10-Man"),
                        ("man100", "100-Man"),
                        ("min3", "3-Minute"),
                        ("endless", "Endless"),
                        ("cruel", "Cruel")
                    };
                    break;
                case "btt":
                    label = "BTT stage";
                    items = ShellConfigureLaunchService.ListBttStages(_installRoot)
                        .Select(id => (id, id))
                        .ToList();
                    break;
                case "crystal":
                    label = "Crystal stage";
                    items = ShellConfigureLaunchService.ListModeOptions(_installRoot, "crystals_mode.swf");
                    if (items.Count == 0)
                        items = new List<(string, string)> { ("Level 1", "Level 1"), ("Level 2", "Level 2") };
                    break;
                case "hrc":
                    label = "Home-Run Contest";
                    items = ShellConfigureLaunchService.ListModeOptions(_installRoot, "homeruncontest_mode.swf");
                    if (items.Count == 0)
                        items = new List<(string, string)> { ("HRCBarrier", "Barrier"), ("HRCNoBarrier", "No Barrier") };
                    break;
                default:
                    label = "Mode";
                    items = new List<(string, string)>();
                    break;
            }
            if (TxtMode != null)
                TxtMode.Text = label;
            if (items.Count == 0)
                return;
            Fill(CmbMode, items);
            Select(CmbMode, current);
        }

        private void BtnAddIntro_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.OpenFileDialog
            {
                Title = "Custom intro",
                Filter = "Video|*.mp4;*.flv;*.mov;*.webm;*.mkv|All files|*.*"
            };
            if (dlg.ShowDialog() != Forms.DialogResult.OK)
                return;
            var prefs = Collect();
            var id = Path.GetFileNameWithoutExtension(dlg.FileName);
            id = new string(id.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            if (string.IsNullOrWhiteSpace(id))
                id = "intro";
            var n = 1;
            var baseId = id;
            while (prefs.BootCustomIntros.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                id = baseId + (++n);
            prefs.BootCustomIntros.Add(new LaunchCustomIntro
            {
                Id = id,
                Label = Path.GetFileNameWithoutExtension(dlg.FileName),
                Path = dlg.FileName
            });
            LstCustoms.ItemsSource = prefs.BootCustomIntros.ToList();
            RebuildBootChoices(prefs);
        }

        private void BtnRemoveIntro_Click(object sender, RoutedEventArgs e)
        {
            if (LstCustoms.SelectedItem is not LaunchCustomIntro hit)
                return;
            var prefs = Collect();
            prefs.BootCustomIntros.RemoveAll(c => c.Id.Equals(hit.Id, StringComparison.OrdinalIgnoreCase));
            LstCustoms.ItemsSource = prefs.BootCustomIntros.ToList();
            if (prefs.BootIntro.Equals("custom:" + hit.Id, StringComparison.OrdinalIgnoreCase))
                prefs.BootIntro = "default";
            RebuildBootChoices(prefs);
        }

        public bool IsHosted { get; set; }
        public event EventHandler? HostedClose;

        private void RequestClose(bool launched)
        {
            if (IsHosted)
            {
                HostedClose?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (launched)
                DialogResult = true;
            else
                Close();
        }

        private void BtnSaveClose_Click(object sender, RoutedEventArgs e)
        {
            PersistPrefs(Collect());
            RequestClose(false);
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e) =>
            SendToGame(gotoMenu: false);

        private void BtnGotoMenu_Click(object sender, RoutedEventArgs e) =>
            SendToGame(gotoMenu: true);

        private void SendToGame(bool gotoMenu)
        {
            try
            {
                var prefs = Collect();
                PersistPrefs(prefs);
                ShellConfigureLaunchService.ApplyToRunningGame(_installRoot, prefs, gotoMenu);
                TxtStatus.Text = gotoMenu ? "Goto menu sent." : "Sent to the running game.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
        }

        private void CmbEvent_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateEventOverrideUi();

        private void ChkEventOverride_Changed(object sender, RoutedEventArgs e) =>
            UpdateEventOverrideUi();

        private void UpdateEventOverrideUi()
        {
            if (CmbEvent == null || EventOverridePanel == null || TxtEventChoose == null || CmbEventChar == null)
                return;
            var id = SelectedId(CmbEvent, "");
            var choose = _events != null && _events.Any(ev => ev.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && ev.ChooseCharacter);
            TxtEventChoose.Visibility = choose ? Visibility.Visible : Visibility.Collapsed;
            EventOverridePanel.Visibility = choose ? Visibility.Collapsed : Visibility.Visible;
            CmbEventChar.IsEnabled = !choose && ChkEventOverride.IsChecked == true;
        }

        private void BtnPlayMusic_Click(object sender, RoutedEventArgs e)
        {
            if (ShellConfigureLaunchService.IsGameRunning())
                SendMusic("previewMusic", "Playing in-game.");
            else
                PlaySelectedLocally();
        }

        private void PlaySelectedLocally()
        {
            var id = SelectedMusicId();
            if (id == null)
                return;
            try
            {
                TxtMusicStatus.Text = BgmPreviewService.PlayLocal(_installRoot, id);
            }
            catch (Exception ex)
            {
                TxtMusicStatus.Text = ex.Message;
            }
        }

        private void BtnDefaultInGame_Click(object sender, RoutedEventArgs e)
        {
            ChkMusicOnLaunch.IsChecked = false;
            ChkMusicEverywhere.IsChecked = false;
            try
            {
                var prefs = Collect();
                prefs.UseMusicOnLaunch = false;
                prefs.UseMusicEverywhere = false;
                _settings.SetLaunchConfigure(prefs);
                if (!ShellConfigureLaunchService.IsGameRunning())
                {
                    TxtMusicStatus.Text = "In-game music reset. The next launch uses each stage's own track.";
                    return;
                }
                ShellConfigureLaunchService.SendLiveOp(_installRoot, "defaultMusic");
                TxtMusicStatus.Text = "In-game music set to the stage default.";
            }
            catch (Exception ex)
            {
                TxtMusicStatus.Text = ex.Message;
            }
        }

        private void BtnDefaultMenu_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var prefs = Collect();
                // Keep the playlist; only turn custom menu music off.
                var songs = ShellConfigureLaunchService.ReadMenuPlaylist(_installRoot);
                if (songs.Count == 0 && prefs.MenuPlaylist.Count > 0)
                    songs = prefs.MenuPlaylist.ToList();
                prefs.MenuPlaylist = songs;
                prefs.MenuPlaylistEnabled = false;
                if (string.IsNullOrWhiteSpace(prefs.MenuMusicId) && songs.Count > 0)
                    prefs.MenuMusicId = songs[0];
                _settings.SetLaunchConfigure(prefs);
                ShellConfigureLaunchService.WriteMenuMusic(
                    _installRoot,
                    prefs.MenuMusicId,
                    songs,
                    enabled: false);
                RefreshMenuMusic();
                if (!ShellConfigureLaunchService.IsGameRunning())
                {
                    TxtMusicStatus.Text = songs.Count == 0
                        ? "Menu music reset to the game default."
                        : "Menu playlist disabled. Tracks are kept — re-enable in Manage playlist.";
                    return;
                }
                ShellConfigureLaunchService.SendLiveOp(_installRoot, "defaultMenu", new JObject
                {
                    ["enabled"] = false,
                    ["playlist"] = new JArray(songs),
                    ["id"] = prefs.MenuMusicId ?? "",
                    ["musicId"] = prefs.MenuMusicId ?? ""
                });
                TxtMusicStatus.Text = songs.Count == 0
                    ? "Menu music set to default."
                    : "Menu playlist disabled (songs kept).";
            }
            catch (Exception ex)
            {
                TxtMusicStatus.Text = ex.Message;
            }
        }

        private void BtnManagePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new MenuPlaylistWindow(_installRoot, ShellConfigureLaunchService.ListMusic(_installRoot))
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true)
                return;
            try
            {
                var songs = dlg.Playlist;
                var prefs = Collect();
                prefs.MenuPlaylist = songs;
                prefs.MenuPlaylistEnabled = dlg.PlaylistEnabled;
                prefs.MenuMusicId = songs.FirstOrDefault() ?? prefs.MenuMusicId;
                _settings.SetLaunchConfigure(prefs);
                ShellConfigureLaunchService.WriteMenuMusic(
                    _installRoot,
                    prefs.MenuMusicId,
                    songs,
                    enabled: dlg.PlaylistEnabled);
                RefreshMenuMusic();
                if (!ShellConfigureLaunchService.IsGameRunning())
                {
                    TxtMusicStatus.Text = !dlg.PlaylistEnabled
                        ? (songs.Count == 0
                            ? "Menu playlist empty and disabled."
                            : "Menu playlist saved (disabled).")
                        : (songs.Count == 0
                            ? "Menu playlist cleared."
                            : "Saved menu playlist. It applies the next time the game launches.");
                    return;
                }
                var payload = new JObject
                {
                    ["musicId"] = prefs.MenuMusicId,
                    ["id"] = prefs.MenuMusicId,
                    ["enabled"] = dlg.PlaylistEnabled,
                    ["playlist"] = new JArray(songs)
                };
                ShellConfigureLaunchService.SendLiveOp(_installRoot, "menuMusic", payload);
                TxtMusicStatus.Text = !dlg.PlaylistEnabled
                    ? "Menu playlist disabled (" + songs.Count + " songs kept)."
                    : (songs.Count == 0
                        ? "Menu playlist cleared."
                        : "Menu playlist updated (" + songs.Count + " songs).");
            }
            catch (Exception ex)
            {
                TxtMusicStatus.Text = ex.Message;
            }
        }

        private void BtnStopMusic_Click(object sender, RoutedEventArgs e)
        {
            BgmPreviewService.Stop();
            if (ShellConfigureLaunchService.IsGameRunning())
            {
                try
                {
                    ShellConfigureLaunchService.SendLiveOp(_installRoot, "stopMusic");
                }
                catch (Exception ex)
                {
                    TxtMusicStatus.Text = ex.Message;
                    return;
                }
            }
            TxtMusicStatus.Text = "Stopped.";
        }

        private void BtnUpdateMusic_Click(object sender, RoutedEventArgs e)
        {
            if (!ShellConfigureLaunchService.IsGameRunning())
            {
                TxtMusicStatus.Text = "Launch the game to update in-game music.";
                return;
            }
            SendMusic("previewMusic", "Updated in-game music.");
        }

        private void BtnMenuMusic_Click(object sender, RoutedEventArgs e)
        {
            var id = SelectedMusicId();
            if (id == null)
                return;
            try
            {
                var prefs = Collect();
                prefs.MenuMusicId = id;
                prefs.MenuPlaylist = new List<string> { id };
                prefs.MenuPlaylistEnabled = true;
                _settings.SetLaunchConfigure(prefs);
                ShellConfigureLaunchService.WriteMenuMusic(_installRoot, id, prefs.MenuPlaylist, enabled: true);
                RefreshMenuMusic();
                if (!ShellConfigureLaunchService.IsGameRunning())
                {
                    TxtMusicStatus.Text = "Saved as menu music. It applies the next time the game launches.";
                    return;
                }
                ShellConfigureLaunchService.SendLiveOp(_installRoot, "menuMusic", new JObject
                {
                    ["musicId"] = id,
                    ["id"] = id,
                    ["enabled"] = true,
                    ["playlist"] = new JArray(id)
                });
                TxtMusicStatus.Text = "Set as menu music.";
            }
            catch (Exception ex)
            {
                TxtMusicStatus.Text = ex.Message;
            }
        }

        private string? SelectedMusicId()
        {
            var id = CmbMusic.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(id))
                id = CmbMusic.Text.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                TxtMusicStatus.Text = "Pick a track first.";
                return null;
            }
            return id;
        }

        private void OnMusicEverywhereChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                var prefs = Collect();
                _settings.SetLaunchConfigure(prefs);
                if (!ShellConfigureLaunchService.IsGameRunning())
                    return;
                if (prefs.UseMusicEverywhere)
                {
                    var id = SelectedMusicId();
                    if (id == null)
                        return;
                    ShellConfigureLaunchService.SendLiveOp(_installRoot, "previewMusic", new JObject
                    {
                        ["musicId"] = id,
                        ["id"] = id,
                        ["musicEverywhere"] = true
                    });
                    TxtMusicStatus.Text = "Music everywhere enabled.";
                }
                else
                {
                    ShellConfigureLaunchService.SendLiveOp(_installRoot, "defaultMusic");
                    TxtMusicStatus.Text = "Music everywhere off — stage default restored.";
                }
            }
            catch (Exception ex)
            {
                TxtMusicStatus.Text = ex.Message;
            }
        }

        private void SendMusic(string op, string ok)
        {
            var id = SelectedMusicId();
            if (id == null)
                return;
            try
            {
                var prefs = Collect();
                _settings.SetLaunchConfigure(prefs);
                ShellConfigureLaunchService.SendLiveOp(_installRoot, op, new JObject
                {
                    ["musicId"] = id,
                    ["id"] = id,
                    ["musicEverywhere"] = prefs.UseMusicEverywhere
                });
                TxtMusicStatus.Text = ok;
            }
            catch (Exception ex)
            {
                TxtMusicStatus.Text = ex.Message;
            }
        }

        private void RefreshLiveUi()
        {
            var live = ShellConfigureLaunchService.IsGameRunning();
            if (live != _gameLive)
            {
                _gameLive = live;
                var show = live ? Visibility.Visible : Visibility.Collapsed;
                BtnApply.Visibility = show;
                BtnGoto.Visibility = show;
            }
            if (live)
                RefreshNowPlaying();
            else if (TxtNowPlaying != null)
                TxtNowPlaying.Text = "Current in-game music: —";
            RefreshMenuMusic();
        }

        private void RefreshNowPlaying()
        {
            var path = Path.Combine(ShellWorkspaceService.StateDirectory(_installRoot), "now-playing.json");
            var name = "—";
            try
            {
                if (File.Exists(path))
                {
                    var obj = JObject.Parse(File.ReadAllText(path));
                    name = obj["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        name = obj["id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        name = "—";
                }
            }
            catch
            {
                name = "—";
            }
            TxtNowPlaying.Text = "Current in-game music: " + name;
        }

        private void RefreshMenuMusic()
        {
            if (TxtMenuMusic == null)
                return;
            var name = "—";
            var enabled = false;
            var path = ShellWorkspaceService.UserStateFile(_installRoot, "menu-music.json");
            try
            {
                if (File.Exists(path))
                {
                    var obj = JObject.Parse(File.ReadAllText(path));
                    name = obj["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        name = obj["id"]?.ToString();
                    enabled = ShellConfigureLaunchService.ReadMenuPlaylistEnabled(_installRoot);
                }
            }
            catch
            {
                name = "";
            }
            if (string.IsNullOrWhiteSpace(name) || name == "—")
            {
                var saved = _settings.GetLaunchConfigure().MenuMusicId;
                name = string.IsNullOrWhiteSpace(saved) ? "—" : saved;
            }
            if (name != "—" && !enabled)
                TxtMenuMusic.Text = "Current menu music: " + name + " (disabled)";
            else
                TxtMenuMusic.Text = "Current menu music: " + name;
        }

        protected override void OnClosed(EventArgs e)
        {
            _liveTimer.Stop();
            BgmPreviewService.Stop();
            base.OnClosed(e);
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var prefs = Collect();
                _settings.SetLaunchConfigure(prefs);
                TxtStatus.Text = "Writing launch files…";
                LauncherWindow.ShowLogBeforeGame();
                ShellConfigureLaunchService.Launch(_installRoot, prefs);
                TxtStatus.Text = "Launched SSF2.";
                if (ChkKeepOpen.IsChecked != true)
                    RequestClose(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Configure", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void Fill(System.Windows.Controls.ComboBox box, IEnumerable<(string Id, string Label)> items)
        {
            box.ItemsSource = items.Select(x => new Choice(x.Id, x.Label)).ToList();
            box.DisplayMemberPath = nameof(Choice.Label);
        }

        private static bool IsNoneCharacter(string? character) =>
            string.IsNullOrWhiteSpace(character)
            || character.Equals(NoneCharacter, StringComparison.OrdinalIgnoreCase)
            || character.Equals("(none)", StringComparison.OrdinalIgnoreCase);

        private static void Select(System.Windows.Controls.ComboBox box, string id)
        {
            if (box.ItemsSource is not IEnumerable<Choice> items)
                return;
            var hit = items.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            box.SelectedItem = hit ?? items.FirstOrDefault();
        }

        private static string SelectedId(System.Windows.Controls.ComboBox box, string fallback)
        {
            if (box.SelectedItem is Choice c && !string.IsNullOrWhiteSpace(c.Id))
                return c.Id;
            return fallback;
        }

        private static int ParseInt(string? text, int fallback) =>
            int.TryParse(text, out var n) ? n : fallback;

        private sealed record Choice(string Id, string Label);

        private sealed class PlayerRow : INotifyPropertyChanged
        {
            public PlayerRow(List<string> characters, ObservableCollection<string> tagChoices)
            {
                Characters = characters;
                TagChoices = tagChoices;
            }
            public string Slot { get; set; } = "";
            public IReadOnlyList<string> Characters { get; }
            public ObservableCollection<string> TagChoices { get; }
            public event EventHandler? AddTagRequested;
            public event EventHandler? ManageTagsRequested;
            private string _character = "";
            private bool _human = true;
            private int _level = 3;
            private int _team = 1;
            private string _tag = "";
            private bool _rowVisible = true;
            private bool _showHumanCpu = true;
            private bool _showTeam;
            public string Character { get => _character; set { _character = value; Notify(); } }
            public bool Human
            {
                get => _human;
                set { _human = value; Notify(); Notify(nameof(ShowCpu)); Notify(nameof(ShowTag)); }
            }
            public int Level { get => _level; set { _level = value; Notify(); } }
            public int Team { get => _team; set { _team = value; Notify(); } }
            public string Tag
            {
                get => _tag;
                set { _tag = value ?? ""; Notify(); Notify(nameof(TagSelection)); }
            }
            public string TagSelection
            {
                get => string.IsNullOrEmpty(_tag) ? NoTag : _tag;
                set
                {
                    if (value == AddTag)
                    {
                        Notify(nameof(TagSelection));
                        AddTagRequested?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    if (value == ManageTags)
                    {
                        Notify(nameof(TagSelection));
                        ManageTagsRequested?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    _tag = string.IsNullOrEmpty(value) || value == NoTag ? "" : value;
                    Notify();
                    Notify(nameof(TagSelection));
                }
            }
            public void RefreshTagSelection() => Notify(nameof(TagSelection));
            public bool RowVisible
            {
                get => _rowVisible;
                set { _rowVisible = value; Notify(); }
            }
            public bool ShowHumanCpu
            {
                get => _showHumanCpu;
                set { _showHumanCpu = value; Notify(); Notify(nameof(ShowHuman)); Notify(nameof(ShowCpu)); Notify(nameof(ShowTag)); }
            }
            public bool ShowTeam
            {
                get => _showTeam;
                set { _showTeam = value; Notify(); }
            }
            public bool ShowHuman => ShowHumanCpu;
            public bool ShowCpu => ShowHumanCpu && !Human;
            public bool ShowTag => !ShowHumanCpu || Human;
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Notify([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
