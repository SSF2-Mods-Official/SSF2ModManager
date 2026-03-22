using SSF2ModManager.Models;
using SSF2ModManager.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;

namespace SSF2ModManager.Dialogs
{
    public class InstalledModSettingsDialog : Window
    {
        private readonly ModManagerService _modManager;
        private readonly InstalledMod _mod;

        public bool Changed { get; private set; }
        public bool ModRemoved { get; private set; }
        public bool ReinstallRequested { get; private set; }

        public InstalledModSettingsDialog(ModManagerService modManager, InstalledMod mod)
        {
            _modManager = modManager;
            _mod = mod;

            Title = $"Mod Settings — {mod.Name}";
            Width = 480;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 46));

            var cardBg = new SolidColorBrush(Color.FromRgb(30, 30, 55));
            var accent = new SolidColorBrush(Color.FromRgb(30, 136, 229));
            var textPrimary = new SolidColorBrush(Color.FromRgb(224, 224, 224));
            var textSecondary = new SolidColorBrush(Color.FromRgb(160, 160, 180));
            var danger = new SolidColorBrush(Color.FromRgb(229, 57, 53));
            var green = new SolidColorBrush(Color.FromRgb(76, 175, 80));

            var root = new StackPanel { Margin = new Thickness(24) };

            // Header
            root.Children.Add(new TextBlock
            {
                Text = mod.Name,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = accent,
                Margin = new Thickness(0, 0, 0, 4),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            // Info fields
            root.Children.Add(new TextBlock
            {
                Text = $"Author: {mod.Author}  •  {mod.Category}",
                FontSize = 12,
                Foreground = textSecondary,
                Margin = new Thickness(0, 0, 0, 2)
            });
            root.Children.Add(new TextBlock
            {
                Text = $"Version: {mod.TargetVersion}  •  Status: {(mod.Enabled ? "✅ " + Localization.Get("Enabled") : "⛔ " + Localization.Get("Disabled"))}",
                FontSize = 12,
                Foreground = textSecondary,
                Margin = new Thickness(0, 0, 0, 2)
            });
            root.Children.Add(new TextBlock
            {
                Text = $"Installed: {mod.InstalledDate:g}",
                FontSize = 11,
                Foreground = textSecondary,
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Separator
            root.Children.Add(MakeSeparator());

            root.Children.Add(new TextBlock
            {
                Text = "Actions",
                FontSize = 13,
                Foreground = textSecondary,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Toggle button
            var toggleText = mod.Enabled ? ("⏸ " + Localization.Get("Disable")) : ("▶ " + Localization.Get("Enable"));
            var btnToggle = MakeButton(toggleText, mod.Enabled ? cardBg : green, 12);
            btnToggle.Click += BtnToggle_Click;
            root.Children.Add(btnToggle);

            // Open folder
            var btnOpen = MakeButton("📂 " + Localization.Get("OpenModsFolder"), cardBg, 12);
            btnOpen.Margin = new Thickness(0, 6, 0, 0);
            btnOpen.Click += BtnOpenFolder_Click;
            root.Children.Add(btnOpen);

            // Change version
            var btnChangeVersion = MakeButton("🔄 " + Localization.Get("ChangeTargetVersion"), cardBg, 12);
            btnChangeVersion.Margin = new Thickness(0, 6, 0, 0);
            btnChangeVersion.Click += BtnChangeVersion_Click;
            root.Children.Add(btnChangeVersion);

            // Add to another build
            var btnAddToBuild = MakeButton("➕ " + Localization.Get("AddToAnotherBuild"), cardBg, 12);
            btnAddToBuild.Margin = new Thickness(0, 6, 0, 0);
            btnAddToBuild.Click += BtnAddToBuild_Click;
            root.Children.Add(btnAddToBuild);

            // Re-install from folder (local changes)
            var btnReinstallLocal = MakeButton("🔄 " + Localization.Get("ReinstallFromFolder"), cardBg, 12);
            btnReinstallLocal.Margin = new Thickness(0, 6, 0, 0);
            btnReinstallLocal.Click += BtnReinstallLocal_Click;
            root.Children.Add(btnReinstallLocal);

            // Re-install from GameBanana
            var btnReinstall = MakeButton("⬇ " + Localization.Get("ReinstallFromGameBanana"), accent, 12);
            btnReinstall.Margin = new Thickness(0, 6, 0, 0);
            btnReinstall.Click += BtnReinstall_Click;
            root.Children.Add(btnReinstall);

            // Separator + Remove
            root.Children.Add(MakeSeparator());

            var bottomRow = new DockPanel();
            var btnRemove = MakeButton("🗑 " + Localization.Get("RemoveMod"), danger, 12);
            btnRemove.Click += BtnRemove_Click;
            DockPanel.SetDock(btnRemove, Dock.Left);
            bottomRow.Children.Add(btnRemove);

            var btnClose = MakeButton(Localization.Get("Close"), cardBg, 13);
            btnClose.HorizontalAlignment = HorizontalAlignment.Right;
            btnClose.Click += (s, e) => DialogResult = true;
            bottomRow.Children.Add(btnClose);

            root.Children.Add(bottomRow);
            Content = root;
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _modManager.ToggleMod(_mod);
                Changed = true;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error toggling mod:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var modPath = Path.Combine(_modManager.ModsBaseDir, _mod.Category, _mod.FolderName);
            if (Directory.Exists(modPath))
                Process.Start(new ProcessStartInfo(modPath) { UseShellExecute = true });
            else
                MessageBox.Show("Mod folder not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnChangeVersion_Click(object sender, RoutedEventArgs e)
        {
            if (_modManager.Versions.Count == 0)
            {
                MessageBox.Show("No versions configured.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var versionNames = _modManager.Versions.Select(v => v.DisplayName).ToList();
            var picker = new ListPickerDialog("Change Target Version",
                $"Select new target version for \"{_mod.Name}\":", versionNames);
            picker.Owner = this;

            if (picker.ShowDialog() == true && picker.SelectedItem != null)
            {
                try
                {
                    // Disable mod in old version first (restore backups)
                    if (_mod.Enabled && !_mod.IsGameFiles)
                    {
                        _modManager.ToggleMod(_mod); // disable
                    }

                    // Find the version entry to get the actual VersionName
                    var entry = _modManager.Versions.FirstOrDefault(v => v.DisplayName == picker.SelectedItem);
                    if (entry != null)
                    {
                        _mod.TargetVersion = entry.VersionName;
                        _modManager.SaveDatabase();

                        // Re-enable in new version
                        if (!_mod.IsGameFiles)
                        {
                            _modManager.ToggleMod(_mod); // enable
                        }

                        Changed = true;
                        MessageBox.Show($"Moved to {entry.DisplayName}.", "Done",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        DialogResult = true;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to change version:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnAddToBuild_Click(object sender, RoutedEventArgs e)
        {
            if (_modManager.Versions.Count <= 1)
            {
                MessageBox.Show("You need at least two builds to copy a mod.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var otherVersions = _modManager.Versions
                .Where(v => v.VersionName != _mod.TargetVersion)
                .Select(v => v.DisplayName).ToList();

            var picker = new ListPickerDialog("Add to Build",
                $"Copy \"{_mod.Name}\" to another build:", otherVersions);
            picker.Owner = this;

            if (picker.ShowDialog() == true && picker.SelectedItem != null)
            {
                var entry = _modManager.Versions.FirstOrDefault(v => v.DisplayName == picker.SelectedItem);
                if (entry != null)
                {
                    // Create a clone of the installed mod for the new version
                    var clone = new InstalledMod
                    {
                        Id = Guid.NewGuid().ToString(),
                        GameBananaId = _mod.GameBananaId,
                        Name = _mod.Name,
                        Author = _mod.Author,
                        Description = _mod.Description,
                        Category = _mod.Category,
                        FolderName = _mod.FolderName,
                        ThumbnailUrl = _mod.ThumbnailUrl,
                        ProfileUrl = _mod.ProfileUrl,
                        Enabled = false, // Start disabled
                        InstalledDate = DateTime.Now,
                        InstalledFiles = _mod.InstalledFiles.ToList(),
                        TargetVersion = entry.VersionName,
                        IsGameFiles = _mod.IsGameFiles,
                        BackedUpFiles = new System.Collections.Generic.List<BackedUpFile>()
                    };

                    _modManager.InstalledMods.Add(clone);
                    _modManager.SaveDatabase();
                    Changed = true;
                    MessageBox.Show($"Added to {entry.DisplayName} (disabled). Enable it from Installed Mods.",
                        "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnReinstall_Click(object sender, RoutedEventArgs e)
        {
            ReinstallRequested = true;
            Changed = true;
            DialogResult = true;
        }

        private void BtnReinstallLocal_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Re-install \"{_mod.Name}\" from its local folder?\n\n" +
                $"This will restore original files first, then re-deploy all mod files from the local folder.\n" +
                $"Useful if you've made local changes to the mod files.",
                "Re-install from Folder", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _modManager.ReinstallFromFolder(_mod);
                Changed = true;
                MessageBox.Show("✅ Re-installed from local folder successfully!", "Done",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error re-installing:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to remove \"{_mod.Name}\"?\nOriginal game files will be restored.",
                "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _modManager.UninstallModAsync(_mod);
                    ModRemoved = true;
                    Changed = true;
                    DialogResult = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error removing mod:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static Border MakeSeparator()
        {
            return new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 12, 0, 12)
            };
        }

        private static Button MakeButton(string text, SolidColorBrush bg, double fontSize)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(14, 7, 14, 7),
                Background = bg,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = fontSize,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
        }
    }
}
