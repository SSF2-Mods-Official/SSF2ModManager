using Newtonsoft.Json;
using System;
using System.IO;

namespace SSF2ModManager.Services
{
    public class SettingsModel
    {
        public string? Theme { get; set; }
        public string? Language { get; set; }
        public bool ProtocolRegistered { get; set; }
    }

    public class SettingsService
    {
        private readonly string _path;
        private SettingsModel _settings = new();

        public SettingsService()
        {
            _path = AppPaths.SettingsFile;
            Load();
            MigrateLegacySettings();
        }

        private void MigrateLegacySettings()
        {
            try
            {
                var legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user-settings.json");
                if (!File.Exists(legacy) || File.Exists(_path)) return;
                File.Copy(legacy, _path, overwrite: false);
                Load();
            }
            catch { }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) { _settings = new SettingsModel(); return; }
                var txt = File.ReadAllText(_path);
                _settings = JsonConvert.DeserializeObject<SettingsModel>(txt) ?? new SettingsModel();
            }
            catch { _settings = new SettingsModel(); }
        }

        private void Save()
        {
            try
            {
                var txt = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_path, txt);
            }
            catch { }
        }

        public string? GetTheme() => _settings.Theme;

        public void SetTheme(string? theme)
        {
            _settings.Theme = theme;
            Save();
        }

        public string? GetLanguage() => _settings.Language;

        public void SetLanguage(string? language)
        {
            _settings.Language = language;
            Save();
        }

        public bool IsProtocolRegistered() => _settings.ProtocolRegistered;

        public void SetProtocolRegistered(bool registered)
        {
            _settings.ProtocolRegistered = registered;
            Save();
        }
    }
}
