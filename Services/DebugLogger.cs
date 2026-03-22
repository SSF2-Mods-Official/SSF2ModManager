using System;
using System.Collections.Generic;
using System.IO;

namespace SSF2ModManager.Services
{
    public static class DebugLogger
    {
        private static readonly List<string> _entries = new();
        private static readonly string _logPath;
        private static readonly object _lock = new();

        static DebugLogger()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SSF2ModManager");
            Directory.CreateDirectory(appData);
            _logPath = Path.Combine(appData, "debug.log");
        }

        public static IReadOnlyList<string> Entries => _entries;

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            lock (_lock)
            {
                _entries.Add(line);
                try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { }
            }
        }

        public static void Error(string message, Exception? ex = null)
        {
            var detail = ex != null ? $" | {ex.GetType().Name}: {ex.Message}" : "";
            var line = $"[{DateTime.Now:HH:mm:ss}] ERROR: {message}{detail}";
            lock (_lock)
            {
                _entries.Add(line);
                try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { }
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                try { File.WriteAllText(_logPath, ""); } catch { }
            }
        }

        public static string GetFullLog()
        {
            lock (_lock)
            {
                return string.Join(Environment.NewLine, _entries);
            }
        }
    }
}
