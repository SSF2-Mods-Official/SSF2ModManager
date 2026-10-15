using System;
using System.IO;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Optional verbose file logging for diagnostics. Disabled in Release unless --verbose/--diagnostics is passed.
    /// </summary>
    public static class DevFileLog
    {
        private static bool _enabled;

#if DEBUG
        private static readonly bool DefaultEnabled = true;
#else
        private static readonly bool DefaultEnabled = false;
#endif

        static DevFileLog()
        {
            _enabled = DefaultEnabled
                || string.Equals(Environment.GetEnvironmentVariable("SSF2MM_VERBOSE"), "1", StringComparison.Ordinal);
        }

        public static bool Enabled => _enabled;

        public static void SetEnabled(bool enabled) => _enabled = enabled;

        public static void Write(string message)
        {
            if (!_enabled) return;
            try
            {
                File.AppendAllText(AppPaths.DebugLogFile, message.EndsWith('\n') ? message : message + Environment.NewLine);
            }
            catch { }
        }

        public static void Reset(string header)
        {
            if (!_enabled) return;
            try
            {
                File.WriteAllText(AppPaths.DebugLogFile, header.EndsWith('\n') ? header : header + Environment.NewLine);
            }
            catch { }
        }
    }
}
