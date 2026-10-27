using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutoGameSoftwareSync.PlayniteExtension.Logic;
using AutoGameSoftwareSync.PlayniteExtension.Settings;
using Xunit;

namespace AutoGameSoftwareSync.PlayniteExtension.Tests
{
    public class Schema2Tests
    {
        // Format written by the 2.0.x app, WITHOUT the new origin fields.
        private const string V2Old = "{\"schema\":2,\"app_version\":\"2.0.3\",\"updated_at_utc\":\"2026-10-27T20:14:00Z\",\"watching\":true," +
            "\"shared_folder_reachable\":true,\"machine\":\"Handheld\",\"machine_color\":\"#4FC3F7\",\"last_check_utc\":\"2026-10-27T20:14:00Z\"," +
            "\"reactivity_mode\":\"auto\",\"conflict_mode\":\"ask\",\"shared_folder_configured\":true,\"detection_scope\":\"all\",\"games\":[" +
            "{\"name\":\"Balatro\",\"key\":\"balatro\",\"exe\":\"Balatro.exe\",\"install_dir\":\"\",\"save_path\":\"x\",\"state\":\"synced\"," +
            "\"last_sync_utc\":\"2026-10-27T20:14:00Z\",\"detail\":\"\",\"display_name\":\"Balatro\",\"original_name\":\"Balatro\"," +
            "\"last_action\":\"backup\",\"remote_head_machine\":\"Desktop PC\",\"machine_color\":\"#FFB74D\",\"local_files\":3," +
            "\"schedule\":{\"mode\":\"interval\",\"next_run_utc\":null},\"locations\":[{\"id\":\"a\",\"label\":\"l\",\"path\":\"p\",\"is_file\":false,\"filter\":null,\"enabled\":true,\"exists\":true,\"files\":1,\"size_bytes\":2}]}," +
            "{\"name\":\"Hades\",\"key\":\"hades\",\"exe\":\"\",\"install_dir\":\"\",\"save_path\":\"x\",\"state\":\"no_shared_folder\",\"last_sync_utc\":null,\"detail\":\"\"}]}";

        // Same with the additive origin fields.
        private static readonly string V2New = V2Old.Replace("\"remote_head_machine\":\"Desktop PC\"",
            "\"remote_head_machine\":\"Desktop PC\",\"origin_device\":\"Handheld\",\"origin_device_color\":\"#4FC3F7\"");

        [Fact]
        public void ReadsSchema2WithoutOriginFields()
        {
            var f = StatusParser.Parse(V2Old);
            Assert.NotNull(f);
            Assert.Equal(2, f.Schema);
            Assert.Equal("Handheld", f.Machine);
            var b = f.Games[0];
            Assert.Equal(AgssState.Synced, b.State);
            Assert.Equal("", b.OriginDevice);
            Assert.Equal("Desktop PC", b.RemoteHeadMachine);
            Assert.Equal("#FFB74D", b.MachineColor);
            Assert.Equal(AgssState.NoSharedFolder, f.Games[1].State);
        }

        [Fact]
        public void ReadsSchema2WithOriginFields()
        {
            var b = StatusParser.Parse(V2New).Games[0];
            Assert.Equal("Handheld", b.OriginDevice);
            Assert.Equal("#4FC3F7", b.OriginDeviceColor);
        }

        [Fact]
        public void FutureSchemaIsStillRead()
        {
            Assert.NotNull(StatusParser.Parse("{\"schema\":3,\"games\":[{\"name\":\"A\",\"state\":\"synced\",\"new_thing\":{\"x\":1}}]}"));
        }

        [Fact]
        public void DeviceFallsBackToRemoteHeadThenNothing()
        {
            string n, c;
            BadgeComposer.ResolveDevice(StatusParser.Parse(V2New).Games[0], out n, out c);
            Assert.Equal("Handheld", n); Assert.Equal("#4FC3F7", c);
            BadgeComposer.ResolveDevice(StatusParser.Parse(V2Old).Games[0], out n, out c);
            Assert.Equal("Desktop PC", n); Assert.Equal("#FFB74D", c);
            BadgeComposer.ResolveDevice(StatusParser.Parse(V2Old).Games[1], out n, out c);
            Assert.Null(n); Assert.Null(c);
            BadgeComposer.ResolveDevice(new AgssGameEntry { OriginDevice = "PC", OriginDeviceColor = "" }, out n, out c);
            Assert.Equal("PC", n); Assert.Null(c);
        }

        [Fact]
        public void MatchesOnOriginalNameToo()
        {
            var g = new GameCandidate { Id = Guid.NewGuid(), Name = "Hades II" };
            var m = Matcher.Match(new[] { new AgssGameEntry { Name = "My renamed game", Key = "k", OriginalName = "Hades II" } }, new[] { g });
            Assert.True(m.ContainsKey(g.Id));
        }
    }

    public class StateTextTests
    {
        [Fact]
        public void EveryStateHasShortTextInEveryLanguage()
        {
            foreach (var lang in StateTexts.Languages)
                foreach (AgssState s in Enum.GetValues(typeof(AgssState)))
                    if (s != AgssState.Unknown) Assert.True(StateTexts.Has(lang, s), lang + " " + s);
        }

        [Fact]
        public void EveryStateHasLongLabelInEveryLanguage()
        {
            foreach (var lang in StateTexts.Languages)
                foreach (AgssState s in Enum.GetValues(typeof(AgssState)))
                    if (s != AgssState.Unknown) Assert.False(string.IsNullOrWhiteSpace(Labels.Get(s, lang)), lang + " " + s);
        }

        [Fact]
        public void ShortTextsAreDistinctPerLanguage()
        {
            foreach (var lang in StateTexts.Languages)
            {
                var all = Enum.GetValues(typeof(AgssState)).Cast<AgssState>().Where(s => s != AgssState.Unknown).Select(s => StateTexts.Get(s, lang)).ToList();
                Assert.Equal(all.Count, all.Distinct().Count());
            }
        }

        [Theory]
        [InlineData("fr_FR", AgssState.Synced, "Protégé")]
        [InlineData("en_US", AgssState.Synced, "Protected")]
        [InlineData("es_ES", AgssState.NeverSynced, "Sin protección")]
        [InlineData("ru_RU", AgssState.Synced, "Защищено")]
        [InlineData("zh_CN", AgssState.Synced, "已保护")]
        [InlineData("de_DE", AgssState.Error, "Error")]
        public void LocalizedShortTexts(string lang, AgssState s, string expected)
        {
            Assert.Equal(expected, StateTexts.Get(s, lang));
        }

        [Fact]
        public void UnknownStateHasNoText() { Assert.Null(StateTexts.Get(AgssState.Unknown, "fr_FR")); }

        [Theory]
        [InlineData(AgssState.Synced, StateTone.Good)]
        [InlineData(AgssState.NeedsBackup, StateTone.Pending)]
        [InlineData(AgssState.CloudNewer, StateTone.Pending)]
        [InlineData(AgssState.Running, StateTone.Pending)]
        [InlineData(AgssState.Paused, StateTone.Pending)]
        [InlineData(AgssState.NeverSynced, StateTone.Bad)]
        [InlineData(AgssState.Conflict, StateTone.Bad)]
        [InlineData(AgssState.Error, StateTone.Bad)]
        [InlineData(AgssState.Unreachable, StateTone.Bad)]
        [InlineData(AgssState.NoSharedFolder, StateTone.Bad)]
        public void StateToTone(AgssState s, StateTone t) { Assert.Equal(t, StateTexts.Tone(s)); }

        [Fact]
        public void ToneToThemeKeysAndFallbackColours()
        {
            Assert.Equal("GreenBrush", ColorMath.ResourceKeys(StateTone.Good)[0]);
            Assert.Equal("OrangeBrush", ColorMath.ResourceKeys(StateTone.Pending)[0]);
            Assert.Equal("RedBrush", ColorMath.ResourceKeys(StateTone.Bad)[0]);
            Assert.Equal("#4CAF50", ColorMath.Fallback(StateTone.Good).ToString());
            Assert.Equal("#FF9800", ColorMath.Fallback(StateTone.Pending).ToString());
            Assert.Equal("#F44336", ColorMath.Fallback(StateTone.Bad).ToString());
        }
    }

    public class ColorTests
    {
        [Theory]
        [InlineData("#4FC3F7", true, "#4FC3F7")]
        [InlineData("4fc3f7", true, "#4FC3F7")]
        [InlineData("#abc", true, "#AABBCC")]
        [InlineData("", false, null)]
        [InlineData("#12345", false, null)]
        [InlineData("#GGGGGG", false, null)]
        [InlineData(null, false, null)]
        public void ParsesHex(string s, bool ok, string expected)
        {
            Rgb c;
            Assert.Equal(ok, ColorMath.TryParseHex(s, out c));
            if (ok) Assert.Equal(expected, c.ToString());
        }

        [Fact]
        public void ReadableColourIsKept()
        {
            var c = new Rgb(0x4F, 0xC3, 0xF7);
            Assert.Equal(c, ColorMath.EnsureReadable(c, ColorMath.DarkBackground));
        }

        [Fact]
        public void DarkColourIsLightenedOnDarkBackground()
        {
            var navy = new Rgb(0x0D, 0x1B, 0x6E);
            var r = ColorMath.EnsureReadable(navy, ColorMath.DarkBackground);
            Assert.NotEqual(navy, r);
            Assert.True(ColorMath.Contrast(r, ColorMath.DarkBackground) >= ColorMath.MinContrast);
            Assert.True(ColorMath.Luminance(r) > ColorMath.Luminance(navy));
            Assert.True(r.B > r.R); // still blue-ish, not plain white
        }

        [Fact]
        public void LightColourIsDarkenedOnLightBackground()
        {
            var yellow = new Rgb(0xFF, 0xF1, 0x76);
            var r = ColorMath.EnsureReadable(yellow, ColorMath.LightBackground);
            Assert.True(ColorMath.Contrast(r, ColorMath.LightBackground) >= ColorMath.MinContrast);
        }

        [Fact]
        public void BackgroundGuessedFromTextColour()
        {
            Assert.Equal(ColorMath.DarkBackground, ColorMath.BackgroundForText(new Rgb(255, 255, 255)));
            Assert.Equal(ColorMath.LightBackground, ColorMath.BackgroundForText(new Rgb(20, 20, 20)));
        }
    }

    public class WhenTests
    {
        private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

        [Fact]
        public void FrenchShortDateAndTimeInLocalTime()
        {
            // 20:14 UTC on 27/10/2026 = 21:14 in Paris (winter time)
            Assert.Equal("27/10 21:14", WhenFormatter.Format("2026-10-27T20:14:00Z", CultureInfo.GetCultureInfo("fr-FR"), Paris, new DateTime(2026, 10, 27)));
        }

        [Fact]
        public void EnglishUsesItsOwnOrderAndClock()
        {
            var s = WhenFormatter.Format("2026-10-27T20:14:00Z", CultureInfo.GetCultureInfo("en-US"), Paris, new DateTime(2026, 10, 27));
            Assert.StartsWith("10/27 9:14", s);
        }

        [Fact]
        public void YearShownWhenNotCurrentYear()
        {
            Assert.Equal("27/10/2025 21:14", WhenFormatter.Format("2025-10-27T20:14:00Z", CultureInfo.GetCultureInfo("fr-FR"), Paris, new DateTime(2026, 10, 27)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a date")]
        public void InvalidDateGivesNull(string iso)
        {
            Assert.Null(WhenFormatter.Format(iso, CultureInfo.GetCultureInfo("fr-FR"), Paris, DateTime.Now));
        }

        [Theory]
        [InlineData("fr-FR", "dd/MM")]
        [InlineData("en-US", "M/d")]
        [InlineData("ru-RU", "dd.MM")]
        [InlineData("es-ES", "dd/MM")]
        [InlineData("zh-CN", "M/d")]
        public void DayMonthPatternWithoutYear(string culture, string expected)
        {
            Assert.Equal(expected, WhenFormatter.DayMonthPattern(CultureInfo.GetCultureInfo(culture)));
        }

        [Theory]
        [InlineData("fr_FR", "fr-FR")]
        [InlineData("zh_CN", "zh-CN")]
        [InlineData("english", "en-US")]
        [InlineData(null, "en-US")]
        public void CultureFromPlayniteLanguage(string lang, string expected)
        {
            Assert.Equal(expected, WhenFormatter.CultureFor(lang).Name);
        }
    }

    public class BadgeModelTests
    {
        private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

        [Fact]
        public void ComposesFullBadge()
        {
            var e = new AgssGameEntry { State = AgssState.Synced, LastSyncUtc = "2026-10-27T20:14:00Z", OriginDevice = "Handheld", OriginDeviceColor = "#4FC3F7" };
            var m = BadgeComposer.Compose(e, "fr_FR", Paris, new DateTime(2026, 10, 27));
            Assert.Equal(StateTone.Good, m.Tone);
            Assert.Equal("Protégé · 27/10 21:14 · ● Handheld", m.PlainText);
            Assert.Equal(new Rgb(0x4F, 0xC3, 0xF7), m.DeviceColor.Value);
        }

        [Fact]
        public void MissingPartsAreOmitted()
        {
            var m = BadgeComposer.Compose(new AgssGameEntry { State = AgssState.NeverSynced, OriginDeviceColor = "#zzz" }, "en_US", Paris, DateTime.Now);
            Assert.Equal("Not protected", m.PlainText);
            Assert.Null(m.DeviceColor);
            Assert.Null(BadgeComposer.Compose(new AgssGameEntry { State = AgssState.Unknown }, "en_US", Paris, DateTime.Now));
            Assert.Null(BadgeComposer.Compose(null, "en_US", Paris, DateTime.Now));
        }
    }

    public class FallbackGenreTests
    {
        private const string P = FallbackGenre.DefaultPrefix;

        private static BadgeModel M(AgssState s, string device = null, string when = "27/10 21:14")
        {
            return new BadgeModel { State = s, StateText = StateTexts.Get(s, "fr_FR"), DeviceText = device, WhenText = when };
        }

        [Fact]
        public void GenreHasStateAndDeviceButNeverDate()
        {
            Assert.Equal("● AGSS : Protégé · Handheld", FallbackGenre.Text(M(AgssState.Synced, "Handheld"), P));
            Assert.Equal("● AGSS : Non protégé", FallbackGenre.Text(M(AgssState.NeverSynced), P));
            Assert.DoesNotContain("21:14", FallbackGenre.Text(M(AgssState.Synced, "Handheld"), P));
            Assert.Null(FallbackGenre.Text(null, P));
        }

        [Fact]
        public void GenreHasNoEmoji()
        {
            foreach (var lang in StateTexts.Languages)
                foreach (AgssState s in Enum.GetValues(typeof(AgssState)))
                {
                    if (s == AgssState.Unknown) continue;
                    var t = FallbackGenre.Text(new BadgeModel { StateText = StateTexts.Get(s, lang), DeviceText = "PC" }, P);
                    Assert.DoesNotContain(t, ch => char.IsSurrogate(ch)); // no character outside the basic plane (emoji)
                }
        }

        [Fact]
        public void PrefixSortsFirstInEveryLanguage()
        {
            // Before letters and digits in every language (a genre starting with another symbol such as "#" may still come first).
            var genres = new[] { "Action", "Aventure", "1st person", "Ação", "Экшен", "动作", "Zombie", "3D" };
            foreach (var c in new[] { "fr-FR", "en-US", "ru-RU", "es-ES", "zh-CN" })
            {
                var cmp = StringComparer.Create(CultureInfo.GetCultureInfo(c), false);
                var sorted = genres.Concat(new[] { "● AGSS : Protégé · Handheld" }).OrderBy(x => x, cmp).ToList();
                Assert.StartsWith("●", sorted[0]);
            }
        }

        [Fact]
        public void StateChangeReplacesOldGenreAndKeepsOthers()
        {
            var r = ItemPlanner.Plan(new[] { "Action", P + "Protégé · Handheld", "Roguelike" }, P, P + "À envoyer · Handheld");
            Assert.Equal(new[] { "Action", "Roguelike", P + "À envoyer · Handheld" }, r.ToArray());
        }

        [Fact]
        public void BadgeModeRemovesGenre()
        {
            Assert.Equal(new[] { "Action" }, ItemPlanner.Plan(new[] { "Action", P + "Protégé" }, P, null).ToArray());
        }

        [Fact]
        public void MetadataOverwriteIsRepaired()
        {
            // A metadata download replaced the genres: our genre is missing and must come back.
            var r = ItemPlanner.Plan(new[] { "Card Game", "Strategy" }, P, P + "Protégé · Handheld");
            Assert.Equal(new[] { "Card Game", "Strategy", P + "Protégé · Handheld" }, r.ToArray());
        }

        [Fact]
        public void UnusedOwnGenresAreFound()
        {
            var items = new[] { P + "Protégé · Handheld", P + "À envoyer · Handheld", "Action", "AGSS : old" };
            var used = new HashSet<string> { P + "Protégé · Handheld" };
            var unused = FallbackGenre.Unused(items, x => x, x => used.Contains(x), P);
            Assert.Equal(new[] { P + "À envoyer · Handheld" }, unused.ToArray());
            Assert.Empty(FallbackGenre.Unused(items, x => x, x => false, ""));
        }
    }

    public class ChooserTests
    {
        [Theory]
        [InlineData(DisplayMode.Auto, true, false, false, DisplayKind.Badge)]
        [InlineData(DisplayMode.Auto, false, false, false, DisplayKind.Field)]
        [InlineData(DisplayMode.Auto, true, true, false, DisplayKind.Field)]
        [InlineData(DisplayMode.Auto, false, false, true, DisplayKind.Badge)]
        [InlineData(DisplayMode.BadgeOnly, false, false, false, DisplayKind.Badge)]
        [InlineData(DisplayMode.FieldOnly, true, false, false, DisplayKind.Field)]
        public void ChoosesBadgeOrField(DisplayMode mode, bool recipe, bool failed, bool custom, DisplayKind expected)
        {
            Assert.Equal(expected, DisplayChooser.Choose(mode, recipe, failed, custom));
        }

        [Fact]
        public void SolarisRecipeFoundOnlyForFullscreenSolaris()
        {
            Assert.NotNull(ThemeRecipes.Find("Solaris_b6e50d04-24ae-4ecd-bd3a-080367930992", true));
            Assert.NotNull(ThemeRecipes.Find("solaris_B6E50D04-24ae-4ecd-bd3a-080367930992", true));
            Assert.Null(ThemeRecipes.Find("Solaris_b6e50d04-24ae-4ecd-bd3a-080367930992", false));
            Assert.Null(ThemeRecipes.Find("Playnite_builtin_DefaultFullscreen", true));
            Assert.Null(ThemeRecipes.Find("AgssTest_SolarisWithoutRecipe", true));
            Assert.Null(ThemeRecipes.Find(null, true));
        }

        [Fact]
        public void RecipesAreComplete()
        {
            foreach (var r in ThemeRecipes.All.Where(x => x.Layout == BadgeLayout.Welded)) // copied layouts: see RecipeTests
            {
                Assert.False(string.IsNullOrEmpty(r.RowName));
                Assert.False(string.IsNullOrEmpty(r.AnchorName));
                Assert.False(string.IsNullOrEmpty(r.BoxStyleKey));
                Assert.NotEmpty(r.ThemeIds);
            }
        }
    }

    public class LocatorAndSettingsTests
    {
        [Fact]
        public void ExplicitPathWins()
        {
            Assert.Equal(@"X:\s.json", DataLocator.Resolve(@"X:\s.json", new[] { @"E:\env" }, @"C:\L", new string[0], p => true));
        }

        [Fact]
        public void EnvironmentThenDefaultThenLegacy()
        {
            var env = new[] { null, @"E:\env" };
            var legacy = new[] { @"C:\L\Old\status.json" };
            Assert.Equal(@"E:\env\status.json", DataLocator.Resolve("", env, @"C:\L", legacy, p => p == @"E:\env\status.json"));
            Assert.Equal(@"C:\L\AutoGameSoftwareSync\status.json", DataLocator.Resolve("", env, @"C:\L", legacy, p => p == @"C:\L\AutoGameSoftwareSync\status.json"));
            Assert.Equal(@"C:\L\Old\status.json", DataLocator.Resolve("", env, @"C:\L", legacy, p => p == @"C:\L\Old\status.json"));
            // Nothing exists yet: the environment folder (or the default) is watched until the file appears.
            Assert.Equal(@"E:\env\status.json", DataLocator.Resolve("", env, @"C:\L", legacy, p => false));
            Assert.Equal(@"C:\L\AutoGameSoftwareSync\status.json", DataLocator.Resolve("", null, @"C:\L", null, p => false));
        }

        [Fact]
        public void OldSettingsAreMigratedAndOldItemsScheduledForCleanup()
        {
            var s = new AgssSettings { SettingsVersion = 0, FieldIndex = 0, Prefix = "AGSS : " };
            Assert.True(AgssSettings.Migrate(s));
            Assert.Equal(1, s.FieldIndex);
            Assert.Equal(FallbackGenre.DefaultPrefix, s.Prefix);
            Assert.Equal("AGSS : ", s.CleanupPrefix);
            Assert.Equal(0, s.CleanupFieldIndex);
            Assert.False(AgssSettings.Migrate(s)); // once only
        }

        [Fact]
        public void NewSettingsDefaultToAutomaticGenreFallback()
        {
            var s = new AgssSettings();
            Assert.Equal(DisplayMode.Auto, s.Mode);
            Assert.Equal(1, s.FieldIndex);
            Assert.Equal("● AGSS : ", s.Prefix);
            Assert.True(s.Enabled);
            Assert.Equal("", s.StatusPath);
        }
    }
}
