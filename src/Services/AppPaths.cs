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

        /// <summary>Bundled news articles copied/extracted next to the executable.</summary>
        public static string NewsFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "News");

        /// <summary>Remote news articles synced from GitHub and cached locally.</summary>
        public static string NewsCacheFolder
        {
            get
            {
                var dir = Path.Combine(AppDataDir, "News");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
