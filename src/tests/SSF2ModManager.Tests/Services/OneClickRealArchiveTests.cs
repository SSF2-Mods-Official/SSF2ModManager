using System;
using System.IO;
using System.Linq;
using SSF2ModManager.Services;
using Xunit;
using Xunit.Abstractions;

namespace SSF2ModManager.Tests.Services
{
    /// <summary>Runs the classifier on real GameBanana downloads when they are present in %TEMP%\gbprobe.</summary>
    public class OneClickRealArchiveTests
    {
        private readonly ITestOutputHelper _out;
        public OneClickRealArchiveTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void FountainOfDreams_149685()
        {
            var path = Path.Combine(Path.GetTempPath(), "gbprobe", "149685", "fod.rar");
            if (!File.Exists(path))
                return;
            var receipt = OneClickReceive.ClassifyArchive(path);
            try
            {
                foreach (var f in Directory.GetFiles(receipt.ExtractedRoot, "*.ssf", SearchOption.AllDirectories))
                {
                    var raw = File.ReadAllBytes(f);
                    _out.WriteLine(Path.GetFileName(f) + " doc=" + SwfPackage.DocumentClass(raw) + " registers="
                        + string.Join(", ", SwfPackage.RegisterCalls(raw).Select(r => r.Key + "=" + r.Value)));
                }
                Assert.Equal(new[] { "Song bgm_bloodytears", "Stage draculascastle" },
                    receipt.Items.Select(i => i.Kind + " " + i.Id).OrderBy(s => s).ToArray());
            }
            finally
            {
                receipt.Cleanup();
            }
        }
    }
}
