using AutoGameSoftwareSync.PlayniteExtension.Logic;
using Xunit;

namespace AutoGameSoftwareSync.PlayniteExtension.Tests
{
    public class LegacyTests
    {
        [Fact]
        public void NewPathWinsWhenItExists()
        {
            Assert.Equal("new.json", Legacy.Resolve("new.json", p => true));
        }

        [Fact]
        public void FallsBackToOldPathOnlyWhenNewIsMissingAndOldExists()
        {
            var old = Legacy.OldStatusPath;
            Assert.Equal(old, Legacy.Resolve("new.json", p => p == old));
            Assert.Equal("new.json", Legacy.Resolve("new.json", p => false));
        }
    }
}
