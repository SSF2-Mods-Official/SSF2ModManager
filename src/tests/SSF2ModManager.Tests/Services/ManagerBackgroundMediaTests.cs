using System.IO;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ManagerBackgroundMediaTests
    {
        [Theory]
        [InlineData("shot.png", ManagerBackgroundKind.Still)]
        [InlineData("loop.GIF", ManagerBackgroundKind.Gif)]
        [InlineData("clip.mp4", ManagerBackgroundKind.Video)]
        [InlineData("clip.webm", ManagerBackgroundKind.Video)]
        [InlineData("photo.jpg", ManagerBackgroundKind.Still)]
        public void KindFor_Extension(string name, ManagerBackgroundKind kind)
        {
            Assert.Equal(kind, ManagerBackgroundMedia.KindFor(name));
        }

        [Fact]
        public void Install_CopiesAndReplacesPrevious()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-bg-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var png = Path.Combine(dir, "a.png");
                var mp4 = Path.Combine(dir, "b.mp4");
                File.WriteAllBytes(png, new byte[] { 1, 2, 3 });
                File.WriteAllBytes(mp4, new byte[] { 4, 5 });
                var first = ManagerBackgroundMedia.Install(png, dir);
                Assert.True(File.Exists(first));
                var second = ManagerBackgroundMedia.Install(mp4, dir);
                Assert.Equal(".mp4", Path.GetExtension(second));
                Assert.False(File.Exists(first));
                Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(second));
                var again = Path.Combine(dir, "c.png");
                File.WriteAllBytes(again, new byte[] { 7, 7, 7 });
                var third = ManagerBackgroundMedia.Install(again, dir);
                Assert.NotEqual(second, third);
                Assert.False(File.Exists(second));
                Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(third));
                Assert.Single(Directory.GetFiles(dir, "manager-background*"));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
