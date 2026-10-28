using FluentAssertions;
using SSF2ModManager.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class PressorServiceTests
    {
        [Fact]
        public void CompressDecompress_WithGarbageHeader_RoundTrips()
        {
            var svc = new PressorService();
            var swf = Encoding.ASCII.GetBytes("FWS" + new string('x', 200));
            // Make it look like SWF magic
            swf[0] = (byte)'F'; swf[1] = (byte)'W'; swf[2] = (byte)'S';

            var ssf = svc.Compress(swf, PressorMode.WithGarbageHeader);
            ssf.Should().NotBeEmpty();
            PressorService.LooksLikeSwf(ssf).Should().BeFalse();

            var outSwf = svc.Decompress(ssf, out var mode);
            mode.Should().Be(PressorMode.WithGarbageHeader);
            outSwf.Should().Equal(swf);

            using var input = new MemoryStream(ssf);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var inflated = new MemoryStream();
            zlib.CopyTo(inflated);
            var payload = inflated.ToArray();
            var encodedLength =
                (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3];
            encodedLength.Should().Be(swf.Length);
        }

        [Fact]
        public void CompressDecompress_ZlibOnly_RoundTrips()
        {
            var svc = new PressorService();
            var swf = new byte[128];
            swf[0] = (byte)'F'; swf[1] = (byte)'W'; swf[2] = (byte)'S';
            for (int i = 3; i < swf.Length; i++) swf[i] = (byte)(i % 251);

            var ssf = svc.Compress(swf, PressorMode.ZlibOnly);
            var outSwf = svc.Decompress(ssf, out var mode);
            mode.Should().Be(PressorMode.ZlibOnly);
            outSwf.Should().Equal(swf);
        }

        [Fact]
        public void Decompress_RawSwf_Passthrough()
        {
            var svc = new PressorService();
            var swf = new byte[] { (byte)'F', (byte)'W', (byte)'S', 1, 2, 3, 4 };
            var outSwf = svc.Decompress(swf, out var mode);
            mode.Should().Be(PressorMode.ZlibOnly);
            outSwf.Should().Equal(swf);
        }

        [Fact]
        public void TryParseGarbageHeader_RejectsBadLengths()
        {
            PressorService.TryParseGarbageHeader(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, out _).Should().BeFalse();
        }

        [Fact]
        public void Decompress_Unchecked_LeavesGarbageHeader()
        {
            var svc = new PressorService();
            var swf = FakeSwf("id\0bgm_test\0");
            var packed = svc.Compress(swf, PressorMode.WithGarbageHeader);

            var raw = svc.Decompress(packed, PressorMode.ZlibOnly);
            PressorService.LooksLikeSwf(raw).Should().BeFalse();
            raw.Should().NotEqual(swf);

            svc.Decompress(packed, PressorMode.WithGarbageHeader).Should().Equal(swf);
        }

        [Fact]
        public void ReadAssetSort_UsesIdThenCharactersThenCamera()
        {
            PressorService.ReadAssetSort(FakeSwf("id\0bgm_test\0")).Folder.Should().Be("music");
            PressorService.ReadAssetSort(FakeSwf("id\0mainmenu\0")).Folder.Should().Be("menu");
            PressorService.ReadAssetSort(FakeSwf("id\0classicmode\0")).Folder.Should().Be("mode");
            var assists = PressorService.ReadAssetSort(FakeSwf("id\0vk\0id\0assists\0guid$7908\0"));
            assists.Id.Should().Be("assists");
            assists.Folder.Should().Be("misc");
            PressorService.ReadAssetSort(FakeSwf("id\0sandbag\0characters\0")).Folder.Should().Be("misc");
            var misc = PressorService.ReadAssetSort(FakeSwf("id\0widgets\0"));
            misc.Folder.Should().Be("misc");
            misc.Id.Should().Be("widgets");
            PressorService.ReadAssetSort(FakeSwf("nope")).Id.Should().Be("DunderMuffin");
        }

        [Fact]
        public void CompressOutputPath_DashReturnsToParentFolder()
        {
            PressorService.CompressOutputPath(@"C:\game\data\music\tune-bgm_test.swf")
                .Should().Be(Path.Combine(@"C:\game\data", "tune.ssf"));
            PressorService.CompressOutputPath(@"C:\game\data\tune.swf")
                .Should().Be(@"C:\game\data\tune.ssf");
        }

        [Fact]
        public void PlacementRelative_UsesCategoryAndFileName()
        {
            var entry = new DatMappingEntry
            {
                Category = "music",
                FriendlyRelativePath = @"sound\bgm_mutecity.ssf",
                DatFileName = "DAT81.ssf"
            };
            PressorService.PlacementRelative(entry).Should().Be(Path.Combine("music", "bgm_mutecity.swf"));

            var extra = new DatMappingEntry
            {
                Category = "extra",
                FriendlyRelativePath = @"misc\sandbox.ssf",
                DatFileName = "DAT1.ssf"
            };
            PressorService.PlacementRelative(extra).Should().Be(Path.Combine("misc", "sandbox.swf"));
        }

        [Fact]
        public void TryReadMappings_ReadsMappingsJsonBinary()
        {
            var json = "{\"character\":{\"mario\":{\"file\":\"mario.ssf\",\"file_pub\":\"DAT14.ssf\"}}}";
            var swf = SwfWithBinary("Mappings_mappingsJSON", Encoding.UTF8.GetBytes(json));
            var entries = DatMappingService.TryReadMappings(swf);
            entries.Should().NotBeNull();
            entries!.Should().ContainSingle();
            entries[0].DatFileName.Should().Be("DAT14.ssf");
            entries[0].Category.Should().Be("character");
            PressorService.PlacementRelative(entries[0]).Should().Be(Path.Combine("character", "mario.swf"));
        }

        [Fact]
        public void TryReadMappings_ReadsBetaMappingsSwf()
        {
            var root = Environment.GetEnvironmentVariable("SSF2_MODDABLE_BUILD");
            if (string.IsNullOrWhiteSpace(root))
                return;
            var path = Path.Combine(root, "data", "misc", "DAT49-mappings.swf");
            if (!File.Exists(path))
                return;

            var swf = File.ReadAllBytes(path);
            var entries = DatMappingService.TryReadMappings(swf, root);
            entries.Should().NotBeNull();
            entries!.Count.Should().BeGreaterThan(50);
            entries.Should().Contain(e => e.DatFileName.Equals("DAT0.ssf", StringComparison.OrdinalIgnoreCase));
            entries.Should().Contain(e => e.Category == "music");
            entries.Should().Contain(e => e.Category == "character");
        }

        [Fact]
        public void TryReadManifest_AssistsIsDat57()
        {
            var root = Environment.GetEnvironmentVariable("SSF2_MODDABLE_BUILD");
            if (string.IsNullOrWhiteSpace(root))
                return;
            var main = Path.Combine(root, "SSF2.swf");
            if (!File.Exists(main))
                return;

            var entries = DatMappingService.TryReadManifest(File.ReadAllBytes(main), root);
            entries.Should().NotBeNull();
            var assists = entries!.Single(e =>
                e.DatFileName.Equals("DAT57.ssf", StringComparison.OrdinalIgnoreCase));
            assists.Category.Should().Be("misc");
            PressorService.PlacementRelative(assists).Should().Be(Path.Combine("misc", "assists.swf"));
        }

        [Fact]
        public async Task CreateModdableBuild_PlacesDatFromMappings()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-pressor-map-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "game");
            var docs = Path.Combine(root, "docs");
            Directory.CreateDirectory(Path.Combine(source, "data"));
            try
            {
                var json = "{\"character\":{\"mario\":{\"file\":\"mario.ssf\",\"file_pub\":\"DAT14.ssf\"}},\"misc\":{\"mappings\":{\"file\":\"mappings.ssf\",\"file_pub\":\"DAT49.ssf\"}}}";
                var mapSwf = SwfWithBinary("Mappings_mappingsJSON", Encoding.UTF8.GetBytes(json));
                var mario = FakeSwf("id\0mario\0characters\0");
                var svc = new PressorService();
                await File.WriteAllBytesAsync(Path.Combine(source, "data", "DAT14.ssf"), svc.Compress(mario, PressorMode.WithGarbageHeader));
                await File.WriteAllBytesAsync(Path.Combine(source, "data", "DAT49.ssf"), svc.Compress(mapSwf, PressorMode.WithGarbageHeader));

                var result = await svc.CreateModdableBuildAsync(source, useGarbageHeader: true, outputParent: docs);
                var written = Path.Combine(result.DataFolder, "character", "mario.swf");
                File.Exists(written).Should().BeTrue();
                (await File.ReadAllBytesAsync(written)).Should().Equal(mario);
                File.Exists(Path.Combine(result.DataFolder, "misc", "mappings.swf")).Should().BeTrue();
                File.Exists(Path.Combine(result.DataFolder, "DAT14.ssf")).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public async Task CreateModdableBuild_SortsDataSsfAndRemovesIt()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-pressor-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "game");
            var docs = Path.Combine(root, "docs");
            Directory.CreateDirectory(Path.Combine(source, "data"));
            try
            {
                var swf = FakeSwf("id\0bgm_test\0");
                var packed = new PressorService().Compress(swf, PressorMode.WithGarbageHeader);
                await File.WriteAllBytesAsync(Path.Combine(source, "data", "tune.ssf"), packed);
                await File.WriteAllBytesAsync(Path.Combine(source, "SSF2.exe"), new byte[] { 1 });

                var result = await new PressorService().CreateModdableBuildAsync(
                    source, useGarbageHeader: true, outputParent: docs);

                var written = Path.Combine(result.DataFolder, "music", "bgm_test.swf");
                File.Exists(written).Should().BeTrue();
                (await File.ReadAllBytesAsync(written)).Should().Equal(swf);
                File.Exists(Path.Combine(result.DataFolder, "tune.ssf")).Should().BeFalse();
                Directory.Exists(Path.Combine(result.DataFolder, "character")).Should().BeTrue();
                File.Exists(Path.Combine(result.OutputRoot, "SSF2.exe")).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static byte[] SwfWithBinary(string symbolName, byte[] data)
        {
            var bin = new MemoryStream();
            using (var bw = new BinaryWriter(bin, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write((ushort)1);
                bw.Write((uint)0);
                bw.Write(data);
            }
            var tags = new MemoryStream();
            WriteSwfTag(tags, 87, bin.ToArray());
            var sym = new MemoryStream();
            using (var bw = new BinaryWriter(sym, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write((ushort)1);
                bw.Write((ushort)1);
                bw.Write(Encoding.UTF8.GetBytes(symbolName));
                bw.Write((byte)0);
            }
            WriteSwfTag(tags, 76, sym.ToArray());
            WriteSwfTag(tags, 0, Array.Empty<byte>());
            var body = tags.ToArray();
            var swf = new byte[8 + 1 + 2 + 2 + body.Length];
            swf[0] = (byte)'F';
            swf[1] = (byte)'W';
            swf[2] = (byte)'S';
            swf[3] = 14;
            var length = swf.Length;
            swf[4] = (byte)length;
            swf[5] = (byte)(length >> 8);
            swf[6] = (byte)(length >> 16);
            swf[7] = (byte)(length >> 24);
            var at = 8;
            swf[at++] = 0;
            swf[at++] = 0x00;
            swf[at++] = 0x18;
            swf[at++] = 0x01;
            swf[at++] = 0x00;
            Buffer.BlockCopy(body, 0, swf, at, body.Length);
            return swf;
        }

        private static void WriteSwfTag(Stream stream, int type, byte[] data)
        {
            if (data.Length < 63)
            {
                var code = (type << 6) | data.Length;
                stream.WriteByte((byte)code);
                stream.WriteByte((byte)(code >> 8));
            }
            else
            {
                var code = (type << 6) | 0x3F;
                stream.WriteByte((byte)code);
                stream.WriteByte((byte)(code >> 8));
                var len = data.Length;
                stream.WriteByte((byte)len);
                stream.WriteByte((byte)(len >> 8));
                stream.WriteByte((byte)(len >> 16));
                stream.WriteByte((byte)(len >> 24));
            }
            stream.Write(data, 0, data.Length);
        }

        private static byte[] FakeSwf(string payload)
        {
            var text = Encoding.ASCII.GetBytes(payload);
            var swf = new byte[8 + text.Length];
            swf[0] = (byte)'F';
            swf[1] = (byte)'W';
            swf[2] = (byte)'S';
            Buffer.BlockCopy(text, 0, swf, 8, text.Length);
            return swf;
        }

        [Fact]
        public async Task Decompress_RealOfficialDat_ProducesFfdecReadableSwf()
        {
            var source = @"C:\Program Files (x86)\Super Smash Flash 2 Beta\data\DAT0.ssf";
            var ffdec = @"C:\Program Files (x86)\FFDec";
            if (!File.Exists(source) || !Directory.Exists(ffdec)) return;

            var service = new PressorService();
            var swf = service.Decompress(await File.ReadAllBytesAsync(source), out var mode);
            mode.Should().Be(PressorMode.WithGarbageHeader);
            PressorService.LooksLikeSwf(swf).Should().BeTrue();

            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-pressor-real-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var swfPath = Path.Combine(dir, "real.swf");
                var xmlPath = Path.Combine(dir, "real.xml");
                await File.WriteAllBytesAsync(swfPath, swf);
                await new JpexsCliService().SwfToXmlAsync(ffdec, swfPath, xmlPath);
                new FileInfo(xmlPath).Length.Should().BeGreaterThan(100);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public async Task Decompress_ProjectSandbagDat_UsesZlibOnlyFallback()
        {
            var source = Environment.GetEnvironmentVariable("SSF2_TEST_SANDBAG") ?? "";
            if (!File.Exists(source)) return;

            var swf = new PressorService().Decompress(await File.ReadAllBytesAsync(source), out var mode);
            mode.Should().Be(PressorMode.ZlibOnly);
            PressorService.LooksLikeSwf(swf).Should().BeTrue();
        }
    }

    public class CloneFolderTests
    {
        [Fact]
        public void SanitizeExpansionFolderName_StripsIllegalChars()
        {
            CharacterCloneService.SanitizeExpansionFolderName("SFC link")
                .Should().Be("SFC link");
            CharacterCloneService.SanitizeExpansionFolderName("bad:name*here")
                .Should().Be("badnamehere");
        }

        [Fact]
        public void ExtractCostumeBlock_RewritesKey()
        {
            var misc = @"
         _loc1_[""fox""] = new Array();
         _loc1_[""fox""].push({ ""team"":""red"" });
         _loc1_[""mario""] = new Array();
         _loc1_[""mario""].push({
            ""team"":""red"",
            ""paletteSwap"":{ ""colors"":[-1], ""replacements"":[-2] }
         });
         _loc1_[""luigi""] = new Array();
         _loc1_[""luigi""].push({ ""team"":""green"" });
";
            var block = CharacterCloneService.ExtractCostumeBlock(misc, "mario", "mario_clone");
            block.Should().NotBeNull();
            block!.Should().NotContain("new Array()");
            block.Should().StartWith("_loc1_[\"mario_clone\"].push");
            block.Should().NotContain("_loc1_[\"mario\"]");
            block.Should().NotContain("luigi");
            block.Should().NotContain("fox");
        }

        [Fact]
        public void ExtractCostumeBlock_IsCaseInsensitive()
        {
            var misc = @"_loc1_[""goku""] = new Array();
_loc1_[""goku""].push({ ""base"":true });
_loc1_[""mario""] = new Array();
";
            var block = CharacterCloneService.ExtractCostumeBlock(misc, "Goku", "SUPREMO_Goku");
            block.Should().NotBeNull();
            block.Should().Contain("_loc1_[\"SUPREMO_Goku\"]");
            block.Should().NotContain("_loc1_[\"goku\"]");
        }

        [Fact]
        public void CreateStubCostumes_HasFallbackPushWithoutArrayInit()
        {
            var stub = CharacterCloneService.CreateStubCostumes("wario");
            stub.Should().Contain("_loc1_[\"wario\"].push");
            stub.Should().NotContain("new Array()");
        }

        [Fact]
        public void CreatePlaceholderPng_IsValidMinimalPng()
        {
            var bytes = CharacterCloneService.CreatePlaceholderPngBytes();
            bytes.Length.Should().BeGreaterThan(8);
            bytes[0].Should().Be(0x89);
            bytes[1].Should().Be((byte)'P');
        }

        [Fact]
        public void DatMapping_ParseJson_ExtractsFilePub()
        {
            var json = @"{
  ""character"": {
    ""mario"": {
      ""file"": ""mario.ssf"",
      ""file_pub"": ""DAT1.ssf"",
      ""guid"": ""abc""
    }
  }
}";
            var entries = DatMappingService.ParseMappingsJson(json);
            entries.Should().ContainSingle();
            entries[0].DatFileName.Should().Be("DAT1.ssf");
            entries[0].FriendlyRelativePath.Should().Contain("mario.ssf");
        }

        [Fact]
        public void DatMapping_DecodesFlashUtilsEncryptedJson()
        {
            const string key = "b7d60849-fa99-4514-b41e-16693ce7acc0";
            const string json = "{\"character\":{\"mario\":{\"file\":\"mario.ssf\",\"file_pub\":\"DAT1.ssf\"}}}";
            var jsonBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            var encryptedChars = new char[jsonBase64.Length];
            for (var i = 0; i < jsonBase64.Length; i++)
            {
                var modulo = i % key.Length;
                var keyIndex = modulo == 0 ? key.Length - 1 : modulo - 1;
                encryptedChars[i] = (char)(jsonBase64[i] + key[keyIndex]);
            }
            var blob = Convert.ToBase64String(Encoding.UTF8.GetBytes(encryptedChars));

            DatMappingService.DecodeJsonBlob(blob, key).Should().Be(json);
        }

        [Fact]
        public async Task DatMapping_RealPublicInstall_ReadsSsf2ManifestAndMappingsDat()
        {
            var root = @"C:\Program Files (x86)\Super Smash Flash 2 Beta";
            if (!File.Exists(Path.Combine(root, "SSF2.swf"))) return;

            var entries = await new DatMappingService().LoadMappingsAsync(
                root,
                @"C:\Program Files (x86)\FFDec");
            entries.Count.Should().BeGreaterThan(100);
            entries.Should().Contain(e => e.FriendlyRelativePath.EndsWith(
                "mario.ssf", StringComparison.OrdinalIgnoreCase));
        }

    }

    public class GamePrepareHelperTests
    {
        [Fact]
        public void FormatConfigArgument_QuotesValue()
        {
            JpexsCliService.FormatConfigArgument("export.zoom=1.0,snapToPixels=true")
                .Should().Be("-config \"export.zoom=1.0,snapToPixels=true\"");
        }

        [Fact]
        public void FlashUtilsE_RoundTripsWithD()
        {
            const string key = "cedbe0e0-b9de-4e16-92cc-d32781a0bfe0";
            var inner = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"file\":\"mario.ssf\"}"));
            var outer = DatMappingService.FlashUtilsE(inner, key);
            DatMappingService.FlashUtilsD(outer, key).Should().Be(inner);
        }

        [Fact]
        public void RewriteFileExtensionsToSwf_ChangesFileOnly()
        {
            var json = @"{""character"":{""mario"":{""file"":""mario.ssf"",""file_pub"":""DAT1.ssf"",""guid"":""x""}}}";
            var rewritten = DatMappingService.RewriteFileExtensionsToSwf(json);
            rewritten.Should().Contain("mario.swf");
            rewritten.Should().Contain("DAT1.ssf");
            rewritten.Should().NotContain("\"file\":\"mario.ssf\"");
        }

        [Fact]
        public void EncodeDecodeJsonBlob_WithKey_RoundTrips()
        {
            const string key = "cedbe0e0-b9de-4e16-92cc-d32781a0bfe0";
            var json = @"{""character"":{""mario"":{""file"":""mario.swf"",""file_pub"":""DAT1.ssf""}}}";
            var blob = Encoding.UTF8.GetString(DatMappingService.EncodeJsonBlob(json, key));
            var decoded = DatMappingService.DecodeJsonBlob(blob, key);
            decoded.Should().Contain("mario.swf");
        }

        [Fact]
        public void PatchEncryptFiles_SetsFalse()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-patch-" + Guid.NewGuid().ToString("N"), "scripts");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "Config.as"),
                    "public static const encrypt_files:Boolean = true;");
                GamePrepareService.PatchEncryptFiles(dir).Should().BeTrue();
                File.ReadAllText(Path.Combine(dir, "Config.as")).Should().Contain("= false");
            }
            finally
            {
                try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { }
            }
        }

        [Fact]
        public void PatchResourceAllowDecompressed_SkipsRawSwfUncompress()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-patch2-" + Guid.NewGuid().ToString("N"), "util");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "Resource.as"), @"
            var c:ByteArray = null;
            var l:int = 0;
            var n:int = 0;
            var x:int = 0;
            var b:ByteArray = m_urlLoader.data as ByteArray;
            c = new ByteArray();
            try
            {
               b.uncompress(CompressionAlgorithm.ZLIB);
               l = b.readInt();
               n = b.readInt();
               for(x = 0; x < n; x++)
               {
                  b.readInt();
               }
               c.writeBytes(b,b.position,l);
               b = c;
               c = null;
            }
            catch(err:*)
            {
               if(m_id !== ""menu_news"")
               {
                  return;
               }
            }
            m_loader.loadBytes(b,m_loaderContext);
");
                GamePrepareService.PatchResourceAllowDecompressed(Path.GetDirectoryName(dir)!)
                    .Should().BeTrue();
                var text = File.ReadAllText(Path.Combine(dir, "Resource.as"));
                text.Should().Contain("SSF2MM_RAW_SWF");
                text.Should().Contain("var rawCopy:ByteArray = null");
                text.Should().Contain("var isRawSwf:Boolean = false");
                text.Should().Contain("isRawSwf =");
                text.Should().NotContain("menu_news");
                // No leftover braces after the marker (would break FFDec import).
                System.Text.RegularExpressions.Regex.IsMatch(
                    text, @"SSF2MM_RAW_SWF\s*\}").Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { }
            }
        }

        [Fact]
        public void PatchMainEncrypted_ForcesFalse()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-patch-main-" + Guid.NewGuid().ToString("N"), "ssf2");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "Main.as"),
                    "public static const ENCRYPTED:Boolean = Config.encrypt_files;");
                GamePrepareService.PatchMainEncrypted(Path.GetDirectoryName(dir)!)
                    .Should().BeTrue();
                File.ReadAllText(Path.Combine(dir, "Main.as"))
                    .Should().Contain("ENCRYPTED:Boolean = false");
            }
            finally
            {
                try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { }
            }
        }

        [Fact]
        public void FindScriptFile_PrefersMatchingParentFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-findscript-" + Guid.NewGuid().ToString("N"));
            var ssf2 = Path.Combine(root, "scripts", "com", "mcleodgaming", "ssf2");
            var other = Path.Combine(root, "scripts", "other");
            Directory.CreateDirectory(ssf2);
            Directory.CreateDirectory(other);
            try
            {
                File.WriteAllText(Path.Combine(other, "Config.as"), "wrong");
                var preferred = Path.Combine(ssf2, "Config.as");
                File.WriteAllText(preferred, "right");

                GamePrepareService.FindScriptFile(Path.Combine(root, "scripts"), "Config.as", "ssf2")
                    .Should().Be(preferred);
                GamePrepareService.FindScriptFile(Path.Combine(root, "scripts"), "Missing.as")
                    .Should().BeNull();
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void ScriptNameConstants_MatchFfdecReplaceTargets()
        {
            GamePrepareService.ScriptNameConfig.Should().Be("com.mcleodgaming.ssf2.Config");
            GamePrepareService.ScriptNameMain.Should().Be("com.mcleodgaming.ssf2.Main");
            GamePrepareService.ScriptNameResource.Should().Be("com.mcleodgaming.ssf2.util.Resource");
        }

        [Fact]
        public void Decompress_StripsNakedGarbageHeaderWithoutZlib()
        {
            var svc = new PressorService();
            var swf = new byte[64];
            swf[0] = (byte)'F'; swf[1] = (byte)'W'; swf[2] = (byte)'S';
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                PressorService.WriteInt32BigEndian(bw, swf.Length);
                PressorService.WriteInt32BigEndian(bw, 0);
                bw.Write(swf);
            }
            var naked = ms.ToArray();
            var outSwf = svc.Decompress(naked, out var mode);
            mode.Should().Be(PressorMode.WithGarbageHeader);
            outSwf.Should().Equal(swf);
        }

        [Fact]
        public async Task ConvertToDatAlongside_KeepsSourceSwf()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-alongside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var swfPath = Path.Combine(dir, "mario.swf");
                var datPath = Path.Combine(dir, "DAT99.ssf");
                var swf = new byte[64];
                swf[0] = (byte)'F'; swf[1] = (byte)'W'; swf[2] = (byte)'S';
                await File.WriteAllBytesAsync(swfPath, swf);

                var result = await new PressorService().ConvertToDatAlongsideAsync(swfPath, datPath);
                result.Success.Should().BeTrue();
                File.Exists(swfPath).Should().BeTrue();
                File.Exists(datPath).Should().BeTrue();
                PressorService.LooksLikeSwf(await File.ReadAllBytesAsync(datPath)).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

    }
}
