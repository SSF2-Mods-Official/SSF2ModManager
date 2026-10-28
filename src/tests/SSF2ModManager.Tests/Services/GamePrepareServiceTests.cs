using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class GamePrepareServiceTests
    {
        [Fact]
        public void PatchCharacterSelectScale_AddsScaleAllAfterSpaceObjects()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-css-scale-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "CharacterSelectMenu.as");
                File.WriteAllText(path, """
                    package com.mcleodgaming.ssf2.menus {
                    public class CharacterSelectMenu {
                        protected function updateIcons():void {
                            var i:int;
                            var j:int;
                            var mappings:Object = ResourceManager.getResourceByID("mappings").getProp("metadata");
                            this.displayObjectTable.spaceObjects();
                        }
                    }
                    }
                    """);

                GamePrepareService.PatchCharacterSelectScale(path).Should().BeTrue();
                var text = File.ReadAllText(path);
                text.Should().Contain(GamePrepareService.SelectScaleMarker);
                text.Should().Contain("scaleAll((mappings.character_select_screen.icon_size.width / 42)");
                text.Should().NotContain("HandMC.scaleX");
                text.Should().NotContain("m_playerChips[i].MC.scaleX");
                text.Should().Contain("indexOf(\"btn_game\")");
                text.Should().Contain(GamePrepareService.SelectScaleMarker);
                // Idempotent
                GamePrepareService.PatchCharacterSelectScale(path).Should().BeTrue();

                File.WriteAllText(path, """
                    package com.mcleodgaming.ssf2.menus {
                    public class CharacterSelectMenu {
                        protected function updateIcons():void {
                            var i:int;
                            var j:int;
                            var mappings:Object = ResourceManager.getResourceByID("mappings").getProp("metadata");
                            this.displayObjectTable.spaceObjects();
                            /* SSF2MM_SELECT_SCALE_V2 */
                            this.displayObjectTable.scaleAll((mappings.character_select_screen.icon_size.width / 42), (mappings.character_select_screen.icon_size.height / 42));
                            i = 0;
                            while(i < this.m_playerChips.length)
                            {
                               this.m_playerChips[i].MC.scaleX = (mappings.character_select_screen.icon_size.width / 42);
                               this.m_playerChips[i].HandMC.scaleX = (mappings.character_select_screen.icon_size.width / 42);
                               i++;
                            };
                        }
                    }
                    }
                    """);
                GamePrepareService.PatchCharacterSelectScale(path).Should().BeTrue();
                text = File.ReadAllText(path);
                text.Should().NotContain("HandMC.scaleX");
                text.Should().NotContain("m_playerChips[i].MC.scaleX");
                text.Should().Contain("scaleAll((mappings.character_select_screen.icon_size.width / 42)");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void PatchStageSelectScale_ScalesNormalAndPastTables()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-sss-scale-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "StageSelectMenu.as");
                File.WriteAllText(path, """
                    package com.mcleodgaming.ssf2.menus {
                    public class StageSelectMenu {
                        override public function show():void {
                            this.updateIcons();
                            this.normalStageTable.spaceObjects();
                            this.m_normal_btn.gotoAndStop("on");
                        }
                    }
                    }
                    """);

                GamePrepareService.PatchStageSelectScale(path).Should().BeTrue();
                var text = File.ReadAllText(path);
                text.Should().Contain(GamePrepareService.SelectScaleMarker);
                text.Should().Contain("normalStageTable.scaleAll");
                text.Should().Contain("pastStageTable.scaleAll");
                text.Should().Contain("/ 48)");
                text.Should().Contain("/ 26)");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void PatchSsf2ApiDebugConsole_IsIdempotentAndMarksPrint()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-dbg-api-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                // Real Beta shape: nested braces + MenuController.debugConsole (not stub m_api).
                File.WriteAllText(Path.Combine(dir, "SSF2API.as"), """
                    package com.mcleodgaming.ssf2.api {
                    public class SSF2API {
                      private static var _api:StageData;
                      public static function print(text:String) : void
                      {
                         if(Main.DEBUG && Boolean(MenuController.debugConsole))
                         {
                            MenuController.debugConsole.writeTextData(text);
                         }
                      }
                      public static function addEventListener(type:String, func:Function) : void
                      {
                         _api.addEventListener(type,func);
                      }
                    }
                    }
                    """);

                GamePrepareService.PatchSsf2ApiDebugConsole(dir).Should().BeTrue();
                var text = File.ReadAllText(Path.Combine(dir, "SSF2API.as"));
                text.Should().Contain("ssf2mmDebugSend");
                text.Should().Contain("_ssf2mmSock");
                text.Should().Contain(GamePrepareService.DebugConsolePort.ToString());
                // Must not close the class early — next method stays inside the class.
                text.Should().Contain("public static function addEventListener");
                var printIdx = text.IndexOf("function print", StringComparison.Ordinal);
                var addIdx = text.IndexOf("function addEventListener", StringComparison.Ordinal);
                addIdx.Should().BeGreaterThan(printIdx);
                GamePrepareService.PatchSsf2ApiDebugConsole(dir).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void FindMatchingBrace_HandlesNestedBlocks()
        {
            var s = "function print() { if(a) { b(); } } next";
            var open = s.IndexOf('{');
            var close = GamePrepareService.FindMatchingBrace(s, open);
            close.Should().Be(s.LastIndexOf('}'));
            s.Substring(close + 1).TrimStart().Should().StartWith("next");
        }

        [Fact]
        public void PatchMainUncaughtDebugConsole_InjectsHandler()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-dbg-main-" + Path.GetRandomFileName());
            var ssf2 = Path.Combine(dir, "ssf2");
            Directory.CreateDirectory(ssf2);
            try
            {
                File.WriteAllText(Path.Combine(ssf2, "Main.as"), """
                    package com.mcleodgaming.ssf2 {
                    public class Main extends MovieClip {
                      public function Main() {
                         super();
                         this.init();
                      }
                    }
                    }
                    """);

                GamePrepareService.PatchMainUncaughtDebugConsole(dir).Should().BeTrue();
                var text = File.ReadAllText(Path.Combine(ssf2, "Main.as"));
                text.Should().Contain(GamePrepareService.DebugConsoleMarker);
                text.Should().Contain("ssf2mmOnUncaught");
                text.Should().Contain("UNCAUGHT_ERROR");
                GamePrepareService.PatchMainUncaughtDebugConsole(dir).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }
    }
}
