using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Listens for newline-delimited UTF-8 log lines from the SSF2 debug console patch.
    /// Answers Flash socket policy requests (null-terminated, <b>not</b> newline-terminated).
    /// </summary>
    public sealed class GameDebugConsoleHost : IDisposable
    {
        public const int DefaultPort = GamePrepareService.DebugConsolePort;

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly object _gate = new();
        private readonly List<TcpClient> _clients = new();

        public event Action<string>? LineReceived;
        public event Action<string>? StatusChanged;

        public int Port { get; private set; } = DefaultPort;
        public bool IsListening => _listener != null;

        public void Start(int port = DefaultPort)
        {
            if (_listener != null) return;
            Port = port;
            _cts = new CancellationTokenSource();
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.ExclusiveAddressUse = true;
                _listener.Start();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                || ex.SocketErrorCode == SocketError.AccessDenied)
            {
                _listener = null;
                _cts.Dispose();
                _cts = null;
                throw new InvalidOperationException(
                    $"Port {port} is already in use (another Game Debug Console or leftover listener). " +
                    "Close the other console, or end whatever is bound to 127.0.0.1:" + port + ", then Retry listen.",
                    ex);
            }
            catch
            {
                _listener = null;
                try { _cts?.Dispose(); } catch { /* ignore */ }
                _cts = null;
                throw;
            }

            StatusChanged?.Invoke($"Listening on 127.0.0.1:{port}");
            _ = AcceptLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
            lock (_gate)
            {
                foreach (var c in _clients)
                {
                    try { c.Close(); } catch { /* ignore */ }
                }
                _clients.Clear();
            }
            StatusChanged?.Invoke("Stopped");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(ct);
                    lock (_gate) _clients.Add(client);
                    StatusChanged?.Invoke("Game connected");
                    _ = ReadClientAsync(client, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("Accept error: " + ex.Message);
                }
            }
        }

        private async Task ReadClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                await using var stream = client.GetStream();
                var raw = new byte[4096];
                var pending = new MemoryStream();
                var policyAnswered = false;

                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var n = await stream.ReadAsync(raw.AsMemory(0, raw.Length), ct);
                    if (n == 0) break;
                    pending.Write(raw, 0, n);

                    // Flash policy request is null-terminated and has NO newline — never use ReadLine for it.
                    if (!policyAnswered && ContainsPolicyRequest(pending))
                    {
                        var policy =
                            "<?xml version=\"1.0\"?>" +
                            "<!DOCTYPE cross-domain-policy SYSTEM \"http://www.adobe.com/xml/dtds/cross-domain-policy.dtd\">" +
                            "<cross-domain-policy>" +
                            "<allow-access-from domain=\"*\" to-ports=\"" + Port + "\"/>" +
                            "</cross-domain-policy>\0";
                        var bytes = Encoding.UTF8.GetBytes(policy);
                        await stream.WriteAsync(bytes, ct);
                        await stream.FlushAsync(ct);
                        policyAnswered = true;
                        StatusChanged?.Invoke("Answered Flash socket policy");
                        pending.SetLength(0);
                        // Flash usually closes this socket and reconnects for data.
                        continue;
                    }

                    // Emit complete newline-delimited log lines; keep a trailing partial line.
                    if (!TryExtractLines(pending, out var lines, out var rest))
                        continue;
                    pending.SetLength(0);
                    if (rest.Length > 0)
                        pending.Write(rest, 0, rest.Length);

                    foreach (var line in lines)
                    {
                        if (line.Length == 0) continue;
                        if (line.Contains("policy-file-request", StringComparison.OrdinalIgnoreCase))
                            continue;
                        LineReceived?.Invoke(line);
                    }
                }
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (IOException) { /* disconnect */ }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Client error: " + ex.Message);
            }
            finally
            {
                lock (_gate) _clients.Remove(client);
                try { client.Close(); } catch { /* ignore */ }
                if (IsListening)
                    StatusChanged?.Invoke($"Listening on 127.0.0.1:{Port} (game disconnected)");
                else
                    StatusChanged?.Invoke("Game disconnected");
            }
        }

        private static bool ContainsPolicyRequest(MemoryStream pending)
        {
            if (pending.Length == 0) return false;
            var data = pending.ToArray();
            var text = Encoding.UTF8.GetString(data);
            return text.Contains("policy-file-request", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryExtractLines(MemoryStream pending, out List<string> lines, out byte[] rest)
        {
            lines = new List<string>();
            rest = Array.Empty<byte>();
            var data = pending.ToArray();
            if (data.Length == 0) return false;

            var text = Encoding.UTF8.GetString(data);
            // Normalize nulls that can trail policy leftovers.
            text = text.Replace("\0", "");
            var parts = text.Split('\n');
            if (parts.Length == 1 && !text.EndsWith('\n') && !text.EndsWith("\r\n"))
            {
                // No complete line yet.
                return false;
            }

            var complete = text.EndsWith('\n') || text.EndsWith('\r');
            var count = complete ? parts.Length : parts.Length - 1;
            for (var i = 0; i < count; i++)
            {
                var line = parts[i].TrimEnd('\r');
                if (line.Length > 0) lines.Add(line);
            }

            if (!complete && parts.Length > 0)
                rest = Encoding.UTF8.GetBytes(parts[^1]);

            return lines.Count > 0 || rest.Length > 0 || complete;
        }

        public void Dispose() => Stop();
    }
}
