using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class FolderCompileListTests
    {
        [Fact]
        public void ListDataFolders_IncludesSsfFolders()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-folders-" + Guid.NewGuid().ToString("N"));
            try
            {
                Touch(root, Path.Combine("data", "engine", "engine2.swf"));
                Touch(root, Path.Combine("data", "ssf2mm", "boot.swf"));
                Touch(root, Path.Combine("data", "sound", "bgm_test.ssf"));
                Touch(root, Path.Combine("data", "character", "mario.ssf"));
                Touch(root, Path.Combine("data", "stage", "fd.ssf"));

                FolderCompileService.ListDataFolders(root).Should().Equal(
                    "character", "engine", "sound", "ssf2mm", "stage");

                var stage = FolderCompileService.ListSwfsInFolder(root, "stage");
                stage.Should().ContainSingle(x => x.Relative == "stage/fd.ssf");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private static void Touch(string root, string relative)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 1 });
        }
    }
}
