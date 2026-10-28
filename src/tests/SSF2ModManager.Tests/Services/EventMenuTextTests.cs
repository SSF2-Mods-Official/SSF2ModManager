using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class EventMenuTextTests
    {
        [Fact]
        public void Description_ReadsTheMenuBlurb()
        {
            var bytes = new List<byte>();
            Write(bytes, "GourmetRace");
            Write(bytes, "3. Gourmet Race");
            Write(bytes, "After an epic showdown, you\u2019re damaged pretty seriously.");

            var text = EventMenuText.DescriptionForId(bytes.ToArray(), "GourmetRace");

            Assert.Equal("After an epic showdown, you\u2019re damaged pretty seriously.", text);
        }

        [Fact]
        public void Description_SkipsTheDescriptionToken()
        {
            var bytes = new List<byte>();
            Write(bytes, "TroubledKing");
            Write(bytes, "1. Troubled King");
            Write(bytes, "description");
            Write(bytes, "Fight Mario in a classic Mushroom Kingdom clash!");

            var text = EventMenuText.DescriptionForId(bytes.ToArray(), "TroubledKing");

            Assert.Equal("Fight Mario in a classic Mushroom Kingdom clash!", text);
        }

        [Fact]
        public void Description_ReadsThePackFile()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-menu-" + Path.GetRandomFileName());
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(src);
            try
            {
                var script = Path.Combine(src, "EventMode_Custom.as");
                File.WriteAllText(script, "package { }");
                File.WriteAllText(Path.Combine(root, "info.json"), new JObject { ["description"] = "Pink." }.ToString());

                var text = EventMenuText.Description("", new EventSourceChoice { Path = script });

                Assert.Equal("Pink.", text);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void Write(List<byte> bytes, string text)
        {
            var raw = Encoding.UTF8.GetBytes(text);
            bytes.Add((byte)raw.Length);
            bytes.AddRange(raw);
        }
    }
}
