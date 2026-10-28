namespace SSF2ModManager.Services
{
    /// <summary>A relative path that stays inside one mod folder.</summary>
    public static class ModFolder
    {
        public static bool Inside(string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
                return false;
            var text = relative.Replace('\\', '/');
            if (text.Contains("..", StringComparison.Ordinal))
                return false;
            if (text.Contains(':', StringComparison.Ordinal))
                return false;
            if (text.StartsWith('/'))
                return false;
            return true;
        }
    }
}
