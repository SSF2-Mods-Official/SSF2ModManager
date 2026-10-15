using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSF2ModManager.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace SSF2ModManager.Views
{
    public partial class ManageModsWindow : Window
    {
        private readonly string _shellRoot;
        private readonly Dictionary<string, Image> _previewSlots = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _previewCts;
        private string? _section;

        public event Action? SectionChanged;

        public ManageModsWindow(string shellRoot)
        {
            _shellRoot = shellRoot;
            InitializeComponent();
            Closed += (_, _) =>
            {
                try { _previewCts?.Cancel(); } catch { /* ignore */ }
                _previewCts?.Dispose();
                _previewCts = null;
            };
            Reload();
        }

        public void OpenSection(string section)
        {
            _section = string.IsNullOrWhiteSpace(section)
                       || section.Equals("home", StringComparison.OrdinalIgnoreCase)
                ? null
                : section;
            Reload();
        }

        public IReadOnlyList<(string Id, string Label)> GetTrail()
        {
            var trail = new List<(string Id, string Label)> { ("home", "Manage Mods") };
            if (!string.IsNullOrEmpty(_section))
                trail.Add((_section, _section));
            return trail;
        }

        private void Reload()
        {
            try
            {
                ReloadBody();
            }
            finally
            {
                SectionChanged?.Invoke();
            }
        }

        private void ReloadBody()
        {
            try { _previewCts?.Cancel(); } catch { /* ignore */ }
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();
            _previewSlots.Clear();
            SectionsHost.Children.Clear();

            List<(string Section, List<ExpansionModEntry> Mods)> sections;
            try
            {
                sections = ExpansionModsService.Load(_shellRoot);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Failed to load mods: " + ex.Message;
                return;
            }
            if (sections.Count == 0)
            {
                TxtStatus.Text = "No packs under expansions.";
                return;
            }

            if (string.IsNullOrEmpty(_section) || sections.All(s => s.Section != _section))
            {
                foreach (var (section, mods) in sections)
                {
                    var captured = section;
                    var button = new Button
                    {
                        Content = section + "  (" + mods.Count + ")",
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 0, 0, 8),
                        Style = (Style)FindResource("AccentButton")
                    };
                    button.Click += (_, _) =>
                    {
                        _section = captured;
                        Reload();
                    };
                    SectionsHost.Children.Add(button);
                }
                TxtStatus.Text = "Pick a section.";
                return;
            }

            var back = new Button
            {
                Content = "Back",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 0, 12),
                Style = (Style)FindResource("ModernButton")
            };
            back.Click += (_, _) =>
            {
                _section = null;
                Reload();
            };
            SectionsHost.Children.Add(back);

            var needPreview = new List<string>();
            foreach (var (section, mods) in sections)
            {
                if (section != _section)
                    continue;
                SectionsHost.Children.Add(new TextBlock
                {
                    Text = section,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 8)
                });
                if (mods.Count == 0)
                {
                    var empty = new TextBlock { Text = "Nothing here yet.", Margin = new Thickness(0, 0, 0, 8) };
                    empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                    SectionsHost.Children.Add(empty);
                }
                foreach (var mod in mods)
                {
                    SectionsHost.Children.Add(BuildCard(mod));
                    if (mod.Section.Equals("character", StringComparison.OrdinalIgnoreCase)
                        && CharacterSelectPreviewService.NeedsExport(mod.FolderPath))
                        needPreview.Add(mod.FolderPath);
                }
            }

            if (needPreview.Count == 0)
            {
                TxtStatus.Text = "Enable or disable takes effect the next time SSF2 starts.";
                return;
            }

            TxtStatus.Text = $"Building {needPreview.Count} character preview(s) in the background…";
            var token = _previewCts.Token;
            var progress = new Progress<string>(msg =>
            {
                if (!token.IsCancellationRequested)
                    TxtStatus.Text = msg;
            });
            _ = Task.Run(async () =>
            {
                try
                {
                    await CharacterSelectPreviewService.WarmPreviewsAsync(
                        needPreview,
                        onReady: (folder, path) =>
                        {
                            Dispatcher.BeginInvoke(() => ApplyPreview(folder, path));
                        },
                        progress: progress,
                        cancellationToken: token);
                    if (!token.IsCancellationRequested)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            TxtStatus.Text = "Enable or disable takes effect the next time SSF2 starts.";
                        });
                    }
                }
                catch (OperationCanceledException) { /* closed */ }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        TxtStatus.Text = "Preview build issue: " + ex.Message;
                    });
                }
            }, token);
        }

        private void ApplyPreview(string folder, string path)
        {
            if (!_previewSlots.TryGetValue(folder, out var img) || !File.Exists(path))
                return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                img.Source = bmp;
                img.Visibility = Visibility.Visible;
            }
            catch { /* ignore bad png */ }
        }

        private Border BuildCard(ExpansionModEntry mod)
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var img = new Image
            {
                Width = 64,
                Height = 64,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var previewHost = new Border
            {
                Width = 64,
                Height = 64,
                ClipToBounds = true,
                Child = img
            };
            Grid.SetColumn(previewHost, 0);
            grid.Children.Add(previewHost);
            if (mod.Section.Equals("character", StringComparison.OrdinalIgnoreCase))
                _previewSlots[mod.FolderPath] = img;

            if (!string.IsNullOrWhiteSpace(mod.PreviewPath) && File.Exists(mod.PreviewPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(mod.PreviewPath);
                    bmp.EndInit();
                    bmp.Freeze();
                    img.Source = bmp;
                    img.Visibility = Visibility.Visible;
                }
                catch { }
            }

            var body = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
            Grid.SetColumn(body, 1);
            var nameBlock = new TextBlock
            {
                Text = mod.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            };
            nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            body.Children.Add(nameBlock);
            var meta = string.Join(" · ", new[] { mod.Type, mod.Creator }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (meta.Length > 0)
            {
                var metaBlock = new TextBlock
                {
                    Text = meta,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                metaBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                body.Children.Add(metaBlock);
            }
            if (!string.IsNullOrWhiteSpace(mod.Description))
            {
                var description = new TextBlock
                {
                    Text = mod.Description,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = 11,
                    MaxHeight = 36,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                body.Children.Add(description);
            }
            foreach (var setting in mod.Settings)
                body.Children.Add(BuildSetting(mod, setting));
            grid.Children.Add(body);

            var buttons = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
                MinWidth = 88
            };
            Grid.SetColumn(buttons, 2);
            if (mod.CanToggleEnabled)
            {
                var enabled = new CheckBox
                {
                    Content = "Enabled",
                    IsChecked = mod.Enabled,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                enabled.Checked += (_, _) => SetEnabled(mod, true);
                enabled.Unchecked += (_, _) => SetEnabled(mod, false);
                buttons.Children.Add(enabled);
            }
            if (CanRemove(mod))
            {
                var remove = new Button
                {
                    Content = "Remove",
                    Padding = new Thickness(10, 4, 10, 4),
                    Style = (Style)FindResource("ModernButton")
                };
                remove.Click += (_, _) => RemoveMod(mod);
                buttons.Children.Add(remove);
            }
            if (buttons.Children.Count > 0)
                grid.Children.Add(buttons);

            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 8),
                Child = grid
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            return card;
        }

        private UIElement BuildSetting(ExpansionModEntry mod, ModSettingEntry setting)
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Right);

            var save = new Button
            {
                Content = "Save",
                Padding = new Thickness(10, 4, 10, 4),
                Style = (Style)FindResource("ModernButton"),
                Margin = new Thickness(0, 0, 6, 0)
            };
            var apply = new Button
            {
                Content = "Save and Apply",
                Padding = new Thickness(10, 4, 10, 4),
                Style = (Style)FindResource("ModernButton")
            };
            buttons.Children.Add(save);
            buttons.Children.Add(apply);
            row.Children.Add(buttons);

            save.Click += (_, _) => SaveMod(mod, setting.Label, applyLive: false);
            apply.Click += (_, _) => SaveMod(mod, setting.Label, applyLive: true);

            if (setting.Type.Equals("bool", StringComparison.OrdinalIgnoreCase))
            {
                var on = IsBoolSettingOn(setting.Value);
                setting.Value = on ? "true" : "false";
                var box = new CheckBox { Content = setting.Label };
                // Click only fires from the user — Checked/Unchecked also fire when
                // IsChecked is assigned, which used to persist "false" on open.
                box.Click += (_, _) => setting.Value = box.IsChecked == true ? "true" : "false";
                box.IsChecked = on;
                row.Children.Add(box);
                return row;
            }

            if (IsDropdownSetting(setting))
            {
                // Normalize so Save persists type=dropdown + optionsSource.
                if (string.IsNullOrWhiteSpace(setting.Type)
                    || setting.Type.Equals("string", StringComparison.OrdinalIgnoreCase))
                {
                    setting.Type = "dropdown";
                }
                if (string.IsNullOrWhiteSpace(setting.OptionsSource)
                    && setting.Id.Equals("stageId", StringComparison.OrdinalIgnoreCase))
                {
                    setting.OptionsSource = "stages";
                }

                var boxWrap = new DockPanel();
                var label = new TextBlock
                {
                    Text = setting.Label,
                    Width = 140,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(label, Dock.Left);
                boxWrap.Children.Add(label);

                var options = ResolveDropdownOptions(setting);
                var combo = new ComboBox
                {
                    IsEditable = false,
                    IsTextSearchEnabled = true,
                    MinWidth = 220,
                    MaxWidth = 420,
                    VerticalAlignment = VerticalAlignment.Center,
                    ItemsSource = options
                };
                var selected = options.FirstOrDefault(o =>
                    string.Equals(OptionValue(o), setting.Value ?? "", StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                    combo.SelectedItem = selected;
                else if (options.Count > 0)
                    combo.SelectedIndex = 0;
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is string selectedId)
                        setting.Value = OptionValue(selectedId);
                };
                // Sync initial value (e.g. "(None)" → "").
                setting.Value = OptionValue(combo.SelectedItem as string ?? "");
                boxWrap.Children.Add(combo);
                row.Children.Add(boxWrap);
                return row;
            }

            var textWrap = new DockPanel();
            var textLabel = new TextBlock
            {
                Text = setting.Label,
                Width = 140,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(textLabel, Dock.Left);
            textWrap.Children.Add(textLabel);
            var input = new TextBox { Text = setting.Value };
            input.TextChanged += (_, _) => setting.Value = input.Text;
            textWrap.Children.Add(input);
            row.Children.Add(textWrap);
            return row;
        }

        private static bool IsDropdownSetting(ModSettingEntry setting)
        {
            if (setting.Type.Equals("dropdown", StringComparison.OrdinalIgnoreCase)
                || setting.Type.Equals("select", StringComparison.OrdinalIgnoreCase)
                || setting.Type.Equals("combo", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(setting.OptionsSource))
                return true;
            if (setting.Options is { Count: > 0 })
                return true;
            return setting.Id.Equals("stageId", StringComparison.OrdinalIgnoreCase);
        }

        private static string OptionValue(string? display) =>
            string.IsNullOrWhiteSpace(display) || display == "(None)" ? "" : display;

        private List<string> ResolveDropdownOptions(ModSettingEntry setting)
        {
            var list = new List<string>();
            if (setting.Options is { Count: > 0 })
                list.AddRange(setting.Options.Where(o => !string.IsNullOrWhiteSpace(o)));
            if (setting.OptionsSource.Equals("stages", StringComparison.OrdinalIgnoreCase)
                || setting.Id.Equals("stageId", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var id in ShellConfigureLaunchService.ListStages(_shellRoot))
                    {
                        if (!list.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                            list.Add(id);
                    }
                }
                catch { /* ignore */ }
            }
            list = list
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "(None)")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!string.IsNullOrWhiteSpace(setting.Value)
                && !list.Any(x => x.Equals(setting.Value, StringComparison.OrdinalIgnoreCase)))
            {
                list.Insert(0, setting.Value);
            }
            list.Insert(0, "(None)");
            return list;
        }

        private void SetEnabled(ExpansionModEntry mod, bool enabled)
        {
            mod.Enabled = enabled;
            ExpansionModsService.Save(mod);
            TxtStatus.Text = (enabled ? "Enabled " : "Disabled ") + mod.Name + ". Restart SSF2 to apply.";
        }

        private static bool CanRemove(ExpansionModEntry mod) =>
            !mod.Section.Equals("essentials", StringComparison.OrdinalIgnoreCase)
            && (mod.Section.Equals("intros", StringComparison.OrdinalIgnoreCase)
                || mod.Section.Equals("alts", StringComparison.OrdinalIgnoreCase)
                || mod.Section.Equals("character", StringComparison.OrdinalIgnoreCase)
                || mod.Section.Equals("stages", StringComparison.OrdinalIgnoreCase)
                || mod.Section.Equals("events", StringComparison.OrdinalIgnoreCase)
                || mod.Section.Equals("mods", StringComparison.OrdinalIgnoreCase)
                || mod.Section.Equals("songs", StringComparison.OrdinalIgnoreCase)
                || mod.Section.Equals("music", StringComparison.OrdinalIgnoreCase)
                || mod.Section.Equals("sounds", StringComparison.OrdinalIgnoreCase));

        private void RemoveMod(ExpansionModEntry mod)
        {
            var ask = MessageBox.Show(
                "Remove \"" + mod.Name + "\" from this build?",
                "Manage Mods",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes)
                return;
            try
            {
                if (mod.Section.Equals("intros", StringComparison.OrdinalIgnoreCase))
                    ShellConfigureLaunchService.RemoveIntroFile(_shellRoot, mod.FolderName);
                else
                    ExpansionModsService.Delete(mod);
                TxtStatus.Text = "Removed " + mod.Name + ".";
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Remove", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Bool settings default on unless explicitly false / 0 / off (matches the in-game poll).
        /// </summary>
        private static bool IsBoolSettingOn(string? value)
        {
            var raw = (value ?? "").Trim();
            if (raw.Length == 0)
                return true;
            return !raw.Equals("false", StringComparison.OrdinalIgnoreCase)
                && raw != "0"
                && !raw.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        private void SaveMod(ExpansionModEntry mod, string label, bool applyLive)
        {
            ExpansionModsService.Save(mod);
            if (!applyLive)
            {
                TxtStatus.Text = "Saved " + label + " for " + mod.Name + ".";
                return;
            }
            if (!ShellConfigureLaunchService.IsGameRunning())
            {
                TxtStatus.Text = "Saved " + label + " for " + mod.Name + ". SSF2 is not running — will apply on next menu.";
                return;
            }
            // Touch info.json so the in-game mod's poll sees a change; settings already written.
            try
            {
                var path = Path.Combine(mod.FolderPath, "info.json");
                if (File.Exists(path))
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch { /* ignore */ }
            try
            {
                ShellConfigureLaunchService.FocusGameWindow();
            }
            catch { /* ignore */ }
            TxtStatus.Text = "Saved and applied " + label + " for " + mod.Name + " (live).";
        }

        public bool IsHosted { get; set; }
        public event EventHandler? HostedClose;

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (IsHosted)
            {
                HostedClose?.Invoke(this, EventArgs.Empty);
                return;
            }
            Close();
        }
    }
}
