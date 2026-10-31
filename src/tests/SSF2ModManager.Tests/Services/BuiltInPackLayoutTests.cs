using System.IO;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class BuiltInPackLayoutTests
    {
        private static readonly string[] BuiltInIds =
        {
            "camera_background",
            "controller_profiles",
            "debug",
            "expansions",
            "manager_bridge",
            "menu_background",
            "menu_editor",
            "mod_menu",
            "music_player",
            "palette_maker",
            "replay_browser",
            "rumble"
        };

        [Fact]
        public void BuiltInPacks_LoadFromModJson_WithoutWritingInfoJson()
        {
            var root = NewShell();
            try
            {
                var sections = ExpansionModsService.Load(root);
                var essentials = sections.Single(s => s.Section == "Essentials").Mods;

                essentials.Select(m => m.FolderName).Should().BeEquivalentTo(BuiltInIds);
                essentials.Should().OnlyContain(m => !m.CanToggleEnabled && m.Enabled);
                essentials.Single(m => m.FolderName == "menu_background").Settings
                    .Should().ContainSingle(s => s.Id == "stageId" && s.Value == "battlefield22");
                foreach (var id in BuiltInIds)
                    File.Exists(Path.Combine(root, "data", "ssf2mm", id, "info.json")).Should().BeFalse(id);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void BuiltInSaveSettings_WritesModJson()
        {
            var root = NewShell();
            try
            {
                var entry = ExpansionModsService.Load(root)
                    .Single(s => s.Section == "Essentials").Mods
                    .Single(m => m.FolderName == "menu_background");
                entry.Settings[0].Value = "finaldestination";
                ExpansionModsService.Save(entry);

                var mod = JObject.Parse(File.ReadAllText(Path.Combine(entry.FolderPath, "mod.json")));
                mod["entryClass"]!.ToString().Should().Be("MenuBackgroundMod");
                mod["settings"]![0]!["value"]!.ToString().Should().Be("finaldestination");
                File.Exists(Path.Combine(entry.FolderPath, "info.json")).Should().BeFalse();
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void CompileTargets_ListBuiltInSource_AndOutputLandsInDataSsf2mm()
        {
            var root = NewShell();
            try
            {
                var targets = FolderCompileService.ListCompileTargets(root);
                foreach (var id in BuiltInIds)
                    targets.Should().Contain(t => t.Id == id && t.Label == "Built-in: " + id, id);

                var project = Path.Combine(root, "src", "mods", "menu_editor", "asconfig.json");
                var (resolved, output) = FolderCompileService.ResolveModProject(project);

                output.Should().Be(Path.Combine(root, "data", "ssf2mm", "menu_editor", "mod.swf"));
                var opts = (JObject)JObject.Parse(File.ReadAllText(resolved))["compilerOptions"]!;
                opts["source-path"]!.Single().ToString()
                    .Should().Be(Path.Combine(root, "src", "mods", "menu_editor", "src").Replace('\\', '/'));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void StateFiles_LiveUnderState()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-state-" + Guid.NewGuid().ToString("N"));
            ShellWorkspaceService.StateDirectory(root)
                .Should().Be(Path.Combine(root, "expansions", "state"));
            ShellDirectLaunchService.GetLaunchFilePath(root)
                .Should().Be(Path.Combine(root, "expansions", "state", "launch.json"));
        }

        [Fact]
        public void UserState_PrefersExpansionsUser_OverState()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-user-" + Guid.NewGuid().ToString("N"));
            try
            {
                var user = Path.Combine(root, "expansions", "user", "menu-music.json");
                var legacy = Path.Combine(root, "data", "ssf2mm", "state", "menu-music.json");
                ShellWorkspaceService.UserStateFile(root, "menu-music.json").Should().Be(user);

                Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
                File.WriteAllText(legacy, "{\"enabled\":true,\"playlist\":[\"a\"]}");
                ShellWorkspaceService.UserStateFile(root, "menu-music.json").Should().Be(user);
                ShellConfigureLaunchService.ReadMenuPlaylist(root).Should().BeEmpty();

                ShellConfigureLaunchService.WriteMenuMusic(root, "b", new[] { "b" });
                File.Exists(user).Should().BeTrue();
                ShellWorkspaceService.UserStateFile(root, "menu-music.json").Should().Be(user);
                ShellConfigureLaunchService.ReadMenuPlaylist(root).Should().Equal("b");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ControllerProfiles_PreferUserFile_OverOlderCopies()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-profiles-" + Guid.NewGuid().ToString("N"));
            try
            {
                var user = Path.Combine(root, "expansions", "user", "controller_profiles.json");
                ControllerProfilesSyncService.ProfilesPath(root).Should().Be(user);

                var pack = Path.Combine(root, "expansions", "mods", "controller_profiles", "profiles.json");
                Directory.CreateDirectory(Path.GetDirectoryName(pack)!);
                File.WriteAllText(pack, "{\"names\":[\"Oldest\"]}");
                ControllerProfilesSyncService.ProfilesPath(root).Should().Be(pack);

                var state = Path.Combine(ShellWorkspaceService.StateDirectory(root), "controller_profiles.json");
                Directory.CreateDirectory(Path.GetDirectoryName(state)!);
                File.WriteAllText(state, "{\"names\":[\"Old\"]}");
                ControllerProfilesSyncService.ProfilesPath(root).Should().Be(state);

                ControllerProfilesSyncService.AddName(root, "New").Should().BeTrue();
                File.Exists(user).Should().BeTrue();
                ControllerProfilesSyncService.ProfilesPath(root).Should().Be(user);
                ControllerProfilesSyncService.Load(root).Names.Should().Equal("Old", "New");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void PlayableShell_Ssf2mm_HasNoSourceOrBatchFiles()
        {
            var shell = ShellWorkspaceService.FindShellFolder();
            if (shell == null)
                return;
            var ssf2mm = ShellWorkspaceService.Ssf2mmDirectory(shell);
            if (!Directory.Exists(ssf2mm))
                return;
            foreach (var dir in Directory.GetDirectories(ssf2mm))
            {
                var name = Path.GetFileName(dir);
                if (name is "packs" or "state")
                    continue;
                Directory.Exists(Path.Combine(dir, "src")).Should().BeFalse(name);
                File.Exists(Path.Combine(dir, "rebuild.bat")).Should().BeFalse(name);
                File.Exists(Path.Combine(dir, "asconfig.json")).Should().BeFalse(name);
                File.Exists(Path.Combine(dir, "info.json")).Should().BeFalse(name);
                File.Exists(Path.Combine(dir, "mod.json")).Should().BeTrue(name);
            }
        }

        private static string NewShell()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-builtin-" + Guid.NewGuid().ToString("N"));
            foreach (var id in BuiltInIds)
            {
                var pack = Path.Combine(root, "data", "ssf2mm", id);
                Directory.CreateDirectory(pack);
                var mod = new JObject
                {
                    ["id"] = id,
                    ["name"] = id,
                    ["lifetime"] = "boot",
                    ["entryClass"] = id == "menu_background" ? "MenuBackgroundMod" : "Mod",
                };
                if (id == "menu_background")
                {
                    mod["settings"] = new JArray(new JObject
                    {
                        ["id"] = "stageId",
                        ["label"] = "Stage",
                        ["type"] = "dropdown",
                        ["value"] = "battlefield22",
                        ["optionsSource"] = "stages"
                    });
                }
                File.WriteAllText(Path.Combine(pack, "mod.json"), mod.ToString());
                File.WriteAllBytes(Path.Combine(pack, "mod.swf"), new byte[] { 1 });

                var src = Path.Combine(root, "src", "mods", id);
                Directory.CreateDirectory(Path.Combine(src, "src"));
                File.WriteAllText(Path.Combine(src, "asconfig.json"),
                    "{\"compilerOptions\":{\"source-path\":[\"src\"],\"output\":\"../../../data/ssf2mm/" + id + "/mod.swf\"},\"mainClass\":\"Mod\"}");
            }
            Directory.CreateDirectory(Path.Combine(root, "data", "ssf2mm", "packs"));
            Directory.CreateDirectory(ShellWorkspaceService.StateDirectory(root));
            File.WriteAllText(Path.Combine(ShellWorkspaceService.StateDirectory(root), "boot.json"), "{}");
            return root;
        }
    }
}
