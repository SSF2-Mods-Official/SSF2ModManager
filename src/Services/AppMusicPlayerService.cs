using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace SSF2ModManager.Services
{
    public enum MusicRepeatMode
    {
        Off,
        One,
        All,
        Shuffle
    }

    /// <summary>
    /// App-wide music player: queue, prev/next, loop/shuffle, seamless extend, synced visualizer.
    /// </summary>
    public sealed class AppMusicPlayerService : IDisposable
    {
        private const int PcmRate = 4000;
        private readonly MediaPlayer _player = new();
        private readonly DispatcherTimer _timer;
        private readonly List<CachedSongInfo> _queue = new();
        private int _index = -1;
        private bool _loopSeamless = true;
        private DateTime? _extendUntilUtc;
        private int _extendMinutes;
        private double _extendVirtualMs;
        private double _lastPlayerMs = -1;
        private double? _holdDisplayMs;
        private DateTime _holdDisplayUntilUtc = DateTime.MinValue;
        private int _forceSeekFrames;
        private double _forceSeekPlayerMs = -1;
        private short[] _pcm = Array.Empty<short>();
        private int _pcmLoadGen;
        private readonly double[] _barPeaks = new double[16];
        private readonly Random _rng = new();
        private double _volume = 0.85;

        public AppMusicPlayerService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => OnTick();
            _player.MediaEnded += (_, _) => OnMediaEnded();
            _player.MediaOpened += (_, _) =>
            {
                ApplyVolume();
                _lastPlayerMs = _player.Position.TotalMilliseconds;
                PositionChanged?.Invoke(this, EventArgs.Empty);
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        public IReadOnlyList<CachedSongInfo> Queue => _queue;
        public int Index => _index;
        public CachedSongInfo? Current => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
        public bool IsPlaying { get; private set; }
        public MusicRepeatMode RepeatMode { get; set; } = MusicRepeatMode.All;

        /// <summary>0…1. Kept across <see cref="MediaPlayer.Open"/>, which resets the player volume.</summary>
        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 1);
                ApplyVolume();
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public bool SeamlessLoop
        {
            get => _loopSeamless;
            set => _loopSeamless = value;
        }

        /// <summary>True while a virtual extended timeline is active (scrubbable 0…N minutes).</summary>
        public bool IsExtending => _extendMinutes > 0;

        public TimeSpan Position
        {
            get
            {
                if (_holdDisplayMs is double hold && DateTime.UtcNow < _holdDisplayUntilUtc)
                    return TimeSpan.FromMilliseconds(hold);
                return IsExtending
                    ? TimeSpan.FromMilliseconds(Math.Clamp(_extendVirtualMs, 0, DisplayDuration.TotalMilliseconds))
                    : _player.Position;
            }
        }

        public TimeSpan NaturalDuration =>
            _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;

        public TimeSpan DisplayDuration =>
            IsExtending ? TimeSpan.FromMinutes(_extendMinutes) : NaturalDuration;

        public DateTime? ExtendUntilUtc => _extendUntilUtc;
        public int ActiveExtendMinutes => _extendMinutes;

        public double[] VisualizerBars { get; } = new double[16];

        public Func<CachedSongInfo, Task<bool>>? EnsurePlayableAsync { get; set; }

        public event EventHandler? TrackChanged;
        public event EventHandler? PositionChanged;
        public event EventHandler? PlaybackStateChanged;
        public event EventHandler? VisualizerUpdated;

        public void SetQueue(IEnumerable<CachedSongInfo> songs, int startIndex = 0)
        {
            _queue.Clear();
            _queue.AddRange(songs.Where(s => s != null));
            _index = _queue.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _queue.Count - 1);
            TrackChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool HoldPaused { get; set; }

        public void Play()
        {
            HoldPaused = false;
            if (Current == null) return;
            // Resume if already opened (media keys / pause toggle).
            if (_player.Source != null && !IsPlaying)
            {
                try
                {
                    _player.Play();
                    IsPlaying = true;
                    _timer.Start();
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
                catch { /* fall through to reopen */ }
            }
            _ = PlayInternalAsync();
        }

        public void Pause()
        {
            _player.Pause();
            IsPlaying = false;
            _timer.Stop();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void TogglePlayPause()
        {
            if (Current == null) return;
            if (IsPlaying) Pause();
            else Play();
        }

        public void Stop()
        {
            try { _player.Stop(); } catch { /* ignore */ }
            IsPlaying = false;
            _timer.Stop();
            ClearExtend();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearCurrent()
        {
            Stop();
            _index = -1;
            TrackChanged?.Invoke(this, EventArgs.Empty);
        }

        public void PlayTrack(CachedSongInfo song, IEnumerable<CachedSongInfo>? contextQueue = null)
        {
            var list = (contextQueue ?? new[] { song }).Where(s => s != null).ToList();
            var idx = list.FindIndex(s => s.Id.Equals(song.Id, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                list.Insert(0, song);
                idx = 0;
            }
            SetQueue(list, idx);
            ClearExtend();
            _ = PlayInternalAsync();
        }

        public void Next() => _ = StepAsync(+1);
        public void Previous() => _ = StepAsync(-1);

        public void ToggleRepeat()
        {
            RepeatMode = RepeatMode switch
            {
                MusicRepeatMode.Off => MusicRepeatMode.All,
                MusicRepeatMode.All => MusicRepeatMode.One,
                MusicRepeatMode.One => MusicRepeatMode.Shuffle,
                _ => MusicRepeatMode.Off
            };
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ExtendMinutes(int minutes)
        {
            if (minutes <= 0)
            {
                ClearExtend();
                PositionChanged?.Invoke(this, EventArgs.Empty);
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            _extendMinutes = minutes;
            _extendUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
            // Keep current place on the virtual timeline so scrubbing continues from here.
            var maxMs = TimeSpan.FromMinutes(minutes).TotalMilliseconds;
            var curMs = _player.NaturalDuration.HasTimeSpan
                ? _player.Position.TotalMilliseconds
                : 0;
            _extendVirtualMs = Math.Clamp(curMs, 0, maxMs);
            _lastPlayerMs = _player.Position.TotalMilliseconds;
            SeamlessLoop = true;
            if (RepeatMode == MusicRepeatMode.Off) RepeatMode = MusicRepeatMode.One;
            if (!IsPlaying) Play();
            PositionChanged?.Invoke(this, EventArgs.Empty);
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearExtend()
        {
            _extendUntilUtc = null;
            _extendMinutes = 0;
            _extendVirtualMs = 0;
        }

        /// <summary>Seek using the UI/display timeline (natural or extended).</summary>
        public void SeekDisplay(TimeSpan displayPosition)
        {
            var maxMs = Math.Max(0, DisplayDuration.TotalMilliseconds);
            var ms = Math.Clamp(displayPosition.TotalMilliseconds, 0, maxMs);
            _holdDisplayMs = ms;
            _holdDisplayUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);

            if (IsExtending)
            {
                _extendVirtualMs = ms;
                ApplyVirtualToPlayer();
            }
            else
            {
                SeekNatural(TimeSpan.FromMilliseconds(ms));
            }
            PositionChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                UpdateVisualizerBars();
                VisualizerUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch { /* never freeze UI from viz */ }
        }

        public void Seek(TimeSpan position) => SeekDisplay(position);

        private void SeekNatural(TimeSpan position)
        {
            var dur = NaturalDuration;
            if (dur <= TimeSpan.Zero) return;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > dur) position = dur;
            try
            {
                // Pause → seek → play is the most reliable MediaPlayer pattern for click-to-seek.
                var playing = IsPlaying;
                if (playing) _player.Pause();
                _player.Position = position;
                if (playing)
                {
                    _player.Play();
                    _player.Position = position;
                }
            }
            catch { /* ignore */ }
            _lastPlayerMs = position.TotalMilliseconds;
            _forceSeekPlayerMs = position.TotalMilliseconds;
            _forceSeekFrames = 24;
        }

        private void ApplyVirtualToPlayer()
        {
            var nat = NaturalDuration.TotalMilliseconds;
            if (nat <= 1 || Current == null) return;
            var loop = Math.Clamp(Current.LoopMs, 0, (int)nat - 1);
            var loopLen = Math.Max(1, nat - loop);
            double playerMs;
            if (_extendVirtualMs < nat)
                playerMs = _extendVirtualMs;
            else
                playerMs = loop + ((_extendVirtualMs - nat) % loopLen);
            SeekNatural(TimeSpan.FromMilliseconds(playerMs));
        }

        private static bool HasPreview(CachedSongInfo? s) =>
            s != null && !string.IsNullOrWhiteSpace(s.PreviewMp3Path) && File.Exists(s.PreviewMp3Path!);

        private async Task PlayInternalAsync()
        {
            if (Current == null) return;
            if (!HasPreview(Current) && EnsurePlayableAsync != null)
            {
                var ok = await EnsurePlayableAsync(Current);
                if (!ok || !HasPreview(Current)) return;
            }
            if (!HasPreview(Current)) return;

            OpenCurrent();
            if (HoldPaused)
            {
                try { _player.Pause(); } catch { /* ignore */ }
                IsPlaying = false;
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            _player.Play();
            IsPlaying = true;
            _timer.Start();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task StepAsync(int delta)
        {
            if (_queue.Count == 0) return;

            if (delta < 0 && _player.Position.TotalSeconds > 2)
            {
                SeekNatural(TimeSpan.Zero);
                if (IsExtending) _extendVirtualMs = 0;
                PositionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (RepeatMode == MusicRepeatMode.One && delta > 0)
            {
                await PlayInternalAsync();
                return;
            }

            if (RepeatMode == MusicRepeatMode.Shuffle && _queue.Count > 1)
            {
                var next = _index;
                for (var i = 0; i < 8 && next == _index; i++)
                    next = _rng.Next(0, _queue.Count);
                if (next == _index) next = (_index + 1) % _queue.Count;
                _index = next;
                TrackChanged?.Invoke(this, EventArgs.Empty);
                await PlayInternalAsync();
                return;
            }

            var attempts = 0;
            while (attempts < _queue.Count)
            {
                attempts++;
                _index += delta;
                if (_index >= _queue.Count)
                {
                    if (RepeatMode is MusicRepeatMode.All or MusicRepeatMode.Shuffle) _index = 0;
                    else { _index = _queue.Count - 1; Stop(); return; }
                }
                if (_index < 0)
                    _index = RepeatMode is MusicRepeatMode.All or MusicRepeatMode.Shuffle ? _queue.Count - 1 : 0;

                TrackChanged?.Invoke(this, EventArgs.Empty);
                if (!HasPreview(Current) && EnsurePlayableAsync != null)
                {
                    var ok = await EnsurePlayableAsync(Current!);
                    if (!ok) continue;
                }
                if (HasPreview(Current))
                {
                    await PlayInternalAsync();
                    return;
                }
            }
        }

        private void OpenCurrent()
        {
            var cur = Current;
            if (!HasPreview(cur)) return;
            try { _player.Stop(); } catch { /* ignore */ }
            try { _player.Close(); } catch { /* ignore */ }
            _player.Open(new Uri(cur!.PreviewMp3Path!));
            ApplyVolume();
            _lastPlayerMs = -1;
            _ = LoadPcmAsync(cur.PreviewMp3Path!);
            TrackChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyVolume()
        {
            try { _player.Volume = _volume; } catch { /* player may be between files */ }
        }

        private async Task LoadPcmAsync(string mp3Path)
        {
            var gen = ++_pcmLoadGen;
            _pcm = Array.Empty<short>();
            try
            {
                var pcm = await Task.Run(() => DecodePcm(mp3Path));
                if (gen != _pcmLoadGen) return;
                _pcm = pcm;
            }
            catch
            {
                if (gen == _pcmLoadGen) _pcm = Array.Empty<short>();
            }
        }

        private static short[] DecodePcm(string mp3Path)
        {
            var ffmpeg = MusicEditService.FindFfmpeg();
            if (ffmpeg == null || !File.Exists(mp3Path)) return Array.Empty<short>();

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-v error -i \"{mp3Path}\" -ac 1 -ar {PcmRate} -f s16le -acodec pcm_s16le -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return Array.Empty<short>();
            using var ms = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(ms);
            proc.WaitForExit(120_000);
            var bytes = ms.ToArray();
            if (bytes.Length < 4) return Array.Empty<short>();
            var samples = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
            var max = PcmRate * 60 * 10;
            if (samples.Length > max)
            {
                var trimmed = new short[max];
                Array.Copy(samples, trimmed, max);
                return trimmed;
            }
            return samples;
        }

        private void OnMediaEnded()
        {
            if (IsExtending && Current != null)
            {
                var loop = TimeSpan.FromMilliseconds(Math.Max(0, Current.LoopMs));
                _player.Position = loop;
                _lastPlayerMs = loop.TotalMilliseconds;
                _player.Play();
                return;
            }

            if (RepeatMode == MusicRepeatMode.One && Current != null)
            {
                var loop = SeamlessLoop
                    ? TimeSpan.FromMilliseconds(Math.Max(0, Current.LoopMs))
                    : TimeSpan.Zero;
                _player.Position = loop;
                _lastPlayerMs = loop.TotalMilliseconds;
                _player.Play();
                return;
            }

            Next();
        }

        private void OnTick()
        {
            try
            {
                var cur = Current;
                var playerMs = _player.Position.TotalMilliseconds;

                // Re-assert recent seeks — MediaPlayer can briefly ignore Position writes.
                if (_forceSeekFrames > 0 && _forceSeekPlayerMs >= 0)
                {
                    if (Math.Abs(playerMs - _forceSeekPlayerMs) > 180)
                    {
                        try
                        {
                            _player.Position = TimeSpan.FromMilliseconds(_forceSeekPlayerMs);
                            playerMs = _forceSeekPlayerMs;
                        }
                        catch { /* ignore */ }
                    }
                    _forceSeekFrames--;
                    if (_forceSeekFrames <= 0) _forceSeekPlayerMs = -1;
                }

                if (_holdDisplayMs != null && DateTime.UtcNow >= _holdDisplayUntilUtc)
                    _holdDisplayMs = null;

                if (cur != null && SeamlessLoop && _player.NaturalDuration.HasTimeSpan && IsPlaying
                    && _forceSeekFrames <= 0)
                {
                    var remain = NaturalDuration - _player.Position;
                    if (remain.TotalMilliseconds < 80 && NaturalDuration.TotalMilliseconds > 200)
                    {
                        var loop = TimeSpan.FromMilliseconds(Math.Max(0, cur.LoopMs));
                        if (loop < NaturalDuration - TimeSpan.FromMilliseconds(200))
                        {
                            _player.Position = loop;
                            playerMs = loop.TotalMilliseconds;
                        }
                    }
                }

                if (IsExtending && IsPlaying)
                {
                    if (_lastPlayerMs >= 0)
                    {
                        var delta = playerMs - _lastPlayerMs;
                        if (delta < -50)
                        {
                            // Wrapped to loop point.
                            var nat = NaturalDuration.TotalMilliseconds;
                            var loop = Math.Max(0, cur?.LoopMs ?? 0);
                            delta = Math.Max(0, (nat - _lastPlayerMs)) + Math.Max(0, playerMs - loop);
                        }
                        if (delta > 0 && delta < 500)
                            _extendVirtualMs = Math.Min(_extendVirtualMs + delta, DisplayDuration.TotalMilliseconds);
                    }

                    // End extend when the virtual timeline finishes (not wall-clock "minutes left").
                    if (_extendVirtualMs >= DisplayDuration.TotalMilliseconds - 25)
                    {
                        ClearExtend();
                        Next();
                        return;
                    }
                }

                _lastPlayerMs = playerMs;

                UpdateVisualizerBars();
                PositionChanged?.Invoke(this, EventArgs.Empty);
                VisualizerUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // Never let visualizer/tick crash the dispatcher.
            }
        }

        private static double AbsSample(int sample) => Math.Abs((double)sample);

        private void UpdateVisualizerBars()
        {
            var n = VisualizerBars.Length;
            if (!IsPlaying || _pcm.Length < 32)
            {
                for (var i = 0; i < n; i++)
                {
                    _barPeaks[i] *= 0.90;
                    VisualizerBars[i] = _barPeaks[i];
                }
                return;
            }

            var center = (int)(_player.Position.TotalSeconds * PcmRate);
            var halfWin = PcmRate / 44;
            for (var bar = 0; bar < n; bar++)
            {
                var step = 1 + bar / 3;
                double acc = 0;
                var count = 0;
                for (var i = -halfWin; i < halfWin; i += step)
                {
                    var idx = center + i;
                    if (idx <= 0 || idx >= _pcm.Length) continue;
                    double v;
                    if (bar < n / 3)
                        v = AbsSample(_pcm[idx]);
                    else if (bar < (2 * n) / 3)
                        v = AbsSample(_pcm[idx] - _pcm[idx - 1]) * 1.6;
                    else
                        v = AbsSample(_pcm[idx] - 2 * _pcm[Math.Max(0, idx - 1)] + _pcm[Math.Max(0, idx - 2)]) * 2.2;
                    acc += v;
                    count++;
                }

                var rms = count > 0 ? acc / count / 32768.0 : 0;
                var target = Math.Clamp(Math.Pow(rms * (2.8 + bar * 0.08), 0.65), 0, 1);

                if (target >= _barPeaks[bar])
                    _barPeaks[bar] = target;
                else
                    _barPeaks[bar] = _barPeaks[bar] * 0.86 + target * 0.14;

                VisualizerBars[bar] = _barPeaks[bar];
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            try { _player.Stop(); } catch { /* ignore */ }
            try { _player.Close(); } catch { /* ignore */ }
        }
    }
}
