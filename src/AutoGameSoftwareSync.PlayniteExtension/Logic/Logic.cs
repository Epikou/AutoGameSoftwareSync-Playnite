using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoGameSoftwareSync.PlayniteExtension.Logic
{
    public static class Labels
    {
        private static readonly Dictionary<string, string[]> table = new Dictionary<string, string[]>
        {
            // order: synced, needs_backup, cloud_newer, conflict, running, never_synced, error, unreachable, paused, no_shared_folder
            { "en", new[] { "Synced", "Needs backup", "Cloud newer", "Conflict", "Running", "Never synced", "Error", "Unreachable", "Paused", "No shared folder" } },
            { "fr", new[] { "Synchronisé", "Sauvegarde nécessaire", "Cloud plus récent", "Conflit", "Jeu en cours", "Jamais sauvegardé", "Erreur", "Dossier injoignable", "En pause", "Aucun dossier partagé" } },
            { "ru", new[] { "Синхронизировано", "Нужна резервная копия", "Облако новее", "Конфликт", "Игра запущена", "Не синхронизировано", "Ошибка", "Папка недоступна", "Пауза", "Нет общей папки" } },
            { "es", new[] { "Sincronizado", "Copia necesaria", "Nube más reciente", "Conflicto", "En ejecución", "Nunca sincronizado", "Error", "Carpeta inaccesible", "En pausa", "Sin carpeta compartida" } },
            { "zh", new[] { "已同步", "需要备份", "云端更新", "冲突", "运行中", "从未同步", "错误", "无法访问", "已暂停", "未设置共享文件夹" } },
        };

        public static string LanguageCode(string playniteLanguage)
        {
            var code = (playniteLanguage ?? "en").Split('_', '-')[0].ToLowerInvariant();
            return table.ContainsKey(code) ? code : "en";
        }

        /// <summary>Label without prefix; null for Unknown.</summary>
        public static string Get(AgssState state, string playniteLanguage)
        {
            int i = (int)state - 1;
            return i < 0 ? null : table[LanguageCode(playniteLanguage)][i];
        }
    }

    public class GameCandidate
    {
        public Guid Id;
        public string Name = "";
        public List<string> ExeNames = new List<string>();
        public string InstallDir = "";
    }

    public static class Matcher
    {
        public static string Normalize(string s)
        {
            return new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static string Dir(string d)
        {
            return (d ?? "").Trim().TrimEnd('\\', '/').ToLowerInvariant();
        }

        /// <summary>Playnite game id -> AGSS entry. First rule giving a match wins per entry; all matching games get it.</summary>
        public static Dictionary<Guid, AgssGameEntry> Match(IEnumerable<AgssGameEntry> entries, IList<GameCandidate> games)
        {
            var result = new Dictionary<Guid, AgssGameEntry>();
            foreach (var e in entries)
            {
                IEnumerable<GameCandidate> hits = Enumerable.Empty<GameCandidate>();
                var exe = string.IsNullOrWhiteSpace(e.Exe) ? "" : Path.GetFileName(e.Exe.Trim()).ToLowerInvariant();
                if (exe != "")
                    hits = games.Where(g => g.ExeNames.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase))).ToList();
                if (!hits.Any())
                {
                    var nk = Normalize(e.Key); var nn = Normalize(e.Name); var no = Normalize(e.OriginalName);
                    hits = games.Where(g => { var n = Normalize(g.Name); return n != "" && (n == nk || n == nn || n == no); }).ToList();
                }
                if (!hits.Any() && Dir(e.InstallDir) != "")
                    hits = games.Where(g => Dir(g.InstallDir) == Dir(e.InstallDir)).ToList();
                foreach (var g in hits)
                    if (!result.ContainsKey(g.Id)) result[g.Id] = e;
            }
            return result;
        }
    }

    /// <summary>
    /// Which genres / tags / features / categories belong to this extension. Only an item written in the extension's
    /// exact format is ours: prefix + a known state text (any supported language, current or older label table),
    /// optionally followed by " · " and a device name. "Action", "Action RPG" or any item the user created are never
    /// ours, whatever the prefix. The prefix itself must be distinctive (1.2.0: at least 4 characters and one symbol).
    /// </summary>
    public static class OwnItems
    {
        public const int MinPrefixLength = 4;
        private const string DeviceSeparator = " · ";
        private static readonly HashSet<string> labels = AllLabels();

        private static HashSet<string> AllLabels()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (AgssState s in Enum.GetValues(typeof(AgssState)))
            {
                if (s == AgssState.Unknown) continue;
                foreach (var lang in StateTexts.Languages)
                {
                    var a = StateTexts.Get(s, lang); if (!string.IsNullOrEmpty(a)) set.Add(a);
                    var b = Labels.Get(s, lang); if (!string.IsNullOrEmpty(b)) set.Add(b);
                }
            }
            return set;
        }

        /// <summary>Why this prefix is refused (English message), or null when it is acceptable.</summary>
        public static string PrefixProblem(string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || prefix.Trim().Length == 0) return "The prefix must not be empty.";
            if (prefix.Trim().Length < MinPrefixLength) return "The prefix must have at least " + MinPrefixLength + " characters (for example \"" + FallbackGenre.DefaultPrefix.Trim() + "\").";
            if (!prefix.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
                return "The prefix must contain at least one symbol such as \":\", \"●\" or \"[\", so that it can never match a genre of your own (for example \"" + FallbackGenre.DefaultPrefix.Trim() + "\").";
            return null;
        }

        /// <summary>True when <paramref name="name"/> is an item written by this extension with <paramref name="prefix"/>.</summary>
        public static bool IsOwn(string name, string prefix)
        {
            if (name == null || string.IsNullOrEmpty(prefix) || !name.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var rest = name.Substring(prefix.Length);
            if (labels.Contains(rest)) return true;
            var i = rest.IndexOf(DeviceSeparator, StringComparison.Ordinal);
            return i > 0 && labels.Contains(rest.Substring(0, i)) && rest.Length > i + DeviceSeparator.Length;
        }
    }

    public static class ItemPlanner
    {
        /// <summary>
        /// current = item names of the game in the chosen field. Returns the new list of names,
        /// or null when nothing must change. Only items in the extension's own format (<see cref="OwnItems.IsOwn"/>)
        /// are ever touched; a user item that merely starts with the prefix is kept.
        /// </summary>
        public static List<string> Plan(IList<string> current, string prefix, string desired)
        {
            var mine = current.Where(n => OwnItems.IsOwn(n, prefix)).ToList();
            bool alreadyOk = desired == null
                ? mine.Count == 0
                : mine.Count == 1 && mine[0] == desired;
            if (alreadyOk) return null;
            var result = current.Where(n => !OwnItems.IsOwn(n, prefix)).ToList();
            if (desired != null) result.Add(desired);
            return result;
        }

        public static string Desired(AgssState state, string prefix, string language, bool showSynced)
        {
            if (state == AgssState.Unknown) return null;
            if (state == AgssState.Synced && !showSynced) return null;
            return prefix + Labels.Get(state, language);
        }

        public static bool ShouldNotify(AgssState? previous, AgssState now)
        {
            return previous.HasValue && previous.Value != now &&
                   (now == AgssState.Conflict || now == AgssState.CloudNewer);
        }

        /// <summary>When AGSS is not watching, only unreachable/paused states are applied.</summary>
        public static IEnumerable<AgssGameEntry> Applicable(AgssStatusFile f)
        {
            return f.Watching ? f.Games : f.Games.Where(g => g.State == AgssState.Unreachable || g.State == AgssState.Paused);
        }
    }
}
