using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SSF2ModManager.Services
{
    public class SettingsModel
    {
        public string? Theme { get; set; }
        public string? Language { get; set; }
        public bool ProtocolRegistered { get; set; }
        public List<string> ReadNewsArticleIds { get; set; } = new();
        public string? LastNewsPromptVersion { get; set; }
        public DateTime? LastNewsSyncUtc { get; set; }
        public string? LastNewsSyncError { get; set; }
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
                _settings.ReadNewsArticleIds ??= new List<string>();
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

        public IReadOnlyList<string> GetReadNewsArticleIds() => _settings.ReadNewsArticleIds;

        public bool IsNewsArticleRead(string id) =>
            _settings.ReadNewsArticleIds.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));

        public void MarkNewsArticleRead(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || IsNewsArticleRead(id)) return;
            _settings.ReadNewsArticleIds.Add(id);
            Save();
        }

        public void MarkAllNewsRead(IEnumerable<string> articleIds)
        {
            var read = new HashSet<string>(_settings.ReadNewsArticleIds, StringComparer.OrdinalIgnoreCase);
            foreach (var id in articleIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    read.Add(id);
            }
            _settings.ReadNewsArticleIds = read.ToList();
            Save();
        }

        public string? GetLastNewsPromptVersion() => _settings.LastNewsPromptVersion;

        public void SetLastNewsPromptVersion(string? version)
        {
            _settings.LastNewsPromptVersion = version;
            Save();
        }

        public DateTime? GetLastNewsSyncUtc() => _settings.LastNewsSyncUtc;

        public string? GetLastNewsSyncError() => _settings.LastNewsSyncError;

        public void SetLastNewsSync(DateTime? utc, string? error)
        {
            if (utc.HasValue)
                _settings.LastNewsSyncUtc = utc;
            _settings.LastNewsSyncError = error;
            Save();
        }
    }
}
