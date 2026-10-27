using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoGameSoftwareSync.PlayniteExtension.Logic
{
    /// <summary>Colour family of a state: green = protected, orange = waiting / in progress, red = not protected / error.</summary>
    public enum StateTone { Good, Pending, Bad }

    /// <summary>Short state texts shown in the badge and in the fallback genre (5 languages, English fallback).</summary>
    public static class StateTexts
    {
        public static readonly string[] Languages = { "en", "fr", "ru", "es", "zh" };

        private static readonly Dictionary<string, Dictionary<AgssState, string>> table = new Dictionary<string, Dictionary<AgssState, string>>
        {
            ["en"] = new Dictionary<AgssState, string>
            {
                [AgssState.Synced] = "Protected", [AgssState.NeedsBackup] = "To upload", [AgssState.CloudNewer] = "To download",
                [AgssState.Conflict] = "Conflict", [AgssState.Running] = "Playing", [AgssState.NeverSynced] = "Not protected",
                [AgssState.Error] = "Error", [AgssState.Unreachable] = "Unreachable", [AgssState.Paused] = "Paused",
                [AgssState.NoSharedFolder] = "Not set up",
            },
            ["fr"] = new Dictionary<AgssState, string>
            {
                [AgssState.Synced] = "Protégé", [AgssState.NeedsBackup] = "À envoyer", [AgssState.CloudNewer] = "À récupérer",
                [AgssState.Conflict] = "Conflit", [AgssState.Running] = "En cours de jeu", [AgssState.NeverSynced] = "Non protégé",
                [AgssState.Error] = "Erreur", [AgssState.Unreachable] = "Injoignable", [AgssState.Paused] = "En pause",
                [AgssState.NoSharedFolder] = "Non configuré",
            },
            ["ru"] = new Dictionary<AgssState, string>
            {
                [AgssState.Synced] = "Защищено", [AgssState.NeedsBackup] = "Ждёт отправки", [AgssState.CloudNewer] = "Ждёт загрузки",
                [AgssState.Conflict] = "Конфликт", [AgssState.Running] = "Идёт игра", [AgssState.NeverSynced] = "Не защищено",
                [AgssState.Error] = "Ошибка", [AgssState.Unreachable] = "Недоступно", [AgssState.Paused] = "Пауза",
                [AgssState.NoSharedFolder] = "Не настроено",
            },
            ["es"] = new Dictionary<AgssState, string>
            {
                [AgssState.Synced] = "Protegido", [AgssState.NeedsBackup] = "Por enviar", [AgssState.CloudNewer] = "Por recuperar",
                [AgssState.Conflict] = "Conflicto", [AgssState.Running] = "Jugando", [AgssState.NeverSynced] = "Sin protección",
                [AgssState.Error] = "Error", [AgssState.Unreachable] = "Inaccesible", [AgssState.Paused] = "En pausa",
                [AgssState.NoSharedFolder] = "Sin configurar",
            },
            ["zh"] = new Dictionary<AgssState, string>
            {
                [AgssState.Synced] = "已保护", [AgssState.NeedsBackup] = "待上传", [AgssState.CloudNewer] = "待下载",
                [AgssState.Conflict] = "冲突", [AgssState.Running] = "游戏中", [AgssState.NeverSynced] = "未保护",
                [AgssState.Error] = "错误", [AgssState.Unreachable] = "无法访问", [AgssState.Paused] = "已暂停",
                [AgssState.NoSharedFolder] = "未设置",
            },
        };

        /// <summary>Short text, or null for <see cref="AgssState.Unknown"/>.</summary>
        public static string Get(AgssState state, string playniteLanguage)
        {
            if (state == AgssState.Unknown) return null;
            Dictionary<AgssState, string> t;
            if (!table.TryGetValue(Labels.LanguageCode(playniteLanguage), out t)) t = table["en"];
            string s;
            return t.TryGetValue(state, out s) ? s : table["en"][state];
        }

        /// <summary>Raw table access for completeness tests.</summary>
        public static bool Has(string language, AgssState state)
        {
            Dictionary<AgssState, string> t; string s;
            return table.TryGetValue(language, out t) && t.TryGetValue(state, out s) && !string.IsNullOrWhiteSpace(s);
        }

        public static StateTone Tone(AgssState state)
        {
            switch (state)
            {
                case AgssState.Synced: return StateTone.Good;
                case AgssState.NeedsBackup:
                case AgssState.CloudNewer:
                case AgssState.Running:
                case AgssState.Paused: return StateTone.Pending;
                default: return StateTone.Bad;
            }
        }
    }

    /// <summary>Simple RGB colour without any WPF dependency (testable).</summary>
    public struct Rgb : IEquatable<Rgb>
    {
        public readonly byte R, G, B;
        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public bool Equals(Rgb o) { return R == o.R && G == o.G && B == o.B; }
        public override bool Equals(object o) { return o is Rgb && Equals((Rgb)o); }
        public override int GetHashCode() { return (R << 16) | (G << 8) | B; }
        public override string ToString() { return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2"); }
    }

    public static class ColorMath
    {
        /// <summary>Fixed colours used when the theme has no matching resource.</summary>
        public static readonly Rgb FallbackGood = new Rgb(0x4C, 0xAF, 0x50);
        public static readonly Rgb FallbackPending = new Rgb(0xFF, 0x98, 0x00);
        public static readonly Rgb FallbackBad = new Rgb(0xF4, 0x43, 0x36);
        public static readonly Rgb DarkBackground = new Rgb(0x26, 0x26, 0x26);
        public static readonly Rgb LightBackground = new Rgb(0xF0, 0xF0, 0xF0);
        public const double MinContrast = 4.5;

        public static Rgb Fallback(StateTone tone)
        {
            return tone == StateTone.Good ? FallbackGood : tone == StateTone.Pending ? FallbackPending : FallbackBad;
        }

        /// <summary>Theme resource keys tried in order for a tone (first found wins).</summary>
        public static string[] ResourceKeys(StateTone tone)
        {
            switch (tone)
            {
                case StateTone.Good: return new[] { "GreenBrush", "SuccessBrush" };
                case StateTone.Pending: return new[] { "OrangeBrush", "WarningBrush", "YellowBrush" };
                default: return new[] { "RedBrush", "ErrorBrush", "FailureBrush" };
            }
        }

        /// <summary>Accepts "#RRGGBB", "RRGGBB" or "#RGB". Anything else: false.</summary>
        public static bool TryParseHex(string s, out Rgb color)
        {
            color = default(Rgb);
            if (string.IsNullOrWhiteSpace(s)) return false;
            var h = s.Trim().TrimStart('#');
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            if (h.Length != 6) return false;
            int v;
            if (!int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        private static double Channel(byte c)
        {
            var x = c / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        public static double Luminance(Rgb c) { return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B); }

        public static double Contrast(Rgb a, Rgb b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Background assumed behind the badge: dark when the theme text is light, light otherwise.</summary>
        public static Rgb BackgroundForText(Rgb text) { return Luminance(text) > 0.4 ? DarkBackground : LightBackground; }

        private static Rgb Mix(Rgb c, Rgb to, double t)
        {
            Func<byte, byte, byte> m = (x, y) => (byte)Math.Round(x + (y - x) * t);
            return new Rgb(m(c.R, to.R), m(c.G, to.G), m(c.B, to.B));
        }

        /// <summary>Lightens (dark background) or darkens (light background) a colour just enough to be readable.</summary>
        public static Rgb EnsureReadable(Rgb color, Rgb background)
        {
            if (Contrast(color, background) >= MinContrast) return color;
            var target = Luminance(background) < 0.5 ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
            for (int i = 1; i <= 20; i++)
            {
                var c = Mix(color, target, i * 0.05);
                if (Contrast(c, background) >= MinContrast) return c;
            }
            return target;
        }
    }

    /// <summary>Date and time of the last sync, in local time, short form following Playnite's language.</summary>
    public static class WhenFormatter
    {
        public static CultureInfo CultureFor(string playniteLanguage)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(playniteLanguage) && playniteLanguage.Contains("_"))
                    return CultureInfo.GetCultureInfo(playniteLanguage.Replace('_', '-'));
            }
            catch (CultureNotFoundException) { }
            switch (Labels.LanguageCode(playniteLanguage))
            {
                case "fr": return CultureInfo.GetCultureInfo("fr-FR");
                case "ru": return CultureInfo.GetCultureInfo("ru-RU");
                case "es": return CultureInfo.GetCultureInfo("es-ES");
                case "zh": return CultureInfo.GetCultureInfo("zh-CN");
                default: return CultureInfo.GetCultureInfo("en-US");
            }
        }

        /// <summary>Short date pattern of the culture without the year ("dd/MM/yyyy" -> "dd/MM", "yyyy/M/d" -> "M/d").</summary>
        public static string DayMonthPattern(CultureInfo culture)
        {
            var p = culture.DateTimeFormat.ShortDatePattern;
            p = Regex.Replace(p, @"^y+[^dM]*", "");
            p = Regex.Replace(p, @"[^dM]*y+$", "");
            p = Regex.Replace(p, @"y+[^dM]*", "");
            p = p.Trim();
            return p.IndexOf('d') >= 0 && p.IndexOf('M') >= 0 ? p : "dd/MM";
        }

        public static bool TryParseUtc(string iso, out DateTime utc)
        {
            utc = default(DateTime);
            if (string.IsNullOrWhiteSpace(iso)) return false;
            DateTime d;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return false;
            utc = DateTime.SpecifyKind(d, DateTimeKind.Utc);
            return true;
        }

        /// <summary>"27/10 21:14" (fr), "10/27 9:14 PM" (en)... The year is added when it is not the current year. Null if unparsable.</summary>
        public static string Format(string isoUtc, CultureInfo culture, TimeZoneInfo zone, DateTime nowLocal)
        {
            DateTime utc;
            if (!TryParseUtc(isoUtc, out utc)) return null;
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone ?? TimeZoneInfo.Local);
            var date = local.Year == nowLocal.Year ? DayMonthPattern(culture) : culture.DateTimeFormat.ShortDatePattern;
            return local.ToString(date + " " + culture.DateTimeFormat.ShortTimePattern, culture);
        }
    }

    /// <summary>Everything the badge shows for one game, computed without WPF.</summary>
    public class BadgeModel
    {
        public AgssState State;
        public StateTone Tone;
        public string StateText;
        /// <summary>Null when the last sync date is unknown.</summary>
        public string WhenText;
        /// <summary>Null when no device is known.</summary>
        public string DeviceText;
        /// <summary>Null when the device colour is unknown or invalid (theme text colour is then used).</summary>
        public Rgb? DeviceColor;

        /// <summary>Single-line text (tooltips, logs, custom elements without rich text).</summary>
        public string PlainText
        {
            get
            {
                var parts = new List<string> { StateText };
                if (WhenText != null) parts.Add(WhenText);
                if (DeviceText != null) parts.Add("● " + DeviceText);
                return string.Join(" · ", parts);
            }
        }
    }

    public static class BadgeComposer
    {
        /// <summary>Device of the version in place: origin_device (new apps), else remote_head_machine, else nothing.</summary>
        public static void ResolveDevice(AgssGameEntry e, out string name, out string colorHex)
        {
            name = null; colorHex = null;
            if (e == null) return;
            if (!string.IsNullOrWhiteSpace(e.OriginDevice))
            {
                name = e.OriginDevice.Trim();
                colorHex = string.IsNullOrWhiteSpace(e.OriginDeviceColor) ? null : e.OriginDeviceColor;
            }
            else if (!string.IsNullOrWhiteSpace(e.RemoteHeadMachine))
            {
                name = e.RemoteHeadMachine.Trim();
                colorHex = string.IsNullOrWhiteSpace(e.MachineColor) ? null : e.MachineColor;
            }
        }

        /// <summary>Null when the game must show nothing (no entry or unknown state).</summary>
        public static BadgeModel Compose(AgssGameEntry e, string playniteLanguage, TimeZoneInfo zone, DateTime nowLocal)
        {
            if (e == null || e.State == AgssState.Unknown) return null;
            string device, hex;
            ResolveDevice(e, out device, out hex);
            Rgb c;
            return new BadgeModel
            {
                State = e.State,
                Tone = StateTexts.Tone(e.State),
                StateText = StateTexts.Get(e.State, playniteLanguage),
                WhenText = WhenFormatter.Format(e.LastSyncUtc, WhenFormatter.CultureFor(playniteLanguage), zone, nowLocal),
                DeviceText = device,
                DeviceColor = ColorMath.TryParseHex(hex, out c) ? (Rgb?)c : null
            };
        }
    }

    /// <summary>Fallback genre "● AGSS : state · device" (never a date: one genre per state/device pair, few distinct values).</summary>
    public static class FallbackGenre
    {
        public const string DefaultPrefix = "● AGSS : ";

        public static string Text(BadgeModel m, string prefix)
        {
            if (m == null) return null;
            return prefix + m.StateText + (m.DeviceText != null ? " · " + m.DeviceText : "");
        }

        /// <summary>Names of our own items (prefix) that no game uses any more.</summary>
        public static List<T> Unused<T>(IEnumerable<T> items, Func<T, string> name, Func<T, bool> isUsed, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return new List<T>();
            return items.Where(i => { var n = name(i); return n != null && n.StartsWith(prefix, StringComparison.Ordinal) && !isUsed(i); }).ToList();
        }
    }

    /// <summary>User choice in the settings. Auto is the default and needs nothing else.</summary>
    public enum DisplayMode { Auto = 0, BadgeOnly = 1, FieldOnly = 2 }

    /// <summary>What the extension does for the running theme.</summary>
    public enum DisplayKind { Badge, Field }

    /// <summary>
    /// Stable decision shared by Desktop and Fullscreen modes: the fallback genre is library data, so the choice depends
    /// only on the CONFIGURED fullscreen theme (and on what was remembered about it), never on the running mode.
    /// </summary>
    public static class StableDecision
    {
        /// <summary>Key remembered for a theme version ("id@version").</summary>
        public static string ThemeKey(string themeId, string version)
        {
            return (themeId ?? "").Trim().ToLowerInvariant() + "@" + (version ?? "").Trim();
        }

        /// <summary>"Theme" value of Playnite's fullscreenConfig.json; null when absent.</summary>
        public static string ParseFullscreenThemeId(string fullscreenConfigJson)
        {
            if (string.IsNullOrEmpty(fullscreenConfigJson)) return null;
            var m = Regex.Match(fullscreenConfigJson, @"""Theme""\s*:\s*""([^""]*)""");
            return m.Success && m.Groups[1].Value.Trim() != "" ? m.Groups[1].Value.Trim() : null;
        }

        /// <summary>"Version: 1.3.6" in theme.yaml; empty when absent.</summary>
        public static string ParseThemeVersion(string themeYaml)
        {
            if (string.IsNullOrEmpty(themeYaml)) return "";
            var m = Regex.Match(themeYaml, @"^\s*Version\s*:\s*['""]?([^'""\r\n]+?)['""]?\s*$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <param name="fullscreenThemeId">Fullscreen theme configured in Playnite (read even in Desktop mode).</param>
        /// <param name="failedKeys">Theme versions where the badge failed durably (remembered).</param>
        /// <param name="customElementThemes">Fullscreen themes seen hosting our custom element (remembered, ids lower case).</param>
        public static DisplayKind Decide(DisplayMode mode, string fullscreenThemeId, string themeVersion,
            ICollection<string> failedKeys, ICollection<string> customElementThemes)
        {
            bool hasRecipe = ThemeRecipes.Find(fullscreenThemeId, true) != null;
            bool failed = failedKeys != null && failedKeys.Contains(ThemeKey(fullscreenThemeId, themeVersion));
            bool custom = customElementThemes != null && customElementThemes.Contains((fullscreenThemeId ?? "").Trim().ToLowerInvariant());
            return DisplayChooser.Choose(mode, hasRecipe, failed, custom);
        }

        /// <summary>Badge insertion is only attempted in Fullscreen, for a theme with a recipe whose failure is not remembered.</summary>
        public static bool RunInjector(bool fullscreenRunning, DisplayMode mode, string fullscreenThemeId, string themeVersion, ICollection<string> failedKeys)
        {
            if (!fullscreenRunning || mode == DisplayMode.FieldOnly) return false;
            if (ThemeRecipes.Find(fullscreenThemeId, true) == null) return false;
            return failedKeys == null || !failedKeys.Contains(ThemeKey(fullscreenThemeId, themeVersion));
        }
    }

    public static class DisplayChooser
    {
        /// <param name="hasRecipe">The active theme has an insertion recipe.</param>
        /// <param name="injectionFailed">The recipe failed durably during this session (theme updated, elements renamed...).</param>
        /// <param name="customElementUsed">The theme hosts our custom element itself.</param>
        public static DisplayKind Choose(DisplayMode mode, bool hasRecipe, bool injectionFailed, bool customElementUsed)
        {
            switch (mode)
            {
                case DisplayMode.FieldOnly: return DisplayKind.Field;
                case DisplayMode.BadgeOnly: return DisplayKind.Badge;
                default:
                    if (customElementUsed) return DisplayKind.Badge;
                    return hasRecipe && !injectionFailed ? DisplayKind.Badge : DisplayKind.Field;
            }
        }
    }

    /// <summary>How the badge is built in a theme.</summary>
    public enum BadgeLayout
    {
        /// <summary>Two boxes of the theme's own box style (icon | text), next to a named box (Solaris).</summary>
        Welded,
        /// <summary>Copy of the entry containing the anchor (label + value...), label replaced by "AGSS", value by the state.</summary>
        Clone,
        /// <summary>Like Clone, the label being the sibling just before the anchor (label and value are separate children).</summary>
        CloneWithPrevious,
        /// <summary>A single line (icon + text) using the anchor's text style, after the block containing the anchor.</summary>
        Line
    }

    /// <summary>Names targeted in one theme to place the badge. Not a public API of the theme: check after theme updates.</summary>
    public class BadgeRecipe
    {
        public string Name;
        /// <summary>Theme ids handled (exact, case-insensitive).</summary>
        public string[] ThemeIds;
        /// <summary>Theme version the recipe was checked with (information; a failure is remembered per version anyway).</summary>
        public string TestedVersion;
        /// <summary>Fullscreen or desktop theme.</summary>
        public bool Fullscreen = true;
        public BadgeLayout Layout = BadgeLayout.Welded;
        /// <summary>Clone/Line: binding path of the TextBlock used as anchor ("InstallSize", "Platforms[0]"...), searched in the game page only.</summary>
        public string AnchorBinding;
        /// <summary>Clone/Line without RowName: the row is the N-th panel above the anchor (1 = nearest).</summary>
        public int RowLevel = 1;
        /// <summary>Clone: one/two-character glyphs of the theme (icon fonts) are replaced by the shield.</summary>
        public bool GlyphToIcon;
        /// <summary>Line: margin of the inserted line (left, top, right, bottom).</summary>
        public double[] LineMargin = { 0, 10, 0, 0 };
        /// <summary>Text replacing the copied label.</summary>
        public string LabelText = "AGSS";
        public string RowName;
        /// <summary>Named element whose row child is the anchor: the badge goes right after it.</summary>
        public string AnchorName;
        public string BoxStyleKey;
        public string LeftTag, RightTag;
        public string IconStyleKey;
        public string IconTag;
        public string TextStyleKey;
        public string TextBrushKey = "TextBrush";
        public double RightMargin = 10;
    }

    public static class ThemeRecipes
    {
        public static readonly List<BadgeRecipe> All = new List<BadgeRecipe>
        {
            new BadgeRecipe
            {
                Name = "Solaris",
                ThemeIds = new[] { "Solaris_b6e50d04-24ae-4ecd-bd3a-080367930992" },
                TestedVersion = "1.3.6",
                Fullscreen = true,
                RowName = "Row2", AnchorName = "InstallSize",
                BoxStyleKey = "InfoBox", LeftTag = "Left", RightTag = "Right",
                IconStyleKey = "PathIconStyle", IconTag = "Mini",
                TextStyleKey = "TextBlockSmallStyle",
            },
            // Info row "Played / Last activity / Released / Source / Platform": new column after "Released".
            new BadgeRecipe { Name = "ReskinPS5", ThemeIds = new[] { "RePS5_3d09a5d1-a183-49da-adb6-e78428f7d90e" }, TestedVersion = "1.11",
                Layout = BadgeLayout.Clone, AnchorBinding = "ReleaseYear", RowName = "GameInfo" },
            // Row "Time played / Last activity / Install size": new column after the install size.
            new BadgeRecipe { Name = "(u)biquity", ThemeIds = new[] { "(u)biquity_e71482ce-33e2-4b23-aaea-de576975d3a9" }, TestedVersion = "2.0.4",
                Layout = BadgeLayout.Clone, AnchorBinding = "InstallSize", RowLevel = 2 },
            // "Game details" card, list "label: value": new line after "Genre".
            new BadgeRecipe { Name = "PS5ish", ThemeIds = new[] { "PS5ish_676e10ec-adfe-48d8-a1bd-4d5771b5a2ca" }, TestedVersion = "5.0.3",
                Layout = BadgeLayout.Clone, AnchorBinding = "Genres", RowLevel = 2 },
            // Details list "label / value": new entry after "Genres".
            new BadgeRecipe { Name = "PlayniteModernUI", ThemeIds = new[] { "PlayniteModernUI_b600472c-c10c-4136-86d0-82bf0e576200" }, TestedVersion = "1.6.2",
                Layout = BadgeLayout.Clone, AnchorBinding = "Genres", RowLevel = 2 },
            // Side list "Release date: ... / Platform: ...": new line after the platform.
            new BadgeRecipe { Name = "Playnite Deck", ThemeIds = new[] { "Playnite Deck_905f24da-b02b-40df-9f6b-14c41fcdb05d" }, TestedVersion = "3.0.2",
                Layout = BadgeLayout.Clone, AnchorBinding = "Platforms[0]", RowLevel = 2 },
            // Column "Last played / Release date / Platform" (header bar + value): new pair after the platform.
            new BadgeRecipe { Name = "Nintendo Switch-ish", ThemeIds = new[] { "Nintendo Switch-ish_0d020ed5-0f3c-4c1a-bf9a-c983ef7d74b7" }, TestedVersion = "2.0.0",
                Layout = BadgeLayout.CloneWithPrevious, AnchorBinding = "Platforms[0]", RowLevel = 1 },
            // XBOXSERIESish 2.0.0: no recipe on purpose. Its "Game details" card has a fixed height, labels and values in two
            // separate hand-aligned columns, and nothing below the list: any insertion overflows the card (checked in a capture).
            // Row of entries [glyph][value / label]: new entry after the release year, glyph replaced by the shield.
            new BadgeRecipe { Name = "Toggle", ThemeIds = new[] { "Toggle_28b7d2c0-105b-4632-8dca-d11348bd61d2" }, TestedVersion = "1.10.1",
                Layout = BadgeLayout.Clone, AnchorBinding = "ReleaseYear", RowLevel = 3, GlyphToIcon = true },
        };

        public static BadgeRecipe Find(string themeId, bool fullscreen)
        {
            if (string.IsNullOrWhiteSpace(themeId)) return null;
            return All.FirstOrDefault(r => r.Fullscreen == fullscreen && r.ThemeIds.Any(id => string.Equals(id, themeId.Trim(), StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>Finds status.json without any setting: explicit path, then environment, then default data folder, then older folders.</summary>
    public static class DataLocator
    {
        public const string AppFolder = "AutoGameSoftwareSync";
        public const string EnvVar = "AUTOGAMESOFTWARESYNC_DATA_DIR";
        public const string FileName = "status.json";

        /// <param name="explicitPath">Path typed in the settings (empty = automatic).</param>
        /// <param name="envDirs">Data folders imposed by the environment, by priority (may contain null/empty).</param>
        /// <param name="localAppData">%LOCALAPPDATA%.</param>
        /// <param name="legacyFiles">Older status.json locations, by priority.</param>
        public static string Resolve(string explicitPath, IEnumerable<string> envDirs, string localAppData, IEnumerable<string> legacyFiles, Func<string, bool> exists)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath.Trim();
            var candidates = new List<string>();
            foreach (var d in envDirs ?? Enumerable.Empty<string>())
                if (!string.IsNullOrWhiteSpace(d)) candidates.Add(System.IO.Path.Combine(d.Trim(), FileName));
            var main = string.IsNullOrWhiteSpace(localAppData) ? null : System.IO.Path.Combine(localAppData, AppFolder, FileName);
            if (main != null) candidates.Add(main);
            candidates.AddRange((legacyFiles ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)));
            foreach (var c in candidates)
                if (exists(c)) return c;
            // Nothing yet: the first environment folder if any, else the default location (watched until it appears).
            return candidates.FirstOrDefault() ?? main;
        }
    }
}
