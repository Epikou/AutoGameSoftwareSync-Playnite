using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoGameSoftwareSync.PlayniteExtension.Logic
{
    /// <summary>
    /// COMPATIBILITE : seul fichier de l'extension qui cite les anciens noms de l'application (anciens dossiers de donnees,
    /// anciennes variables d'environnement). Supprimable d'un bloc : l'extension ne lit alors que le nouveau dossier.
    /// </summary>
    public static class Legacy
    {
        private const string OldDataFolder = "AutoGameSaveSync";
        private static readonly string[] OldDataFolders = { "AutoGameSaveSync", "GameSaveSync", "SaveSync" };
        private static readonly string[] OldEnvVars = { "AUTOGAMESAVESYNC_DATA_DIR", "GAMESAVESYNC_DATA_DIR", "SAVESYNC_DATA_DIR" };

        private static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        /// <summary>Ancien chemin de status.json (repli quand le nouveau n'existe pas encore).</summary>
        public static string OldStatusPath
        {
            get { return Path.Combine(LocalAppData, OldDataFolder, "status.json"); }
        }

        /// <summary>Tous les anciens emplacements de status.json, par priorite.</summary>
        public static IEnumerable<string> OldStatusPaths
        {
            get { return OldDataFolders.Select(d => Path.Combine(LocalAppData, d, "status.json")); }
        }

        /// <summary>Dossiers imposes par d'anciennes variables d'environnement (processus puis utilisateur).</summary>
        public static IEnumerable<string> OldEnvDirs(Func<string, EnvironmentVariableTarget, string> getEnv)
        {
            foreach (var v in OldEnvVars)
            {
                yield return getEnv(v, EnvironmentVariableTarget.Process);
                yield return getEnv(v, EnvironmentVariableTarget.User);
            }
        }

        /// <summary>Retourne l'ancien chemin seulement si le nouveau est absent et l'ancien present.</summary>
        public static string Resolve(string newPath, Func<string, bool> exists)
        {
            if (exists(newPath)) return newPath;
            var old = OldStatusPath;
            return exists(old) ? old : newPath;
        }
    }
}
