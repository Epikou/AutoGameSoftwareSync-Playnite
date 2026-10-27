using System.Collections.Generic;
using System.Linq;
using AutoGameSoftwareSync.PlayniteExtension.Logic;
using Xunit;

namespace AutoGameSoftwareSync.PlayniteExtension.Tests
{
    /// <summary>Badge recipes of popular fullscreen themes (checked with the versions given in TestedVersion).</summary>
    public class RecipeTests
    {
        [Theory]
        [InlineData("Solaris_b6e50d04-24ae-4ecd-bd3a-080367930992", "Solaris", BadgeLayout.Welded)]
        [InlineData("RePS5_3d09a5d1-a183-49da-adb6-e78428f7d90e", "ReskinPS5", BadgeLayout.Clone)]
        [InlineData("(u)biquity_e71482ce-33e2-4b23-aaea-de576975d3a9", "(u)biquity", BadgeLayout.Clone)]
        [InlineData("PS5ish_676e10ec-adfe-48d8-a1bd-4d5771b5a2ca", "PS5ish", BadgeLayout.Clone)]
        [InlineData("PlayniteModernUI_b600472c-c10c-4136-86d0-82bf0e576200", "PlayniteModernUI", BadgeLayout.Clone)]
        [InlineData("Playnite Deck_905f24da-b02b-40df-9f6b-14c41fcdb05d", "Playnite Deck", BadgeLayout.Clone)]
        [InlineData("Nintendo Switch-ish_0d020ed5-0f3c-4c1a-bf9a-c983ef7d74b7", "Nintendo Switch-ish", BadgeLayout.CloneWithPrevious)]
        [InlineData("Toggle_28b7d2c0-105b-4632-8dca-d11348bd61d2", "Toggle", BadgeLayout.Clone)]
        public void PopularThemesHaveARecipe(string themeId, string name, BadgeLayout layout)
        {
            var r = ThemeRecipes.Find(themeId, true);
            Assert.NotNull(r);
            Assert.Equal(name, r.Name);
            Assert.Equal(layout, r.Layout);
            Assert.Equal(DisplayKind.Badge, StableDecision.Decide(DisplayMode.Auto, themeId, r.TestedVersion, null, null));
        }

        [Fact]
        public void XboxSeriesIshHasNoRecipeAndKeepsTheGenre()
        {
            const string id = "XBOXSERIESish_74735df9-3351-4669-a1ff-50f18f39b63b";
            Assert.Null(ThemeRecipes.Find(id, true));
            Assert.Equal(DisplayKind.Field, StableDecision.Decide(DisplayMode.Auto, id, "2.0.0", null, null));
            Assert.False(StableDecision.RunInjector(true, DisplayMode.Auto, id, "2.0.0", null));
        }

        [Fact]
        public void EveryRecipeIsUsable()
        {
            foreach (var r in ThemeRecipes.All)
            {
                Assert.NotEmpty(r.ThemeIds);
                Assert.False(string.IsNullOrEmpty(r.TestedVersion), r.Name);
                Assert.True(r.Fullscreen, r.Name);
                if (r.Layout == BadgeLayout.Welded)
                {
                    Assert.False(string.IsNullOrEmpty(r.RowName), r.Name);
                    Assert.False(string.IsNullOrEmpty(r.AnchorName), r.Name);
                    Assert.False(string.IsNullOrEmpty(r.BoxStyleKey), r.Name);
                }
                else
                {
                    Assert.False(string.IsNullOrEmpty(r.AnchorBinding), r.Name);
                    Assert.True(r.RowLevel >= 1 || !string.IsNullOrEmpty(r.RowName), r.Name);
                    Assert.Equal("AGSS", r.LabelText);
                }
            }
        }

        [Fact]
        public void ThemeIdsAreUniqueAcrossRecipes()
        {
            var ids = ThemeRecipes.All.SelectMany(r => r.ThemeIds).Select(i => i.ToLowerInvariant()).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void ADurableFailureOfANewRecipeIsRememberedPerVersion()
        {
            const string id = "Toggle_28b7d2c0-105b-4632-8dca-d11348bd61d2";
            var failed = new List<string> { StableDecision.ThemeKey(id, "1.10.1") };
            Assert.Equal(DisplayKind.Field, StableDecision.Decide(DisplayMode.Auto, id, "1.10.1", failed, null));
            Assert.Equal(DisplayKind.Badge, StableDecision.Decide(DisplayMode.Auto, id, "1.11.0", failed, null));
        }

        [Fact]
        public void RecipeLookupNeedsTheExactThemeId()
        {
            Assert.Null(ThemeRecipes.Find("Toggle", true));
            Assert.Null(ThemeRecipes.Find("PS5ish Slim_595a8e66-1a8b-4628-874e-ed853daddc3d", true));
            Assert.NotNull(ThemeRecipes.Find("toggle_28B7D2C0-105b-4632-8dca-d11348bd61d2", true));
        }
    }
}
