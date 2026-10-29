using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AutoGameSoftwareSync.PlayniteExtension.Logic;
using AutoGameSoftwareSync.PlayniteExtension.Settings;
using AutoGameSoftwareSync.PlayniteExtension.UI;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace AutoGameSoftwareSync.PlayniteExtension
{
    /// <summary>
    /// Display of the AGSS state of each game. Never launches, syncs or changes anything in AGSS.
    /// Display cascade: badge inserted into recognised themes > fallback genre "● AGSS : state · device" > custom elements.
    /// Writes: the fallback genre / tag on followed games (only when the badge cannot be shown) and, since 1.2.0, the list of
    /// installed games <c>playnite-library.json</c> next to an EXISTING status.json of AGSS (removed when the list is switched
    /// off or the extension disabled). Nothing else.
    /// </summary>
    public class AutoGameSoftwareSyncPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string CustomSource = "AGSS";

        private readonly AgssSettingsViewModel settings;
        private readonly Dictionary<Guid, AgssState> previous = new Dictionary<Guid, AgssState>();
        private readonly FileStatusSource source;
        private readonly List<WeakReference<AgssBadgeControl>> customControls = new List<WeakReference<AgssBadgeControl>>();
        private Dictionary<Guid, AgssGameEntry> statusByGame = new Dictionary<Guid, AgssGameEntry>();
        private FileSystemWatcher watcher;
        private string watchedPath;
        private Timer safetyTimer;
        private Timer debounce;
        private BadgeInjector injector;
        private string fullscreenThemeId;
        private string themeVersion = "";
        private string lastText;
        private volatile bool dirty = true;
        private bool selfUpdating;
        private bool started;

        public override Guid Id { get; } = Guid.Parse("b2f3c6a1-7d4e-4a52-9c1e-3f0a5d8e1b77");

        public AutoGameSoftwareSyncPlugin(IPlayniteAPI api) : base(api)
        {
            settings = new AgssSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };
            source = new FileStatusSource(() => settings.Settings.EffectiveStatusPath);
            libraryDebounce = new Timer(_ => WriteLibrary(), null, Timeout.Infinite, Timeout.Infinite);
            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                SourceName = CustomSource,
                ElementList = new List<string> { "Badge", "Text" }
            });
        }

        // ---- Lifecycle ----------------------------------------------------------------------

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try { PlayniteApi.Database.Games.ItemUpdated += OnGamesUpdated; } catch (Exception ex) { logger.Warn(ex, "AGSS: no game update events"); }
            try { PlayniteApi.Database.Games.ItemCollectionChanged += (s, e) => ScheduleLibrary(); } catch (Exception ex) { logger.Warn(ex, "AGSS: no game collection events"); }
            Start();
            ScheduleLibrary();
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) { dirty = true; ScheduleRefresh(); ScheduleLibrary(); }

        // ---- Installed games list for AGSS (1.2.0) ------------------------------------------

        private readonly Timer libraryDebounce;
        private readonly object libraryLock = new object();
        private volatile bool libraryPending;
        private volatile bool stopping;
        private string lastLibraryError;

        /// <summary>Debounced (5 s): a library update changes many games at once.</summary>
        private void ScheduleLibrary()
        {
            if (stopping) return;
            try { libraryPending = true; libraryDebounce.Change(5000, Timeout.Infinite); }
            catch (Exception ex) { logger.Warn(ex, "AGSS: could not schedule the games list"); }
        }

        /// <summary>Emulator of an emulator action, as Playnite knows it (null when it no longer exists).</summary>
        private EmulatorInfo EmulatorOf(Guid emulatorId, string profileId)
        {
            if (emulatorId == Guid.Empty) return null;
            var e = PlayniteApi.Database.Emulators.Get(emulatorId);
            if (e == null) return null;
            var info = new EmulatorInfo { Name = e.Name ?? "", InstallDir = e.InstallDir ?? "" };
            try
            {
                var p = string.IsNullOrEmpty(profileId) ? null : e.GetProfile(profileId);
                if (p != null)
                {
                    var custom = p as CustomEmulatorProfile;
                    var builtIn = p as BuiltInEmulatorProfile;
                    info.Profile = (builtIn != null ? builtIn.BuiltInProfileName : null) ?? p.Name ?? "";
                    info.Exe = custom?.Executable ?? "";
                }
            }
            catch { }
            return info;
        }

        private LibraryGame ToLibraryGame(Game g)
        {
            var actions = new List<LibraryAction>();
            if (g.GameActions != null)
                foreach (var a in g.GameActions)
                {
                    if (a == null) continue;
                    actions.Add(new LibraryAction
                    {
                        Kind = a.Type == GameActionType.File ? LibraryAction.File : a.Type == GameActionType.Emulator ? LibraryAction.Emulator : LibraryAction.Other,
                        Path = a.Path ?? "",
                        EmulatorId = a.EmulatorId,
                        ProfileId = a.EmulatorProfileId ?? ""
                    });
                }
            var platforms = g.Platforms == null ? new List<string>() : g.Platforms.Where(p => p != null).Select(p => p.Name).ToList();
            var roms = g.Roms == null ? new List<string>() : g.Roms.Where(r => r != null).Select(r => r.Path).ToList();
            return LibraryGame.Build(g.Id.ToString(), g.Name, g.Source?.Name, g.PluginId == Guid.Empty ? "" : g.PluginId.ToString(),
                g.InstallDirectory, g.GameId, LibraryFile.SteamAppIdOf(g.PluginId, g.GameId), platforms, g.Hidden, actions, roms,
                EmulatorOf, (text, emuDir) => emuDir == null ? PlayniteApi.ExpandGameVariables(g, text) : PlayniteApi.ExpandGameVariables(g, text, emuDir));
        }

        /// <summary>
        /// Path of the list, or null when it must not be written: list switched off, status.json path not ending in
        /// status.json, or no status.json yet in that folder (the list only ever goes next to an EXISTING status.json,
        /// so that a Playnite instance pointed at a folder AGSS does not use never writes there).
        /// </summary>
        private string LibraryPath(out string reason)
        {
            reason = null;
            var status = settings.Settings.EffectiveStatusPath;
            var path = LibraryFile.PathNextTo(status);
            if (path == null) { reason = "status.json path not set or not ending in status.json: " + (status ?? ""); return null; }
            if (!File.Exists(status)) { reason = "no status.json yet in " + Path.GetDirectoryName(status); return null; }
            return path;
        }

        /// <summary>Writes playnite-library.json (installed games only) next to status.json. The only extra file written.</summary>
        private void WriteLibrary()
        {
            lock (libraryLock)
            {
                libraryPending = false;
                try
                {
                    Exception error = null;
                    var path = LibraryFile.PathNextTo(settings.Settings.EffectiveStatusPath);
                    if (!settings.Settings.Enabled || !settings.Settings.WriteGamesList)
                    {
                        if (path != null && LibraryFile.Delete(path, out error)) logger.Info("AGSS: installed games list removed (list switched off): " + path);
                        else if (error != null) WarnOnce(error, "AGSS: could not remove the installed games list " + path);
                        return;
                    }
                    string reason;
                    path = LibraryPath(out reason);
                    if (path == null) { logger.Debug("AGSS: installed games list not written (" + reason + ")"); return; }
                    var games = PlayniteApi.Database.Games.Where(g => g != null && g.IsInstalled).Select(ToLibraryGame).ToList();
                    switch (LibraryFile.Write(path, LibraryFile.ToJson(games, DateTime.UtcNow), out error))
                    {
                        case WriteResult.Written: lastLibraryError = null; logger.Info("AGSS: installed games list written (" + games.Count + " games): " + path); break;
                        case WriteResult.Unchanged: lastLibraryError = null; break;
                        default: WarnOnce(error, "AGSS: could not write the installed games list " + path); break;
                    }
                }
                catch (Exception ex) { WarnOnce(ex, "AGSS: could not build the installed games list"); }
            }
        }

        /// <summary>One warning per cause (the write is retried at every event): the log is not flooded.</summary>
        private void WarnOnce(Exception ex, string message)
        {
            var key = message + "|" + (ex == null ? "" : ex.GetType().Name + ": " + ex.Message);
            if (key == lastLibraryError) return;
            lastLibraryError = key;
            if (ex != null) logger.Warn(ex, message); else logger.Warn(message);
        }

        /// <summary>Every 30 s: the list is (re)written when status.json exists but the list does not (AGSS installed after Playnite started, folder emptied...).</summary>
        private void CheckLibraryPresent()
        {
            try
            {
                var s = settings.Settings;
                if (!s.Enabled || !s.WriteGamesList || libraryPending) return;
                string reason;
                var path = LibraryPath(out reason);
                if (path != null && !File.Exists(path)) ScheduleLibrary();
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: games list check failed"); }
        }

        /// <summary>At exit: a pending write is done now (synchronously), then the timer is released.</summary>
        private void FlushLibrary()
        {
            stopping = true;
            try { libraryDebounce.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            if (libraryPending) WriteLibrary();
            try { libraryDebounce.Dispose(); } catch { }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args) { Stop(); FlushLibrary(); }
        public override void OnGameSelected(OnGameSelectedEventArgs args) { injector?.OnSelectionChanged(); }

        public void OnSettingsChanged()
        {
            Stop();
            dirty = true;
            lastText = null;
            lastLibraryError = null;
            Start();
            ScheduleLibrary(); // new path, list switched on / off, extension disabled: the list follows
        }

        private bool IsFullscreen
        {
            get { try { return PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen; } catch { return false; } }
        }

        /// <summary>Fullscreen theme CONFIGURED in Playnite (read from its configuration, also in Desktop mode), with its version.</summary>
        private void ReadFullscreenTheme()
        {
            fullscreenThemeId = null; themeVersion = "";
            string cfgDir = null;
            try { cfgDir = PlayniteApi.Paths.ConfigurationPath; } catch { }
            try
            {
                var cfg = cfgDir == null ? null : Path.Combine(cfgDir, "fullscreenConfig.json");
                if (cfg != null && File.Exists(cfg)) fullscreenThemeId = StableDecision.ParseFullscreenThemeId(File.ReadAllText(cfg));
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: could not read the fullscreen configuration"); }
            if (string.IsNullOrWhiteSpace(fullscreenThemeId))
            {
                try { fullscreenThemeId = PlayniteApi.ApplicationSettings.FullscreenTheme; } catch { }
            }
            try
            {
                var yaml = cfgDir == null || string.IsNullOrWhiteSpace(fullscreenThemeId) ? null : Path.Combine(cfgDir, "Themes", "Fullscreen", fullscreenThemeId, "theme.yaml");
                if (yaml != null && File.Exists(yaml)) themeVersion = StableDecision.ParseThemeVersion(File.ReadAllText(yaml));
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: could not read the theme version"); }
        }

        private void Start()
        {
            try
            {
                if (!settings.Settings.Enabled) return;
                started = true;
                ReadFullscreenTheme();
                var s = settings.Settings;
                var recipe = ThemeRecipes.Find(fullscreenThemeId, true);
                logger.Info("AGSS: mode " + (IsFullscreen ? "fullscreen" : "desktop") + ", configured fullscreen theme " + (fullscreenThemeId ?? "?") + " " + themeVersion +
                    (recipe != null ? " (badge recipe " + recipe.Name + ")" : " (no badge recipe)") + ", display " + CurrentKind() + ", status file " + s.EffectiveStatusPath);
                if (StableDecision.RunInjector(IsFullscreen, s.Mode, fullscreenThemeId, themeVersion, s.BadgeFailedThemes))
                {
                    injector = new BadgeInjector(recipe, ModelFor, () => PlayniteApi.MainView.SelectedGames?.FirstOrDefault(), DetailsVisible);
                    injector.Failed += OnInjectionFailed;
                    injector.Start();
                }
                debounce = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
                safetyTimer = new Timer(_ => { EnsureWatcher(); Refresh(); CheckLibraryPresent(); }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
                EnsureWatcher();
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: start failed (30 s polling still active)"); }
        }

        /// <summary>Remembered per theme version: the genre fallback stays (no retry) until the theme is updated.</summary>
        private void OnInjectionFailed()
        {
            var key = StableDecision.ThemeKey(fullscreenThemeId, themeVersion);
            var s = settings.Settings;
            if (s.BadgeFailedThemes == null) s.BadgeFailedThemes = new List<string>();
            if (!s.BadgeFailedThemes.Contains(key)) { s.BadgeFailedThemes.Add(key); settings.Save(); }
            dirty = true;
            ScheduleRefresh();
        }

        private void Stop()
        {
            started = false;
            try { watcher?.Dispose(); safetyTimer?.Dispose(); debounce?.Dispose(); } catch { }
            watcher = null; watchedPath = null; safetyTimer = null; debounce = null;
            try
            {
                var inj = injector;
                injector = null;
                if (inj != null) PlayniteApi.MainView.UIDispatcher.Invoke(new Action(inj.Stop));
            }
            catch { }
        }

        private bool? DetailsVisible()
        {
            try { return PlayniteApi.MainView.ActiveFullscreenView == FullscreenView.Details; } catch { return null; }
        }

        /// <summary>Watches the folder of status.json (created later, or moved by an environment change: re-checked every 30 s).</summary>
        private void EnsureWatcher()
        {
            try
            {
                var path = settings.Settings.EffectiveStatusPath;
                if (watcher != null && string.Equals(path, watchedPath, StringComparison.OrdinalIgnoreCase)) return;
                var moved = watchedPath != null;
                watcher?.Dispose(); watcher = null; watchedPath = null;
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                var w = new FileSystemWatcher(dir, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                w.Changed += (s, e) => ScheduleRefresh();
                w.Created += (s, e) => { ScheduleRefresh(); ScheduleLibrary(); }; // status.json appears: the list can now be written next to it
                w.Renamed += (s, e) => ScheduleRefresh();
                w.EnableRaisingEvents = true;
                watcher = w; watchedPath = path;
                if (moved) ScheduleLibrary(); // data folder changed (environment variable, settings): the list follows
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: could not watch the status file (30 s polling still active)"); }
        }

        private void ScheduleRefresh()
        {
            try { debounce?.Change(500, Timeout.Infinite); } catch { }
        }

        private void OnGamesUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            if (!selfUpdating) ScheduleLibrary(); // installed / uninstalled, folder moved...
            if (selfUpdating || !started) return;
            // A metadata download may have replaced the genres: re-apply (no write when everything is already right).
            dirty = true;
            ScheduleRefresh();
        }

        private void Refresh()
        {
            try
            {
                if (!settings.Settings.Enabled) return;
                var file = source.Read();
                if (file == null) return; // missing/invalid: keep what is displayed
                var text = source.LastText;
                if (!dirty && text == lastText) return;
                lastText = text;
                dirty = false;
                PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() => Apply(file)));
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS refresh failed"); }
        }

        // ---- Model --------------------------------------------------------------------------

        private BadgeModel ModelFor(Game game)
        {
            if (game == null) return null;
            AgssGameEntry e;
            var map = statusByGame;
            if (!map.TryGetValue(game.Id, out e)) return null;
            return BadgeComposer.Compose(e, PlayniteApi.ApplicationSettings.Language, TimeZoneInfo.Local, DateTime.Now);
        }

        private DisplayKind CurrentKind()
        {
            var s = settings.Settings;
            return StableDecision.Decide(s.Mode, fullscreenThemeId, themeVersion, s.BadgeFailedThemes, s.CustomElementThemes);
        }

        // ---- Field access -------------------------------------------------------------------

        private static List<Guid> GetIds(Game g, int field)
        {
            switch (field)
            {
                case 0: return g.TagIds;
                case 2: return g.FeatureIds;
                case 3: return g.CategoryIds;
                default: return g.GenreIds;
            }
        }

        private static void SetIds(Game g, int field, List<Guid> ids)
        {
            switch (field)
            {
                case 0: g.TagIds = ids; break;
                case 2: g.FeatureIds = ids; break;
                case 3: g.CategoryIds = ids; break;
                default: g.GenreIds = ids; break;
            }
        }

        private IEnumerable<DatabaseObject> Items(int field)
        {
            var db = PlayniteApi.Database;
            switch (field)
            {
                case 0: return db.Tags;
                case 2: return db.Features;
                case 3: return db.Categories;
                default: return db.Genres;
            }
        }

        private string NameOf(Guid id, int field)
        {
            var db = PlayniteApi.Database;
            switch (field)
            {
                case 0: return db.Tags.Get(id)?.Name;
                case 2: return db.Features.Get(id)?.Name;
                case 3: return db.Categories.Get(id)?.Name;
                default: return db.Genres.Get(id)?.Name;
            }
        }

        private Guid EnsureItem(string name, int field)
        {
            var db = PlayniteApi.Database;
            switch (field)
            {
                case 0: return db.Tags.Add(name).Id;
                case 2: return db.Features.Add(name).Id;
                case 3: return db.Categories.Add(name).Id;
                default: return db.Genres.Add(name).Id;
            }
        }

        private void RemoveItems(int field, IEnumerable<Guid> ids)
        {
            var db = PlayniteApi.Database;
            foreach (var id in ids)
            {
                try
                {
                    switch (field)
                    {
                        case 0: db.Tags.Remove(id); break;
                        case 2: db.Features.Remove(id); break;
                        case 3: db.Categories.Remove(id); break;
                        default: db.Genres.Remove(id); break;
                    }
                }
                catch (Exception ex) { logger.Warn(ex, "AGSS: could not delete an unused item"); }
            }
        }

        /// <summary>Puts the desired own item (or none) on a game. Only items in our exact format (prefix + state) are ever touched.</summary>
        private bool SetOwnItem(Game game, int field, string prefix, string desired)
        {
            var ids = GetIds(game, field) ?? new List<Guid>();
            var names = ids.Select(id => NameOf(id, field)).ToList();
            var plan = ItemPlanner.Plan(names, prefix, desired);
            if (plan == null) return false;
            var newIds = new List<Guid>();
            for (int i = 0; i < ids.Count; i++)
                if (!OwnItems.IsOwn(names[i], prefix)) newIds.Add(ids[i]);
            if (desired != null) newIds.Add(EnsureItem(desired, field));
            SetIds(game, field, newIds.Distinct().ToList());
            PlayniteApi.Database.Games.Update(game);
            return true;
        }

        /// <summary>Deletes our own items (prefix + state, never a user item) used by no game, so that nothing stays in the filters.</summary>
        private void DeleteUnused(int field, string prefix, List<Game> games)
        {
            var used = new HashSet<Guid>(games.SelectMany(g => GetIds(g, field) ?? new List<Guid>()));
            var unused = FallbackGenre.Unused(Items(field).ToList(), i => i.Name, i => used.Contains(i.Id), prefix);
            if (unused.Count > 0) RemoveItems(field, unused.Select(i => i.Id).ToList());
        }

        // ---- Apply --------------------------------------------------------------------------

        private GameCandidate ToCandidate(Game g)
        {
            var c = new GameCandidate { Id = g.Id, Name = g.Name ?? "", InstallDir = g.InstallDirectory ?? "" };
            if (g.GameActions != null)
            {
                foreach (var a in g.GameActions)
                {
                    if (a.Type != GameActionType.File || string.IsNullOrEmpty(a.Path)) continue;
                    try { c.ExeNames.Add(Path.GetFileName(PlayniteApi.ExpandGameVariables(g, a.Path))); } catch { }
                }
            }
            return c;
        }

        private void Apply(AgssStatusFile file)
        {
            try
            {
                var s = settings.Settings;
                var lang = PlayniteApi.ApplicationSettings.Language;
                var games = PlayniteApi.Database.Games.ToList();
                statusByGame = Matcher.Match(file.Games, games.Select(ToCandidate).ToList());
                var kind = CurrentKind();
                int field = s.FieldIndex;
                var prefix = s.Prefix;
                int changed = 0;

                selfUpdating = true;
                try
                {
                    using (PlayniteApi.Database.BufferedUpdate())
                    {
                        // 1. Fallback field: our item on followed games, nothing elsewhere (and nothing at all when the badge works).
                        if (!string.IsNullOrEmpty(prefix))
                        {
                            foreach (var game in games)
                            {
                                string desired = null;
                                if (kind == DisplayKind.Field)
                                {
                                    var m = ModelFor(game);
                                    if (m != null && !(m.State == AgssState.Synced && !s.ShowSynced))
                                        desired = FallbackGenre.Text(m, prefix);
                                }
                                if (SetOwnItem(game, field, prefix, desired)) changed++;
                            }
                            DeleteUnused(field, prefix, games);
                        }

                        // 2. Items written by an older version of the extension: removed once.
                        if (!string.IsNullOrEmpty(s.CleanupPrefix) && s.CleanupFieldIndex >= 0 &&
                            !(s.CleanupPrefix == prefix && s.CleanupFieldIndex == field))
                        {
                            foreach (var game in games) SetOwnItem(game, s.CleanupFieldIndex, s.CleanupPrefix, null);
                            DeleteUnused(s.CleanupFieldIndex, s.CleanupPrefix, games);
                            s.CleanupPrefix = ""; s.CleanupFieldIndex = -1;
                            settings.Save();
                            logger.Info("AGSS: items of the previous version removed");
                        }
                    }
                }
                finally { selfUpdating = false; }
                if (changed > 0) logger.Info("AGSS: fallback " + (kind == DisplayKind.Field ? "genre written" : "genre removed") + " on " + changed + " game(s)");

                // 3. Notifications (optional, off by default).
                var notify = new HashSet<AgssGameEntry>(ItemPlanner.Applicable(file));
                foreach (var kv in statusByGame)
                {
                    AgssState p;
                    var had = previous.TryGetValue(kv.Key, out p);
                    if (s.Notifications && notify.Contains(kv.Value) && ItemPlanner.ShouldNotify(had ? (AgssState?)p : null, kv.Value.State))
                    {
                        var g = games.FirstOrDefault(x => x.Id == kv.Key);
                        PlayniteApi.Notifications.Add(new NotificationMessage("agss-" + kv.Key,
                            (g?.Name ?? kv.Value.Name) + " - " + Labels.Get(kv.Value.State, lang), NotificationType.Info));
                    }
                    previous[kv.Key] = kv.Value.State;
                }

                // 4. Visible badges.
                injector?.RefreshAll();
                RefreshCustomControls();
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS apply failed"); }
        }

        private void RefreshCustomControls()
        {
            lock (customControls)
            {
                customControls.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in customControls)
                    if (w.TryGetTarget(out var c)) c.Update();
            }
        }

        // ---- Custom elements (bonus for theme authors) --------------------------------------

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            if (args.Name != "Badge" && args.Name != "Text") return null;
            var control = new AgssBadgeControl(ModelFor, args.Name == "Badge");
            lock (customControls) customControls.Add(new WeakReference<AgssBadgeControl>(control));
            // Only a FULLSCREEN theme hosting the element counts (remembered: same decision in both modes).
            var id = (fullscreenThemeId ?? "").Trim().ToLowerInvariant();
            var s = settings.Settings;
            if (IsFullscreen && id != "" && (s.CustomElementThemes == null || !s.CustomElementThemes.Contains(id)))
            {
                if (s.CustomElementThemes == null) s.CustomElementThemes = new List<string>();
                s.CustomElementThemes.Add(id);
                settings.Save();
                dirty = true;
                ScheduleRefresh();
            }
            return control;
        }

        // ---- Menu: cleanup ------------------------------------------------------------------

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                MenuSection = "@AutoGameSoftwareSync",
                Description = "AGSS : remove all added items",
                Action = a => RemoveAll()
            };
        }

        private void RemoveAll()
        {
            var prefix = settings.Settings.Prefix;
            int field = settings.Settings.FieldIndex;
            if (string.IsNullOrEmpty(prefix)) return;
            var answer = PlayniteApi.Dialogs.ShowMessage(
                "Remove every item added by AutoGameSoftwareSync (\"" + prefix + "\" followed by a state) from the current field of all games? Your own items are kept.",
                "AutoGameSoftwareSync", MessageBoxButton.YesNo);
            if (answer != MessageBoxResult.Yes) return;
            var games = PlayniteApi.Database.Games.ToList();
            selfUpdating = true;
            try
            {
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var game in games) SetOwnItem(game, field, prefix, null);
                    DeleteUnused(field, prefix, games);
                }
            }
            finally { selfUpdating = false; }
        }

        // ---- Settings -----------------------------------------------------------------------

        public override ISettings GetSettings(bool firstRunSettings) { return settings; }
        public override UserControl GetSettingsView(bool firstRunSettings) { return new AgssSettingsView(); }
    }
}
