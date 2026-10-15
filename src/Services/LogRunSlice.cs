using System;

namespace SSF2ModManager.Services
{
    public static class LogRunSlice
    {
        public static string Latest(string? body, string marker)
        {
            if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(marker))
                return body ?? "";
            var at = body.LastIndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
                return body;
            var line = body.LastIndexOf('\n', at);
            return line < 0 ? body : body[(line + 1)..];
        }
    }
}
