using System.Windows;
using System.Windows.Controls;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;

namespace SSF2ModManager.Dialogs
{
    /// <summary>Delete nametags from the synced controller_profiles list.</summary>
    public class ManageTagsDialog : Window
    {
        private readonly string _installRoot;
        private readonly StackPanel _list;
        private readonly TextBlock _empty;

        public ManageTagsDialog(string installRoot)
        {
            _installRoot = installRoot;
            Title = "Manage tags";
            Width = 360;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            DialogTheme.ApplyWindow(this);

            var root = new DockPanel { Margin = new Thickness(16) };

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var title = DialogTheme.Text("Manage tags", fontSize: 16);
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);
            var btnClose = DialogTheme.StyledButton("Close", "ModernButton");
            btnClose.IsCancel = true;
            btnClose.Click += (_, _) => DialogResult = true;
            DockPanel.SetDock(btnClose, Dock.Right);
            header.Children.Add(btnClose);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var hint = DialogTheme.Text(
                "These nametags are shared with the in-game Controller Profiles mod.",
                "TextSecondaryBrush",
                fontSize: 12,
                wrap: TextWrapping.Wrap);
            hint.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(hint, Dock.Top);
            root.Children.Add(hint);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _list = new StackPanel();
            _empty = DialogTheme.Text("No tags yet.", "TextSecondaryBrush", fontSize: 13);
            _empty.Margin = new Thickness(0, 8, 0, 0);
            _list.Children.Add(_empty);
            scroll.Content = _list;
            root.Children.Add(scroll);

            Content = root;
            Loaded += (_, _) => Refresh();
        }

        private void Refresh()
        {
            _list.Children.Clear();
            var names = Services.ControllerProfilesSyncService.Load(_installRoot).Names;
            if (names.Count == 0)
            {
                _list.Children.Add(_empty);
                return;
            }
            foreach (var name in names)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var label = DialogTheme.Text(name, fontSize: 14);
                label.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);

                var del = DialogTheme.StyledButton("Delete", "DangerButton");
                del.Tag = name;
                del.Click += OnDelete;
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                _list.Children.Add(row);
            }
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string name)
                return;
            if (MessageBox.Show(this,
                    $"Delete tag \"{name}\"?\nThis removes it for both the Mod Manager and the game.",
                    "Delete tag",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            Services.ControllerProfilesSyncService.DeleteName(_installRoot, name);
            Refresh();
        }
    }
}
