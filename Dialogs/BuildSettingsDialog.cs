using SSF2ModManager.Models;
using SSF2ModManager.Services;
using System;
using System.Diagnostics;
using System.IO;
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
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 46));

            var bg = new SolidColorBrush(Color.FromRgb(26, 26, 46));
            var cardBg = new SolidColorBrush(Color.FromRgb(30, 30, 55));
            var accent = new SolidColorBrush(Color.FromRgb(30, 136, 229));
            var textPrimary = new SolidColorBrush(Color.FromRgb(224, 224, 224));
            var textSecondary = new SolidColorBrush(Color.FromRgb(160, 160, 180));
            var danger = new SolidColorBrush(Color.FromRgb(229, 57, 53));
            var fieldBg = new SolidColorBrush(Color.FromRgb(15, 52, 96));
            var fieldBorder = new SolidColorBrush(Color.FromRgb(51, 51, 102));

            var root = new StackPanel { Margin = new Thickness(24) };

            // Header
            root.Children.Add(new TextBlock
            {
                Text = entry.DisplayName,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = accent,
                Margin = new Thickness(0, 0, 0, 4)
            });
            root.Children.Add(new TextBlock
            {
                Text = entry.VersionName,
                FontSize = 12,
                Foreground = textSecondary,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // Nickname
            root.Children.Add(new TextBlock
            {
                Text = "Nickname",
                FontSize = 13,
                Foreground = textSecondary,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _nicknameBox = new TextBox
            {
                Text = entry.Nickname ?? "",
                Background = fieldBg,
                Foreground = textPrimary,
                BorderBrush = fieldBorder,
                CaretBrush = Brushes.White,
                FontSize = 14,
                Padding = new Thickness(10, 8, 10, 8)
            };
            root.Children.Add(_nicknameBox);

            // Path
            root.Children.Add(new TextBlock
            {
                Text = "Build Path",
                FontSize = 13,
                Foreground = textSecondary,
                Margin = new Thickness(0, 16, 0, 4)
            });

            var pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
            var btnChangePath = MakeButton("Browse...", accent, 12);
            DockPanel.SetDock(btnChangePath, Dock.Right);
            btnChangePath.Margin = new Thickness(8, 0, 0, 0);
            btnChangePath.Click += BtnChangePath_Click;
            pathRow.Children.Add(btnChangePath);

            _pathText = new TextBlock
            {
                Text = entry.FolderPath,
                FontSize = 12,
                Foreground = textPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = entry.FolderPath
            };
            pathRow.Children.Add(_pathText);
            root.Children.Add(pathRow);

            // Actions section
            root.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 20, 0, 16)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Actions",
                FontSize = 13,
                Foreground = textSecondary,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var actionsPanel = new WrapPanel();

            var btnOpen = MakeButton("📂 Open Folder", cardBg, 12);
            btnOpen.Click += (s, e) =>
            {
                if (Directory.Exists(_entry.FolderPath))
                    Process.Start(new ProcessStartInfo(_entry.FolderPath) { UseShellExecute = true });
            };
            actionsPanel.Children.Add(btnOpen);

            var btnDisable = MakeButton("⛔ Disable All Mods", danger, 12);
            btnDisable.Click += BtnDisableAll_Click;
            actionsPanel.Children.Add(btnDisable);

            root.Children.Add(actionsPanel);

            // Bottom buttons
            root.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 20, 0, 16)
            });

            var bottomRow = new DockPanel();
            var btnRemove = MakeButton("🗑 Remove Build", danger, 12);
            btnRemove.Click += BtnRemove_Click;
            DockPanel.SetDock(btnRemove, Dock.Left);
            bottomRow.Children.Add(btnRemove);

            var rightButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = MakeButton("Cancel", cardBg, 13);
            btnCancel.Click += (s, e) => DialogResult = false;
            rightButtons.Children.Add(btnCancel);

            var btnSave = MakeButton("  Save  ", accent, 13);
            btnSave.Margin = new Thickness(8, 0, 0, 0);
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

            // Check for duplicate nickname (excluding current entry)
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
                Margin = new Thickness(0, 0, 8, 0)
            };
        }
    }
}
