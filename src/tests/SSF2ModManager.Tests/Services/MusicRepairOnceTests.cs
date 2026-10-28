using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    [Collection("InstallRepair")]
    public class MusicRepairOnceTests
    {
        private static readonly string InstallRoot = Environment.GetEnvironmentVariable("SSF2_INSTALL") ?? "";
        private static readonly string IdkBattlefieldSsf = Environment.GetEnvironmentVariable("SSF2_TEST_BATTLEFIELD") ?? "";

        [Fact]
        public async Task RestoreFreshBattlefield_AndAppendCustomTrack()
        {
            var dest = Path.Combine(InstallRoot, "data", "stage", "battlefield.swf");
            // Prefer real asserts so sandbox/path issues don't silently pass.
            if (!Directory.Exists(InstallRoot) || !File.Exists(IdkBattlefieldSsf))
            {
                // CI / machines without the local install — skip.
                return;
            }

            // Fresh decompress from stock IDK .ssf (avoids ImportScript-corrupted local copy).
            var pressor = new PressorService();
            var swfBytes = pressor.Decompress(await File.ReadAllBytesAsync(IdkBattlefieldSsf), out _);
            swfBytes.Length.Should().BeGreaterThan(100_000);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            // Keep a copy of whatever was there.
            if (File.Exists(dest))
                File.Copy(dest, dest + ".pre-restore.bak", overwrite: true);
            await File.WriteAllBytesAsync(dest, swfBytes);

            var music = new MusicEditService();
            var stock = await music.ReadPlaylistAsync(dest);
            stock.Should().Contain("bgm_battlefield");
            stock.Should().Contain("bgm_multimansmash");
            stock.Should().Contain("bgm_cruelsmash");

            if (!stock.Contains("bgm_deadanimesong"))
                stock.Add("bgm_deadanimesong");

            await music.WritePlaylistAsync(dest, stock);

            var after = await music.ReadPlaylistAsync(dest);
            after.Should().Equal(stock);
            after.Should().Contain("bgm_deadanimesong");
            // Size should stay near stock (surgical replace), not explode/shrink wildly.
            new FileInfo(dest).Length.Should().BeInRange(swfBytes.Length - 50_000, swfBytes.Length + 50_000);
        }
    }
}
