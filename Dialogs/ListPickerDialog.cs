using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Orientation = System.Windows.Controls.Orientation;

namespace SSF2ModManager.Dialogs
{
    public class ListPickerDialog : Window
    {
        private readonly ListBox _listBox;
        public string? SelectedItem { get; private set; }
        public bool ModPackSelected { get; private set; }

        public ListPickerDialog(string title, string prompt, IEnumerable<string> items,
            bool showModPackButton = false)
        {
            Title = title;
            Width = 440;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 46));

            var dock = new DockPanel { Margin = new Thickness(20) };

            // Prompt at top
            var promptBlock = new TextBlock
            {
                Text = prompt,
                Foreground = System.Windows.Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(promptBlock, Dock.Top);
            dock.Children.Add(promptBlock);

            // Buttons at bottom (docked before listbox so they always show)
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var btnOk = new Button
            {
                Content = "  OK  ",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromRgb(30, 136, 229)),
                Foreground = System.Windows.Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? Brushes.Black,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnOk.Click += (s, e) =>
            {
                if (_listBox.SelectedItem is ListBoxItem selected)
                {
                    SelectedItem = selected.Content?.ToString();
                    DialogResult = true;
                }
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromRgb(229, 57, 53)),
                Foreground = System.Windows.Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? Brushes.Black,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => DialogResult = false;

            if (showModPackButton)
            {
                var btnModPack = new Button
                {
                    Content = "\U0001F4E6 This is a Mod Pack",
                    Padding = new Thickness(16, 8, 16, 8),
                    Background = new SolidColorBrush(Color.FromRgb(255, 167, 38)),
                    Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 46)),
                    BorderThickness = new Thickness(0),
                    FontSize = 13,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                btnModPack.Click += (s, e) =>
                {
                    ModPackSelected = true;
                    DialogResult = true;
                };
                btnPanel.Children.Add(btnModPack);
            }

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            DockPanel.SetDock(btnPanel, Dock.Bottom);
            dock.Children.Add(btnPanel);

            // ListBox fills remaining space
            _listBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 52, 96)),
                Foreground = System.Windows.Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 102)),
                FontSize = 13
            };

            foreach (var item in items)
            {
                _listBox.Items.Add(new ListBoxItem
                {
                    Content = item,
                    Foreground = System.Windows.Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    Padding = new Thickness(10, 6, 10, 6)
                });
            }

            if (_listBox.Items.Count > 0)
                ((ListBoxItem)_listBox.Items[0]).IsSelected = true;

            _listBox.MouseDoubleClick += (s, e) =>
            {
                if (_listBox.SelectedItem is ListBoxItem selected)
                {
                    SelectedItem = selected.Content?.ToString();
                    DialogResult = true;
                }
            };

            dock.Children.Add(_listBox);

            Content = dock;
        }
    }
}
