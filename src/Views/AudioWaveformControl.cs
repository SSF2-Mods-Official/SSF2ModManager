using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SSF2ModManager.Services;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SSF2ModManager.Views
{
    /// <summary>
    /// Simple waveform with draggable trim start / trim end / loop markers and a playhead.
    /// </summary>
    public class AudioWaveformControl : Canvas
    {
        private float[] _peaks = Array.Empty<float>();
        private double _durationMs;
        private string? _dragKind; // trimStart | trimEnd | loop | null
        private readonly Polyline _wave = new() { StrokeThickness = 1.2, IsHitTestVisible = false };
        private readonly Line _trimStartLine = MakeMarker(Colors.DeepSkyBlue);
        private readonly Line _trimEndLine = MakeMarker(Colors.Orange);
        private readonly Line _loopLine = MakeMarker(Colors.LimeGreen);
        private readonly Line _playhead = MakeMarker(Colors.White);
        private readonly Rectangle _trimShadeLeft = new() { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), IsHitTestVisible = false };
        private readonly Rectangle _trimShadeRight = new() { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), IsHitTestVisible = false };

        public static readonly DependencyProperty TrimStartMsProperty =
            DependencyProperty.Register(nameof(TrimStartMs), typeof(double), typeof(AudioWaveformControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnMarkerChanged));
        public static readonly DependencyProperty TrimEndMsProperty =
            DependencyProperty.Register(nameof(TrimEndMs), typeof(double), typeof(AudioWaveformControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnMarkerChanged));
        public static readonly DependencyProperty LoopMsProperty =
            DependencyProperty.Register(nameof(LoopMs), typeof(double), typeof(AudioWaveformControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnMarkerChanged));
        public static readonly DependencyProperty PlayheadMsProperty =
            DependencyProperty.Register(nameof(PlayheadMs), typeof(double), typeof(AudioWaveformControl),
                new PropertyMetadata(0.0, OnMarkerChanged));

        public double TrimStartMs { get => (double)GetValue(TrimStartMsProperty); set => SetValue(TrimStartMsProperty, value); }
        public double TrimEndMs { get => (double)GetValue(TrimEndMsProperty); set => SetValue(TrimEndMsProperty, value); }
        public double LoopMs { get => (double)GetValue(LoopMsProperty); set => SetValue(LoopMsProperty, value); }
        public double PlayheadMs { get => (double)GetValue(PlayheadMsProperty); set => SetValue(PlayheadMsProperty, value); }
        public double DurationMs => _durationMs;

        public event EventHandler? MarkersChanged;

        public AudioWaveformControl()
        {
            Height = 88;
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            ClipToBounds = true;
            Children.Add(_trimShadeLeft);
            Children.Add(_trimShadeRight);
            Children.Add(_wave);
            Children.Add(_trimStartLine);
            Children.Add(_trimEndLine);
            Children.Add(_loopLine);
            Children.Add(_playhead);
            _wave.Stroke = new SolidColorBrush(Color.FromRgb(120, 180, 255));
            SizeChanged += (_, _) => Redraw();
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += (_, _) => { _dragKind = null; ReleaseMouseCapture(); };
            MouseLeave += (_, _) => { _dragKind = null; };
        }

        private static Line MakeMarker(Color c) => new()
        {
            Stroke = new SolidColorBrush(c),
            StrokeThickness = 2,
            Y1 = 0,
            IsHitTestVisible = false
        };

        private static void OnMarkerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AudioWaveformControl w) w.LayoutMarkers();
        }

        public async Task LoadFromAudioFileAsync(string path)
        {
            _peaks = Array.Empty<float>();
            _durationMs = 0;
            Redraw();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var ffmpeg = MusicEditService.FindFfmpeg();
            if (ffmpeg == null) return;

            var buckets = Math.Max(200, (int)ActualWidth * 2);
            if (buckets < 200) buckets = 400;

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-v error -i \"{path}\" -ac 1 -ar 8000 -f s16le -acodec pcm_s16le -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;
            await using var stream = proc.StandardOutput.BaseStream;
            var buf = new byte[8192];
            var samples = new System.Collections.Generic.List<short>(8000 * 60);
            int read;
            while ((read = await stream.ReadAsync(buf)) > 0)
            {
                for (var i = 0; i + 1 < read; i += 2)
                    samples.Add(BitConverter.ToInt16(buf, i));
            }
            await proc.WaitForExitAsync();
            if (samples.Count == 0) return;

            _durationMs = samples.Count / 8.0; // 8000 Hz → ms
            var peaks = new float[buckets];
            var block = Math.Max(1, samples.Count / buckets);
            for (var b = 0; b < buckets; b++)
            {
                var start = b * block;
                var end = Math.Min(samples.Count, start + block);
                float peak = 0;
                for (var i = start; i < end; i++)
                    peak = Math.Max(peak, Math.Abs(samples[i] / 32768f));
                peaks[b] = peak;
            }
            _peaks = peaks;
            if (TrimEndMs <= 0 || TrimEndMs > _durationMs) TrimEndMs = _durationMs;
            Redraw();
        }

        private void Redraw()
        {
            var w = ActualWidth;
            var h = ActualHeight;
            if (w < 2 || h < 2 || _peaks.Length == 0)
            {
                _wave.Points = new PointCollection();
                return;
            }

            var mid = h / 2;
            var pts = new PointCollection(_peaks.Length * 2);
            for (var i = 0; i < _peaks.Length; i++)
            {
                var x = i / (double)(_peaks.Length - 1) * w;
                var amp = _peaks[i] * (h * 0.45);
                pts.Add(new Point(x, mid - amp));
            }
            for (var i = _peaks.Length - 1; i >= 0; i--)
            {
                var x = i / (double)(_peaks.Length - 1) * w;
                var amp = _peaks[i] * (h * 0.45);
                pts.Add(new Point(x, mid + amp));
            }
            _wave.Points = pts;
            LayoutMarkers();
        }

        private void LayoutMarkers()
        {
            var w = ActualWidth;
            var h = ActualHeight;
            if (w < 2 || _durationMs <= 0) return;

            double X(double ms) => Math.Clamp(ms / _durationMs, 0, 1) * w;

            void Place(Line line, double ms)
            {
                var x = X(ms);
                line.X1 = x; line.X2 = x; line.Y1 = 0; line.Y2 = h;
            }

            Place(_trimStartLine, TrimStartMs);
            Place(_trimEndLine, TrimEndMs > 0 ? TrimEndMs : _durationMs);
            // Loop marker is relative to trimmed clip → absolute = trimStart + loop
            Place(_loopLine, TrimStartMs + LoopMs);
            Place(_playhead, PlayheadMs);

            var ts = X(TrimStartMs);
            var te = X(TrimEndMs > 0 ? TrimEndMs : _durationMs);
            SetLeft(_trimShadeLeft, 0);
            _trimShadeLeft.Width = Math.Max(0, ts);
            _trimShadeLeft.Height = h;
            SetLeft(_trimShadeRight, te);
            _trimShadeRight.Width = Math.Max(0, w - te);
            _trimShadeRight.Height = h;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_durationMs <= 0) return;
            CaptureMouse();
            var x = e.GetPosition(this).X;
            var ms = x / ActualWidth * _durationMs;
            var absLoop = TrimStartMs + LoopMs;
            var dStart = Math.Abs(ms - TrimStartMs);
            var dEnd = Math.Abs(ms - (TrimEndMs > 0 ? TrimEndMs : _durationMs));
            var dLoop = Math.Abs(ms - absLoop);
            var nearest = Math.Min(dStart, Math.Min(dEnd, dLoop));
            if (nearest > _durationMs * 0.03 && nearest > 80)
            {
                // Click seeks playhead only
                PlayheadMs = ms;
                MarkersChanged?.Invoke(this, EventArgs.Empty);
                _dragKind = null;
                return;
            }
            if (nearest == dStart) _dragKind = "trimStart";
            else if (nearest == dEnd) _dragKind = "trimEnd";
            else _dragKind = "loop";
            ApplyDrag(ms);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragKind == null || e.LeftButton != MouseButtonState.Pressed || _durationMs <= 0) return;
            ApplyDrag(e.GetPosition(this).X / ActualWidth * _durationMs);
        }

        private void ApplyDrag(double ms)
        {
            ms = Math.Clamp(ms, 0, _durationMs);
            switch (_dragKind)
            {
                case "trimStart":
                    TrimStartMs = Math.Min(ms, (TrimEndMs > 0 ? TrimEndMs : _durationMs) - 50);
                    break;
                case "trimEnd":
                    TrimEndMs = Math.Max(ms, TrimStartMs + 50);
                    break;
                case "loop":
                    LoopMs = Math.Max(0, ms - TrimStartMs);
                    break;
            }
            MarkersChanged?.Invoke(this, EventArgs.Empty);
            LayoutMarkers();
        }
    }
}
