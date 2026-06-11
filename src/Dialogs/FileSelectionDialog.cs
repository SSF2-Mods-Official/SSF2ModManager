using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CheckBox = System.Windows.Controls.CheckBox;
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
            DialogTheme.ApplyWindow(this);

            var dock = new DockPanel { Margin = new Thickness(20) };

            var promptBlock = DialogTheme.Text(prompt, wrap: TextWrapping.Wrap);
            promptBlock.Margin = new Thickness(0, 0, 0, 12);
            DockPanel.SetDock(promptBlock, Dock.Top);
            dock.Children.Add(promptBlock);

            if (metadata != null)
            {
                var infoBorder = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                DialogTheme.Set(infoBorder, Border.BackgroundProperty, "CardBrush");

                var infoStack = new StackPanel();
                if (!string.IsNullOrWhiteSpace(metadata.Creator))
                    infoStack.Children.Add(DialogTheme.Text($"Creator: {metadata.Creator}", fontSize: 12));
                if (!string.IsNullOrWhiteSpace(metadata.Ssf2Version))
                    infoStack.Children.Add(DialogTheme.Text($"SSF2 Version: {metadata.Ssf2Version}", fontSize: 12));
                if (!string.IsNullOrWhiteSpace(metadata.ModType))
                    infoStack.Children.Add(DialogTheme.Text($"Mod Type: {metadata.ModType}", fontSize: 12));
                infoBorder.Child = infoStack;
                DockPanel.SetDock(infoBorder, Dock.Top);
                dock.Children.Add(infoBorder);
            }

            var selectRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var btnSelectAll = DialogTheme.StyledButton("Select All", "ModernButton", new Thickness(0, 0, 6, 0));
            btnSelectAll.Padding = new Thickness(10, 4, 10, 4);
            btnSelectAll.FontSize = 11;
            btnSelectAll.Click += (s, e) => { foreach (var cb in _checkBoxes) cb.IsChecked = true; };

            var btnSelectNone = DialogTheme.SurfaceButton("Select None", fontSize: 11, margin: new Thickness(0));
            btnSelectNone.Padding = new Thickness(10, 4, 10, 4);
            btnSelectNone.Click += (s, e) => { foreach (var cb in _checkBoxes) cb.IsChecked = false; };

            selectRow.Children.Add(btnSelectAll);
            selectRow.Children.Add(btnSelectNone);
            DockPanel.SetDock(selectRow, Dock.Top);
            dock.Children.Add(selectRow);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var btnOk = DialogTheme.StyledButton("  Install Selected  ", "ModernButton", new Thickness(0, 0, 8, 0));
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

            var btnCancel = DialogTheme.StyledButton("Cancel", "DangerButton");
            btnCancel.IsCancel = true;
            btnCancel.Click += (_, _) => DialogResult = false;
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            DockPanel.SetDock(btnPanel, Dock.Bottom);
            dock.Children.Add(btnPanel);

            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var fileStack = new StackPanel();

            foreach (var file in files)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

                var sizeLabel = DialogTheme.Text(file.DisplaySize, "TextSecondaryBrush", fontSize: 11);
                sizeLabel.VerticalAlignment = VerticalAlignment.Center;
                sizeLabel.Margin = new Thickness(8, 0, 0, 0);
                sizeLabel.MinWidth = 60;
                sizeLabel.TextAlignment = TextAlignment.Right;
                DockPanel.SetDock(sizeLabel, Dock.Right);
                row.Children.Add(sizeLabel);

                var cb = new CheckBox
                {
                    Content = file.Path,
                    IsChecked = file.IsSelected,
                    Tag = file.Path,
                    FontSize = 12,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                DialogTheme.Set(cb, CheckBox.ForegroundProperty, "TextPrimaryBrush");
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
