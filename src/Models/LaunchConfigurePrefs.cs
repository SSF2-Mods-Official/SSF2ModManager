using System.Collections.Generic;

namespace SSF2ModManager.Services
{
    public sealed class LaunchCustomIntro
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
    }

    public sealed class LaunchPlayerPref
    {
        public string Character { get; set; } = "";
        public bool Human { get; set; } = true;
        public int Level { get; set; } = 3;
        public int Team { get; set; } = 1;
        public string Tag { get; set; } = "";
    }

    /// <summary>
    /// Game-facing SSF2 Stream launch fields. Stored in user-settings.json.
    /// Does not include editor / stream-panel settings.
    /// </summary>
    public sealed class LaunchConfigurePrefs
    {
        /// <summary>When true, the launcher Play button writes these settings before starting the game.</summary>
        public bool UseOnEveryLaunch { get; set; }
        public string StartingMenu { get; set; } = "home";
        public bool LoadMatchImmediate { get; set; }
        public string BootIntro { get; set; } = "default";
        public string VaultAction { get; set; } = "intro";
        public string VaultIntro { get; set; } = "intro3";
        public string VaultPaletteChar { get; set; } = "mario";
        public List<LaunchCustomIntro> BootCustomIntros { get; set; } = new();
        public List<string> BootRandomPool { get; set; } = new();
        public string MatchStage { get; set; } = "battlefield";
        public string MatchMusic { get; set; } = "";
        /// <summary>Write <see cref="MatchMusic"/> into the next launch.</summary>
        public bool UseMusicOnLaunch { get; set; }
        /// <summary>Keep <see cref="MatchMusic"/> playing in menus and matches.</summary>
        public bool UseMusicEverywhere { get; set; }
        public string EventId { get; set; } = "";
        /// <summary>For events that do not open a character select, replace the scripted fighter.</summary>
        public bool EventOverride { get; set; }
        public string EventCharacter { get; set; } = "mario";
        /// <summary>Track substituted for menumusic. Also written to expansions/user/menu-music.json.</summary>
        public string MenuMusicId { get; set; } = "";
        /// <summary>Songs rotated at random whenever the game asks for menu music.</summary>
        public List<string> MenuPlaylist { get; set; } = new();
        /// <summary>When false, stock menu music plays but the playlist is kept.</summary>
        public bool MenuPlaylistEnabled { get; set; } = true;
        /// <summary>Leave Configure open after Launch.</summary>
        public bool KeepConfigureOpen { get; set; }
        /// <summary>Start the game in fullscreen when launching from Configure.</summary>
        public bool OpenFullscreen { get; set; }
        public string MatchCustomModeId { get; set; } = "";
        /// <summary>Home-Run Contest and Arena: replace the sandbag with this fighter.</summary>
        public bool SandbagOverride { get; set; }
        public string SandbagCharacter { get; set; } = "mario";
        public string MatchDifficulty { get; set; } = "NORMAL";
        public int MatchScoreLimit { get; set; } = 5;
        public int MatchSpecialModes { get; set; }
        public bool MatchHazards { get; set; } = true;
        public bool MatchUsingTime { get; set; } = true;
        public bool MatchUsingLives { get; set; } = true;
        public int MatchLives { get; set; } = 3;
        public int MatchTime { get; set; } = 8;
        public List<LaunchPlayerPref> Players { get; set; } = new();
        public List<string> PlayerTags { get; set; } = new();

        public void EnsureDefaults()
        {
            StartingMenu = string.IsNullOrWhiteSpace(StartingMenu) ? "home" : StartingMenu;
            BootIntro = string.IsNullOrWhiteSpace(BootIntro) ? "default" : BootIntro;
            VaultAction = string.IsNullOrWhiteSpace(VaultAction) ? "intro" : VaultAction;
            VaultIntro = string.IsNullOrWhiteSpace(VaultIntro) ? "intro3" : VaultIntro;
            VaultPaletteChar = string.IsNullOrWhiteSpace(VaultPaletteChar) ? "mario" : VaultPaletteChar;
            EventCharacter = string.IsNullOrWhiteSpace(EventCharacter) ? "mario" : EventCharacter;
            SandbagCharacter = string.IsNullOrWhiteSpace(SandbagCharacter) ? "mario" : SandbagCharacter;
            MenuMusicId ??= "";
            MenuPlaylist ??= new List<string>();
            // MenuPlaylistEnabled defaults true via property initializer
            MatchStage = string.IsNullOrWhiteSpace(MatchStage) ? "battlefield" : MatchStage;
            MatchDifficulty = string.IsNullOrWhiteSpace(MatchDifficulty) ? "NORMAL" : MatchDifficulty;
            BootCustomIntros ??= new List<LaunchCustomIntro>();
            BootRandomPool ??= new List<string>();
            PlayerTags ??= new List<string>();
            Players ??= new List<LaunchPlayerPref>();
            while (Players.Count < 4)
                Players.Add(new LaunchPlayerPref());
            if (Players.Count > 4)
                Players.RemoveRange(4, Players.Count - 4);
            if (string.IsNullOrWhiteSpace(Players[0].Character)) Players[0].Character = "mario";
            if (string.IsNullOrWhiteSpace(Players[1].Character)) Players[1].Character = "fox";
            Players[0].Human = Players[0].Human;
            if (Players[0].Level < 1) Players[0].Level = 1;
            if (Players[1].Level < 1) Players[1].Level = 3;
            if (Players[2].Level < 1) Players[2].Level = 3;
            if (Players[3].Level < 1) Players[3].Level = 3;
            if (Players[0].Team < 1) Players[0].Team = 1;
            if (Players[1].Team < 1) Players[1].Team = 3;
            if (Players[2].Team < 1) Players[2].Team = 1;
            if (Players[3].Team < 1) Players[3].Team = 3;
        }
    }
}
