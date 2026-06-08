using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace SSF2ModManager.Dialogs
{
    public class FileSelectionItem
    {
        public string Path { get; set; } = string.Empty;
        public string DisplaySize { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;
    }

    public class FileSelectionDialog : Window
    {
        private readonly List<CheckBox> _checkBoxes = new();

        public List<string> SelectedFiles { get; private set; } = new();

        public class InfoMetadata
        {
            public string? Creator { get; set; }
            public string? Ssf2Version { get; set; }
            public string? ModType { get; set; }
        }

        public FileSelectionDialog(string title, string prompt, IEnumerable<FileSelectionItem> files, InfoMetadata? metadata = null)
        {
            Title = title;
            Width = 560;
            Height = 550;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var app = System.Windows.Application.Current;
            var bgBrush = app.TryFindResource("SurfaceBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(26, 26, 46));
            Background = bgBrush;

            var cardBg = app.TryFindResource("CardBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(15, 52, 96));
            var textPrimary = app.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(224, 224, 224));
            var textSecondary = app.TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(160, 160, 180));
            var accent = app.TryFindResource("PrimaryBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(30, 136, 229));
            var danger = app.TryFindResource("ErrorBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(229, 57, 53));

            var dock = new DockPanel { Margin = new Thickness(20) };

            // Prompt
            var promptBlock = new TextBlock
            {
                Text = prompt,
                Foreground = textPrimary,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(promptBlock, Dock.Top);
            dock.Children.Add(promptBlock);

            // If metadata provided, show a small info panel
            if (metadata != null)
            {
                var infoBorder = new Border { Background = cardBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 12) };
                var infoStack = new StackPanel();
                if (!string.IsNullOrWhiteSpace(metadata.Creator))
                    infoStack.Children.Add(new TextBlock { Text = $"Creator: {metadata.Creator}", Foreground = textPrimary, FontSize = 12 });
                if (!string.IsNullOrWhiteSpace(metadata.Ssf2Version))
                    infoStack.Children.Add(new TextBlock { Text = $"SSF2 Version: {metadata.Ssf2Version}", Foreground = textPrimary, FontSize = 12 });
                if (!string.IsNullOrWhiteSpace(metadata.ModType))
                    infoStack.Children.Add(new TextBlock { Text = $"Mod Type: {metadata.ModType}", Foreground = textPrimary, FontSize = 12 });
                infoBorder.Child = infoStack;
                DockPanel.SetDock(infoBorder, Dock.Top);
                dock.Children.Add(infoBorder);
            }

            // Select All / None row
            var selectRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var btnSelectAll = new Button
            {
                Content = "Select All",
                Padding = new Thickness(10, 4, 10, 4),
                Background = accent,
                Foreground = textPrimary,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0)
            };
            btnSelectAll.Click += (s, e) => { foreach (var cb in _checkBoxes) cb.IsChecked = true; };

            var btnSelectNone = new Button
            {
                Content = "Select None",
                Padding = new Thickness(10, 4, 10, 4),
                Background = cardBg,
                Foreground = textPrimary,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            btnSelectNone.Click += (s, e) => { foreach (var cb in _checkBoxes) cb.IsChecked = false; };

            selectRow.Children.Add(btnSelectAll);
            selectRow.Children.Add(btnSelectNone);
            DockPanel.SetDock(selectRow, Dock.Top);
            dock.Children.Add(selectRow);

            // Buttons at bottom
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var btnOk = new Button
            {
                Content = "  Install Selected  ",
                Padding = new Thickness(16, 8, 16, 8),
                Background = accent,
                Foreground = textPrimary,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnOk.Click += (s, e) =>
            {
                SelectedFiles = _checkBoxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => (string)cb.Tag)
                    .ToList();
                if (SelectedFiles.Count == 0)
                {
                    System.Windows.MessageBox.Show("Please select at least one file.",
                        "No Files Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(16, 8, 16, 8),
                Background = danger,
                Foreground = textPrimary,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => DialogResult = false;

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            DockPanel.SetDock(btnPanel, Dock.Bottom);
            dock.Children.Add(btnPanel);

            // Scrollable file list with checkboxes
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var fileStack = new StackPanel();

            foreach (var file in files)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

                var sizeLabel = new TextBlock
                {
                    Text = file.DisplaySize,
                    FontSize = 11,
                    Foreground = textSecondary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    MinWidth = 60,
                    TextAlignment = TextAlignment.Right
                };
                DockPanel.SetDock(sizeLabel, Dock.Right);
                row.Children.Add(sizeLabel);

                var cb = new CheckBox
                {
                    Content = file.Path,
                    IsChecked = file.IsSelected,
                    Tag = file.Path,
                    Foreground = textPrimary,
                    FontSize = 12,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                _checkBoxes.Add(cb);
                row.Children.Add(cb);

                fileStack.Children.Add(row);
            }

            scrollViewer.Content = fileStack;
            dock.Children.Add(scrollViewer);

            Content = dock;
        }
    }
}
