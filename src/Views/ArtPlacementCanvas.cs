using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSF2ModManager.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using PixelFormats = System.Windows.Media.PixelFormats;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using FontFamily = System.Windows.Media.FontFamily;
using FlowDirection = System.Windows.FlowDirection;

namespace SSF2ModManager.Views
{
    public enum ArtPlacementPreviewMode
    {
        Plain,
        /// <summary>Black frame + top name bar with mask — matches CSS chip / PA look.</summary>
        CssChip,
        /// <summary>Event PA strip: series symbol + display name (read-only chrome).</summary>
        EventPa
    }

    /// <summary>Per-slot placement state so switching wizard slots does not leak transforms.</summary>
    public sealed class ArtPlacementTransform
    {
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double Scale { get; set; } = 1;
        public bool FlipH { get; set; }
        public bool FlipV { get; set; }
    }

    /// <summary>
    /// WYSIWYG art placer: greyed placeholder under a draggable/scalable user PNG.
    /// Preview uses nearest-neighbor + integer zoom; export is art-only (no name chrome).
    /// </summary>
    public class ArtPlacementCanvas : FrameworkElement
    {
        public const double DefaultChipWidth = 54;
        public const double DefaultChipHeight = 54;
        public const double DefaultNameBarHeight = 14;
        public const double DefaultBorderThickness = 2;
        public const double DefaultNameFontSize = 8;

        private BitmapSource? _placeholder;
        private BitmapSource? _overlay;
        private bool _dragging;
        private Point _dragStart;
        private double _originX;
        private double _originY;
        private int _viewZoom = 1;
        private bool _viewZoomInitialized;
        private readonly Brush _backdrop = new SolidColorBrush(Color.FromRgb(28, 28, 32));

        public static readonly DependencyProperty CanvasWidthPxProperty =
            DependencyProperty.Register(nameof(CanvasWidthPx), typeof(double), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(DefaultChipWidth, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty CanvasHeightPxProperty =
            DependencyProperty.Register(nameof(CanvasHeightPx), typeof(double), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(DefaultChipHeight, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty OffsetXProperty =
            DependencyProperty.Register(nameof(OffsetX), typeof(double), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty OffsetYProperty =
            DependencyProperty.Register(nameof(OffsetY), typeof(double), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty ScaleProperty =
            DependencyProperty.Register(nameof(Scale), typeof(double), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty FlipHProperty =
            DependencyProperty.Register(nameof(FlipH), typeof(bool), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FlipVProperty =
            DependencyProperty.Register(nameof(FlipV), typeof(bool), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PreviewModeProperty =
            DependencyProperty.Register(nameof(PreviewMode), typeof(ArtPlacementPreviewMode), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(ArtPlacementPreviewMode.Plain, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty DisplayNameProperty =
            DependencyProperty.Register(nameof(DisplayName), typeof(string), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty NameBarHeightProperty =
            DependencyProperty.Register(nameof(NameBarHeight), typeof(double), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(DefaultNameBarHeight, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty NameFontSizeProperty =
            DependencyProperty.Register(nameof(NameFontSize), typeof(double), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(DefaultNameFontSize, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty SeriesSymbolSourceProperty =
            DependencyProperty.Register(nameof(SeriesSymbolSource), typeof(ImageSource), typeof(ArtPlacementCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public double CanvasWidthPx { get => (double)GetValue(CanvasWidthPxProperty); set => SetValue(CanvasWidthPxProperty, value); }
        public double CanvasHeightPx { get => (double)GetValue(CanvasHeightPxProperty); set => SetValue(CanvasHeightPxProperty, value); }
        public double OffsetX { get => (double)GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
        public double OffsetY { get => (double)GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }
        public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
        public bool FlipH { get => (bool)GetValue(FlipHProperty); set => SetValue(FlipHProperty, value); }
        public bool FlipV { get => (bool)GetValue(FlipVProperty); set => SetValue(FlipVProperty, value); }
        public ArtPlacementPreviewMode PreviewMode { get => (ArtPlacementPreviewMode)GetValue(PreviewModeProperty); set => SetValue(PreviewModeProperty, value); }
        public string DisplayName { get => (string)GetValue(DisplayNameProperty); set => SetValue(DisplayNameProperty, value); }
        public double NameBarHeight { get => (double)GetValue(NameBarHeightProperty); set => SetValue(NameBarHeightProperty, value); }
        public double NameFontSize { get => (double)GetValue(NameFontSizeProperty); set => SetValue(NameFontSizeProperty, value); }
        public ImageSource? SeriesSymbolSource { get => (ImageSource?)GetValue(SeriesSymbolSourceProperty); set => SetValue(SeriesSymbolSourceProperty, value); }
        public int ViewZoom => _viewZoom;

        public event EventHandler? TransformChanged;

        public ArtPlacementCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            MinWidth = 120;
            MinHeight = 120;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
            MouseWheel += OnMouseWheel;
            MouseLeave += (_, _) => { _dragging = false; ReleaseMouseCapture(); };
        }

        public void LoadPlaceholder(string? pngPath)
        {
            _placeholder = LoadBitmap(pngPath);
            if (_placeholder != null)
            {
                CanvasWidthPx = _placeholder.PixelWidth;
                CanvasHeightPx = _placeholder.PixelHeight;
            }
            _viewZoomInitialized = false;
            InvalidateVisual();
        }

        public void LoadOverlay(string? pngPath)
        {
            _overlay = LoadBitmap(pngPath);
            if (_overlay != null && Scale <= 0.01)
                FitOverlay();
            InvalidateVisual();
            TransformChanged?.Invoke(this, EventArgs.Empty);
        }

        public ArtPlacementTransform GetTransform() => new()
        {
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            Scale = Scale,
            FlipH = FlipH,
            FlipV = FlipV
        };

        public void SetTransform(ArtPlacementTransform? t)
        {
            if (t == null)
            {
                OffsetX = 0;
                OffsetY = 0;
                Scale = 0; // LoadOverlay / FitOverlay treats <= 0.01 as "needs fit"
                FlipH = false;
                FlipV = false;
                return;
            }
            OffsetX = t.OffsetX;
            OffsetY = t.OffsetY;
            Scale = t.Scale;
            FlipH = t.FlipH;
            FlipV = t.FlipV;
            InvalidateVisual();
            TransformChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearOverlay()
        {
            _overlay = null;
            InvalidateVisual();
            TransformChanged?.Invoke(this, EventArgs.Empty);
        }

        public void FitOverlay()
        {
            if (_overlay == null) return;
            var artH = PreviewMode == ArtPlacementPreviewMode.CssChip
                ? Math.Max(1, CanvasHeightPx - NameBarHeight)
                : CanvasHeightPx;
            var sx = CanvasWidthPx / Math.Max(1, _overlay.PixelWidth);
            var sy = artH / Math.Max(1, _overlay.PixelHeight);
            Scale = Math.Min(sx, sy);
            OffsetX = Math.Round((CanvasWidthPx - _overlay.PixelWidth * Scale) / 2);
            OffsetY = Math.Round(PreviewMode == ArtPlacementPreviewMode.CssChip
                ? NameBarHeight + (artH - _overlay.PixelHeight * Scale) / 2
                : (CanvasHeightPx - _overlay.PixelHeight * Scale) / 2);
            InvalidateVisual();
            TransformChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResetTransform()
        {
            OffsetX = 0;
            OffsetY = PreviewMode == ArtPlacementPreviewMode.CssChip ? NameBarHeight : 0;
            Scale = 1;
            FlipH = false;
            FlipV = false;
            InvalidateVisual();
            TransformChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Export art-only flattened PNG (no name bar / frame chrome) for FFDec -replace.</summary>
        public void ExportFlattenedPng(string path)
        {
            var w = Math.Max(1, (int)Math.Round(CanvasWidthPx));
            var h = Math.Max(1, (int)Math.Round(CanvasHeightPx));
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
                DrawOverlay(dc);
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var w = double.IsInfinity(availableSize.Width) ? 280 : availableSize.Width;
            var h = double.IsInfinity(availableSize.Height) ? 280 : availableSize.Height;
            return new Size(Math.Max(120, w), Math.Max(120, h));
        }

        protected override void OnRender(DrawingContext dc)
        {
            var viewW = ActualWidth > 0 ? ActualWidth : 280;
            var viewH = ActualHeight > 0 ? ActualHeight : 280;
            dc.DrawRectangle(_backdrop, null, new Rect(0, 0, viewW, viewH));

            var cw = Math.Max(1, CanvasWidthPx);
            var ch = Math.Max(1, CanvasHeightPx);
            FitViewZoom();
            var scale = _viewZoom;
            var ox = Math.Floor((viewW - cw * scale) / 2);
            var oy = Math.Floor((viewH - ch * scale) / 2);

            dc.PushTransform(new TranslateTransform(ox, oy));
            dc.PushTransform(new ScaleTransform(scale, scale));
            DrawContent(dc, cw, ch, forExport: false);
            dc.Pop();
            dc.Pop();

            var hud = $"X={OffsetX:0}  Y={OffsetY:0}  Scale={Scale:0.###}  View={_viewZoom}×";
            var ft = new FormattedText(
                hud, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.WhiteSmoke, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null, new Rect(6, viewH - 22, ft.Width + 10, 18));
            dc.DrawText(ft, new Point(11, viewH - 20));
        }

        private void FitViewZoom()
        {
            var viewW = ActualWidth > 0 ? ActualWidth : 280;
            var viewH = ActualHeight > 0 ? ActualHeight : 280;
            var cw = Math.Max(1, CanvasWidthPx);
            var ch = Math.Max(1, CanvasHeightPx);
            var maxFit = (int)Math.Floor(Math.Min(viewW / cw, viewH / ch));
            if (maxFit < 1) maxFit = 1;
            if (!_viewZoomInitialized)
            {
                _viewZoom = Math.Clamp(Math.Min(maxFit, 4), 1, 16);
                _viewZoomInitialized = true;
            }
            _viewZoom = Math.Clamp(_viewZoom, 1, 16);
        }

        private void DrawContent(DrawingContext dc, double cw, double ch, bool forExport)
        {
            if (PreviewMode == ArtPlacementPreviewMode.CssChip && !forExport)
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, cw, ch));
                var barH = Math.Clamp(NameBarHeight, 4, ch * 0.45);
                var artRect = new Rect(DefaultBorderThickness, barH, cw - DefaultBorderThickness * 2, ch - barH - DefaultBorderThickness);

                dc.PushClip(new RectangleGeometry(artRect));
                DrawPlaceholder(dc, cw, ch, opacity: 0.4);
                DrawOverlay(dc);
                dc.Pop();

                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, cw, barH));
                DrawChipNameBarLabel(dc, cw, barH);
                dc.DrawRectangle(null, new Pen(Brushes.Black, DefaultBorderThickness), new Rect(0.5, 0.5, cw - 1, ch - 1));
            }
            else if (PreviewMode == ArtPlacementPreviewMode.EventPa && !forExport)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(120, 170, 220)), null, new Rect(0, 0, cw, ch));
                DrawPlaceholder(dc, cw, ch, opacity: 0.35);
                DrawOverlay(dc);
                var pad = 6.0;
                var icon = Math.Min(ch - pad * 2, 28);
                if (SeriesSymbolSource != null)
                    dc.DrawImage(SeriesSymbolSource, new Rect(pad, (ch - icon) / 2, icon, icon));
                var label = string.IsNullOrWhiteSpace(DisplayName)
                    ? "NAME"
                    : MenuSlotService.BuildSingleLineChipLabel(DisplayName);
                var ft = new FormattedText(
                    label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    Math.Clamp(NameFontSize + 4, 10, ch - 4), Brushes.Black,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point(pad + icon + 6, Math.Max(0, (ch - ft.Height) / 2)));
            }
            else
            {
                if (!forExport)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(40, 40, 44)), null, new Rect(0, 0, cw, ch));
                DrawPlaceholder(dc, cw, ch, opacity: forExport ? 0 : 0.4);
                DrawOverlay(dc);
            }
        }

        /// <summary>
        /// In-game CSS chip label: single-line (CLONE-SAMUS), stretched horizontally to fill the bar, centered.
        /// </summary>
        private void DrawChipNameBarLabel(DrawingContext dc, double cw, double barH)
        {
            var label = string.IsNullOrWhiteSpace(DisplayName)
                ? "NAME"
                : MenuSlotService.BuildSingleLineChipLabel(DisplayName);
            var fontSize = Math.Clamp(NameFontSize, 4, barH - 1);
            var ft = new FormattedText(
                label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                fontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxLineCount = 1,
                Trimming = TextTrimming.None
            };
            var padX = 3.0;
            var availW = Math.Max(1, cw - padX * 2);
            var scaleX = ft.Width > 0.5 ? Math.Clamp(availW / ft.Width, 0.45, 1.35) : 1.0;
            var drawW = ft.Width * scaleX;
            var x = (cw - drawW) / 2.0;
            var y = Math.Max(0, (barH - ft.Height) / 2);
            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushTransform(new ScaleTransform(scaleX, 1.0));
            dc.DrawText(ft, new Point(0, 0));
            dc.Pop();
            dc.Pop();
        }

        private void DrawPlaceholder(DrawingContext dc, double cw, double ch, double opacity)
        {
            if (_placeholder == null || opacity <= 0) return;
            dc.PushOpacity(opacity);
            dc.DrawImage(_placeholder, new Rect(0, 0, cw, ch));
            dc.Pop();
        }

        private void DrawOverlay(DrawingContext dc)
        {
            if (_overlay == null) return;
            var w = _overlay.PixelWidth * Scale;
            var h = _overlay.PixelHeight * Scale;
            var x = Math.Round(OffsetX);
            var y = Math.Round(OffsetY);
            dc.PushTransform(new TranslateTransform(x + (FlipH ? w : 0), y + (FlipV ? h : 0)));
            dc.PushTransform(new ScaleTransform(FlipH ? -1 : 1, FlipV ? -1 : 1));
            dc.DrawImage(_overlay, new Rect(0, 0, w, h));
            dc.Pop();
            dc.Pop();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            _dragging = true;
            _dragStart = e.GetPosition(this);
            _originX = OffsetX;
            _originY = OffsetY;
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var p = e.GetPosition(this);
            var sx = Math.Max(1, _viewZoom);
            OffsetX = Math.Round(_originX + (p.X - _dragStart.X) / sx);
            OffsetY = Math.Round(_originY + (p.Y - _dragStart.Y) / sx);
            InvalidateVisual();
            TransformChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Ctrl+wheel: integer view zoom (pixel-perfect magnify)
                _viewZoom = Math.Clamp(_viewZoom + (e.Delta > 0 ? 1 : -1), 1, 16);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var factor = e.Delta > 0 ? 1.08 : 1 / 1.08;
            var newScale = Math.Clamp(Scale * factor, 0.05, 20);
            var sx = Math.Max(1, _viewZoom);
            var pos = e.GetPosition(this);
            var (ox, oy) = ViewOrigin();
            var cx = (pos.X - ox) / sx;
            var cy = (pos.Y - oy) / sx;
            OffsetX = Math.Round(cx - (cx - OffsetX) * (newScale / Scale));
            OffsetY = Math.Round(cy - (cy - OffsetY) * (newScale / Scale));
            Scale = newScale;
            InvalidateVisual();
            TransformChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private (double ox, double oy) ViewOrigin()
        {
            var viewW = ActualWidth > 0 ? ActualWidth : 280;
            var viewH = ActualHeight > 0 ? ActualHeight : 280;
            var sx = Math.Max(1, _viewZoom);
            return (Math.Floor((viewW - CanvasWidthPx * sx) / 2), Math.Floor((viewH - CanvasHeightPx * sx) / 2));
        }

        private static BitmapSource? LoadBitmap(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
    }
}
