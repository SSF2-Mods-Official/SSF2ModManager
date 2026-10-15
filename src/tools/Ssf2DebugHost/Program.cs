using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

const int Port = 39333;
const string LogFileName = "ssf2mm_debuglog.txt";

var install = "";
for (var i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--install", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        install = args[++i].Trim().Trim('"');
    else if (args[i].StartsWith("--install=", StringComparison.OrdinalIgnoreCase))
        install = args[i]["--install=".Length..].Trim().Trim('"');
}

if (string.IsNullOrWhiteSpace(install) || !Directory.Exists(install))
{
    Console.Error.WriteLine("Usage: ssf2mm-debug-host --install <ssf2-install-root>");
    return 1;
}

Console.OutputEncoding = Encoding.UTF8;
try { Console.Title = "SSF2 Debug Console"; } catch { /* ignore */ }

var logPath = Path.Combine(install, LogFileName);
Console.WriteLine("SSF2 Debug Host");
Console.WriteLine("Install: " + install);
Console.WriteLine("Log:     " + logPath);
Console.WriteLine($"Listen:  127.0.0.1:{Port}");
Console.WriteLine(new string('-', 60));

void LogBoth(string text)
{
    Console.WriteLine(text);
    try { File.AppendAllText(logPath, text + Environment.NewLine, Encoding.UTF8); } catch { /* ignore */ }
}

try
{
    File.WriteAllText(
        logPath,
        $"# SSF2 debug log {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
        $"# install={install}{Environment.NewLine}",
        Encoding.UTF8);
}
catch (Exception ex)
{
    Console.WriteLine("Warning: could not create log: " + ex.Message);
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

TcpListener? listener = null;
try
{
    listener = new TcpListener(IPAddress.Loopback, Port);
    listener.ExclusiveAddressUse = true;
    listener.Start();
    LogBoth($"-- listening on 127.0.0.1:{Port}");
}
catch (Exception ex)
{
    Console.Error.WriteLine("Failed to listen: " + ex.Message);
    return 2;
}

_ = AcceptLoopAsync(listener, LogBoth, cts.Token);

LogBoth("-- waiting for game traces (SSF2API.print)…");

var sawGame = false;
while (!cts.IsCancellationRequested)
{
    var running = Process.GetProcessesByName("SSF2").Length > 0
                  || Process.GetProcessesByName("adl").Length > 0;
    if (running) sawGame = true;
    if (sawGame && !running)
    {
        LogBoth("-- SSF2/adl exited — closing debug host.");
        break;
    }
    Thread.Sleep(500);
}

try { listener.Stop(); } catch { /* ignore */ }
LogBoth("-- stopped. Log saved to: " + logPath);
return 0;

static async Task AcceptLoopAsync(TcpListener listener, Action<string> log, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            var client = await listener.AcceptTcpClientAsync(ct);
            log("-- game connected");
            _ = ReadClientAsync(client, log, ct);
        }
        catch (OperationCanceledException) { break; }
        catch (ObjectDisposedException) { break; }
        catch (Exception ex) { log("-- accept error: " + ex.Message); }
    }
}

static async Task ReadClientAsync(TcpClient client, Action<string> log, CancellationToken ct)
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

            if (!policyAnswered && ContainsPolicy(pending))
            {
                policyAnswered = true;
                var policy =
                    "<?xml version=\"1.0\"?>" +
                    "<!DOCTYPE cross-domain-policy SYSTEM \"http://www.adobe.com/xml/dtds/cross-domain-policy.dtd\">" +
                    "<cross-domain-policy>" +
                    "<allow-access-from domain=\"*\" to-ports=\"*\"/>" +
                    "</cross-domain-policy>\0";
                var bytes = Encoding.UTF8.GetBytes(policy);
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                // Drop policy request from buffer
                var buf = pending.ToArray();
                var nul = Array.IndexOf(buf, (byte)0);
                pending.SetLength(0);
                if (nul >= 0 && nul + 1 < buf.Length)
                    pending.Write(buf, nul + 1, buf.Length - nul - 1);
                continue;
            }

            while (true)
            {
                var buf = pending.ToArray();
                var nl = Array.IndexOf(buf, (byte)'\n');
                if (nl < 0) break;
                var line = Encoding.UTF8.GetString(buf, 0, nl).TrimEnd('\r');
                pending.SetLength(0);
                if (nl + 1 < buf.Length)
                    pending.Write(buf, nl + 1, buf.Length - nl - 1);
                if (line.Length > 0)
                    log(line);
            }
        }
    }
    catch (OperationCanceledException) { /* ignore */ }
    catch (Exception ex) { log("-- read error: " + ex.Message); }
    finally
    {
        try { client.Close(); } catch { /* ignore */ }
        log("-- game disconnected");
    }
}

static bool ContainsPolicy(MemoryStream pending)
{
    var s = Encoding.UTF8.GetString(pending.ToArray());
    return s.Contains("<policy-file-request", StringComparison.OrdinalIgnoreCase)
           || s.Contains("policy-file-request", StringComparison.OrdinalIgnoreCase);
}
