using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HCI_Lab_codes.Models
{
    /// <summary>
    /// Loads <c>adaptive_hints_{face_id}.json</c> written by
    /// <c>python/gaze_adapter.py</c> + <c>python/emotion_adapter.py</c>
    /// at the end of each session, and exposes a typed view for the game UI.
    ///
    /// Used by <see cref="TuioDemoApp.TuioDemo"/> at level start to bias
    /// difficulty / audio hints / hint timing for the current student.
    ///
    /// Schema (excerpt):
    /// <code>
    /// {
    ///   "face_id": "student:youssef",
    ///   "gaze": { "recommended_cta_zone": "Top-Left", "enlarge_zones": ["..."] },
    ///   "emotion": {
    ///     "dominant": "happy|confused|bored",
    ///     "happy_rate":   0.65,
    ///     "confused_rate":0.20,
    ///     "bored_rate":   0.15,
    ///     "start_with_audio_hint": true,
    ///     "difficulty_bias": "easier|harder|same"
    ///   },
    ///   "combined": { "layout_bias": "center", "auto_hint_threshold_sec": 4 }
    /// }
    /// </code>
    /// </summary>
    public class AdaptiveHints
    {
        // ── gaze ────────────────────────────────────────────────────────────
        public string RecommendedCtaZone = "Center";
        public string LayoutBias         = "center";

        // ── emotion ─────────────────────────────────────────────────────────
        public string DominantEmotion   = "happy";
        public double HappyRate         = 0.0;
        public double ConfusedRate      = 0.0;
        public double BoredRate         = 0.0;
        public bool   StartWithAudioHint = false;
        public string DifficultyBias    = "same";   // "easier" | "harder" | "same"

        // ── combined / general ──────────────────────────────────────────────
        public int    AutoHintThresholdSec = 4;

        public bool   WasLoaded = false;
        public string SourcePath = "";

        /// <summary>
        /// Loads hints for the given profile key (e.g. <c>"student:youssef"</c>).
        /// Returns an instance with <see cref="WasLoaded"/> = false if no file
        /// exists yet (first session). Never throws.
        /// </summary>
        public static AdaptiveHints Load(string faceId)
        {
            var result = new AdaptiveHints();
            if (string.IsNullOrWhiteSpace(faceId)) return result;

            string safeId = SanitizeFaceId(faceId);
            string filename = "adaptive_hints_" + safeId + ".json";
            string path = LocateHintsFile(filename);
            if (path == null) return result;

            try
            {
                string json = File.ReadAllText(path);
                var root = SimpleJson.Parse(json);
                if (root == null) return result;

                if (root.ContainsKey("gaze") && root["gaze"] is System.Collections.Generic.Dictionary<string, object> g)
                {
                    if (g.ContainsKey("recommended_cta_zone"))
                        result.RecommendedCtaZone = g["recommended_cta_zone"]?.ToString() ?? "Center";
                }
                if (root.ContainsKey("emotion") && root["emotion"] is System.Collections.Generic.Dictionary<string, object> e)
                {
                    if (e.ContainsKey("dominant"))               result.DominantEmotion    = e["dominant"]?.ToString() ?? "happy";
                    if (e.ContainsKey("happy_rate"))             result.HappyRate          = ToDouble(e["happy_rate"]);
                    if (e.ContainsKey("confused_rate"))          result.ConfusedRate       = ToDouble(e["confused_rate"]);
                    if (e.ContainsKey("bored_rate"))             result.BoredRate          = ToDouble(e["bored_rate"]);
                    if (e.ContainsKey("start_with_audio_hint"))  result.StartWithAudioHint = ToBool(e["start_with_audio_hint"]);
                    if (e.ContainsKey("difficulty_bias"))        result.DifficultyBias     = e["difficulty_bias"]?.ToString() ?? "same";
                }
                if (root.ContainsKey("combined") && root["combined"] is System.Collections.Generic.Dictionary<string, object> c)
                {
                    if (c.ContainsKey("layout_bias"))             result.LayoutBias          = c["layout_bias"]?.ToString() ?? "center";
                    if (c.ContainsKey("auto_hint_threshold_sec")) result.AutoHintThresholdSec = (int)ToDouble(c["auto_hint_threshold_sec"]);
                }
                result.WasLoaded  = true;
                result.SourcePath = path;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AdaptiveHints] Failed to load " + path + ": " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Maps emotion.difficulty_bias from past sessions into the same
        /// "easier"|"harder"|"normal" vocabulary used by
        /// <c>TuioDemo.ApplyDifficultyHint</c>.
        /// </summary>
        public string ResolvedDifficultyHint()
        {
            switch ((DifficultyBias ?? "same").ToLowerInvariant())
            {
                case "easier":   return "easier";
                case "harder":   return "harder";
                default:         return "normal";
            }
        }

        /// <summary>One-line summary suitable for an HUD chip.</summary>
        public string ShortSummary()
        {
            if (!WasLoaded) return "";
            string bias = ResolvedDifficultyHint();
            return string.Format(
                "🎯 Adaptive: {0} | mood {1} | hints @ {2}s",
                bias, DominantEmotion, AutoHintThresholdSec);
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static string SanitizeFaceId(string faceId)
        {
            string s = Regex.Replace(faceId ?? "user", @"[^A-Za-z0-9]+", "_").Trim('_');
            return string.IsNullOrEmpty(s) ? "user" : s;
        }

        /// <summary>
        /// Hint files can live in either:
        ///   1. <c>bin/Debug/Data/</c> (same place as users.json)
        ///   2. <c>{ProjectRoot}/Data/</c> (where Python writes by default)
        /// We check both so things work regardless of which side ran first.
        /// </summary>
        private static string LocateHintsFile(string filename)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir,                       "Data", filename),
                Path.Combine(baseDir, "..",           "..", "Data", filename), // project root
                Path.Combine(baseDir, "..", "..", "..", "Data", filename),     // safety net
            };
            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return Path.GetFullPath(c);
                }
                catch { }
            }
            return null;
        }

        private static double ToDouble(object v)
        {
            if (v == null) return 0.0;
            if (v is double d) return d;
            if (v is int i)    return i;
            if (v is long l)   return l;
            double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result);
            return result;
        }

        private static bool ToBool(object v)
        {
            if (v == null) return false;
            if (v is bool b) return b;
            string s = v.ToString().ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }
    }
}
