using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Forwards CLI/protocol arguments from a second process to the running instance.
    /// </summary>
    public static class SingleInstanceService
    {
        public const string MutexName = "SSF2ModManager_SINGLE_INSTANCE_MUTEX";
        public const string PipeName = "SSF2ModManager_IPC_v1";

        private static CancellationTokenSource? _cts;

        public static bool TryForwardArguments(string[] args)
        {
            if (args.Length == 0) return false;

            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(2000);
                var payload = JsonSerializer.Serialize(args);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.Write(payload);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void StartListening(Action<string[]> onArgumentsReceived)
        {
            StopListening();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(
                            PipeName, PipeDirection.In, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync(token);
                        using var reader = new StreamReader(server);
                        var payload = await reader.ReadToEndAsync(token);
                        if (string.IsNullOrWhiteSpace(payload)) continue;

                        var args = JsonSerializer.Deserialize<string[]>(payload);
                        if (args is { Length: > 0 })
                            onArgumentsReceived(args);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        try { await Task.Delay(100, token); } catch { break; }
                    }
                }
            }, token);
        }

        public static void StopListening()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
        }
    }
}
