namespace SSF2ModManager.Models
{
    public class CliOptions
    {
        // General
        public bool Help { get; set; }
        public bool Version { get; set; }
        public bool Verbose { get; set; }
        public string? LogFile { get; set; }

        // Startup / UI
        public bool StartMinimized { get; set; }
        public bool StartHidden { get; set; }
        public string? OpenPage { get; set; }
        public bool OpenNews { get; set; }
        public string? NewsPath { get; set; }
        public string? Theme { get; set; }

        // News & Content
        public bool RefreshNews { get; set; }
        public bool NewsCacheClear { get; set; }
        public bool NewsPreviewOnly { get; set; }

        // GameBanana / Mod operations
        public List<string> Install { get; } = new();
        public List<string> Uninstall { get; } = new();
        public List<string> Enable { get; } = new();
        public List<string> Disable { get; } = new();
        public List<string> Update { get; } = new();
        public List<string> ImportGb { get; } = new();
        public string? InstallBatch { get; set; }

        // Search / Browse automation
        public string? SearchQuery { get; set; }
        public int? DownloadResultIndex { get; set; }
        public int? BrowseLimit { get; set; }

        // Builds
        public List<string> AddBuild { get; } = new();
        public List<string> RemoveBuild { get; } = new();
        public List<string> LaunchBuild { get; } = new();

        // Config
        public string? ConfigPath { get; set; }
        public string? ExportConfig { get; set; }
        public string? ImportConfig { get; set; }
        public List<string> SetPrefs { get; } = new();

        // Network
        public string? Proxy { get; set; }
        public bool NoNetwork { get; set; }

        // Updates
        public bool CheckUpdates { get; set; }
        public bool ApplyUpdate { get; set; }
        public bool SelfInstall { get; set; }

        // Diagnostics
        public string? DumpLog { get; set; }
        public bool TraceApi { get; set; }
        public string? ScreenshotNews { get; set; }
        public bool Diagnostics { get; set; }

        // Automation
        public bool CiMode { get; set; }
        public bool Yes { get; set; }
        public bool DryRun { get; set; }
        public string? OutputFormat { get; set; }

        // Security / Protocol
        public bool RegisterProtocol { get; set; }
        public bool UnregisterProtocol { get; set; }
    }
}
