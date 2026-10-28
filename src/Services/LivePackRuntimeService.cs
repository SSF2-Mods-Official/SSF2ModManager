using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Inject-once engine patches so pack <c>assets/</c> + <c>costumes.json</c> load live at runtime.
    /// Remaps playableId UI linkages to Prepare-built <c>mm_c_###</c> pool shells.
    /// </summary>
    public sealed class LivePackRuntimeService
    {
        public const string LiveApiMarker = "SSF2MM_LIVE_API_V1";
        public const string ScriptNameSsf2Api = "com.mcleodgaming.ssf2.api.SSF2API";

        private readonly JpexsCliService _jpexs;

        public LivePackRuntimeService(JpexsCliService? jpexs = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
        }

        public async Task EnsureLivePackApiAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var mainSwf = GamePrepareService.FindMainSwf(installRoot)
                ?? throw new FileNotFoundException("Could not find SSF2.swf in the install root.");
            // Durable check: helper name survives FFDec comment stripping.
            if (GamePrepareService.SwfContainsAscii(mainSwf, "ssf2mmFindPackForUiLink"))
            {
                progress?.Report("Live pack API patch already present.");
                return;
            }

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-live-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report("Exporting SSF2API for live pack loaders…");
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.RunFfdecAsync(
                    jpexsPath ?? "",
                    $"-selectclass {ScriptNameSsf2Api} -export script \"{exportDir}\" \"{mainSwf}\"",
                    cancellationToken);
                var apiAs = Directory.GetFiles(exportDir, "SSF2API.as", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new FileNotFoundException("SSF2API.as not found.");
                if (!PatchSsf2ApiFile(apiAs))
                    throw new InvalidOperationException("Could not patch SSF2API for live pack assets.");

                var outfile = Path.Combine(work, "ssf2-live-api.swf");
                progress?.Report("Replacing SSF2API (live pack UI + costumes)…");
                await _jpexs.ReplaceAsync(
                    jpexsPath ?? "", mainSwf, outfile, ScriptNameSsf2Api, apiAs, cancellationToken);
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("FFDec did not produce SSF2.swf with live API.");
                File.Copy(outfile, mainSwf, overwrite: true);
                progress?.Report("SSF2API now remaps pack UI to pool shells and loads costumes.json.");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        public static bool PatchSsf2ApiFile(string ssf2ApiAsPath)
        {
            var text = File.ReadAllText(ssf2ApiAsPath);
            if (text.Contains(LiveApiMarker, StringComparison.Ordinal))
                return true;

            // Engine SSF2API (com.mcleodgaming.ssf2.api) uses ResourceManager, not m_api.
            var mcPat = new Regex(
                @"public\s+static\s+function\s+getMCByLinkageName\s*\(\s*(\w+)\s*:\s*String\s*\)\s*:\s*MovieClip\s*\{\s*" +
                @"return\s+ResourceManager\.getLibraryMC\s*\(\s*\1\s*\)\s*;\s*\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Fallback: older stub API.
            var mcPatStub = new Regex(
                @"public\s+static\s+function\s+getMCByLinkageName\s*\(\s*(\w+)\s*:\s*String\s*\)\s*:\s*MovieClip\s*\{\s*" +
                @"return\s+m_api\.getMCByLinkageName\s*\(\s*\1\s*\)\s*;\s*\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (mcPat.IsMatch(text))
                text = mcPat.Replace(text, BuildGetMcByLinkageName("ResourceManager.getLibraryMC"), 1);
            else if (mcPatStub.IsMatch(text))
                text = mcPatStub.Replace(text, BuildGetMcByLinkageName("m_api.getMCByLinkageName"), 1);
            else
                return false;

            var cosPat = new Regex(
                @"public\s+static\s+function\s+getCostumeData\s*\(\s*(\w+)\s*:\s*String\s*,\s*(\w+)\s*:\s*int\s*,\s*(\w+)\s*:\s*int\s*=\s*-1\s*\)\s*:\s*Object\s*\{\s*" +
                @"return\s+ResourceManager\.getCostume\s*\(\s*\1\s*,\s*Utils\.getColorString\s*\(\s*\2\s*\)\s*,\s*\3\s*\)\s*;\s*\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var cosPatStub = new Regex(
                @"public\s+static\s+function\s+getCostumeData\s*\(\s*(\w+)\s*:\s*String\s*,\s*(\w+)\s*:\s*int\s*,\s*(\w+)\s*:\s*int\s*=\s*-1\s*\)\s*:\s*Object\s*\{\s*" +
                @"return\s+m_api\.getCostumeData\s*\(\s*\1\s*,\s*\2\s*,\s*\3\s*\)\s*;\s*\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (cosPat.IsMatch(text))
                text = cosPat.Replace(text, BuildGetCostumeData(useResourceManager: true), 1);
            else if (cosPatStub.IsMatch(text))
                text = cosPatStub.Replace(text, BuildGetCostumeData(useResourceManager: false), 1);
            else
                return false;

            // Inject helpers before the final class-closing braces.
            var helpers = BuildLiveApiHelpers();
            var classClose = text.LastIndexOf("\n   }", StringComparison.Ordinal);
            if (classClose < 0)
            {
                var last = text.LastIndexOf('}');
                if (last < 0) return false;
                classClose = last;
            }
            text = text.Insert(classClose, helpers);

            File.WriteAllText(ssf2ApiAsPath, text);
            return File.ReadAllText(ssf2ApiAsPath).Contains(LiveApiMarker, StringComparison.Ordinal);
        }

        private static string BuildGetMcByLinkageName(string underlyingCall) =>
            "public static function getMCByLinkageName(linkage:String) : MovieClip\r\n" +
            "      {\r\n" +
            "         /* " + LiveApiMarker + " */\r\n" +
            "         var link:String = linkage;\r\n" +
            "         var pack:Object = null;\r\n" +
            "         var rosterLink:String = null;\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            pack = ssf2mmFindPackForUiLink(linkage);\r\n" +
            "            if(pack != null && pack.rosterId)\r\n" +
            "            {\r\n" +
            "               rosterLink = ssf2mmRosterUiLink(linkage, String(pack.rosterId));\r\n" +
            "               if(rosterLink != null)\r\n" +
            "               {\r\n" +
            "                  link = rosterLink;\r\n" +
            "               };\r\n" +
            "            };\r\n" +
            "         }\r\n" +
            "         catch(eMap:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "         var mc:MovieClip = " + underlyingCall + "(link);\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            if(mc != null && pack != null)\r\n" +
            "            {\r\n" +
            "               ssf2mmApplyLiveUiArt(mc, pack, linkage);\r\n" +
            "            };\r\n" +
            "         }\r\n" +
            "         catch(eArt:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "         return mc;\r\n" +
            "      }";

        private static string BuildGetCostumeData(bool useResourceManager) =>
            "public static function getCostumeData(statsName:String, team:int, num:int = -1) : Object\r\n" +
            "      {\r\n" +
            "         /* " + LiveApiMarker + " */\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var live:Object = ssf2mmLoadCostumeEntry(statsName, num);\r\n" +
            "            if(live != null)\r\n" +
            "            {\r\n" +
            "               return live;\r\n" +
            "            };\r\n" +
            "         }\r\n" +
            "         catch(eCos:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            (useResourceManager
                ? "         return ResourceManager.getCostume(statsName,Utils.getColorString(team),num);\r\n"
                : "         return m_api.getCostumeData(statsName,team,num);\r\n") +
            "      }";

        private static string BuildLiveApiHelpers() =>
            "\r\n" +
            "      /* " + LiveApiMarker + " helpers */\r\n" +
            "      private static var ssf2mmCostumeCache:Object = {};\r\n" +
            "      \r\n" +
            "      private static var ssf2mmLiveIndex:Object = null;\r\n" +
            "      \r\n" +
            "      private static function ssf2mmGetLiveIndex() : Object\r\n" +
            "      {\r\n" +
            "         if(ssf2mmLiveIndex != null)\r\n" +
            "         {\r\n" +
            "            return ssf2mmLiveIndex;\r\n" +
            "         };\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var FileCls:Class = flash.utils.getDefinitionByName(\"flash.filesystem.File\") as Class;\r\n" +
            "            var FileStreamCls:Class = flash.utils.getDefinitionByName(\"flash.filesystem.FileStream\") as Class;\r\n" +
            "            var FileModeCls:Class = flash.utils.getDefinitionByName(\"flash.filesystem.FileMode\") as Class;\r\n" +
            "            var appDir:* = FileCls[\"applicationDirectory\"];\r\n" +
            "            var f:* = appDir.resolvePath(\"data/ssf2mm/live.json\");\r\n" +
            "            if(!f.exists)\r\n" +
            "            {\r\n" +
            "               return null;\r\n" +
            "            };\r\n" +
            "            var fs:* = new FileStreamCls();\r\n" +
            "            fs.open(f, FileModeCls[\"READ\"]);\r\n" +
            "            var txt:String = fs.readUTFBytes(fs.bytesAvailable);\r\n" +
            "            fs.close();\r\n" +
            "            ssf2mmLiveIndex = JSON.parse(txt);\r\n" +
            "            return ssf2mmLiveIndex;\r\n" +
            "         }\r\n" +
            "         catch(eIdx:*)\r\n" +
            "         {\r\n" +
            "            return null;\r\n" +
            "         };\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmFindPackForUiLink(link:String) : Object\r\n" +
            "      {\r\n" +
            "         if(link == null)\r\n" +
            "         {\r\n" +
            "            return null;\r\n" +
            "         };\r\n" +
            "         var idx:Object = ssf2mmGetLiveIndex();\r\n" +
            "         if(idx == null || idx.packs == null)\r\n" +
            "         {\r\n" +
            "            return null;\r\n" +
            "         };\r\n" +
            "         var i:int = 0;\r\n" +
            "         while(i < idx.packs.length)\r\n" +
            "         {\r\n" +
            "            var pk:Object = idx.packs[i];\r\n" +
            "            if(pk && pk.type == \"character\" && pk.playableId)\r\n" +
            "            {\r\n" +
            "               var pid:String = String(pk.playableId);\r\n" +
            "               if(link == pid + \"_charselect\" || link == pid + \"_cssp\" || link == pid + \"_stock\" || link == pid + \"_stock0\" || link == pid + \"_classic\" || link == pid + \"_classic_trophy\" || link == pid + \"_allstar\" || link == pid + \"_event\")\r\n" +
            "               {\r\n" +
            "                  return pk;\r\n" +
            "               };\r\n" +
            "            };\r\n" +
            "            i++;\r\n" +
            "         };\r\n" +
            "         return null;\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmRosterUiLink(playableLink:String, rosterId:String) : String\r\n" +
            "      {\r\n" +
            "         var suffixes:Array = [\"_charselect\",\"_cssp\",\"_stock\",\"_stock0\",\"_classic\",\"_classic_trophy\",\"_allstar\",\"_event\"];\r\n" +
            "         var si:int = 0;\r\n" +
            "         while(si < suffixes.length)\r\n" +
            "         {\r\n" +
            "            var suf:String = String(suffixes[si]);\r\n" +
            "            if(playableLink.length > suf.length && playableLink.substr(playableLink.length - suf.length) == suf)\r\n" +
            "            {\r\n" +
            "               return rosterId + suf;\r\n" +
            "            };\r\n" +
            "            si++;\r\n" +
            "         };\r\n" +
            "         return null;\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmApplyLiveUiArt(mc:MovieClip, pack:Object, originalLink:String) : void\r\n" +
            "      {\r\n" +
            "         if(mc == null || pack == null || pack.id == null)\r\n" +
            "         {\r\n" +
            "            return;\r\n" +
            "         };\r\n" +
            "         var kind:String = null;\r\n" +
            "         if(originalLink.indexOf(\"_stock\") >= 0 && originalLink.indexOf(\"_stock0\") < 0)\r\n" +
            "         {\r\n" +
            "            kind = \"stock\";\r\n" +
            "         }\r\n" +
            "         else if(originalLink.indexOf(\"_charselect\") >= 0 || originalLink.indexOf(\"_cssp\") >= 0)\r\n" +
            "         {\r\n" +
            "            kind = \"pa\";\r\n" +
            "         }\r\n" +
            "         else\r\n" +
            "         {\r\n" +
            "            return;\r\n" +
            "         };\r\n" +
            "         if(mc.ssf2mmLiveKind == kind && mc.ssf2mmLivePackId == String(pack.id))\r\n" +
            "         {\r\n" +
            "            return;\r\n" +
            "         };\r\n" +
            "         mc.ssf2mmLiveKind = kind;\r\n" +
            "         mc.ssf2mmLivePackId = String(pack.id);\r\n" +
            "         mc.ssf2mmLivePlayableId = String(pack.playableId);\r\n" +
            "         var FileCls:Class = flash.utils.getDefinitionByName(\"flash.filesystem.File\") as Class;\r\n" +
            "         var appDir:* = FileCls[\"applicationDirectory\"];\r\n" +
            "         var fileName:String = kind == \"stock\" ? \"stock.png\" : \"pa.png\";\r\n" +
            "         var png:* = appDir.resolvePath(\"expansions/packs/\" + String(pack.id) + \"/assets/\" + fileName);\r\n" +
            "         if(!png.exists && kind == \"pa\")\r\n" +
            "         {\r\n" +
            "            png = appDir.resolvePath(\"expansions/packs/\" + String(pack.id) + \"/assets/pa.jpg\");\r\n" +
            "         };\r\n" +
            "         if(!png.exists)\r\n" +
            "         {\r\n" +
            "            return;\r\n" +
            "         };\r\n" +
            "         var old:* = mc.getChildByName(\"ssf2mmLiveArt\");\r\n" +
            "         if(old != null)\r\n" +
            "         {\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               mc.removeChild(old);\r\n" +
            "            }\r\n" +
            "            catch(eRm:*)\r\n" +
            "            {\r\n" +
            "            };\r\n" +
            "         };\r\n" +
            "         var hi:int = 0;\r\n" +
            "         while(hi < mc.numChildren)\r\n" +
            "         {\r\n" +
            "            var ch:* = mc.getChildAt(hi);\r\n" +
            "            if(ch != null && !(ch is flash.text.TextField) && ch.name != \"ssf2mmLiveArt\" && ch.name != \"hitBox\")\r\n" +
            "            {\r\n" +
            "               try\r\n" +
            "               {\r\n" +
            "                  ch.visible = false;\r\n" +
            "               }\r\n" +
            "               catch(eHide:*)\r\n" +
            "               {\r\n" +
            "               };\r\n" +
            "            };\r\n" +
            "            hi++;\r\n" +
            "         };\r\n" +
            "         var b:flash.geom.Rectangle = mc.getBounds(mc);\r\n" +
            "         if(b.width < 2 || b.height < 2)\r\n" +
            "         {\r\n" +
            "            b = new flash.geom.Rectangle(0,0,kind == \"stock\" ? 24 : 180,kind == \"stock\" ? 24 : 200);\r\n" +
            "         };\r\n" +
            "         var loader:flash.display.Loader = new flash.display.Loader();\r\n" +
            "         loader.name = \"ssf2mmLiveArt\";\r\n" +
            "         mc.ssf2mmLiveBoxX = b.x;\r\n" +
            "         mc.ssf2mmLiveBoxY = b.y;\r\n" +
            "         mc.ssf2mmLiveBoxW = b.width;\r\n" +
            "         mc.ssf2mmLiveBoxH = b.height;\r\n" +
            "         loader.contentLoaderInfo.addEventListener(flash.events.Event.COMPLETE, ssf2mmOnLiveArtComplete);\r\n" +
            "         mc.addChildAt(loader, 0);\r\n" +
            "         loader.load(new flash.net.URLRequest(png.url));\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmOnLiveArtComplete(e:flash.events.Event) : void\r\n" +
            "      {\r\n" +
            "         try\r\n" +
            "         {\r\n" +
            "            var li:flash.display.LoaderInfo = e.target as flash.display.LoaderInfo;\r\n" +
            "            if(li == null || li.loader == null)\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var loader:flash.display.Loader = li.loader;\r\n" +
            "            var mc:MovieClip = loader.parent as MovieClip;\r\n" +
            "            if(mc == null)\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var boxW:Number = Number(mc.ssf2mmLiveBoxW);\r\n" +
            "            var boxH:Number = Number(mc.ssf2mmLiveBoxH);\r\n" +
            "            var boxX:Number = Number(mc.ssf2mmLiveBoxX);\r\n" +
            "            var boxY:Number = Number(mc.ssf2mmLiveBoxY);\r\n" +
            "            var cw:Number = loader.content.width;\r\n" +
            "            var ch:Number = loader.content.height;\r\n" +
            "            if(!(cw > 0) || !(ch > 0))\r\n" +
            "            {\r\n" +
            "               return;\r\n" +
            "            };\r\n" +
            "            var scale:Number = Math.min(boxW / cw, boxH / ch);\r\n" +
            "            if(mc.ssf2mmLiveKind == \"pa\")\r\n" +
            "            {\r\n" +
            "               scale = Math.max(boxW / cw, boxH / ch);\r\n" +
            "            };\r\n" +
            "            loader.scaleX = scale;\r\n" +
            "            loader.scaleY = scale;\r\n" +
            "            loader.x = boxX + (boxW - cw * scale) * 0.5;\r\n" +
            "            loader.y = boxY + (boxH - ch * scale) * 0.5;\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               var cd:Object = getCostumeData(String(mc.ssf2mmLivePlayableId), -1, -1);\r\n" +
            "               var swap:Object = null;\r\n" +
            "               if(cd != null)\r\n" +
            "               {\r\n" +
            "                  swap = mc.ssf2mmLiveKind == \"pa\" ? cd.paletteSwapPA || cd.paletteSwap : cd.paletteSwap;\r\n" +
            "               };\r\n" +
            "               if(swap != null && swap.colors && swap.replacements)\r\n" +
            "               {\r\n" +
            "                  SSF2Utils.replacePalette(mc, swap, 2, false, true);\r\n" +
            "               };\r\n" +
            "            }\r\n" +
            "            catch(ePal:*)\r\n" +
            "            {\r\n" +
            "            };\r\n" +
            "            try\r\n" +
            "            {\r\n" +
            "               print(\"[SSF2MM_PACK] live OK id=\" + mc.ssf2mmLivePackId + \" kind=\" + mc.ssf2mmLiveKind);\r\n" +
            "            }\r\n" +
            "            catch(ePr:*)\r\n" +
            "            {\r\n" +
            "            };\r\n" +
            "         }\r\n" +
            "         catch(eDone:*)\r\n" +
            "         {\r\n" +
            "         };\r\n" +
            "      }\r\n" +
            "      \r\n" +
            "      private static function ssf2mmLoadCostumeEntry(playableId:String, costumeIndex:int) : Object\r\n" +
            "      {\r\n" +
            "         if(playableId == null || playableId.length == 0)\r\n" +
            "         {\r\n" +
            "            return null;\r\n" +
            "         };\r\n" +
            "         var pack:Object = null;\r\n" +
            "         var idxObj:Object = ssf2mmGetLiveIndex();\r\n" +
            "         if(idxObj == null || idxObj.packs == null)\r\n" +
            "         {\r\n" +
            "            return null;\r\n" +
            "         };\r\n" +
            "         var i:int = 0;\r\n" +
            "         while(i < idxObj.packs.length)\r\n" +
            "         {\r\n" +
            "            var pk:Object = idxObj.packs[i];\r\n" +
            "            if(pk && pk.type == \"character\" && String(pk.playableId) == playableId)\r\n" +
            "            {\r\n" +
            "               pack = pk;\r\n" +
            "               break;\r\n" +
            "            };\r\n" +
            "            i++;\r\n" +
            "         };\r\n" +
            "         if(pack == null)\r\n" +
            "         {\r\n" +
            "            return null;\r\n" +
            "         };\r\n" +
            "         var packId:String = String(pack.id);\r\n" +
            "         var arr:Array = ssf2mmCostumeCache[packId] as Array;\r\n" +
            "         if(arr == null)\r\n" +
            "         {\r\n" +
            "            var FileCls:Class = flash.utils.getDefinitionByName(\"flash.filesystem.File\") as Class;\r\n" +
            "            var FileStreamCls:Class = flash.utils.getDefinitionByName(\"flash.filesystem.FileStream\") as Class;\r\n" +
            "            var FileModeCls:Class = flash.utils.getDefinitionByName(\"flash.filesystem.FileMode\") as Class;\r\n" +
            "            var appDir:* = FileCls[\"applicationDirectory\"];\r\n" +
            "            var f:* = appDir.resolvePath(\"expansions/packs/\" + packId + \"/costumes.json\");\r\n" +
            "            if(!f.exists)\r\n" +
            "            {\r\n" +
            "               return null;\r\n" +
            "            };\r\n" +
            "            var fs:* = new FileStreamCls();\r\n" +
            "            fs.open(f, FileModeCls[\"READ\"]);\r\n" +
            "            var txt:String = fs.readUTFBytes(fs.bytesAvailable);\r\n" +
            "            fs.close();\r\n" +
            "            arr = JSON.parse(txt) as Array;\r\n" +
            "            ssf2mmCostumeCache[packId] = arr;\r\n" +
            "         };\r\n" +
            "         if(arr == null || arr.length == 0)\r\n" +
            "         {\r\n" +
            "            return null;\r\n" +
            "         };\r\n" +
            "         var idx:int = costumeIndex;\r\n" +
            "         if(idx < 0 || idx >= arr.length)\r\n" +
            "         {\r\n" +
            "            var bi:int = 0;\r\n" +
            "            while(bi < arr.length)\r\n" +
            "            {\r\n" +
            "               if(arr[bi] && arr[bi].base == true)\r\n" +
            "               {\r\n" +
            "                  return arr[bi];\r\n" +
            "               };\r\n" +
            "               bi++;\r\n" +
            "            };\r\n" +
            "            return arr[0];\r\n" +
            "         };\r\n" +
            "         return arr[idx];\r\n" +
            "      }\r\n";

        /// <summary>
        /// Convert Misc.as-style <c>_loc1_["id"].push({...});</c> blocks into a JSON array for live load.
        /// </summary>
        public static string CostumeBlockToJsonArray(string costumeBlockOrJson)
        {
            if (string.IsNullOrWhiteSpace(costumeBlockOrJson))
                return "[]";
            var trimmed = costumeBlockOrJson.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                // Already JSON.
                try
                {
                    var tok = JToken.Parse(trimmed);
                    return tok.Type == JTokenType.Array
                        ? CompactJsonNumberArrays(tok.ToString(Formatting.Indented))
                        : "[]";
                }
                catch
                {
                    return "[]";
                }
            }

            var arr = new JArray();
            var idx = 0;
            while (idx < costumeBlockOrJson.Length)
            {
                var pushAt = costumeBlockOrJson.IndexOf(".push(", idx, StringComparison.OrdinalIgnoreCase);
                if (pushAt < 0) break;
                var brace = costumeBlockOrJson.IndexOf('{', pushAt);
                if (brace < 0) break;
                var depth = 0;
                var end = -1;
                for (var i = brace; i < costumeBlockOrJson.Length; i++)
                {
                    var c = costumeBlockOrJson[i];
                    if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            end = i;
                            break;
                        }
                    }
                }
                if (end < 0) break;
                var obj = costumeBlockOrJson.Substring(brace, end - brace + 1);
                obj = Regex.Replace(obj, @",\s*([\}\]])", "$1");
                try { arr.Add(JObject.Parse(obj)); }
                catch { /* skip */ }
                idx = end + 1;
            }

            if (arr.Count == 0)
            {
                try
                {
                    var jo = JObject.Parse(trimmed);
                    return CompactJsonNumberArrays(new JArray(jo).ToString(Formatting.Indented));
                }
                catch
                {
                    return "[]";
                }
            }

            return CompactJsonNumberArrays(arr.ToString(Formatting.Indented));
        }

        /// <summary>
        /// Pretty-print objects but keep numeric arrays on one line.
        /// </summary>
        public static string CompactJsonNumberArrays(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;
            return Regex.Replace(
                json,
                @"\[\s*(?:-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\s*,\s*)*-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\s*\]",
                m =>
                {
                    var nums = Regex.Matches(m.Value, @"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?");
                    return "[" + string.Join(", ", nums.Select(n => n.Value)) + "]";
                },
                RegexOptions.Multiline);
        }

        public static async Task WriteLiveCostumesJsonAsync(
            string installRoot,
            string donorId,
            string playableId,
            string packDir,
            string? jpexsPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(packDir);
            var dest = Path.Combine(packDir, "costumes.json");
            var misc = Path.Combine(installRoot, "data", "misc", "misc.swf");
            if (!File.Exists(misc))
                misc = Path.Combine(installRoot, "data", "misc", "misc.ssf");

            string? block = null;
            if (File.Exists(misc))
            {
                var work = Path.Combine(Path.GetTempPath(), "ssf2mm-live-cos-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    var jpexs = new JpexsCliService();
                    var swf = SwfPackage.EnsurePlainSwf(misc, work);
                    await jpexs.ExportAsync(jpexsPath ?? "", swf, work, "script", cancellationToken);
                    var miscAs = Directory.GetFiles(work, "Misc.as", SearchOption.AllDirectories).FirstOrDefault();
                    if (miscAs != null)
                    {
                        var text = await File.ReadAllTextAsync(miscAs, cancellationToken);
                        var keys = new[] { donorId, playableId, (donorId ?? "").ToLowerInvariant(), (playableId ?? "").ToLowerInvariant() }
                            .Where(k => !string.IsNullOrWhiteSpace(k))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        foreach (var key in keys)
                        {
                            block = CharacterCloneService.ExtractCostumeBlock(text, key ?? "", playableId ?? "", keys);
                            if (!string.IsNullOrEmpty(block))
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report("costumes.json extract warning: " + ex.Message);
                }
                finally
                {
                    try { Directory.Delete(work, true); } catch { /* ignore */ }
                }
            }

            var json = CharacterCloneService.ToCostumesJson(block, playableId);

            await File.WriteAllTextAsync(dest, json, Encoding.UTF8, cancellationToken);
            progress?.Report("Wrote live costumes.json (" + dest + ").");
        }
    }
}
