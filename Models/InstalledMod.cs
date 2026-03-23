using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace SSF2ModManager.Models
{
    public class SSF2VersionEntry
    {
        public string VersionName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string Nickname { get; set; } = string.Empty;
        public string DisplayName => string.IsNullOrEmpty(Nickname) ? VersionName : $"{Nickname} ({VersionName})";
    }

    public class BackedUpFile
    {
        public string OriginalRelativePath { get; set; } = string.Empty;
        public string BackupFullPath { get; set; } = string.Empty;
    }

    public class InstalledMod
    {
        public string Id { get; set; } = string.Empty;
        public int GameBananaId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        // Optional metadata from info.json
        public string Creator { get; set; } = string.Empty;
        public string ModType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string ProfileUrl { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public DateTime InstalledDate { get; set; } = DateTime.Now;
        public List<string> InstalledFiles { get; set; } = new();
        public string TargetVersion { get; set; } = string.Empty;
        public List<BackedUpFile> BackedUpFiles { get; set; } = new();
        public bool IsGameFiles { get; set; }

        [JsonIgnore]
        public string BuildDisplayName { get; set; } = string.Empty;
    }

    public class ModDatabase
    {
        public List<InstalledMod> Mods { get; set; } = new();
        public List<SSF2VersionEntry> Versions { get; set; } = new();
        public string ActiveVersion { get; set; } = string.Empty;
    }
}
