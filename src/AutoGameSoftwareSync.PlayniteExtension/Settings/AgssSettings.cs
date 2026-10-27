using System;
using System.Collections.Generic;
using System.Linq;
using AutoGameSoftwareSync.PlayniteExtension.Logic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace AutoGameSoftwareSync.PlayniteExtension.Settings
{
    public class AgssSettings : ObservableObject
    {
        public const int CurrentVersion = 2;
        /// <summary>Default prefix of versions 0.x (items written in Tags).</summary>
        public const string OldDefaultPrefix = "AGSS : ";

        private bool enabled = true;
        private int displayMode = (int)Logic.DisplayMode.Auto;
        private int fieldIndex = 1; // 0 Tags, 1 Genres (default: shown by most themes), 2 Features, 3 Categories
        private string prefix = FallbackGenre.DefaultPrefix;
        private bool showSynced = true;
        private string statusPath = "";
        private bool notifications = false;
        private int settingsVersion = 0; // 0 = saved by an older version (set to CurrentVersion for new installs)
        private string cleanupPrefix = "";
        private int cleanupFieldIndex = -1;

        public bool Enabled { get => enabled; set => SetValue(ref enabled, value); }
        /// <summary>0 automatic (badge when the theme is recognised, genre otherwise), 1 badge only, 2 field only.</summary>
        public int DisplayMode { get => displayMode; set => SetValue(ref displayMode, value); }
        public int FieldIndex { get => fieldIndex; set => SetValue(ref fieldIndex, value); }
        public string Prefix { get => prefix; set => SetValue(ref prefix, value); }
        public bool ShowSynced { get => showSynced; set => SetValue(ref showSynced, value); }
        /// <summary>Empty = automatic detection of the AGSS data folder.</summary>
        public string StatusPath { get => statusPath; set => SetValue(ref statusPath, value); }
        public bool Notifications { get => notifications; set => SetValue(ref notifications, value); }
        public int SettingsVersion { get => settingsVersion; set => SetValue(ref settingsVersion, value); }
        /// <summary>Items written by an older version (prefix + field) still to remove once.</summary>
        public string CleanupPrefix { get => cleanupPrefix; set => SetValue(ref cleanupPrefix, value); }
        public int CleanupFieldIndex { get => cleanupFieldIndex; set => SetValue(ref cleanupFieldIndex, value); }
        /// <summary>Fullscreen theme versions ("id@version") where the inserted badge failed durably: genre fallback, no retry.</summary>
        public List<string> BadgeFailedThemes { get; set; } = new List<string>();
        /// <summary>Fullscreen themes (ids, lower case) seen hosting the custom element: no genre needed.</summary>
        public List<string> CustomElementThemes { get; set; } = new List<string>();

        public static string DefaultStatusPath
        {
            get
            {
                Func<string, EnvironmentVariableTarget, string> env = (n, t) =>
                {
                    try { return Environment.GetEnvironmentVariable(n, t); } catch { return null; }
                };
                var envDirs = new List<string> { env(DataLocator.EnvVar, EnvironmentVariableTarget.Process), env(DataLocator.EnvVar, EnvironmentVariableTarget.User) };
                envDirs.AddRange(Legacy.OldEnvDirs(env));
                return DataLocator.Resolve("", envDirs,
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Legacy.OldStatusPaths, p => { try { return System.IO.File.Exists(p); } catch { return false; } });
            }
        }

        [DontSerialize]
        public string EffectiveStatusPath
        {
            get { return string.IsNullOrWhiteSpace(statusPath) ? DefaultStatusPath : statusPath; }
        }

        [DontSerialize]
        public Logic.DisplayMode Mode
        {
            get { return Enum.IsDefined(typeof(Logic.DisplayMode), displayMode) ? (Logic.DisplayMode)displayMode : Logic.DisplayMode.Auto; }
        }

        /// <summary>
        /// Brings settings saved by an older version to the current defaults (genre + "● AGSS : " prefix) and remembers the
        /// old prefix/field so that the items written before are removed once. Returns true when something changed.
        /// </summary>
        public static bool Migrate(AgssSettings s)
        {
            if (s == null || s.SettingsVersion >= CurrentVersion) return false;
            if (!string.IsNullOrEmpty(s.Prefix) && (s.Prefix != FallbackGenre.DefaultPrefix || s.FieldIndex != 1))
            {
                s.CleanupPrefix = s.Prefix;
                s.CleanupFieldIndex = s.FieldIndex;
            }
            s.Prefix = FallbackGenre.DefaultPrefix;
            s.FieldIndex = 1;
            s.DisplayMode = (int)Logic.DisplayMode.Auto;
            s.SettingsVersion = CurrentVersion;
            return true;
        }
    }

    public class AgssSettingsViewModel : ObservableObject, ISettings
    {
        private readonly AutoGameSoftwareSyncPlugin plugin;
        private AgssSettings editing;
        public AgssSettings Settings { get; set; }

        public AgssSettingsViewModel(AutoGameSoftwareSyncPlugin plugin)
        {
            this.plugin = plugin;
            var loaded = plugin.LoadPluginSettings<AgssSettings>();
            if (loaded == null)
            {
                // Never saved: an older version may still have written its default items ("AGSS : " in Tags).
                Settings = new AgssSettings { SettingsVersion = AgssSettings.CurrentVersion, CleanupPrefix = AgssSettings.OldDefaultPrefix, CleanupFieldIndex = 0 };
            }
            else
            {
                Settings = loaded;
                if (AgssSettings.Migrate(loaded)) plugin.SavePluginSettings(loaded);
            }
        }

        public void Save() { plugin.SavePluginSettings(Settings); }

        public void BeginEdit() { editing = Serialization.GetClone(Settings); }
        public void CancelEdit() { Settings = editing; OnPropertyChanged(nameof(Settings)); }
        public void EndEdit() { plugin.SavePluginSettings(Settings); plugin.OnSettingsChanged(); }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrEmpty(Settings.Prefix))
                errors.Add("The prefix must not be empty (it protects items you created yourself).");
            if (Settings.FieldIndex < 0 || Settings.FieldIndex > 3) Settings.FieldIndex = 1;
            if (Settings.DisplayMode < 0 || Settings.DisplayMode > 2) Settings.DisplayMode = 0;
            return errors.Count == 0;
        }
    }
}
