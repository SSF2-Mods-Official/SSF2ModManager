using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.Media;
using WinRT;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Hooks Windows System Media Transport Controls so keyboard media keys work
    /// and song metadata appears in the OS media flyout / taskbar.
    /// </summary>
    public sealed class SystemMediaControlsService : IDisposable
    {
        private static readonly Guid SmtcInterfaceIid = new("99fa3ff4-1742-42a6-902e-087d41f965ec");

        private SystemMediaTransportControls? _smtc;
        private AppMusicPlayerService? _player;
        private Dispatcher? _dispatcher;
        private DateTime _lastTimelineUtc = DateTime.MinValue;
        private string _lastMetaKey = "";
        private bool _disposed;

        [ComImport]
        [Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a")]
        [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        private interface ISystemMediaTransportControlsInterop
        {
            SystemMediaTransportControls GetForWindow(IntPtr appWindow, [In] ref Guid riid);
        }

        public bool IsAttached => _smtc != null;

        public void Attach(IntPtr hwnd, AppMusicPlayerService player, Dispatcher dispatcher)
        {
            if (hwnd == IntPtr.Zero || player == null || dispatcher == null) return;
            DetachEvents();
            _player = player;
            _dispatcher = dispatcher;

            try
            {
                _smtc = GetForWindow(hwnd);
                if (_smtc == null) return;

                _smtc.IsEnabled = true;
                _smtc.IsPlayEnabled = true;
                _smtc.IsPauseEnabled = true;
                _smtc.IsNextEnabled = true;
                _smtc.IsPreviousEnabled = true;
                _smtc.IsStopEnabled = true;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;

                _smtc.ButtonPressed += OnButtonPressed;
                _smtc.PlaybackPositionChangeRequested += OnPositionChangeRequested;

                _player.TrackChanged += OnPlayerChanged;
                _player.PlaybackStateChanged += OnPlayerChanged;
                _player.PositionChanged += OnPositionChanged;

                PushAll();
            }
            catch (Exception ex)
            {
                DebugLogger.Error("System media controls init failed", ex);
                _smtc = null;
            }
        }

        private static SystemMediaTransportControls? GetForWindow(IntPtr hwnd)
        {
            var factory = ActivationFactory.Get("Windows.Media.SystemMediaTransportControls");
            var interop = factory.AsInterface<ISystemMediaTransportControlsInterop>();
            var iid = SmtcInterfaceIid;
            return interop.GetForWindow(hwnd, ref iid);
        }

        private void OnPlayerChanged(object? sender, EventArgs e) =>
            _dispatcher?.BeginInvoke(PushAll);

        private void OnPositionChanged(object? sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _lastTimelineUtc).TotalMilliseconds < 500) return;
            _dispatcher?.BeginInvoke(PushTimeline);
        }

        private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            var button = args.Button;
            _dispatcher?.BeginInvoke(() =>
            {
                if (_player == null) return;
                switch (button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        _player.Play();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        _player.Pause();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        _player.Next();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        _player.Previous();
                        break;
                    case SystemMediaTransportControlsButton.Stop:
                        _player.Stop();
                        break;
                }
            });
        }

        private void OnPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
        {
            var pos = args.RequestedPlaybackPosition;
            _dispatcher?.BeginInvoke(() => _player?.SeekDisplay(pos));
        }

        private void PushAll()
        {
            if (_smtc == null || _player == null) return;
            try
            {
                var cur = _player.Current;
                if (cur == null)
                {
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                    _smtc.IsEnabled = false;
                    _lastMetaKey = "";
                    return;
                }

                _smtc.IsEnabled = true;
                _smtc.PlaybackStatus = _player.IsPlaying
                    ? MediaPlaybackStatus.Playing
                    : MediaPlaybackStatus.Paused;

                var title = string.IsNullOrWhiteSpace(cur.Name) ? cur.Id : cur.Name.Trim();
                var artist = string.IsNullOrWhiteSpace(cur.Artist) ? "SSF2" : cur.Artist.Trim();
                var metaKey = $"{cur.Id}|{title}|{artist}";
                if (!string.Equals(metaKey, _lastMetaKey, StringComparison.Ordinal))
                {
                    _lastMetaKey = metaKey;
                    var updater = _smtc.DisplayUpdater;
                    updater.Type = MediaPlaybackType.Music;
                    updater.MusicProperties.Title = title;
                    updater.MusicProperties.Artist = artist;
                    updater.MusicProperties.AlbumTitle = "Super Smash Flash 2";
                    updater.AppMediaId = "SSF2ModManager." + (cur.Id ?? "track");
                    updater.Update();
                }

                PushTimeline();
            }
            catch (Exception ex)
            {
                DebugLogger.Error("System media controls update failed", ex);
            }
        }

        private void PushTimeline()
        {
            if (_smtc == null || _player == null || _player.Current == null) return;
            try
            {
                _lastTimelineUtc = DateTime.UtcNow;
                var dur = _player.DisplayDuration;
                var pos = _player.Position;
                if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                if (pos > dur && dur > TimeSpan.Zero) pos = dur;

                _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    Position = pos,
                    EndTime = dur,
                    MaxSeekTime = dur
                });
            }
            catch
            {
                // Timeline is best-effort; never break playback.
            }
        }

        private void DetachEvents()
        {
            if (_player != null)
            {
                _player.TrackChanged -= OnPlayerChanged;
                _player.PlaybackStateChanged -= OnPlayerChanged;
                _player.PositionChanged -= OnPositionChanged;
            }

            if (_smtc != null)
            {
                try
                {
                    _smtc.ButtonPressed -= OnButtonPressed;
                    _smtc.PlaybackPositionChangeRequested -= OnPositionChangeRequested;
                    _smtc.IsEnabled = false;
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                }
                catch { /* ignore */ }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DetachEvents();
            _smtc = null;
            _player = null;
            _dispatcher = null;
        }
    }
}
