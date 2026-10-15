using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSF2ModManager.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace SSF2ModManager.Views
{
    public partial class CrazyCssDesignerWindow : Window
    {
        private readonly IReadOnlyList<string> _candidates;
        private readonly Func<string, string> _displayName;
        private readonly HashSet<string> _artCapable;
        private CrazyCssDesign _design;
        private bool _suppressBrushSync;
        private bool _labelAuto = true;
        private bool _ready;

        public CrazyCssDesign? ResultDesign { get; private set; }

        public CrazyCssDesignerWindow(
            IReadOnlyList<string> candidates,
            Func<string, string>? displayName = null,
            IEnumerable<string>? artCapableIds = null,
            CrazyCssDesign? initial = null)
        {
            // Assign before InitializeComponent — CheckBox IsChecked="True" fires Checked
            // during XAML load and must not touch null delegates/fields.
            _candidates = candidates ?? Array.Empty<string>();
            _displayName = displayName ?? (id => id);
            _artCapable = new HashSet<string>(
                artCapableIds ?? _candidates,
                StringComparer.OrdinalIgnoreCase);
            _design = initial ?? CrazyCssDesign.Fill(
                3, 8,
                FirstId(preferArt: true),
                FirstId(preferArt: false),
                "");

            InitializeComponent();

            PopulateCombo(CmbArt, preferArtCapable: true);
            PopulateCombo(CmbPlayable, preferArtCapable: false);

            if (string.IsNullOrWhiteSpace(_design.Cells.FirstOrDefault()?.ChipLabel))
            {
                var id = FirstId(preferArt: true);
                var name = _displayName(id);
                foreach (var cell in _design.Cells)
                {
                    if (string.IsNullOrWhiteSpace(cell.DonorId)) cell.DonorId = id;
                    if (string.IsNullOrWhiteSpace(cell.PlayableId)) cell.PlayableId = id;
                    if (string.IsNullOrWhiteSpace(cell.ChipLabel)) cell.ChipLabel = name;
                }
            }

            TxtRows.Text = _design.Rows.ToString(CultureInfo.InvariantCulture);
            TxtCols.Text = _design.Cols.ToString(CultureInfo.InvariantCulture);
            SelectComboId(CmbArt, _design.Cells[0].DonorId);
            SelectComboId(CmbPlayable, _design.Cells[0].PlayableId);
            TxtLabel.Text = _design.Cells[0].ChipLabel;
            ChkLinkPlayable.IsChecked = true;
            CmbPlayable.IsEnabled = false;
            SyncLinkedPlayable();
            RebuildPreview();
            UpdateSummary();
            _ready = true;
        }

        private string FirstId(bool preferArt)
        {
            if (preferArt)
            {
                var art = _candidates.FirstOrDefault(id => _artCapable.Contains(id));
                if (!string.IsNullOrWhiteSpace(art)) return art!;
            }
            return _candidates.FirstOrDefault() ?? "mario";
        }

        private void PopulateCombo(ComboBox combo, bool preferArtCapable)
        {
            combo.Items.Clear();
            var ordered = preferArtCapable
                ? _candidates.Where(id => _artCapable.Contains(id))
                    .Concat(_candidates.Where(id => !_artCapable.Contains(id)))
                : _candidates;
            foreach (var id in ordered)
            {
                var name = _displayName(id);
                combo.Items.Add(new ComboBoxItem
                {
                    Content = string.IsNullOrWhiteSpace(name) || name.Equals(id, StringComparison.OrdinalIgnoreCase)
                        ? id
                        : $"{id}  —  {name}",
                    Tag = id
                });
            }
            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private static void SelectComboId(ComboBox combo, string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item
                    && item.Tag is string tag
                    && tag.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            combo.Text = id;
        }

        private string? ReadComboId(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                return MenuSlotService.SanitizeId(tag);
            return MenuSlotService.SanitizeId(combo.Text ?? "");
        }

        private CrazyCssCell ReadBrush()
        {
            var art = ReadComboId(CmbArt) ?? "";
            var playable = ChkLinkPlayable.IsChecked == true
                ? art
                : (ReadComboId(CmbPlayable) ?? art);
            var label = TxtLabel.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(label))
                label = _displayName(playable);
            return new CrazyCssCell
            {
                DonorId = art,
                PlayableId = playable,
                ChipLabel = label
            };
        }

        private void SyncLinkedPlayable()
        {
            if (ChkLinkPlayable.IsChecked != true) return;
            _suppressBrushSync = true;
            try
            {
                var art = ReadComboId(CmbArt);
                if (!string.IsNullOrWhiteSpace(art))
                    SelectComboId(CmbPlayable, art);
                CmbPlayable.IsEnabled = false;
            }
            finally
            {
                _suppressBrushSync = false;
            }
        }

        private void ChkLinkPlayable_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready || ChkLinkPlayable == null || CmbPlayable == null) return;
            CmbPlayable.IsEnabled = ChkLinkPlayable.IsChecked != true;
            SyncLinkedPlayable();
            MaybeAutoLabel();
        }

        private void BrushChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _suppressBrushSync) return;
            if (ReferenceEquals(sender, CmbArt))
                SyncLinkedPlayable();
            MaybeAutoLabel();
        }

        private void TxtLabel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            if (TxtLabel.IsFocused)
                _labelAuto = false;
        }

        private void MaybeAutoLabel()
        {
            if (!_ready || !_labelAuto) return;
            var playable = ChkLinkPlayable.IsChecked == true
                ? ReadComboId(CmbArt)
                : ReadComboId(CmbPlayable);
            if (string.IsNullOrWhiteSpace(playable)) return;
            _suppressBrushSync = true;
            try
            {
                TxtLabel.Text = _displayName(playable!);
            }
            finally
            {
                _suppressBrushSync = false;
            }
        }

        private void BtnResize_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSize(out var rows, out var cols)) return;
            var old = _design.Cells.Select(c => c.Clone()).ToList();
            _design.Rows = rows;
            _design.Cols = cols;
            _design.EnsureSize();
            var brush = ReadBrush();
            for (var i = 0; i < _design.Cells.Count; i++)
            {
                if (i < old.Count && !string.IsNullOrWhiteSpace(old[i].DonorId))
                    _design.Cells[i] = old[i];
                else if (string.IsNullOrWhiteSpace(_design.Cells[i].DonorId))
                    _design.Cells[i] = brush.Clone();
            }
            RebuildPreview();
            UpdateSummary();
        }

        private bool TryReadSize(out int rows, out int cols)
        {
            rows = cols = 0;
            if (!int.TryParse(TxtRows.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rows)
                || rows < 1)
            {
                MessageBox.Show(
                    $"Rows must be between 1 and {MappingsEditService.CssMaxRows}.",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!int.TryParse(TxtCols.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cols)
                || cols < 1)
            {
                MessageBox.Show(
                    $"Cols must be between 1 and {MappingsEditService.CssMaxColsPerRow}.",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            rows = Math.Clamp(rows, 1, MappingsEditService.CssMaxRows);
            cols = Math.Clamp(cols, 1, MappingsEditService.CssMaxColsPerRow);
            TxtRows.Text = rows.ToString(CultureInfo.InvariantCulture);
            TxtCols.Text = cols.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private void BtnFillAll_Click(object sender, RoutedEventArgs e)
        {
            var brush = ReadBrush();
            if (string.IsNullOrWhiteSpace(brush.DonorId) || string.IsNullOrWhiteSpace(brush.PlayableId))
            {
                MessageBox.Show("Pick chip art and a playable fighter first.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _design.EnsureSize();
            for (var i = 0; i < _design.Cells.Count; i++)
                _design.Cells[i] = brush.Clone();
            RebuildPreview();
            UpdateSummary();
        }

        private void BtnPaintCenter_Click(object sender, RoutedEventArgs e)
        {
            var brush = ReadBrush();
            if (string.IsNullOrWhiteSpace(brush.DonorId) || string.IsNullOrWhiteSpace(brush.PlayableId))
            {
                MessageBox.Show("Pick chip art and a playable fighter first.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _design.EnsureSize();
            if (_design.Cells.Count == 0) return;
            var r = _design.Rows / 2;
            var c = _design.Cols / 2;
            var idx = r * _design.Cols + c;
            if (idx >= 0 && idx < _design.Cells.Count)
                _design.Cells[idx] = brush.Clone();
            RebuildPreview();
            UpdateSummary();
        }

        private void BtnClearEmpty_Click(object sender, RoutedEventArgs e)
        {
            _design.EnsureSize();
            foreach (var cell in _design.Cells)
            {
                if (string.IsNullOrWhiteSpace(cell.DonorId) && string.IsNullOrWhiteSpace(cell.PlayableId))
                {
                    cell.DonorId = "";
                    cell.PlayableId = "";
                    cell.ChipLabel = "";
                }
            }
            RebuildPreview();
            UpdateSummary();
        }

        private void RebuildPreview()
        {
            _design.EnsureSize();
            GridPreview.Children.Clear();
            GridPreview.Rows = _design.Rows;
            GridPreview.Columns = _design.Cols;

            var total = _design.Cells.Count;
            // Keep cells usable on large grids without making the window unusable.
            var cellW = total > 200 ? 28.0 : total > 80 ? 40.0 : 56.0;
            var cellH = total > 200 ? 34.0 : total > 80 ? 44.0 : 52.0;
            var font = total > 200 ? 8.0 : total > 80 ? 9.0 : 10.0;

            for (var i = 0; i < total; i++)
            {
                var cell = _design.Cells[i];
                var idx = i;
                var btn = new Button
                {
                    Width = cellW,
                    Height = cellH,
                    Margin = new Thickness(1),
                    Padding = new Thickness(1),
                    FontSize = font,
                    Tag = idx,
                    ToolTip = BuildCellTooltip(idx, cell),
                    Content = BuildCellContent(cell, total > 80),
                    Style = TryFindResource("ModernButton") as Style
                };
                btn.Background = BrushForCell(cell);
                btn.Click += Cell_Click;
                GridPreview.Children.Add(btn);
            }
        }

        private static string BuildCellContent(CrazyCssCell cell, bool compact)
        {
            var label = string.IsNullOrWhiteSpace(cell.ChipLabel)
                ? (string.IsNullOrWhiteSpace(cell.PlayableId) ? "·" : cell.PlayableId)
                : cell.ChipLabel;
            if (compact && label.Length > 4)
                label = label[..4];
            return label.ToUpperInvariant();
        }

        private string BuildCellTooltip(int index, CrazyCssCell cell)
        {
            var row = index / Math.Max(1, _design.Cols);
            var col = index % Math.Max(1, _design.Cols);
            return $"[{row},{col}] art={cell.DonorId}  plays={cell.PlayableId}  \"{cell.ChipLabel}\"";
        }

        private Brush BrushForCell(CrazyCssCell cell)
        {
            if (string.IsNullOrWhiteSpace(cell.PlayableId) && string.IsNullOrWhiteSpace(cell.DonorId))
                return (Brush)(TryFindResource("CardBrush") ?? Brushes.DimGray);

            var key = (cell.PlayableId ?? "") + "|" + (cell.DonorId ?? "");
            unchecked
            {
                var hash = (uint)key.GetHashCode(StringComparison.OrdinalIgnoreCase);
                var r = (byte)(80 + (hash & 0x7F));
                var g = (byte)(80 + ((hash >> 8) & 0x7F));
                var b = (byte)(80 + ((hash >> 16) & 0x7F));
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }

        private void Cell_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not int idx) return;
            var brush = ReadBrush();
            if (string.IsNullOrWhiteSpace(brush.DonorId) || string.IsNullOrWhiteSpace(brush.PlayableId))
            {
                MessageBox.Show("Pick chip art and a playable fighter first.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (idx < 0 || idx >= _design.Cells.Count) return;
            _design.Cells[idx] = brush.Clone();
            btn.Content = BuildCellContent(_design.Cells[idx], _design.Cells.Count > 80);
            btn.ToolTip = BuildCellTooltip(idx, _design.Cells[idx]);
            btn.Background = BrushForCell(_design.Cells[idx]);
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            _design.EnsureSize();
            var filled = _design.Cells.Count(c =>
                !string.IsNullOrWhiteSpace(c.DonorId) && !string.IsNullOrWhiteSpace(c.PlayableId));
            var playables = _design.Cells
                .Select(c => c.PlayableId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var arts = _design.Cells
                .Select(c => c.DonorId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var labels = _design.Cells
                .Select(c => c.ChipLabel)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            TxtSummary.Text =
                $"{_design.Cols}×{_design.Rows} · {filled}/{_design.Total} filled · " +
                $"{arts} art · {playables} playable · {labels} label(s) · " +
                $"max {MappingsEditService.CssMaxColsPerRow}×{MappingsEditService.CssMaxRows}";
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSize(out var rows, out var cols)) return;
            if (_design.Rows != rows || _design.Cols != cols)
            {
                MessageBox.Show("Grid size changed — click Resize grid first.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _design.EnsureSize();
            for (var i = 0; i < _design.Cells.Count; i++)
            {
                var cell = _design.Cells[i];
                if (string.IsNullOrWhiteSpace(cell.DonorId) || string.IsNullOrWhiteSpace(cell.PlayableId))
                {
                    MessageBox.Show(
                        $"Cell {i} is empty. Fill all cells (or paint each one) before applying.",
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!_artCapable.Contains(cell.DonorId))
                {
                    MessageBox.Show(
                        $"Chip art '{cell.DonorId}' has no CSS select chip in menu_characters.swf.\n" +
                        "Pick a character that already appears on the character select screen.",
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var confirm =
                $"Replace the character select screen with {_design.Total} custom chips " +
                $"({_design.Cols}×{_design.Rows})?\n\n" +
                "A vanilla snapshot is saved so you can restore later. Big grids can take a few minutes.";
            if (MessageBox.Show(confirm, Title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
                return;

            ResultDesign = new CrazyCssDesign
            {
                Rows = _design.Rows,
                Cols = _design.Cols,
                Cells = _design.Cells.Select(c => c.Clone()).ToList()
            };
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
