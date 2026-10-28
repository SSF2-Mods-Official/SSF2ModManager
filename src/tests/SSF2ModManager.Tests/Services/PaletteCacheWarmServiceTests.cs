using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class PaletteCacheWarmServiceTests
    {
        [Fact]
        public async Task Warm_WithoutGameExe_LeavesThePackAlone()
        {
            var shell = Directory.CreateTempSubdirectory("ssf2mm-palette-shell-").FullName;
            var pack = Path.Combine(shell, "expansions", "character", "Goku");
            Directory.CreateDirectory(pack);
            File.WriteAllText(Path.Combine(pack, "info.json"), "{\"id\":\"goku\"}");
            File.WriteAllText(Path.Combine(pack, "keep.txt"), "pack");
            try
            {
                Assert.Equal("goku", PaletteCacheWarmService.CharacterId(pack));
                var warmed = await PaletteCacheWarmService.WarmAsync(shell, "goku", null, CancellationToken.None);
                Assert.False(warmed);
                Assert.False(PaletteCacheWarmService.IsDone(shell));
                Assert.False(File.Exists(PaletteCacheWarmService.SignalPath(shell)));
                Assert.Equal("pack", File.ReadAllText(Path.Combine(pack, "keep.txt")));
                Assert.Equal(2, Directory.GetFiles(pack).Length);
            }
            finally
            {
                Directory.Delete(shell, true);
            }
        }
    }
}
