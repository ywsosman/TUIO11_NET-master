using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TuioDemoApp
{
    /// <summary>
    /// Per-user game progress persisted to %USERPROFILE%\Documents\TUIO_Evaluation\progress.txt.
    /// Profile key is e.g. "student:default" or "teacher:default" (face identification via Python
    /// only distinguishes Student vs Teacher today, so we use a single shared profile per kind).
    ///
    /// File format is a pipe-delimited table (keeps the project Newtonsoft-free):
    ///   # TUIO_NET progress v1
    ///   student:default|student|2|2026-05-13T10:30:00Z
    ///   teacher:default|teacher|-1|2026-05-13T11:00:00Z
    /// Columns: profileKey | kind | highestCompletedLevelIndex (-1 = none) | lastSeenUtc
    /// </summary>
    public class UserProgressStore
    {
        public class Profile
        {
            public string Key;
            public string Kind = "student";
            public int HighestCompletedLevel = -1;
            public DateTime LastSeenUtc = DateTime.MinValue;
        }

        private readonly string filePath;
        private readonly Dictionary<string, Profile> profiles =
            new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);
        private readonly object syncRoot = new object();

        public string FilePath { get { return filePath; } }

        private UserProgressStore(string path)
        {
            filePath = path;
        }

        public static UserProgressStore Load()
        {
            string evalDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TUIO_Evaluation");
            try { Directory.CreateDirectory(evalDir); } catch { }
            string path = Path.Combine(evalDir, "progress.txt");
            var store = new UserProgressStore(path);
            store.ReadFromDisk();
            return store;
        }

        private void ReadFromDisk()
        {
            if (!File.Exists(filePath)) return;
            try
            {
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                foreach (string raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    string line = raw.Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                    string[] parts = line.Split('|');
                    if (parts.Length < 3) continue;

                    string key = parts[0].Trim();
                    if (string.IsNullOrEmpty(key)) continue;

                    int level;
                    if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out level))
                        level = -1;

                    DateTime seen = DateTime.MinValue;
                    if (parts.Length > 3)
                        DateTime.TryParse(parts[3].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out seen);

                    var profile = new Profile
                    {
                        Key = key,
                        Kind = parts[1].Trim(),
                        HighestCompletedLevel = level,
                        LastSeenUtc = seen
                    };
                    profiles[key] = profile;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UserProgressStore] Read failed: " + ex.Message);
            }
        }

        private void WriteToDisk()
        {
            try
            {
                var sb = new StringBuilder(256);
                sb.AppendLine("# TUIO_NET progress v1");
                sb.AppendLine("# profileKey|kind|highestCompletedLevelIndex|lastSeenUtc");
                foreach (var kv in profiles)
                {
                    Profile p = kv.Value;
                    sb.Append(SafeField(p.Key)).Append('|')
                      .Append(SafeField(p.Kind ?? "student")).Append('|')
                      .Append(p.HighestCompletedLevel.ToString(CultureInfo.InvariantCulture)).Append('|')
                      .Append(p.LastSeenUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
                      .AppendLine();
                }
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UserProgressStore] Write failed: " + ex.Message);
            }
        }

        private static string SafeField(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('|', '_').Replace('\n', ' ').Replace('\r', ' ');
        }

        public Profile EnsureProfile(string key, string kind)
        {
            lock (syncRoot)
            {
                Profile p;
                if (!profiles.TryGetValue(key, out p))
                {
                    p = new Profile
                    {
                        Key = key,
                        Kind = string.IsNullOrEmpty(kind) ? "student" : kind,
                        HighestCompletedLevel = -1,
                        LastSeenUtc = DateTime.UtcNow
                    };
                    profiles[key] = p;
                    WriteToDisk();
                }
                else
                {
                    if (!string.IsNullOrEmpty(kind))
                        p.Kind = kind;
                    p.LastSeenUtc = DateTime.UtcNow;
                }
                return p;
            }
        }

        /// <summary>How many levels (counting from 1) the user has access to. Teachers always get all.</summary>
        public int UnlockedLevels(string key, int totalLevels)
        {
            if (totalLevels <= 0) return 0;
            lock (syncRoot)
            {
                Profile p;
                if (!profiles.TryGetValue(key, out p))
                    return 1;
                if (string.Equals(p.Kind, "teacher", StringComparison.OrdinalIgnoreCase))
                    return totalLevels;
                int unlocked = p.HighestCompletedLevel + 2;
                if (unlocked < 1) unlocked = 1;
                if (unlocked > totalLevels) unlocked = totalLevels;
                return unlocked;
            }
        }

        /// <summary>Returns true if the given level (0-based) is locked for the profile.</summary>
        public bool IsLevelLocked(string key, int levelIndex, int totalLevels)
        {
            return levelIndex >= UnlockedLevels(key, totalLevels);
        }

        /// <summary>Record that a level was completed. Advances the unlock cursor if needed.</summary>
        public void RecordLevelCompleted(string key, string kind, int levelIndex)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (syncRoot)
            {
                Profile p;
                if (!profiles.TryGetValue(key, out p))
                {
                    p = new Profile
                    {
                        Key = key,
                        Kind = string.IsNullOrEmpty(kind) ? "student" : kind,
                        HighestCompletedLevel = -1
                    };
                    profiles[key] = p;
                }
                if (levelIndex > p.HighestCompletedLevel)
                    p.HighestCompletedLevel = levelIndex;
                p.LastSeenUtc = DateTime.UtcNow;
                WriteToDisk();
            }
        }

        public int HighestCompletedLevel(string key)
        {
            lock (syncRoot)
            {
                Profile p;
                if (!profiles.TryGetValue(key, out p)) return -1;
                return p.HighestCompletedLevel;
            }
        }
    }
}
