using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AutoGameSoftwareSync.PlayniteExtension.Logic
{
    /// <summary>State of a game as written by AGSS. New values are only ever appended (the order is used by labels).</summary>
    public enum AgssState
    {
        Unknown, Synced, NeedsBackup, CloudNewer, Conflict, Running, NeverSynced, Error, Unreachable, Paused, NoSharedFolder
    }

    public class AgssGameEntry
    {
        public string Name = "";
        public string Key = "";
        public string Exe = "";
        public string InstallDir = "";
        public string SavePath = "";
        public AgssState State = AgssState.Unknown;
        public string LastSyncUtc;
        public string Detail = "";
        // ---- schema 2 (all optional) ----
        public string DisplayName = "";
        public string OriginalName = "";
        /// <summary>Device the version currently in place comes from (newer apps).</summary>
        public string OriginDevice = "";
        /// <summary>#RRGGBB colour of <see cref="OriginDevice"/>.</summary>
        public string OriginDeviceColor = "";
        /// <summary>Device that sent the newest shared version (older fallback for the device name).</summary>
        public string RemoteHeadMachine = "";
        /// <summary>#RRGGBB colour of <see cref="RemoteHeadMachine"/>.</summary>
        public string MachineColor = "";
    }

    public class AgssStatusFile
    {
        public int Schema = 1;
        public bool Watching;
        public bool SharedFolderReachable = true;
        public string UpdatedAtUtc = "";
        /// <summary>Name and colour of the device running AGSS (schema 2, may be empty).</summary>
        public string Machine = "";
        public string MachineColor = "";
        public List<AgssGameEntry> Games = new List<AgssGameEntry>();
    }

    public interface IStatusSource
    {
        /// <summary>Returns the current status, or null when unavailable. Never throws.</summary>
        AgssStatusFile Read();
    }

    public class FakeStatusSource : IStatusSource
    {
        public AgssStatusFile Current;
        public AgssStatusFile Read() { return Current; }
    }

    public static class StatusParser
    {
        /// <summary>Schemas understood. Newer schemas only add fields, so a higher number is still read.</summary>
        public const int MinSchema = 1;

        public static AgssState ParseState(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "synced": return AgssState.Synced;
                case "needs_backup": return AgssState.NeedsBackup;
                case "cloud_newer": return AgssState.CloudNewer;
                case "conflict": return AgssState.Conflict;
                case "running": return AgssState.Running;
                case "never_synced": return AgssState.NeverSynced;
                case "error": return AgssState.Error;
                case "unreachable": return AgssState.Unreachable;
                case "paused": return AgssState.Paused;
                case "no_shared_folder": return AgssState.NoSharedFolder;
                default: return AgssState.Unknown;
            }
        }

        /// <summary>Tolerant parse: returns null on empty / invalid JSON / missing or invalid schema. Never throws.</summary>
        public static AgssStatusFile Parse(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return null;
                json = json.TrimStart('﻿');
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return null;

                object schemaObj;
                if (!root.TryGetValue("schema", out schemaObj) || !(schemaObj is int)) return null;
                int schema = (int)schemaObj;
                if (schema < MinSchema) return null;

                var file = new AgssStatusFile
                {
                    Schema = schema,
                    Watching = Bool(root, "watching", false),
                    SharedFolderReachable = Bool(root, "shared_folder_reachable", true),
                    UpdatedAtUtc = Str(root, "updated_at_utc"),
                    Machine = Str(root, "machine"),
                    MachineColor = Str(root, "machine_color")
                };
                object games;
                if (root.TryGetValue("games", out games) && games is IEnumerable && !(games is string))
                {
                    foreach (var item in (IEnumerable)games)
                    {
                        var d = item as Dictionary<string, object>;
                        if (d == null) continue;
                        file.Games.Add(new AgssGameEntry
                        {
                            Name = Str(d, "name"), Key = Str(d, "key"), Exe = Str(d, "exe"),
                            InstallDir = Str(d, "install_dir"), SavePath = Str(d, "save_path"),
                            State = ParseState(Str(d, "state")), LastSyncUtc = Str(d, "last_sync_utc"),
                            Detail = Str(d, "detail"),
                            DisplayName = Str(d, "display_name"), OriginalName = Str(d, "original_name"),
                            OriginDevice = Str(d, "origin_device"), OriginDeviceColor = Str(d, "origin_device_color"),
                            RemoteHeadMachine = Str(d, "remote_head_machine"), MachineColor = Str(d, "machine_color")
                        });
                    }
                }
                return file;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Str(Dictionary<string, object> d, string k)
        {
            object v; return d.TryGetValue(k, out v) && v != null && !(v is IEnumerable && !(v is string)) ? v.ToString() : "";
        }

        private static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return def;
            return v is bool ? (bool)v : def;
        }
    }

    /// <summary>Reads status.json with shared access and a few retries. Never throws.</summary>
    public class FileStatusSource : IStatusSource
    {
        private readonly Func<string> pathProvider;
        public FileStatusSource(Func<string> pathProvider) { this.pathProvider = pathProvider; }

        /// <summary>Raw text of the last successful read (used to skip work when nothing changed).</summary>
        public string LastText { get; private set; }

        public AgssStatusFile Read()
        {
            try
            {
                var path = pathProvider();
                if (string.IsNullOrWhiteSpace(path)) return null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (!File.Exists(path)) return null;
                        string text;
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                            text = sr.ReadToEnd();
                        var parsed = StatusParser.Parse(text);
                        if (parsed != null) { LastText = text; return parsed; }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    Thread.Sleep(150); // partially written or locked: retry
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
