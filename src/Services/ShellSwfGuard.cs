using System;
using System.IO;
using System.Security.Cryptography;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// The shell booter is <c>SSF2.swf</c>. A different file does not start.
    /// </summary>
    public static class ShellSwfGuard
    {
        /// <summary>sha256 of the shell booter that checks <c>engine2.swf</c> and <c>mod.swf</c>.</summary>
        public const string BooterSha256 = "08105db73699196c4e484cfdb33280fe21230c371209c03cd7d1e75da6dbee88";

        /// <summary>Null when the hash is the known booter. Otherwise a short reason.</summary>
        public static string? Compare(string? actualHex)
        {
            if (string.IsNullOrWhiteSpace(actualHex))
                return "missing";
            if (!string.Equals(actualHex.Trim(), BooterSha256, StringComparison.OrdinalIgnoreCase))
                return "modified";
            return null;
        }

        public static void Ensure(string installRoot)
        {
            var report = BuildTrust.Check(installRoot);
            if (!report.BooterOk)
                throw new InvalidOperationException("This shell's SSF2.swf was modified. The manager will not start it.");
            if (report.DoNotRun.Count > 0)
                throw new InvalidOperationException(
                    "This build should not be run. An expansion mod failed the security check: "
                    + string.Join("; ", report.DoNotRun));
        }
    }
}
