using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Frame time and memory for the Log window.
    /// </summary>
    public static class GameProfilerMath
    {
        public const int SpikeMs = 68;
        public const int SmoothMs = 34;

        public static int? Fps(ulong firstFrame, ulong lastFrame, double elapsedMs)
        {
            if (elapsedMs < 50 || lastFrame < firstFrame)
                return null;
            return (int)Math.Round((lastFrame - firstFrame) * 1000.0 / elapsedMs);
        }

        public static int FrameMs(int fps)
        {
            if (fps <= 0)
                return 0;
            return (int)Math.Round(1000.0 / fps);
        }

        /// <summary>ok at 34 ms or under, slow through 68, spike over 68. none when the frame time is unknown.</summary>
        public static string Band(int frameMs)
        {
            if (frameMs <= 0)
                return "none";
            if (frameMs > SpikeMs)
                return "spike";
            if (frameMs > SmoothMs)
                return "slow";
            return "ok";
        }

        /// <summary>First bar index so <paramref name="selected"/> stays inside a window of <paramref name="maxBars"/>.</summary>
        public static int WindowStart(int frameCount, int selected, int maxBars)
        {
            if (frameCount <= 0 || maxBars <= 0 || frameCount <= maxBars)
                return 0;
            if (selected < 0)
                selected = 0;
            if (selected >= frameCount)
                selected = frameCount - 1;
            var start = selected - (maxBars / 2);
            if (start < 0)
                start = 0;
            var maxStart = frameCount - maxBars;
            if (start > maxStart)
                start = maxStart;
            return start;
        }
    }

    public sealed class TelemetryEvent
    {
        public string Name { get; init; } = "";
        public int Delta { get; init; }
        public int Span { get; init; }
        public int Value { get; init; }
        public int W { get; init; }
        public int H { get; init; }
        public int Hits { get; init; }
    }

    public sealed class ProfilerFrame
    {
        public int DeltaMs { get; init; }
        public int PrivateMb { get; init; }
        public IReadOnlyList<TelemetryEvent> Spans { get; init; } = Array.Empty<TelemetryEvent>();
    }

    public sealed class ProfilerSnapshot
    {
        public bool HasEvents { get; init; }
        public int Fps { get; init; }
        public int MaxMs { get; init; }
        public int FlashMb { get; init; }
        public int PrivateMb { get; init; }
        public IReadOnlyList<ProfilerFrame> Frames { get; init; } = Array.Empty<ProfilerFrame>();
        public string SpikeText { get; init; } = "";
        public int Batch { get; set; }
    }

    public static class GameProfilerSession
    {
        public const int Port = 7945;

        public static ProfilerSnapshot Read(IReadOnlyList<TelemetryEvent> events)
        {
            var pending = new List<TelemetryEvent>();
            var frames = new List<ProfilerFrame>();
            var flashKb = 0;
            var privateKb = 0;
            foreach (var ev in events)
            {
                if (ev.Name == ".mem.total")
                    flashKb = ev.Value;
                else if (ev.Name == ".mem.private")
                    privateKb = ev.Value;
                else if (ev.Name == ".enter")
                {
                    frames.Add(new ProfilerFrame
                    {
                        DeltaMs = ev.Delta,
                        PrivateMb = privateKb / 1024,
                        Spans = pending.ToArray()
                    });
                    pending = new List<TelemetryEvent>();
                }
                else if (ev.Name.Length > 0 && ev.Name[0] != '.')
                    pending.Add(ev);
            }

            var sum = 0;
            var count = 0;
            var max = 0;
            for (var i = frames.Count - 1; i >= 0; i--)
            {
                if (sum >= 1000 && count > 0)
                    break;
                var delta = frames[i].DeltaMs;
                sum += delta;
                count++;
                if (delta > max)
                    max = delta;
            }

            var fps = 0;
            if (sum > 0 && count > 0)
                fps = (int)Math.Round(count * 1000.0 / sum);

            var spike = "";
            for (var i = frames.Count - 1; i >= 0; i--)
            {
                if (frames[i].DeltaMs > GameProfilerMath.SpikeMs)
                {
                    spike = SpikeText(frames[i]);
                    break;
                }
            }

            return new ProfilerSnapshot
            {
                HasEvents = events.Count > 0,
                Fps = fps,
                MaxMs = max,
                FlashMb = flashKb / 1024,
                PrivateMb = privateKb / 1024,
                Frames = frames,
                SpikeText = spike
            };
        }

        public static string SampleLine(ProfilerSnapshot snap)
        {
            var line = "[profiler] sample fps=" + snap.Fps
                + " maxMs=" + snap.MaxMs
                + " flashMb=" + snap.FlashMb
                + " privateMb=" + snap.PrivateMb;
            if (snap.MaxMs > GameProfilerMath.SpikeMs && snap.SpikeText.Length > 0)
                line += " spike=" + snap.SpikeText;
            return line;
        }

        public static string SpikeText(ProfilerFrame frame)
        {
            var text = new StringBuilder();
            text.Append(frame.DeltaMs).Append(" ms");
            var order = new List<string>();
            var sums = new Dictionary<string, int>();
            var extra = new Dictionary<string, string>();
            foreach (var span in frame.Spans)
            {
                if (span.Span <= 0 || span.Name.Length == 0)
                    continue;
                if (!sums.ContainsKey(span.Name))
                {
                    order.Add(span.Name);
                    sums[span.Name] = 0;
                    var tail = new StringBuilder();
                    if (span.W > 0 || span.H > 0)
                        tail.Append("  ").Append(span.W).Append('x').Append(span.H);
                    if (span.Hits > 0)
                        tail.Append(" hits=").Append(span.Hits);
                    extra[span.Name] = tail.ToString();
                }
                sums[span.Name] += span.Span;
            }
            foreach (var name in order)
                text.Append("  ").Append(name).Append(' ').Append(sums[name]).Append(" ms").Append(extra[name]);
            return text.ToString();
        }

        public static bool TryParseLine(string line, out TelemetryEvent ev)
        {
            ev = new TelemetryEvent();
            if (string.IsNullOrWhiteSpace(line))
                return false;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("name", out var nameEl))
                    return false;
                var name = nameEl.GetString() ?? "";
                if (name.Length == 0)
                    return false;
                ev = new TelemetryEvent
                {
                    Name = name,
                    Delta = Num(root, "delta"),
                    Span = Num(root, "span"),
                    Value = Num(root, "value"),
                    W = Num(root, "w"),
                    H = Num(root, "h"),
                    Hits = Num(root, "hits")
                };
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static int Num(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var prop))
                return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n))
                return n;
            return 0;
        }
    }

    public sealed class GameProfilerListener : IDisposable
    {
        private const int MaxEvents = 4000;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancel = new();
        private readonly object _gate = new();
        private readonly List<TelemetryEvent> _events = new();
        private int _batch;

        public GameProfilerListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, GameProfilerSession.Port);
            _listener.Start();
            _ = Task.Run(() => AcceptLoop(_cancel.Token));
        }

        public ProfilerSnapshot Snapshot()
        {
            TelemetryEvent[] copy;
            lock (_gate)
                copy = _events.ToArray();
            var snap = GameProfilerSession.Read(copy);
            snap.Batch = Volatile.Read(ref _batch);
            return snap;
        }

        public void Dispose()
        {
            _cancel.Cancel();
            try
            {
                _listener.Stop();
            }
            catch
            {
            }
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(token);
                }
                catch
                {
                    break;
                }
                _ = Task.Run(() => ReadClient(client, token));
            }
        }

        private async Task ReadClient(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var buf = new byte[4096];
                var pending = new StringBuilder();
                while (!token.IsCancellationRequested)
                {
                    int n;
                    try
                    {
                        n = await stream.ReadAsync(buf, token);
                    }
                    catch
                    {
                        break;
                    }
                    if (n == 0)
                        break;
                    pending.Append(Encoding.UTF8.GetString(buf, 0, n));
                    Drain(pending);
                }
            }
        }

        private void Drain(StringBuilder pending)
        {
            var sawEnter = false;
            while (true)
            {
                var text = pending.ToString();
                var nl = text.IndexOf('\n');
                if (nl < 0)
                    break;
                var line = text[..nl].Trim('\r');
                pending.Remove(0, nl + 1);
                if (!GameProfilerSession.TryParseLine(line, out var ev))
                    continue;
                if (ev.Name == ".enter")
                    sawEnter = true;
                lock (_gate)
                {
                    _events.Add(ev);
                    if (_events.Count > MaxEvents)
                        _events.RemoveRange(0, _events.Count - MaxEvents);
                }
            }
            if (sawEnter)
                Interlocked.Increment(ref _batch);
        }
    }

    public sealed class GameProcessSample
    {
        public bool Running { get; init; }
        public int PrivateMb { get; init; }
        public int CpuPercent { get; init; }
    }

    public sealed class GameProcessProbe
    {
        private static readonly string[] ProcessNames =
        {
            "Super Smash Flash 2 Beta",
            "Super Smash Flash 2",
            "SSF2",
            "ssf2"
        };

        private bool _have;
        private DateTime _when;
        private TimeSpan _cpu;
        private int _pid;

        public GameProcessSample Read()
        {
            var extras = new List<Process>();
            Process? proc = null;
            try
            {
                proc = Find(extras);
                if (proc == null)
                {
                    Reset();
                    return new GameProcessSample();
                }
                proc.Refresh();
                var now = DateTime.UtcNow;
                var priv = (int)(proc.PrivateMemorySize64 / 1048576);
                var cpuNow = proc.TotalProcessorTime;
                var cpu = 0;
                if (_have && proc.Id == _pid)
                {
                    var elapsed = (now - _when).TotalMilliseconds;
                    if (elapsed > 0)
                    {
                        var cpuMs = (cpuNow - _cpu).TotalMilliseconds;
                        cpu = (int)Math.Round(cpuMs / elapsed / Environment.ProcessorCount * 100.0);
                        if (cpu < 0)
                            cpu = 0;
                        if (cpu > 100)
                            cpu = 100;
                    }
                }
                _when = now;
                _cpu = cpuNow;
                _pid = proc.Id;
                _have = true;
                return new GameProcessSample
                {
                    Running = true,
                    PrivateMb = priv,
                    CpuPercent = cpu
                };
            }
            catch
            {
                Reset();
                return new GameProcessSample();
            }
            finally
            {
                proc?.Dispose();
                foreach (var extra in extras)
                    extra.Dispose();
            }
        }

        private void Reset()
        {
            _have = false;
            _pid = 0;
        }

        private static Process? Find(List<Process> extras)
        {
            foreach (var name in ProcessNames)
            {
                Process[] found;
                try
                {
                    found = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }
                Process? chosen = null;
                foreach (var proc in found)
                {
                    if (chosen == null)
                        chosen = proc;
                    else
                        extras.Add(proc);
                }
                if (chosen != null)
                    return chosen;
            }
            return null;
        }
    }
}
