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

    public static class ItemPlanner
    {
        /// <summary>
        /// current = item names of the game in the chosen field. Returns the new list of names,
        /// or null when nothing must change. Items not starting with prefix are never touched.
        /// </summary>
        public static List<string> Plan(IList<string> current, string prefix, string desired)
        {
            var mine = current.Where(n => n != null && n.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            bool alreadyOk = desired == null
                ? mine.Count == 0
                : mine.Count == 1 && mine[0] == desired;
            if (alreadyOk) return null;
            var result = current.Where(n => !(n != null && n.StartsWith(prefix, StringComparison.Ordinal))).ToList();
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
