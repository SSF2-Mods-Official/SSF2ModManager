using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class OptionalPackTests
    {
        private static readonly string[] OptionalIds =
        {
            "window_scale"
        };

        [Fact]
        public void ProductName_SaysBetaWorkInProgress()
        {
            AppInfo.ProductName.Should().Be("SSF2 Mod Manager BETA *WORK IN PROGRESS*");
        }

        [Fact]
        public void ExpansionsMods_AreToggleable_Ssf2mm_StaysEssential()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-opt-" + Guid.NewGuid().ToString("N"));
            try
            {
                WritePack(root, Path.Combine("expansions", "mods", "window_scale"));
                WritePack(root, Path.Combine("data", "ssf2mm", "menu_editor"));

                var sections = ExpansionModsService.Load(root);
                var mods = sections.Single(s => s.Section == "Mods").Mods;
                var essentials = sections.Single(s => s.Section == "Essentials").Mods;

                mods.Should().Contain(m => m.FolderName == "window_scale" && m.CanToggleEnabled);
                essentials.Should().Contain(m => m.FolderName == "menu_editor" && !m.CanToggleEnabled);
                essentials.Should().NotContain(m => m.FolderName == "window_scale");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void OptionalPacks_AreNotInSsf2mm()
        {
            var shell = ShellWorkspaceService.FindShellFolder();
            if (shell == null)
                return;
            foreach (var id in OptionalIds)
                Directory.Exists(Path.Combine(shell, "data", "ssf2mm", id)).Should().BeFalse(id);
        }

        private static void WritePack(string root, string relative)
        {
            var dir = Path.Combine(root, relative);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "mod.json"), "{\"id\":\"pack\",\"name\":\"Pack\"}");
        }
    }
}
