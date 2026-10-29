using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AutoGameSoftwareSync.PlayniteExtension.Logic
{
    /// <summary>A Playnite game action, reduced to what the list needs (no Playnite type, so that it can be tested).</summary>
    public class LibraryAction
    {
        public const string File = "file", Emulator = "emulator", Other = "other";
        public string Kind = Other;
        /// <summary>Program path of a file action (may contain Playnite variables such as {InstallDir}).</summary>
        public string Path = "";
        public Guid EmulatorId;
        public string ProfileId = "";
    }

    /// <summary>The emulator of an emulated game (first emulator action), as known by Playnite.</summary>
    public class EmulatorInfo
    {
        public string Name = "";
        public string InstallDir = "";
        /// <summary>Profile name (built-in profile name or custom profile name), empty when the profile is unknown.</summary>
        public string Profile = "";
        /// <summary>Program of a CUSTOM profile (may contain {EmulatorDir}); empty for a built-in profile, whose program Playnite resolves itself.</summary>
        public string Exe = "";
    }

    /// <summary>An INSTALLED Playnite game, as listed for AutoGameSoftwareSync (list format 1, written since 1.2.0).</summary>
    public class LibraryGame
    {
        public string Id = "";
        public string Name = "";
        /// <summary>Playnite source ("Steam", "GOG", "Epic"...), empty for a game added by hand.</summary>
        public string Source = "";
        /// <summary>Library plugin of Playnite (GUID), empty for a game added by hand.</summary>
        public string PluginId = "";
        public string InstallDir = "";
        /// <summary>Programs of the file game actions (expanded paths).</summary>
        public List<string> Exes = new List<string>();
        /// <summary>Id of the game in its library (Steam appid, GOG id, Epic app name...).</summary>
        public string GameId = "";
        public long? SteamAppId;
        public List<string> Platforms = new List<string>();
        /// <summary>Game hidden by the user in Playnite (listed, flagged).</summary>
        public bool Hidden;
        /// <summary>ROM files of an emulated game (expanded paths).</summary>
        public List<string> Roms = new List<string>();
        /// <summary>Emulator of an emulated game (null for a normal game or when Playnite no longer knows the emulator).</summary>
        public EmulatorInfo Emulator;

        private static readonly Regex emulatorDir = new Regex(@"\{EmulatorDir\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Builds the entry from plain data. <paramref name="expand"/>(text, emulatorDir) is Playnite's variable expansion
        /// ({InstallDir}, {PlayniteDir}, {EmulatorDir}...); it is only called for a text containing a variable, and never
        /// for {EmulatorDir} when the emulator folder is unknown: the text is then kept as is rather than emptied.
        /// <paramref name="emulatorOf"/>(emulatorId, profileId) returns the emulator of an emulator action, or null.
        /// </summary>
        public static LibraryGame Build(string id, string name, string source, string pluginId, string installDir, string gameId, long? steamAppId,
            IEnumerable<string> platforms, bool hidden, IEnumerable<LibraryAction> actions, IEnumerable<string> roms,
            Func<Guid, string, EmulatorInfo> emulatorOf, Func<string, string, string> expand)
        {
            var acts = (actions ?? Enumerable.Empty<LibraryAction>()).Where(a => a != null).ToList();
            EmulatorInfo emu = null;
            var emuAction = acts.FirstOrDefault(a => a.Kind == LibraryAction.Emulator);
            if (emuAction != null && emulatorOf != null)
            {
                try { emu = emulatorOf(emuAction.EmulatorId, emuAction.ProfileId ?? ""); } catch { emu = null; }
            }
            var emuDir = string.IsNullOrWhiteSpace(emu?.InstallDir) ? null : emu.InstallDir.Trim();
            Func<string, string> x = t => Expand(t, emuDir, expand);
            var g = new LibraryGame
            {
                Id = id ?? "",
                Name = name ?? "",
                Source = source ?? "",
                PluginId = pluginId ?? "",
                InstallDir = string.IsNullOrEmpty(installDir) ? "" : x(installDir),
                GameId = gameId ?? "",
                SteamAppId = steamAppId,
                Hidden = hidden
            };
            foreach (var a in acts)
                if (a.Kind == LibraryAction.File && !string.IsNullOrEmpty(a.Path)) g.Exes.Add(x(a.Path));
            foreach (var p in platforms ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(p)) g.Platforms.Add(p);
            foreach (var r in roms ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(r)) g.Roms.Add(x(r));
            if (emu != null)
            {
                g.Emulator = new EmulatorInfo
                {
                    Name = emu.Name ?? "",
                    InstallDir = emuDir ?? "",
                    Profile = emu.Profile ?? "",
                    Exe = string.IsNullOrEmpty(emu.Exe) ? "" : x(emu.Exe)
                };
            }
            return g;
        }

        private static string Expand(string text, string emuDir, Func<string, string, string> expand)
        {
            if (text == null) return "";
            if (expand == null || text.IndexOf('{') < 0) return text;
            if (emuDir == null && emulatorDir.IsMatch(text)) return text;
            try { return expand(text, emuDir) ?? text; } catch { return text; }
        }
    }

    public enum WriteResult { Unchanged, Written, Failed }

    /// <summary>
    /// The ONLY file this extension writes besides Playnite's own data: <c>playnite-library.json</c>, next to AGSS's
    /// <c>status.json</c> (the AGSS data folder). Written in one go (unique temporary file then replace), with a format
    /// number, only when its content changes (the generation date alone never causes a rewrite).
    /// </summary>
    public static class LibraryFile
    {
        public const int Schema = 1;
        public const string FileName = "playnite-library.json";
        /// <summary>Playnite's Steam library plugin.</summary>
        public static readonly Guid SteamPlugin = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        private static readonly Regex generatedField = new Regex("\"generated_utc\":\"[^\"]*\",?", RegexOptions.CultureInvariant);

        /// <summary>
        /// Path of the list in the folder of <paramref name="statusPath"/>. Null when that folder is unknown or when the
        /// configured file is not named <c>status.json</c> (a folder typed without the file name would otherwise send the
        /// list one level up, into a folder the user never designated).
        /// </summary>
        public static string PathNextTo(string statusPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(statusPath)) return null;
                var p = statusPath.Trim();
                if (!string.Equals(Path.GetFileName(p), DataLocator.FileName, StringComparison.OrdinalIgnoreCase)) return null;
                var dir = Path.GetDirectoryName(p);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FileName);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Steam appid of a Steam game (Steam plugin and numeric game id), otherwise null.</summary>
        public static long? SteamAppIdOf(Guid pluginId, string gameId)
        {
            long id;
            return pluginId == SteamPlugin && long.TryParse((gameId ?? "").Trim(), out id) && id > 0 ? (long?)id : null;
        }

        /// <summary>JSON text (stable: games sorted by name then id; the same library gives the same file apart from the date).</summary>
        public static string ToJson(IEnumerable<LibraryGame> games, DateTime? generatedUtc = null)
        {
            var list = (games ?? Enumerable.Empty<LibraryGame>())
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name))
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Id, StringComparer.Ordinal)
                .Select(g =>
                {
                    var d = new Dictionary<string, object>
                    {
                        { "id", g.Id ?? "" },
                        { "name", g.Name.Trim() },
                        { "source", g.Source ?? "" },
                        { "plugin_id", g.PluginId ?? "" },
                        { "install_dir", g.InstallDir ?? "" },
                        { "exes", Clean(g.Exes) },
                        { "game_id", g.GameId ?? "" },
                        { "platforms", (g.Platforms ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList() },
                        { "hidden", g.Hidden }
                    };
                    if (g.SteamAppId.HasValue) d["steam_appid"] = g.SteamAppId.Value;
                    var roms = Clean(g.Roms);
                    if (roms.Count > 0) d["roms"] = roms;
                    if (g.Emulator != null)
                        d["emulator"] = new Dictionary<string, object>
                        {
                            { "name", g.Emulator.Name ?? "" },
                            { "install_dir", g.Emulator.InstallDir ?? "" },
                            { "profile", g.Emulator.Profile ?? "" },
                            { "exe", g.Emulator.Exe ?? "" }
                        };
                    return d;
                }).ToList();
            var root = new Dictionary<string, object> { { "schema", Schema } };
            if (generatedUtc.HasValue) root["generated_utc"] = generatedUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            root["games"] = list;
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(root);
        }

        private static List<string> Clean(List<string> l)
        {
            return (l ?? new List<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Same list content: the generation date is ignored, and the comparison is made on the written bytes.</summary>
        public static bool SameContent(byte[] existing, string text)
        {
            if (existing == null || text == null) return false;
            var enc = new UTF8Encoding(false);
            var a = generatedField.Replace(enc.GetString(existing), "");
            var b = generatedField.Replace(enc.GetString(enc.GetBytes(text)), "");
            return a == b;
        }

        /// <summary>
        /// Writes <paramref name="text"/> to <paramref name="path"/> atomically (same-folder temporary file with a unique
        /// name, then replace). Nothing is written when the file already has this content (date apart). Never creates a
        /// folder, never throws: a failure is returned with its cause.
        /// </summary>
        public static WriteResult Write(string path, string text, out Exception error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path) || text == null) { error = new ArgumentException("no path"); return WriteResult.Failed; }
            string tmp = null;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { error = new DirectoryNotFoundException(dir ?? path); return WriteResult.Failed; }
                if (File.Exists(path) && SameContent(File.ReadAllBytes(path), text)) return WriteResult.Unchanged;
                tmp = Path.Combine(dir, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                Exception last = null;
                for (int attempt = 0; attempt < 3; attempt++) // another writer (a second Playnite instance) may hold the file for a moment
                {
                    if (attempt > 0) System.Threading.Thread.Sleep(40);
                    if (MoveFileEx(tmp, path, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) { last = null; break; } // atomic rename, no intermediate file
                    last = new IOException("MoveFileEx failed (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ") for " + path);
                }
                if (last != null)
                {
                    // Volumes refusing the rename-over: copy (a reader may then see a partial file for a moment, as with 1.2.0's File.Replace fallback).
                    try { File.Copy(tmp, path, true); File.Delete(tmp); last = null; }
                    catch (Exception ex) { last = ex; }
                }
                if (last != null) throw last;
                SweepReplaceLeftovers(dir);
                return WriteResult.Written;
            }
            catch (Exception ex)
            {
                error = ex;
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch { }
                return WriteResult.Failed;
            }
        }

        private const int MOVEFILE_REPLACE_EXISTING = 0x1, MOVEFILE_WRITE_THROUGH = 0x8;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool MoveFileEx(string existing, string target, int flags);

        /// <summary>
        /// Removes <c>playnite-library.json~RF*.TMP</c> files: Windows' ReplaceFile (used by 1.2.0 before this build) can leave
        /// such a copy of our own file behind when two writers collide. Only files with that exact pattern, best effort.
        /// </summary>
        private static void SweepReplaceLeftovers(string dir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, FileName + "~RF*.TMP"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        /// <summary>Convenience: true when the file now has this content.</summary>
        public static bool WriteAtomic(string path, string text)
        {
            Exception ignored;
            return Write(path, text, out ignored) != WriteResult.Failed;
        }

        /// <summary>
        /// Deletes the list (and only it: the file must be named <see cref="FileName"/>). Used when the list is switched
        /// off or the extension disabled, so that AGSS does not keep an outdated list. Returns true when a file was deleted.
        /// </summary>
        public static bool Delete(string path, out Exception error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase)) return false;
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception ex) { error = ex; return false; }
        }
    }
}
