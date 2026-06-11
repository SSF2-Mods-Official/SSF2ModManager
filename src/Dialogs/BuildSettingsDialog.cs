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
using TextBox = System.Windows.Controls.TextBox;

namespace SSF2ModManager.Dialogs
{
    public class BuildSettingsDialog : Window
    {
        private readonly ModManagerService _modManager;
        private readonly SSF2VersionEntry _entry;
        private readonly TextBox _nicknameBox;
        private readonly TextBlock _pathText;

        public bool BuildRemoved { get; private set; }
        public bool Changed { get; private set; }

        public BuildSettingsDialog(ModManagerService modManager, SSF2VersionEntry entry)
        {
            _modManager = modManager;
            _entry = entry;

            Title = $"Build Settings — {entry.DisplayName}";
            Width = 500;
            Height = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            DialogTheme.ApplyWindow(this);

            var root = new StackPanel { Margin = new Thickness(24) };

            var header = DialogTheme.Text(entry.DisplayName, "PrimaryBrush", fontSize: 20);
            header.FontWeight = FontWeights.SemiBold;
            header.Margin = new Thickness(0, 0, 0, 4);
            root.Children.Add(header);

            var versionLine = DialogTheme.Text(entry.VersionName, "TextSecondaryBrush", fontSize: 12);
            versionLine.Margin = new Thickness(0, 0, 0, 20);
            root.Children.Add(versionLine);

            var nicknameLabel = DialogTheme.Text("Nickname", "TextSecondaryBrush", fontSize: 13);
            nicknameLabel.Margin = new Thickness(0, 0, 0, 4);
            root.Children.Add(nicknameLabel);

            _nicknameBox = DialogTheme.Input(fontSize: 14);
            _nicknameBox.Text = entry.Nickname ?? "";
            root.Children.Add(_nicknameBox);

            var pathLabel = DialogTheme.Text("Build Path", "TextSecondaryBrush", fontSize: 13);
            pathLabel.Margin = new Thickness(0, 16, 0, 4);
            root.Children.Add(pathLabel);

            var pathRow = new DockPanel();
            var btnChangePath = DialogTheme.StyledButton("Browse...", "ModernButton");
            btnChangePath.FontSize = 12;
            btnChangePath.Padding = new Thickness(14, 7, 14, 7);
            btnChangePath.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(btnChangePath, Dock.Right);
            btnChangePath.Click += BtnChangePath_Click;
            pathRow.Children.Add(btnChangePath);

            _pathText = DialogTheme.Text(entry.FolderPath, fontSize: 12);
            _pathText.VerticalAlignment = VerticalAlignment.Center;
            _pathText.TextTrimming = TextTrimming.CharacterEllipsis;
            _pathText.ToolTip = entry.FolderPath;
            pathRow.Children.Add(_pathText);
            root.Children.Add(pathRow);

            root.Children.Add(DialogTheme.Separator());

            var actionsLabel = DialogTheme.Text("Actions", "TextSecondaryBrush", fontSize: 13);
            actionsLabel.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(actionsLabel);

            var actionsPanel = new WrapPanel();

            var btnOpen = DialogTheme.SurfaceButton("📂 Open Folder", fontSize: 12);
            btnOpen.Click += (s, e) =>
            {
                if (Directory.Exists(_entry.FolderPath))
                    Process.Start(new ProcessStartInfo(_entry.FolderPath) { UseShellExecute = true });
            };
            actionsPanel.Children.Add(btnOpen);

            var btnDisable = DialogTheme.SurfaceButton("⛔ Disable All Mods", "ErrorBrush", fontSize: 12);
            btnDisable.Click += BtnDisableAll_Click;
            actionsPanel.Children.Add(btnDisable);

            root.Children.Add(actionsPanel);
            root.Children.Add(DialogTheme.Separator());

            var bottomRow = new DockPanel();
            var btnRemove = DialogTheme.SurfaceButton("🗑 Remove Build", "ErrorBrush", fontSize: 12);
            btnRemove.Click += BtnRemove_Click;
            DockPanel.SetDock(btnRemove, Dock.Left);
            bottomRow.Children.Add(btnRemove);

            var rightButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = DialogTheme.SurfaceButton("Cancel", fontSize: 13, margin: new Thickness(0));
            btnCancel.Click += (s, e) => DialogResult = false;
            rightButtons.Children.Add(btnCancel);

            var btnSave = DialogTheme.StyledButton("  Save  ", "ModernButton", new Thickness(8, 0, 0, 0));
            btnSave.Click += BtnSave_Click;
            rightButtons.Children.Add(btnSave);

            bottomRow.Children.Add(rightButtons);
            root.Children.Add(bottomRow);

            Content = root;
            Loaded += (s, e) => _nicknameBox.Focus();
        }

        private void BtnChangePath_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Select folder for \"{_entry.DisplayName}\"",
                ShowNewFolderButton = false
            };
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _modManager.ChangeVersionPath(_entry, folderDialog.SelectedPath);
                _pathText.Text = folderDialog.SelectedPath;
                _pathText.ToolTip = folderDialog.SelectedPath;
                Changed = true;
            }
        }

        private void BtnDisableAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Disable all mods for \"{_entry.DisplayName}\"?\nOriginal files will be restored.",
                "Disable All Mods", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _modManager.DisableAllModsForVersion(_entry.VersionName);
                Changed = true;
                MessageBox.Show("All mods disabled.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Remove build \"{_entry.DisplayName}\"?\nInstalled mods won't be affected.",
                "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _modManager.RemoveVersion(_entry);
                BuildRemoved = true;
                Changed = true;
                DialogResult = true;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var newNickname = _nicknameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newNickname))
            {
                MessageBox.Show("Nickname is required for all builds.",
                    "Nickname Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                _nicknameBox.Focus();
                return;
            }

            if (_modManager.Versions.Any(v => v != _entry &&
                v.Nickname.Equals(newNickname, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"A build with nickname \"{newNickname}\" already exists.",
                    "Duplicate Nickname", MessageBoxButton.OK, MessageBoxImage.Warning);
                _nicknameBox.Focus();
                return;
            }

            if (newNickname != (_entry.Nickname ?? ""))
            {
                _modManager.RenameVersion(_entry, newNickname);
                Changed = true;
            }
            DialogResult = true;
        }
    }
}
