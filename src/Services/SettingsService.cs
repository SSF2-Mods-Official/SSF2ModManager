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
        public string? JpexsPath { get; set; }
        public string? CloneOutputDirectory { get; set; }
        public string? PressorLastDirectory { get; set; }
        /// <summary>Show Browse Mods / Installed Mods / Installed Builds for classic GameBanana installs.</summary>
        public bool LegacyModManager { get; set; }
        /// <summary>Install / game root used by the launcher hub.</summary>
        public string? LauncherInstallFolder { get; set; }
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Populate)]
        [System.ComponentModel.DefaultValue(true)]
        public bool UpdateAutomatically { get; set; } = true;
        /// <summary>Per character id → update prefs (assets / SWF checkboxes).</summary>
        public Dictionary<string, CharacterUpdatePrefs> CharacterUpdatePrefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Configure dialog launch prefs (written to data/ssf2mm on Launch).</summary>
        public LaunchConfigurePrefs LaunchConfigure { get; set; } = new();
        /// <summary>Open the log when SSF2 starts and close it when SSF2 exits.</summary>
        public bool OpenLogWithGame { get; set; }
        /// <summary>Blur the shell folder path so screenshots do not show the username.</summary>
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Populate)]
        [System.ComponentModel.DefaultValue(true)]
        public bool BlurFilePaths { get; set; } = true;
        /// <summary>Data folder SWFs are stored as the zlib package the game uncompresses, with a .ssf name.</summary>
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Populate)]
        [System.ComponentModel.DefaultValue(true)]
        public bool CompressData { get; set; } = true;
        /// <summary>Folder that contains Next2Flash. Empty uses the folder next to SSF2 Shell.</summary>
        public string? Next2FlashFolder { get; set; }
        public string? VanillaSsf2Folder { get; set; }
        /// <summary>vscode, cursor, or a path to the editor command.</summary>
        public string? DebugIde { get; set; }
        public bool IdeOfferShown { get; set; }
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
                _settings.CharacterUpdatePrefs ??= new Dictionary<string, CharacterUpdatePrefs>(StringComparer.OrdinalIgnoreCase);
                _settings.LaunchConfigure ??= new LaunchConfigurePrefs();
                _settings.LaunchConfigure.EnsureDefaults();
            }
            catch { _settings = new SettingsModel(); }
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

        public string? GetJpexsPath() => _settings.JpexsPath;

        public void SetJpexsPath(string? path)
        {
            _settings.JpexsPath = path;
            Save();
        }

        public string? GetCloneOutputDirectory() => _settings.CloneOutputDirectory;

        public void SetCloneOutputDirectory(string? path)
        {
            _settings.CloneOutputDirectory = path;
            Save();
        }

        public string? GetPressorLastDirectory() => _settings.PressorLastDirectory;

        public void SetPressorLastDirectory(string? path)
        {
            _settings.PressorLastDirectory = path;
            Save();
        }

        public bool GetLegacyModManager() => _settings.LegacyModManager;

        public void SetLegacyModManager(bool enabled)
        {
            _settings.LegacyModManager = enabled;
            Save();
        }

        public string? GetLauncherInstallFolder() => _settings.LauncherInstallFolder;

        public void SetLauncherInstallFolder(string? path)
        {
            _settings.LauncherInstallFolder = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            Save();
        }

        public bool GetUpdateAutomatically() => _settings.UpdateAutomatically;

        public void SetUpdateAutomatically(bool enabled)
        {
            _settings.UpdateAutomatically = enabled;
            Save();
        }

        public CharacterUpdatePrefs GetCharacterUpdatePrefs(string characterId)
        {
            _settings.CharacterUpdatePrefs ??= new Dictionary<string, CharacterUpdatePrefs>(StringComparer.OrdinalIgnoreCase);
            var id = (characterId ?? "").Trim();
            if (string.IsNullOrEmpty(id))
                return new CharacterUpdatePrefs();
            if (_settings.CharacterUpdatePrefs.TryGetValue(id, out var prefs) && prefs != null)
                return prefs;
            return new CharacterUpdatePrefs();
        }

        public void SetCharacterUpdatePrefs(string characterId, bool updateAssets, bool updateSwf)
        {
            _settings.CharacterUpdatePrefs ??= new Dictionary<string, CharacterUpdatePrefs>(StringComparer.OrdinalIgnoreCase);
            var id = (characterId ?? "").Trim();
            if (string.IsNullOrEmpty(id)) return;
            _settings.CharacterUpdatePrefs[id] = new CharacterUpdatePrefs
            {
                UpdateAssets = updateAssets,
                UpdateSwf = updateSwf
            };
            Save();
        }

        public LaunchConfigurePrefs GetLaunchConfigure()
        {
            _settings.LaunchConfigure ??= new LaunchConfigurePrefs();
            _settings.LaunchConfigure.EnsureDefaults();
            return JsonConvert.DeserializeObject<LaunchConfigurePrefs>(
                       JsonConvert.SerializeObject(_settings.LaunchConfigure))
                   ?? new LaunchConfigurePrefs();
        }

        public void SetLaunchConfigure(LaunchConfigurePrefs prefs)
        {
            prefs ??= new LaunchConfigurePrefs();
            prefs.EnsureDefaults();
            _settings.LaunchConfigure = prefs;
            Save();
        }

        public bool GetOpenLogWithGame() => _settings.OpenLogWithGame;

        public void SetOpenLogWithGame(bool enabled)
        {
            _settings.OpenLogWithGame = enabled;
            Save();
        }

        public bool GetBlurFilePaths() => _settings.BlurFilePaths;

        public void SetBlurFilePaths(bool enabled)
        {
            _settings.BlurFilePaths = enabled;
            Save();
        }

        public bool GetCompressData() => _settings.CompressData;

        public void SetCompressData(bool enabled)
        {
            _settings.CompressData = enabled;
            Save();
        }

        public string? GetNext2FlashFolder() => _settings.Next2FlashFolder;

        public void SetNext2FlashFolder(string? path)
        {
            _settings.Next2FlashFolder = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            Save();
        }

        public string? GetVanillaSsf2Folder() => _settings.VanillaSsf2Folder;

        public void SetVanillaSsf2Folder(string? path)
        {
            _settings.VanillaSsf2Folder = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            Save();
        }

        public string? GetDebugIde() => _settings.DebugIde;

        public void SetDebugIde(string? ide)
        {
            _settings.DebugIde = string.IsNullOrWhiteSpace(ide) ? null : ide.Trim();
            Save();
        }

        public bool GetIdeOfferShown() => _settings.IdeOfferShown;

        public void SetIdeOfferShown(bool shown)
        {
            _settings.IdeOfferShown = shown;
            Save();
        }

        public void Reload() => Load();

        public static event Action? Changed;

        private void Save()
        {
            try
            {
                var txt = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_path, txt);
                Changed?.Invoke();
            }
            catch { }
        }
    }
}
