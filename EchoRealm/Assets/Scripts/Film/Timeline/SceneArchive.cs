using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>Persists a SceneTimeline to JSON (plus a .txt session-log sidecar) and reads
    /// it back. Pure of game code: callers pass the timeline and the session-log text.
    /// Default directory is persistentDataPath/EchoRealmSaves.</summary>
    public static class SceneArchive
    {
        public static string DefaultDir =>
            Path.Combine(Application.persistentDataPath, "EchoRealmSaves");

        /// <summary>Write the timeline as JSON and the log as a .txt sidecar. Returns the json path.
        /// Populates derived meta fields (eventCount, savedAtIso, durationSec) before writing.</summary>
        public static string Save(SceneTimeline tl, string logText, string dir = null, string fileName = null)
        {
            dir = dir ?? DefaultDir;
            Directory.CreateDirectory(dir);

            tl.meta.eventCount = tl.events.Count;
            if (string.IsNullOrEmpty(tl.meta.savedAtIso))
                tl.meta.savedAtIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            if (tl.events.Count > 0)
                tl.meta.durationSec = tl.events[tl.events.Count - 1].t;

            if (string.IsNullOrEmpty(fileName))
            {
                string id = string.IsNullOrEmpty(tl.meta.sessionId)
                    ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : tl.meta.sessionId;
                fileName = $"scene_{id}_act{tl.meta.finalAct}.json";
            }

            string jsonPath = Path.Combine(dir, fileName);
            File.WriteAllText(jsonPath, JsonUtility.ToJson(tl, prettyPrint: true));
            File.WriteAllText(Path.ChangeExtension(jsonPath, ".txt"), logText ?? "");
            Debug.Log($"[SceneArchive] Saved {tl.events.Count} events → {jsonPath}");
            return jsonPath;
        }

        /// <summary>All saved .json files, newest first. Empty list if the dir is missing.</summary>
        public static List<string> List(string dir = null)
        {
            dir = dir ?? DefaultDir;
            var result = new List<string>();
            if (!Directory.Exists(dir)) return result;
            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            result.AddRange(files);
            return result;
        }

        /// <summary>Deserialize a saved timeline, or null on any failure (corrupt/missing).</summary>
        public static SceneTimeline Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var tl = JsonUtility.FromJson<SceneTimeline>(File.ReadAllText(path));
                if (tl == null || tl.events == null) return null;
                return tl;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SceneArchive] Could not load '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
