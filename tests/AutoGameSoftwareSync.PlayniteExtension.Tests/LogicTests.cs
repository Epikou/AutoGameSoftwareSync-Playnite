using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoGameSoftwareSync.PlayniteExtension.Logic;
using Xunit;

namespace AutoGameSoftwareSync.PlayniteExtension.Tests
{
    public class ParsingTests
    {
        private const string Good = "{\"schema\":1,\"app_version\":\"1.0.0\",\"updated_at_utc\":\"2026-01-01T00:00:00Z\",\"watching\":true,\"shared_folder_reachable\":true,\"games\":[" +
            "{\"name\":\"Hades\",\"key\":\"hades\",\"exe\":\"Hades.exe\",\"install_dir\":\"C:\\\\G\\\\Hades\",\"save_path\":\"x\",\"state\":\"cloud_newer\",\"last_sync_utc\":null,\"detail\":\"d\"}]}";

        [Fact]
        public void ParsesValidFile()
        {
            var f = StatusParser.Parse(Good);
            Assert.True(f.Watching);
            Assert.Equal(AgssState.CloudNewer, f.Games[0].State);
            Assert.Equal("Hades.exe", f.Games[0].Exe);
            Assert.Equal(@"C:\G\Hades", f.Games[0].InstallDir);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{\"schema\":1,\"games\":[{\"name\":")]   // partially written
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("{\"schema\":0,\"games\":[]}")]            // invalid schema (2.0.x writes 2: now accepted)
        [InlineData("{\"schema\":\"2\",\"games\":[]}")]        // schema must be a number
        [InlineData("{\"games\":[]}")]
        public void InvalidInputReturnsNullNeverThrows(string json)
        {
            Assert.Null(StatusParser.Parse(json));
        }

        [Fact]
        public void ToleratesBomAndMissingFields()
        {
            var f = StatusParser.Parse("\uFEFF{\"schema\":1,\"games\":[{\"name\":\"A\"},5,null]}");
            Assert.False(f.Watching);
            Assert.Single(f.Games);
            Assert.Equal(AgssState.Unknown, f.Games[0].State);
        }

        [Fact]
        public void FileSourceNeverThrows()
        {
            var dir = Path.Combine(Path.GetTempPath(), "agss_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "status.json");
            try
            {
                var src = new FileStatusSource(() => p);
                Assert.Null(src.Read());                       // absent
                File.WriteAllText(p, "{broken");
                Assert.Null(src.Read());                       // invalid
                File.WriteAllText(p, Good);
                Assert.NotNull(src.Read());                    // valid
                using (new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                    Assert.NotNull(src.Read());                // opened by writer
                Assert.Null(new FileStatusSource(() => "?:\\bad|path").Read());
            }
            finally { Directory.Delete(dir, true); }
        }
    }

    public class MatchingTests
    {
        private static GameCandidate G(string name, string exe = null, string dir = "")
        {
            var c = new GameCandidate { Id = Guid.NewGuid(), Name = name, InstallDir = dir };
            if (exe != null) c.ExeNames.Add(exe);
            return c;
        }

        [Fact]
        public void MatchesExeCaseInsensitive()
        {
            var g = G("Whatever", "HADES.EXE");
            var m = Matcher.Match(new[] { new AgssGameEntry { Name = "Hades", Exe = "hades.exe" } }, new[] { g });
            Assert.True(m.ContainsKey(g.Id));
        }

        [Fact]
        public void FallsBackToNormalizedNameThenInstallDir()
        {
            var a = G("The Witcher 3: Wild Hunt");
            var b = G("Other", null, @"Z:\Games\X\");
            var m = Matcher.Match(new[]
            {
                new AgssGameEntry { Name = "x", Key = "thewitcher3wildhunt" },
                new AgssGameEntry { Name = "y", Key = "y", InstallDir = @"z:\games\x" }
            }, new[] { a, b });
            Assert.Equal(2, m.Count);
        }

        [Fact]
        public void AppliesToAllMatchesAndIgnoresUnknown()
        {
            var a = G("Dup", "d.exe"); var b = G("Dup 2", "d.exe"); var c = G("Zzz");
            var m = Matcher.Match(new[] { new AgssGameEntry { Exe = "d.exe" }, new AgssGameEntry { Name = "Nope", Key = "nope" } }, new[] { a, b, c });
            Assert.Equal(2, m.Count);
            Assert.False(m.ContainsKey(c.Id));
        }

        [Fact]
        public void EmptyExeDoesNotMatchEverything()
        {
            var m = Matcher.Match(new[] { new AgssGameEntry { Name = "A", Key = "a", Exe = "" } }, new[] { G("B", "") });
            Assert.Empty(m);
        }
    }

    public class PlannerTests
    {
        private const string P = "AGSS : ";

        [Fact]
        public void NoChangeWhenAlreadyCorrect()
        {
            Assert.Null(ItemPlanner.Plan(new[] { "RPG", P + "Synced" }, P, P + "Synced"));
        }

        [Fact]
        public void ReplacesOldAndKeepsForeignItems()
        {
            var r = ItemPlanner.Plan(new[] { "RPG", P + "Conflict", "AGSSX" }, P, P + "Synced");
            Assert.Equal(new[] { "RPG", "AGSSX", P + "Synced" }, r.ToArray());
        }

        [Fact]
        public void RemovesDuplicatesAndNullDesiredRemovesOurs()
        {
            Assert.Equal(new[] { P + "Synced" }, ItemPlanner.Plan(new[] { P + "Synced", P + "Synced" }, P, P + "Synced").ToArray());
            Assert.Equal(new[] { "Keep" }, ItemPlanner.Plan(new[] { "Keep", P + "Error" }, P, null).ToArray());
            Assert.Null(ItemPlanner.Plan(new[] { "Keep" }, P, null));
        }

        [Fact]
        public void DesiredRespectsShowSyncedAndUnknown()
        {
            Assert.Null(ItemPlanner.Desired(AgssState.Synced, P, "en", false));
            Assert.Equal(P + "Synced", ItemPlanner.Desired(AgssState.Synced, P, "en_US", true));
            Assert.Null(ItemPlanner.Desired(AgssState.Unknown, P, "en", true));
        }

        [Fact]
        public void NotifiesOnlyOnTransitionToConflictOrCloudNewer()
        {
            Assert.False(ItemPlanner.ShouldNotify(null, AgssState.Conflict));
            Assert.True(ItemPlanner.ShouldNotify(AgssState.Synced, AgssState.Conflict));
            Assert.True(ItemPlanner.ShouldNotify(AgssState.Synced, AgssState.CloudNewer));
            Assert.False(ItemPlanner.ShouldNotify(AgssState.Conflict, AgssState.Conflict));
            Assert.False(ItemPlanner.ShouldNotify(AgssState.Conflict, AgssState.Synced));
        }

        [Fact]
        public void NotWatchingOnlyAppliesUnreachableOrPaused()
        {
            var f = new AgssStatusFile { Watching = false };
            f.Games.Add(new AgssGameEntry { State = AgssState.Synced });
            f.Games.Add(new AgssGameEntry { State = AgssState.Paused });
            Assert.Single(ItemPlanner.Applicable(f));
        }
    }

    public class LabelTests
    {
        [Theory]
        [InlineData("fr_FR", AgssState.CloudNewer, "Cloud plus récent")]
        [InlineData("en_US", AgssState.CloudNewer, "Cloud newer")]
        [InlineData("es_ES", AgssState.Conflict, "Conflicto")]
        [InlineData("ru_RU", AgssState.Paused, "Пауза")]
        [InlineData("zh_CN", AgssState.Synced, "已同步")]
        [InlineData("de_DE", AgssState.Error, "Error")]   // fallback to English
        public void LocalizedLabels(string lang, AgssState state, string expected)
        {
            Assert.Equal(expected, Labels.Get(state, lang));
        }

        [Fact]
        public void EveryStateHasLabelInEveryLanguage()
        {
            foreach (var lang in new[] { "en", "fr", "ru", "es", "zh" })
                foreach (AgssState s in Enum.GetValues(typeof(AgssState)))
                    if (s != AgssState.Unknown) Assert.False(string.IsNullOrEmpty(Labels.Get(s, lang)));
        }

        [Fact]
        public void FakeSourceWorks()
        {
            var f = new FakeStatusSource { Current = new AgssStatusFile() };
            Assert.NotNull(f.Read());
        }
    }
}

namespace AutoGameSoftwareSync.PlayniteExtension.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using AutoGameSoftwareSync.PlayniteExtension.Logic;
    using Xunit;

    /// <summary>1.2.0: only items in the extension's exact format are ever removed; the prefix must be distinctive.</summary>
    public class OwnItemsTests
    {
        private const string P = "● AGSS : ";

        [Fact]
        public void RecognisesOwnFormatInEveryLanguageAndOldLabels()
        {
            Assert.True(OwnItems.IsOwn(P + "Protected", P));
            Assert.True(OwnItems.IsOwn(P + "Protected · EPIGOS", P));
            Assert.True(OwnItems.IsOwn(P + "Protégé · Mon PC portable", P));
            Assert.True(OwnItems.IsOwn(P + "Защищено", P));
            Assert.True(OwnItems.IsOwn(P + "已同步", P));
            Assert.True(OwnItems.IsOwn("AGSS : Synced", "AGSS : "), "labels of versions 0.x (cleanup of the old field)");
            Assert.True(OwnItems.IsOwn("AGSS : Conflict", "AGSS : "));
        }

        [Fact]
        public void NeverAUserItem()
        {
            Assert.False(OwnItems.IsOwn("Action", "Action"));
            Assert.False(OwnItems.IsOwn("Action RPG", "Action"));
            Assert.False(OwnItems.IsOwn("Adventure", "A"));
            Assert.False(OwnItems.IsOwn(P + "Something else", P));
            Assert.False(OwnItems.IsOwn(P + "Protected ·", P), "device separator without a device");
            Assert.False(OwnItems.IsOwn(P + "Protected · ", P));
            Assert.False(OwnItems.IsOwn(P, P));
            Assert.False(OwnItems.IsOwn(null, P));
            Assert.False(OwnItems.IsOwn(P + "Protected", ""));
            Assert.False(OwnItems.IsOwn(P + "Protected", null));
        }

        [Fact]
        public void GenericPrefixNeverDeletesUserGenres()
        {
            // The scenario of the quality review: prefix "Action" (or "A") must not touch "Action", "Action RPG", "Adventure".
            Assert.Null(ItemPlanner.Plan(new[] { "Action", "Action RPG" }, "Action", null));
            Assert.Null(ItemPlanner.Plan(new[] { "Action", "Adventure" }, "A", null));
            Assert.Equal(new[] { "Action", "Action RPG", "Action" + "Protected" }, ItemPlanner.Plan(new[] { "Action", "Action RPG" }, "Action", "ActionProtected").ToArray());
            var items = new[] { "Action", "Action RPG", "ActionProtected" };
            Assert.Equal(new[] { "ActionProtected" }, FallbackGenre.Unused(items, x => x, x => false, "Action").ToArray());
            // Prefix changed by the user: old items in the old format are cleaned, user items with the same letters are kept.
            Assert.Equal(new[] { "AGSSX", "AGSS : my own" }, ItemPlanner.Plan(new[] { "AGSS : Synced", "AGSSX", "AGSS : my own" }, "AGSS : ", null).ToArray());
        }

        [Fact]
        public void PrefixRule()
        {
            Assert.Null(OwnItems.PrefixProblem(FallbackGenre.DefaultPrefix));
            Assert.Null(OwnItems.PrefixProblem("AGSS : "));
            Assert.Null(OwnItems.PrefixProblem("[Saves] "));
            Assert.Null(OwnItems.PrefixProblem("Sync:"));
            Assert.NotNull(OwnItems.PrefixProblem(""));
            Assert.NotNull(OwnItems.PrefixProblem("   "));
            Assert.NotNull(OwnItems.PrefixProblem("A"));
            Assert.NotNull(OwnItems.PrefixProblem("Action"));
            Assert.NotNull(OwnItems.PrefixProblem("Action "));
            Assert.NotNull(OwnItems.PrefixProblem("A:"));
        }
    }
}
