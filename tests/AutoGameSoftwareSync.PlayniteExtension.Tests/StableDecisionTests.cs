using System.Collections.Generic;
using AutoGameSoftwareSync.PlayniteExtension.Logic;
using Xunit;

namespace AutoGameSoftwareSync.PlayniteExtension.Tests
{
    /// <summary>The fallback genre is library data shared by Desktop and Fullscreen: no back-and-forth between modes.</summary>
    public class StableDecisionTests
    {
        private const string Solaris = "Solaris_b6e50d04-24ae-4ecd-bd3a-080367930992";
        private const string NoRecipe = "AgssTest_SolarisWithoutRecipe";
        private static readonly List<string> None = new List<string>();

        [Fact]
        public void DesktopWithSolarisConfiguredWritesNoGenreAndTriesNoBadge()
        {
            Assert.Equal(DisplayKind.Badge, StableDecision.Decide(DisplayMode.Auto, Solaris, "1.3.6", None, None));
            Assert.False(StableDecision.RunInjector(false, DisplayMode.Auto, Solaris, "1.3.6", None));
        }

        [Fact]
        public void DesktopWithRecipelessFullscreenThemeWritesGenre()
        {
            Assert.Equal(DisplayKind.Field, StableDecision.Decide(DisplayMode.Auto, NoRecipe, "1.3.6", None, None));
            Assert.Equal(DisplayKind.Field, StableDecision.Decide(DisplayMode.Auto, "Playnite_builtin_DefaultFullscreen", "", None, None));
        }

        [Theory]
        [InlineData(Solaris)]
        [InlineData(NoRecipe)]
        [InlineData(null)]
        public void SwitchingDesktopFullscreenChangesNothing(string theme)
        {
            // The decision has no "running mode" input at all: same answer before and after a mode switch.
            var failed = new List<string>(); var custom = new List<string>();
            var desktop = StableDecision.Decide(DisplayMode.Auto, theme, "1.3.6", failed, custom);
            var fullscreen = StableDecision.Decide(DisplayMode.Auto, theme, "1.3.6", failed, custom);
            var desktopAgain = StableDecision.Decide(DisplayMode.Auto, theme, "1.3.6", failed, custom);
            Assert.Equal(desktop, fullscreen);
            Assert.Equal(desktop, desktopAgain);
        }

        [Fact]
        public void InjectorOnlyInFullscreenWithRecipe()
        {
            Assert.True(StableDecision.RunInjector(true, DisplayMode.Auto, Solaris, "1.3.6", None));
            Assert.False(StableDecision.RunInjector(true, DisplayMode.Auto, NoRecipe, "1.3.6", None));
            Assert.False(StableDecision.RunInjector(true, DisplayMode.FieldOnly, Solaris, "1.3.6", None));
        }

        [Fact]
        public void RememberedFailureGivesGenreInBothModesWithoutRetry()
        {
            var failed = new List<string> { StableDecision.ThemeKey(Solaris, "1.3.6") };
            Assert.Equal(DisplayKind.Field, StableDecision.Decide(DisplayMode.Auto, Solaris, "1.3.6", failed, None));
            Assert.False(StableDecision.RunInjector(true, DisplayMode.Auto, Solaris, "1.3.6", failed)); // no oscillation at each start
        }

        [Fact]
        public void NewThemeVersionRetriesTheBadgeAndDropsTheGenre()
        {
            var failed = new List<string> { StableDecision.ThemeKey(Solaris, "1.3.6") };
            Assert.Equal(DisplayKind.Badge, StableDecision.Decide(DisplayMode.Auto, Solaris, "1.4.0", failed, None));
            Assert.True(StableDecision.RunInjector(true, DisplayMode.Auto, Solaris, "1.4.0", failed));
        }

        [Fact]
        public void RememberedCustomElementThemeNeedsNoGenre()
        {
            var custom = new List<string> { NoRecipe.ToLowerInvariant() };
            Assert.Equal(DisplayKind.Badge, StableDecision.Decide(DisplayMode.Auto, NoRecipe, "", None, custom));
        }

        [Fact]
        public void ThemeKeyIsCaseInsensitiveOnId()
        {
            Assert.Equal(StableDecision.ThemeKey("SOLARIS_X", "1.0"), StableDecision.ThemeKey("solaris_x", " 1.0 "));
        }

        [Fact]
        public void ParsesConfiguredFullscreenThemeAndVersion()
        {
            Assert.Equal(Solaris, StableDecision.ParseFullscreenThemeId("{\n  \"Monitor\": 0,\n  \"Theme\": \"" + Solaris + "\",\n  \"Rows\": 3 }"));
            Assert.Null(StableDecision.ParseFullscreenThemeId("{ \"Rows\": 3 }"));
            Assert.Null(StableDecision.ParseFullscreenThemeId(null));
            Assert.Equal("1.3.6", StableDecision.ParseThemeVersion("ThemeApiVersion: 2.9.0\nMode: Fullscreen\nId: X\nVersion: 1.3.6\nLinks:"));
            Assert.Equal("1.0", StableDecision.ParseThemeVersion("Version: '1.0'"));
            Assert.Equal("", StableDecision.ParseThemeVersion("Id: X"));
        }
    }
}
