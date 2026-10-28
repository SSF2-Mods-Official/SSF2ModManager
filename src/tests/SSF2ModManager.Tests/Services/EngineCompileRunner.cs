using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SSF2ModManager.Services;
using Xunit;
using Xunit.Abstractions;

namespace SSF2ModManager.Tests.Services
{
    /// <summary>Compiles engine2 with Compile folder's code path. Runs only with SSF2_COMPILE_ENGINE=1 and SSF2_SHELL set.</summary>
    public class EngineCompileRunner
    {
        private readonly ITestOutputHelper _out;
        public EngineCompileRunner(ITestOutputHelper output) => _out = output;

        [Fact]
        public async Task CompileEngine2()
        {
            var shell = Environment.GetEnvironmentVariable("SSF2_SHELL");
            if (Environment.GetEnvironmentVariable("SSF2_COMPILE_ENGINE") != "1" || string.IsNullOrWhiteSpace(shell) || !Directory.Exists(shell))
                return;
            var target = FolderCompileService.ListCompileTargets(shell).Single(t => t.Engine);
            var log = new Progress<string>(line => _out.WriteLine(line));
            var swf = await FolderCompileService.CompileAsync(shell, target, log);
            _out.WriteLine("output=" + swf + " at=" + File.GetLastWriteTime(swf));
            Assert.True(File.Exists(swf));
            Assert.True(DateTime.Now - File.GetLastWriteTime(swf) < TimeSpan.FromMinutes(15));
        }
    }
}
