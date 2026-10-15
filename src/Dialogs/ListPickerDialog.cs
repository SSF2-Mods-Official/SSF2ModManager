using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
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
            DialogTheme.ApplyWindow(this);

            var dock = new DockPanel { Margin = new Thickness(20) };

            var promptBlock = DialogTheme.Text(prompt, wrap: TextWrapping.Wrap);
            promptBlock.Margin = new Thickness(0, 0, 0, 12);
            DockPanel.SetDock(promptBlock, Dock.Top);
            dock.Children.Add(promptBlock);

            _listBox = new ListBox { FontSize = 13 };
            DialogTheme.Set(_listBox, ListBox.BackgroundProperty, "CardBrush");
            DialogTheme.Set(_listBox, ListBox.BorderBrushProperty, "BorderBrush");
            DialogTheme.Set(_listBox, ListBox.ForegroundProperty, "TextPrimaryBrush");

            foreach (var item in items)
            {
                var listItem = new ListBoxItem
                {
                    Content = item,
                    Padding = new Thickness(10, 6, 10, 6)
                };
                DialogTheme.Set(listItem, ListBoxItem.ForegroundProperty, "TextPrimaryBrush");
                _listBox.Items.Add(listItem);
            }

            if (_listBox.Items.Count > 0)
                ((ListBoxItem)_listBox.Items[0]!).IsSelected = true;

            _listBox.MouseDoubleClick += (s, e) =>
            {
                if (_listBox.SelectedItem is ListBoxItem { Content: { } content })
                {
                    SelectedItem = content.ToString();
                    DialogResult = true;
                }
            };

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var btnOk = DialogTheme.StyledButton("  OK  ", "ModernButton", new Thickness(0, 0, 8, 0));
            btnOk.IsDefault = true;
            btnOk.Click += (s, e) =>
            {
                if (_listBox.SelectedItem is ListBoxItem { Content: { } content })
                {
                    SelectedItem = content.ToString();
                    DialogResult = true;
                }
            };

            var btnCancel = DialogTheme.StyledButton("Cancel", "DangerButton");
            btnCancel.IsCancel = true;
            btnCancel.Click += (_, _) => DialogResult = false;

            if (showModPackButton)
            {
                var btnModPack = DialogTheme.StyledButton("\U0001F4E6 This is a Mod Pack", "AccentButton",
                    new Thickness(0, 0, 8, 0));
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

            dock.Children.Add(_listBox);

            Content = dock;
        }
    }
}
