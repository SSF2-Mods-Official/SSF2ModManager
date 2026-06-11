using SSF2ModManager.Models;
using SSF2ModManager.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            DialogTheme.ApplyWindow(this);

            var root = new StackPanel { Margin = new Thickness(24) };

            var header = DialogTheme.Text(mod.Name, "PrimaryBrush", fontSize: 20);
            header.FontWeight = FontWeights.SemiBold;
            header.Margin = new Thickness(0, 0, 0, 4);
            header.TextTrimming = TextTrimming.CharacterEllipsis;
            root.Children.Add(header);

            var authorLine = DialogTheme.Text($"Author: {mod.Author}  •  {mod.Category}", "TextSecondaryBrush", fontSize: 12);
            authorLine.Margin = new Thickness(0, 0, 0, 2);
            root.Children.Add(authorLine);

            var statusLine = DialogTheme.Text(
                $"Version: {mod.TargetVersion}  •  Status: {(mod.Enabled ? "✅ " + Localization.Get("Enabled") : "⛔ " + Localization.Get("Disabled"))}",
                "TextSecondaryBrush", fontSize: 12);
            statusLine.Margin = new Thickness(0, 0, 0, 2);
            root.Children.Add(statusLine);

            var installedLine = DialogTheme.Text($"Installed: {mod.InstalledDate:g}", "TextSecondaryBrush", fontSize: 11);
            installedLine.Margin = new Thickness(0, 0, 0, 16);
            root.Children.Add(installedLine);

            root.Children.Add(DialogTheme.Separator());

            var actionsLabel = DialogTheme.Text("Actions", "TextSecondaryBrush", fontSize: 13);
            actionsLabel.Margin = new Thickness(0, 0, 0, 10);
            root.Children.Add(actionsLabel);

            var toggleText = mod.Enabled ? ("⏸ " + Localization.Get("Disable")) : ("▶ " + Localization.Get("Enable"));
            var btnToggle = DialogTheme.SurfaceButton(toggleText,
                mod.Enabled ? "CardBrush" : "SuccessBrush", fontSize: 12);
            btnToggle.Click += BtnToggle_Click;
            root.Children.Add(btnToggle);

            var btnOpen = DialogTheme.SurfaceButton("📂 " + Localization.Get("OpenModsFolder"), fontSize: 12);
            btnOpen.Margin = new Thickness(0, 6, 0, 0);
            btnOpen.Click += BtnOpenFolder_Click;
            root.Children.Add(btnOpen);

            var btnChangeVersion = DialogTheme.SurfaceButton("🔄 " + Localization.Get("ChangeTargetVersion"), fontSize: 12);
            btnChangeVersion.Margin = new Thickness(0, 6, 0, 0);
            btnChangeVersion.Click += BtnChangeVersion_Click;
            root.Children.Add(btnChangeVersion);

            var btnAddToBuild = DialogTheme.SurfaceButton("➕ " + Localization.Get("AddToAnotherBuild"), fontSize: 12);
            btnAddToBuild.Margin = new Thickness(0, 6, 0, 0);
            btnAddToBuild.Click += BtnAddToBuild_Click;
            root.Children.Add(btnAddToBuild);

            var btnReinstallLocal = DialogTheme.SurfaceButton("🔄 " + Localization.Get("ReinstallFromFolder"), fontSize: 12);
            btnReinstallLocal.Margin = new Thickness(0, 6, 0, 0);
            btnReinstallLocal.Click += BtnReinstallLocal_Click;
            root.Children.Add(btnReinstallLocal);

            var btnReinstall = DialogTheme.SurfaceButton("⬇ " + Localization.Get("ReinstallFromGameBanana"), "PrimaryBrush", fontSize: 12);
            btnReinstall.Margin = new Thickness(0, 6, 0, 0);
            btnReinstall.Click += BtnReinstall_Click;
            root.Children.Add(btnReinstall);

            root.Children.Add(DialogTheme.Separator());

            var bottomRow = new DockPanel();
            var btnRemove = DialogTheme.SurfaceButton("🗑 " + Localization.Get("RemoveMod"), "ErrorBrush", fontSize: 12);
            btnRemove.Click += BtnRemove_Click;
            DockPanel.SetDock(btnRemove, Dock.Left);
            bottomRow.Children.Add(btnRemove);

            var btnClose = DialogTheme.SurfaceButton(Localization.Get("Close"), fontSize: 13, margin: new Thickness(0),
                hAlign: HorizontalAlignment.Right);
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
                    // Check if mod already exists in target build
                    var existing = _modManager.InstalledMods.FirstOrDefault(m =>
                        m.GameBananaId == _mod.GameBananaId && m.TargetVersion == entry.VersionName);

                    if (existing != null)
                    {
                        MessageBox.Show(
                            $"\"{_mod.Name}\" is already installed in {entry.DisplayName}.",
                            "Already Exists", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Create a clone of the installed mod for the new version
                    var clone = new InstalledMod
                    {
                        Id = Guid.NewGuid().ToString(),
                        GameBananaId = _mod.GameBananaId,
                        Name = _mod.Name,
                        Author = _mod.Author,
                        Creator = _mod.Creator,
                        ModType = _mod.ModType,
                        Description = _mod.Description,
                        Category = _mod.Category,
                        Version = _mod.Version,
                        FolderName = _mod.FolderName,
                        ThumbnailUrl = _mod.ThumbnailUrl,
                        ProfileUrl = _mod.ProfileUrl,
                        Enabled = false, // Start disabled
                        InstalledDate = DateTime.Now,
                        InstalledFiles = _mod.InstalledFiles.ToList(),
                        TargetVersion = entry.VersionName,
                        IsGameFiles = _mod.IsGameFiles,
                        IgnoreUpdates = _mod.IgnoreUpdates,
                        BackedUpFiles = new System.Collections.Generic.List<BackedUpFile>()
                    };

                    _modManager.InstalledMods.Add(clone);
                    _modManager.SaveDatabase();
                    Changed = true;

                    // Ask if user wants to enable it now
                    var enableResult = MessageBox.Show(
                        $"Mod added to {entry.DisplayName}.\n\n" +
                        $"Do you want to enable it now?",
                        "Enable Mod?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (enableResult == MessageBoxResult.Yes)
                    {
                        try
                        {
                            _modManager.ToggleMod(clone);
                            Changed = true;
                            MessageBox.Show($"Mod enabled in {entry.DisplayName}.", "Done",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                $"Error enabling mod:\n{ex.Message}\n\n" +
                                $"You can enable it manually from the Installed Mods page.",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Mod added to {entry.DisplayName} (disabled).\n" +
                            $"Enable it from the Installed Mods page.",
                            "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
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
            // Check if this mod is installed in multiple builds
            var allInstances = _modManager.InstalledMods
                .Where(m => m.GameBananaId == _mod.GameBananaId && m.GameBananaId > 0)
                .ToList();

            bool removeFromAll = false;

            if (allInstances.Count > 1)
            {
                // Mod is in multiple builds - ask which ones to remove
                var builds = string.Join(", ", allInstances.Select(m => 
                {
                    var entry = _modManager.Versions.FirstOrDefault(v => v.VersionName == m.TargetVersion);
                    return entry?.DisplayName ?? m.TargetVersion;
                }));

                var choice = MessageBox.Show(
                    $"\"{_mod.Name}\" is installed in {allInstances.Count} builds:\n{builds}\n\n" +
                    $"Do you want to remove it from ALL builds?\n\n" +
                    $"• YES = Remove from all builds\n" +
                    $"• NO = Remove from this build only\n" +
                    $"• CANCEL = Don't remove",
                    "Remove from Multiple Builds?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (choice == MessageBoxResult.Cancel)
                    return;

                removeFromAll = (choice == MessageBoxResult.Yes);
            }
            else
            {
                // Single build - standard confirmation
                var result = MessageBox.Show(
                    $"Are you sure you want to remove \"{_mod.Name}\"?\nOriginal game files will be restored.",
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                if (removeFromAll)
                {
                    // Remove all instances
                    foreach (var instance in allInstances.ToList())
                    {
                        await _modManager.UninstallModAsync(instance);
                    }
                    MessageBox.Show($"Removed \"{_mod.Name}\" from all builds.", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Remove only this instance
                    await _modManager.UninstallModAsync(_mod);
                }

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
}
