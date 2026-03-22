using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace SSF2ModManager
{
    public static class Localization
    {
        private static Dictionary<string, string> _strings = new();
        public static string CurrentLanguage { get; private set; } = "en";

        public static void Load(string langCode)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", $"lang-{langCode}.json");
            if (!File.Exists(path))
            {
                // Fallback to English
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", "lang-en.json");
                CurrentLanguage = "en";
            }
            else
            {
                CurrentLanguage = langCode;
            }

            try
            {
                var json = File.ReadAllText(path);
                _strings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ssf2mm-debug.log"), $"[Localization] Failed to load {path}: {ex}\n"); } catch { }
                // Ensure we have at least English fallback in memory
                if (CurrentLanguage != "en")
                {
                    try
                    {
                        var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", "lang-en.json");
                        if (File.Exists(fallback))
                        {
                            var j2 = File.ReadAllText(fallback);
                            _strings = JsonConvert.DeserializeObject<Dictionary<string, string>>(j2) ?? new();
                            CurrentLanguage = "en";
                            return;
                        }
                    }
                    catch { }
                }
                _strings = new();
            }
        }

        public static string Get(string key)
        {
            if (_strings.TryGetValue(key, out var value))
                return value;
            return key; // fallback to key if missing
        }
    }
}
