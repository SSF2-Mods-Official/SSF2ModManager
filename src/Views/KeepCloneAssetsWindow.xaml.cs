using System.Collections;
using System.Collections.Generic;
using System.Windows;
using SSF2ModManager.Services;
using CheckBox = System.Windows.Controls.CheckBox;

namespace SSF2ModManager.Views
{
    public partial class KeepCloneAssetsWindow : Window
    {
        private readonly List<ImportCloneAssetChoice> _choices;
        private bool _syncing;

        public static Window? PickOwner(Window? preferred, IEnumerable? windows)
        {
            if (preferred != null && preferred.IsVisible)
                return preferred;
            Window? first = null;
            if (windows == null)
                return null;
            foreach (var item in windows)
            {
                if (item is not Window window || !window.IsVisible)
                    continue;
                if (System.Windows.Application.Current?.MainWindow == window)
                    return window;
                first ??= window;
            }
            return first;
        }

        public KeepCloneAssetsWindow(IReadOnlyList<ImportCloneAssetChoice> choices, string? intro = null)
        {
            _choices = new List<ImportCloneAssetChoice>(choices);
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(intro))
                IntroText.Text = intro;
            foreach (var choice in _choices)
            {
                var box = new CheckBox
                {
                    Content = $"{choice.DisplayName}  ({choice.FileName}) — assets from {choice.SourceId}",
                    Tag = choice,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush
                };
                box.Checked += Row_Changed;
                box.Unchecked += Row_Changed;
                ListHost.Children.Add(box);
            }
        }

        public static void SetRowsChecked(IEnumerable<CheckBox> rows, bool keep)
        {
            foreach (var box in rows)
                box.IsChecked = keep;
        }

        private void ChkAll_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing)
                return;
            _syncing = true;
            SetRowsChecked(RowBoxes(), ChkAll.IsChecked == true);
            _syncing = false;
        }

        private void Row_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing)
                return;
            _syncing = true;
            ChkAll.IsChecked = AllRowsChecked();
            _syncing = false;
        }

        private bool AllRowsChecked()
        {
            var any = false;
            foreach (var box in RowBoxes())
            {
                any = true;
                if (box.IsChecked != true)
                    return false;
            }
            return any;
        }

        private IEnumerable<CheckBox> RowBoxes()
        {
            foreach (var child in ListHost.Children)
            {
                if (child is CheckBox box)
                    yield return box;
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in ListHost.Children)
            {
                if (child is not CheckBox box || box.Tag is not ImportCloneAssetChoice choice)
                    continue;
                choice.KeepOldAssets = box.IsChecked == true;
            }
            DialogResult = true;
        }
    }
}
