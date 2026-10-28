using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Standalone CMD debug session: listen for the SSF2API socket bridge, print lines,
    /// optionally launch the game, and mirror output to <c>ssf2mm-debug.log</c>.
    /// </summary>
    public static class GameDebugCmdHost
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleTitle(string lpConsoleTitle);

        public static int Run(string installRoot, bool launchGame)
        {
            installRoot = installRoot?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                TryAllocConsole();
                Console.Error.WriteLine("Install folder not found: " + installRoot);
                return 1;
            }

            TryAllocConsole();
            try { SetConsoleTitle("SSF2 Debug Console"); } catch { /* ignore */ }

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("SSF2 Mod Manager — Game Debug Console");
            Console.WriteLine("Install: " + installRoot);
            Console.WriteLine();

            var swf = GamePrepareService.FindMainSwf(installRoot);
            if (swf == null)
            {
                Console.Error.WriteLine("SSF2.swf not found in install folder.");
                PauseExit();
                return 1;
            }

            var patch = GamePrepareService.GetDebugConsolePatchStatus(installRoot);
            Console.WriteLine(patch switch
            {
                GamePrepareService.DebugConsolePatchStatus.Full =>
                    "Engine patch: installed (SSF2API.print + uncaught errors).",
                GamePrepareService.DebugConsolePatchStatus.PrintOnly =>
                    "Engine patch: prints only — open Game Debug Console → Install/upgrade patch for UNCAUGHT lines.",
                _ =>
                    "Engine patch: NOT installed. Pressor → Game Debug Console → Install / upgrade patch."
            });
            Console.WriteLine(
                "Note: stock SSF2 has Main.DEBUG compiled OFF (DEBUGCONST=false, FORCEDEBUGOFF=true).");
            Console.WriteLine(
                "Socket lines only appear when the game calls SSF2API.print — not for silent native crashes.");

            var logPath = Path.Combine(installRoot, GamePrepareService.DebugLogFileName);
            Console.WriteLine("Log file: " + logPath);
            Console.WriteLine($"Listening on 127.0.0.1:{GamePrepareService.DebugConsolePort}");
            Console.WriteLine("Launch the game now (or it will auto-launch). Close this window when done.");
            Console.WriteLine(new string('-', 60));

            void LogBoth(string text)
            {
                Console.WriteLine(text);
                try { File.AppendAllText(logPath, text + "\r\n"); } catch { /* ignore */ }
            }

            try
            {
                File.WriteAllText(logPath, $"# SSF2 debug log {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n");
                File.AppendAllText(logPath, $"# patch={patch}\r\n");
            }
            catch (Exception ex) { Console.WriteLine("Warning: could not create log file: " + ex.Message); }

            using var host = new GameDebugConsoleHost();
            host.LineReceived += line =>
            {
                var stamp = DateTime.Now.ToString("HH:mm:ss");
                LogBoth($"[{stamp}] {line}");
            };
            host.StatusChanged += status => LogBoth("-- " + status);

            try
            {
                host.Start();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to listen: " + ex.Message);
                Console.Error.WriteLine("Close any other Game Debug Console / leftover listener on port " +
                                        GamePrepareService.DebugConsolePort + " and try again.");
                PauseExit();
                return 2;
            }

            Process? game = null;
            if (launchGame)
            {
                var exe = FindGameExecutable(installRoot);
                if (exe == null)
                {
                    Console.WriteLine("Could not find SSF2.exe — start the game manually.");
                }
                else
                {
                    Console.WriteLine("Starting: " + exe);
                    try
                    {
                        game = Process.Start(new ProcessStartInfo(exe)
                        {
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(exe) ?? installRoot
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Failed to start game: " + ex.Message);
                    }
                }
            }

            if (game != null)
            {
                try
                {
                    game.WaitForExit();
                    Console.WriteLine();
                    Console.WriteLine($"Game exited (code {game.ExitCode}).");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("WaitForExit failed: " + ex.Message);
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Press Enter to stop listening…");
                try { Console.ReadLine(); } catch { Thread.Sleep(Timeout.Infinite); }
            }

            host.Stop();
            Console.WriteLine("Stopped. Log saved to: " + logPath);
            if (game != null)
                PauseExit();
            return 0;
        }

        /// <summary>Spawns this app; child calls AllocConsole for its own debug CMD window.</summary>
        public static void StartDetached(string installRoot, bool launchGame = true)
        {
            var exe = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule?.FileName
                      ?? throw new InvalidOperationException("Could not resolve Mod Manager executable path.");
            var args = launchGame
                ? $"--debug-launch=\"{installRoot}\""
                : $"--debug-listen=\"{installRoot}\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = installRoot
            });
        }

        public static string? FindGameExecutable(string installRoot)
        {
            string[] names = { "Super Smash Flash 2 Beta.exe", "SSF2.exe", "Super Smash Flash 2.exe", "ssf2.exe" };
            foreach (var name in names)
            {
                var direct = Path.Combine(installRoot, name);
                if (File.Exists(direct)) return direct;
            }
            foreach (var name in names)
            {
                try
                {
                    var found = Directory.GetFiles(installRoot, name, SearchOption.AllDirectories);
                    if (found.Length > 0) return found[0];
                }
                catch { /* ignore */ }
            }
            return ShellWorkspaceService.FindFallbackExe(installRoot);
        }

        private static void TryAllocConsole()
        {
            try
            {
                if (!AttachConsole(AttachParentProcess))
                    AllocConsole();
            }
            catch
            {
                try { AllocConsole(); } catch { /* ignore */ }
            }
            try
            {
                // Re-bind System.Console after AllocConsole for WinExe hosts.
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(stdout);
                var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                Console.SetError(stderr);
                Console.SetIn(new StreamReader(Console.OpenStandardInput()));
            }
            catch { /* ignore */ }
        }

        private static void PauseExit()
        {
            Console.WriteLine();
            Console.WriteLine("Press Enter to close…");
            try { Console.ReadLine(); } catch { /* ignore */ }
            try { FreeConsole(); } catch { /* ignore */ }
        }
    }
}
