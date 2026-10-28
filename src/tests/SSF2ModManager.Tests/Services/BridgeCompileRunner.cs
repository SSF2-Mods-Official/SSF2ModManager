using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SSF2ModManager.Services;
using Xunit;
using Xunit.Abstractions;

namespace SSF2ModManager.Tests.Services
{
    public class BridgeCompileRunner
    {
        private readonly ITestOutputHelper _out;
        public BridgeCompileRunner(ITestOutputHelper output) => _out = output;

        [Fact]
        public async Task CompileManagerBridge()
        {
            var shell = Environment.GetEnvironmentVariable("SSF2_SHELL");
            if (Environment.GetEnvironmentVariable("SSF2_COMPILE_BRIDGE") != "1" || string.IsNullOrWhiteSpace(shell))
                return;
            foreach (var id in new[] { "manager_bridge", "palette_maker" })
            {
                var target = FolderCompileService.ListCompileTargets(shell).Single(t => t.Id == id);
                var swf = await FolderCompileService.CompileAsync(shell, target, new Progress<string>(l => _out.WriteLine(l)));
                _out.WriteLine("output=" + swf + " at=" + File.GetLastWriteTime(swf));
                Assert.True(File.Exists(swf));
            }
        }
    }
}
