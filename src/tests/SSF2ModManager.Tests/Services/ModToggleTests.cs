using SSF2ModManager.Models;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ModToggleTests
    {
        [Fact]
        public void ToggleMod_ThrowsWhenFolderMissingOnEnable()
        {
            var api = new GameBananaApiClient();
            var mgr = new ModManagerService(api);
            var mod = new InstalledMod
            {
                Id = "test",
                Name = "Missing Mod",
                Category = "Maps",
                FolderName = "Missing_Mod_999",
                TargetVersion = "Beta",
                Enabled = false,
                GameBananaId = 363487
            };
            mgr.InstalledMods.Add(mod);

            var ex = Assert.Throws<ModFolderNotFoundException>(() => mgr.ToggleMod(mod));
            Assert.False(mod.Enabled);
            Assert.Contains("Missing_Mod_999", ex.ModPath);
        }
    }
}
