using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Asks ssfe_seal.dll to compare a SWF's author mark with a user key.
    /// The DLL is the only reader of that mark.
    /// </summary>
    public static class SsfeNative
    {
        /// <summary>0 matches or was just written. 1 another user. 2 unreadable. 3 no room.</summary>
        public static int Claim(string path, byte[] thumb)
        {
            var file = File.ReadAllBytes(path);
            if (!Open(file, out var plain, out var compressed))
                return 2;
            var cap = plain.Length + 256;
            var buffer = new byte[cap];
            Buffer.BlockCopy(plain, 0, buffer, 0, plain.Length);
            var len = plain.Length;
            var rc = ssfe_claim_bytes(buffer, ref len, cap, thumb);
            if (rc != 0)
                return rc;
            var marked = new byte[len];
            Buffer.BlockCopy(buffer, 0, marked, 0, len);
            File.WriteAllBytes(path, Save(marked, compressed));
            return 0;
        }

        /// <summary>0 the mark matches this user's key.</summary>
        public static int Match(byte[] file, byte[] thumb)
        {
            if (!Open(file, out var plain, out _))
                return 2;
            return ssfe_match_bytes(plain, plain.Length, thumb);
        }

        private static bool Open(byte[] file, out byte[] plain, out bool compressed)
        {
            plain = file;
            compressed = false;
            if (file.Length < 8)
                return false;
            var kind = System.Text.Encoding.ASCII.GetString(file, 0, 3);
            if (kind == "FWS")
                return true;
            if (kind != "CWS")
                return false;
            try
            {
                using var input = new MemoryStream(file, 8, file.Length - 8);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
                using var body = new MemoryStream();
                zlib.CopyTo(body);
                plain = new byte[8 + body.Length];
                Buffer.BlockCopy(file, 0, plain, 0, 8);
                plain[0] = (byte)'F';
                body.ToArray().CopyTo(plain, 8);
                compressed = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Save(byte[] plain, bool compressed)
        {
            if (!compressed)
                return plain;
            plain[0] = (byte)'C';
            using var body = new MemoryStream();
            using (var zlib = new ZLibStream(body, CompressionLevel.SmallestSize, true))
                zlib.Write(plain, 8, plain.Length - 8);
            var packed = body.ToArray();
            var result = new byte[8 + packed.Length];
            Buffer.BlockCopy(plain, 0, result, 0, 8);
            packed.CopyTo(result, 8);
            return result;
        }

        [DllImport("ssfe_seal.dll", EntryPoint = "#1", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ssfe_claim_bytes(byte[] data, ref int len, int cap, byte[] thumb);

        [DllImport("ssfe_seal.dll", EntryPoint = "#2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ssfe_match_bytes(byte[] data, int len, byte[] thumb);

        static SsfeNative()
        {
            NativeLibrary.SetDllImportResolver(typeof(SsfeNative).Assembly, Resolve);
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
        {
            if (!name.Contains("ssfe_seal", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;
            var file = Path.Combine(AppContext.BaseDirectory, "ssfe_seal.dll");
            return File.Exists(file) ? NativeLibrary.Load(file) : IntPtr.Zero;
        }
    }
}
