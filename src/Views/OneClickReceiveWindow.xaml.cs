using System.Windows;
using System.Windows.Controls;
using SSF2ModManager.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

namespace SSF2ModManager.Views
{
    public partial class OneClickReceiveWindow : Window
    {
        private readonly List<(CheckBox Box, OneClickItem Item)> _rows = new();

        public OneClickReceiveWindow(OneClickReceipt receipt)
        {
            InitializeComponent();
            IntroText.Text = OneClickReceive.ReceiptIntro(receipt);

            foreach (var item in receipt.Items)
            {
                var box = new CheckBox
                {
                    Content = item.Label,
                    IsChecked = item.Selected,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
                };
                ListHost.Children.Add(box);
                _rows.Add((box, item));
            }
            BtnImport.IsEnabled = receipt.Items.Count > 0;
        }

        public IReadOnlyList<OneClickItem> Chosen =>
            _rows.Select(row =>
            {
                row.Item.Selected = row.Box.IsChecked == true;
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
