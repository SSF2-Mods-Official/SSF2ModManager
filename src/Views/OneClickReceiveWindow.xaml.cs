using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SSF2ModManager.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace SSF2ModManager.Views
{
    public partial class OneClickReceiveWindow : Window
    {
        private sealed class Row
        {
            public CheckBox Box = null!;
            public OneClickItem Item = null!;
            public TextBox? Name;
            public ComboBox? Preview;
        }

        private sealed class PreviewChoice
        {
            public string? Path { get; init; }
            public string Label { get; init; } = "";
            public override string ToString() => Label;
        }

        private readonly List<Row> _rows = new();

        public OneClickReceiveWindow(OneClickReceipt receipt, string? modName = null)
        {
            InitializeComponent();
            IntroText.Text = OneClickReceive.ReceiptIntro(receipt);
            var text = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            var previews = receipt.IsPack ? new List<string>() : OneClickReceive.PreviewCandidates(receipt);

            foreach (var item in receipt.Items)
            {
                var row = new Row { Item = item };
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                row.Box = new CheckBox { Content = item.Label, IsChecked = item.Selected, Foreground = text };
                panel.Children.Add(row.Box);

                if (item.Kind is OneClickKind.Stage or OneClickKind.Character or OneClickKind.Alt)
                {
                    var line = new DockPanel { Margin = new Thickness(24, 6, 0, 0) };
                    line.Children.Add(new TextBlock
                    {
                        Text = "Name in game:", Width = 110, Foreground = text, VerticalAlignment = VerticalAlignment.Center
                    });
                    row.Name = new TextBox { Text = OneClickReceive.DefaultDisplayName(receipt, item, modName), MinWidth = 240 };
                    line.Children.Add(row.Name);
                    panel.Children.Add(line);
                    if (!string.IsNullOrWhiteSpace(item.OriginalName))
                    {
                        panel.Children.Add(new TextBlock
                        {
                            Text = item.Kind == OneClickKind.Alt
                                ? $"From the folder \"{item.OriginalName}\". Leave empty to show \"<character> Alt 1\", \"Alt 2\"…"
                                : $"The file calls it \"{item.OriginalName}\".",
                            Margin = new Thickness(134, 2, 0, 0),
                            Opacity = 0.7,
                            Foreground = text
                        });
                    }
                }

                if (item.Kind == OneClickKind.Stage)
                {
                    var line = new DockPanel { Margin = new Thickness(24, 6, 0, 0) };
                    line.Children.Add(new TextBlock
                    {
                        Text = "Preview:", Width = 110, Foreground = text, VerticalAlignment = VerticalAlignment.Top
                    });
                    var thumb = new Image { Width = 160, Height = 90, Margin = new Thickness(8, 0, 0, 0), Stretch = System.Windows.Media.Stretch.Uniform };
                    DockPanel.SetDock(thumb, Dock.Right);
                    line.Children.Add(thumb);
                    row.Preview = new ComboBox { MinWidth = 240, VerticalAlignment = VerticalAlignment.Top };
                    row.Preview.Items.Add(new PreviewChoice { Label = "None (keep the file's own, if any)" });
                    foreach (var p in previews)
                    {
                        var label = OneClickReceive.IsGameBananaShot(receipt, p)
                            ? "GameBanana " + System.IO.Path.GetFileNameWithoutExtension(p)
                            : System.IO.Path.GetRelativePath(receipt.ExtractedRoot, p);
                        row.Preview.Items.Add(new PreviewChoice { Path = p, Label = label });
                    }
                    row.Preview.SelectionChanged += (_, _) => thumb.Source = Load((row.Preview.SelectedItem as PreviewChoice)?.Path);
                    row.Preview.SelectedIndex = previews.Count > 0 ? 1 : 0;
                    line.Children.Add(row.Preview);
                    panel.Children.Add(line);
                }

                ListHost.Children.Add(panel);
                _rows.Add(row);
            }
            BtnImport.IsEnabled = receipt.Items.Count > 0;
        }

        private static BitmapImage? Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 320;
                image.UriSource = new Uri(path);
                image.EndInit();
                return image;
            }
            catch
            {
                return null;
            }
        }

        public IReadOnlyList<OneClickItem> Chosen =>
            _rows.Select(row =>
            {
                row.Item.Selected = row.Box.IsChecked == true;
                if (row.Name != null && (!string.IsNullOrWhiteSpace(row.Name.Text) || row.Item.Kind == OneClickKind.Alt))
                    row.Item.DisplayName = row.Name.Text.Trim();
                if (row.Preview != null)
                    row.Item.PreviewPath = (row.Preview.SelectedItem as PreviewChoice)?.Path;
                return row.Item;
            }).ToList();

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (Chosen.All(i => !i.Selected))
            {
                MessageBox.Show("Check at least one file.", "1-Click Install", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }
    }
}
