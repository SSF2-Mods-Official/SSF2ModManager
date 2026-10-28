using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Writes and checks an .ssfe package. The author key stays in the user profile.
    /// The manager verifies the signature and does not embed a second copy of the key.
    /// </summary>
    public static class SsfeSeal
    {
        public static string? KeyFileOverride { get; set; }

        public static string KeyPath =>
            KeyFileOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SSF2ModManager",
            "ssfe-author.xml");

        public static RSA LoadOrCreateKey()
        {
            var rsa = RSA.Create(2048);
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            if (File.Exists(KeyPath))
            {
                rsa.FromXmlString(File.ReadAllText(KeyPath));
                return rsa;
            }
            File.WriteAllText(KeyPath, rsa.ToXmlString(true));
            return rsa;
        }

        public static string Thumbprint(RSA rsa) =>
            Convert.ToHexString(ThumbBytes(rsa)).ToLowerInvariant();

        public static byte[] ThumbBytes(RSA rsa) =>
            SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());

        public static void Package(string folder, string ssfePath)
        {
            var review = SsfeReview.Inspect(folder);
            if (!review.Ok)
                throw new InvalidOperationException(string.Join(Environment.NewLine, review.Errors));

            using var rsa = LoadOrCreateKey();
            var thumb = Thumbprint(rsa);
            var thumbBytes = ThumbBytes(rsa);
            var infoPath = Path.Combine(folder, "info.json");
            var info = JObject.Parse(File.ReadAllText(infoPath));
            var existing = info["ssfeAuthor"]?.ToString();
            if (!string.IsNullOrWhiteSpace(existing)
                && !existing.Equals(thumb, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This mod was sealed by a different author.");
            foreach (var file in review.Files)
            {
                if (!file.RelativePath.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rc = SsfeNative.Claim(file.FullPath, thumbBytes);
                if (rc == 1)
                    throw new InvalidOperationException("This mod was sealed by a different author.");
                if (rc != 0)
                    throw new InvalidOperationException("This SWF could not be marked for its author.");
            }
            info["ssfeAuthor"] = thumb;
            File.WriteAllText(infoPath, info.ToString(Newtonsoft.Json.Formatting.Indented));
            review = SsfeReview.Inspect(folder);
            if (!review.Ok)
                throw new InvalidOperationException(string.Join(Environment.NewLine, review.Errors));

            var files = new JArray();
            foreach (var file in review.Files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(new JObject
                {
                    ["path"] = file.RelativePath,
                    ["sha256"] = file.Sha256
                });
            }
            var seal = new JObject
            {
                ["v"] = 1,
                ["author"] = thumb,
                ["publicKey"] = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()),
                ["files"] = files
            };
            var payload = Canonical(seal);
            seal["sig"] = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            if (File.Exists(ssfePath))
                File.Delete(ssfePath);
            using var zip = ZipFile.Open(ssfePath, ZipArchiveMode.Create);
            foreach (var file in review.Files)
                zip.CreateEntryFromFile(file.FullPath, file.RelativePath.Replace('\\', '/'), CompressionLevel.Optimal);
            var entry = zip.CreateEntry("ssfe-seal.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(seal.ToString(Newtonsoft.Json.Formatting.None));
        }

        public static bool Accept(string ssfePath)
        {
            using var zip = ZipFile.OpenRead(ssfePath);
            var sealEntry = zip.GetEntry("ssfe-seal.json");
            if (sealEntry == null)
                return false;
            JObject seal;
            using (var reader = new StreamReader(sealEntry.Open()))
                seal = JObject.Parse(reader.ReadToEnd());
            var sig = seal["sig"]?.ToString();
            var publicKey = seal["publicKey"]?.ToString();
            if (string.IsNullOrWhiteSpace(sig) || string.IsNullOrWhiteSpace(publicKey))
                return false;
            seal.Remove("sig");
            var payload = Canonical(seal);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!rsa.VerifyData(payload, Convert.FromBase64String(sig), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return false;
            if (seal["files"] is not JArray files)
                return false;
            foreach (var item in files)
            {
                var rel = item?["path"]?.ToString()?.Replace('\\', '/');
                var expect = item?["sha256"]?.ToString();
                if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(expect))
                    return false;
                var packed = zip.GetEntry(rel);
                if (packed == null)
                    return false;
                using var stream = packed.Open();
                using var mem = new MemoryStream();
                stream.CopyTo(mem);
                var hash = Convert.ToHexString(SHA256.HashData(mem.ToArray())).ToLowerInvariant();
                if (!hash.Equals(expect, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (rel.EndsWith(".swf", StringComparison.OrdinalIgnoreCase)
                    && SsfeNative.Match(mem.ToArray(), ThumbBytes(rsa)) != 0)
                    return false;
            }
            return true;
        }

        private static byte[] Canonical(JObject seal)
        {
            var lines = new StringBuilder();
            lines.Append(seal["v"]).Append('\n').Append(seal["author"]).Append('\n');
            if (seal["files"] is JArray files)
            {
                foreach (var item in files)
                    lines.Append(item?["path"]).Append(' ').Append(item?["sha256"]).Append('\n');
            }
            return Encoding.UTF8.GetBytes(lines.ToString());
        }
    }
}
