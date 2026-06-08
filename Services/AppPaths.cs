using System;
using System.IO;

namespace SSF2ModManager.Services
{
    public static class AppPaths
    {
        public static string AppDataDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SSF2ModManager");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsFile => Path.Combine(AppDataDir, "user-settings.json");
        public static string DebugLogFile => Path.Combine(AppDataDir, "ssf2mm-debug.log");
        public static string ModDatabaseFile => Path.Combine(AppDataDir, "mods.json");
    }
}
