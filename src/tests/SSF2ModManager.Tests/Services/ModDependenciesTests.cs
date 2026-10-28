using FluentAssertions;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ModDependenciesTests
    {
        [Fact]
        public void Parse_ReadsIdsAndLinksOnce()
        {
            var ids = ModDependencies.Parse("714453, https://gamebanana.com/mods/100 714453 notes");
            ids.Should().Equal(714453, 100);
        }

        [Fact]
        public void Write_RoundTripsTheArray()
        {
            var info = new JObject();
            ModDependencies.Write(info, new[] { 714453, 12 });
            ModDependencies.Read(info).Should().Equal(714453, 12);
            ModDependencies.Format(ModDependencies.Read(info)).Should().Be("714453, 12");
            ModDependencies.Write(info, []);
            info["dependencies"].Should().BeNull();
        }
    }
}
