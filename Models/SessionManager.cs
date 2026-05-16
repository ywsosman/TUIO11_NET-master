using System;
using System.Collections.Generic;
using System.IO;

namespace HCI_Lab_codes.Models
{
    public class SessionRecord
    {
        public string SessionId { get; set; }
        public string FaceId { get; set; }
        public string Date { get; set; }
        public int Score { get; set; }
        public int Correct { get; set; }
        public int Wrong { get; set; }
        public double DurationMin { get; set; }
        public string Heatmap { get; set; }
        public int AttentionEvents { get; set; }

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                { "session_id", SessionId },
                { "face_id", FaceId },
                { "date", Date },
                { "score", Score },
                { "correct", Correct },
                { "wrong", Wrong },
                { "duration_min", DurationMin },
                { "heatmap", Heatmap },
                { "attention_events", AttentionEvents }
            };
        }

        public static SessionRecord FromDict(Dictionary<string, object> dict)
        {
            return new SessionRecord
            {
                SessionId = dict.ContainsKey("session_id") ? dict["session_id"].ToString() : "",
                FaceId = dict.ContainsKey("face_id") ? dict["face_id"].ToString() : "",
                Date = dict.ContainsKey("date") ? dict["date"].ToString() : "",
                Score = dict.ContainsKey("score") ? Convert.ToInt32(dict["score"]) : 0,
                Correct = dict.ContainsKey("correct") ? Convert.ToInt32(dict["correct"]) : 0,
                Wrong = dict.ContainsKey("wrong") ? Convert.ToInt32(dict["wrong"]) : 0,
                DurationMin = dict.ContainsKey("duration_min") ? Convert.ToDouble(dict["duration_min"]) : 0.0,
                Heatmap = dict.ContainsKey("heatmap") ? dict["heatmap"].ToString() : "",
                AttentionEvents = dict.ContainsKey("attention_events") ? Convert.ToInt32(dict["attention_events"]) : 0
            };
        }
    }

    public static class SessionManager
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "sessions.json");
        private static List<SessionRecord> _sessions = new List<SessionRecord>();

        public static void LoadSessions()
        {
            if (!File.Exists(FilePath))
            {
                SaveSessions();
                return;
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                var dict = SimpleJson.Parse(json);
                _sessions.Clear();
                
                if (dict.ContainsKey("sessions") && dict["sessions"] is List<object> sessList)
                {
                    foreach (var obj in sessList)
                    {
                        if (obj is Dictionary<string, object> sessDict)
                        {
                            _sessions.Add(SessionRecord.FromDict(sessDict));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SessionManager] Load Error: " + ex.Message);
            }
        }

        public static void SaveSessions()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var list = new List<object>();
                foreach (var s in _sessions)
                {
                    list.Add(s.ToDict());
                }

                var root = new Dictionary<string, object> { { "sessions", list } };
                string json = SimpleJson.Serialize(root);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SessionManager] Save Error: " + ex.Message);
            }
        }

        public static SessionRecord CreateSession(string faceId)
        {
            if (_sessions.Count == 0 && File.Exists(FilePath)) LoadSessions();
            
            var s = new SessionRecord
            {
                SessionId = "s_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                FaceId = faceId,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Score = 0,
                Correct = 0,
                Wrong = 0,
                DurationMin = 0,
                Heatmap = "",
                AttentionEvents = 0
            };
            _sessions.Add(s);
            SaveSessions();
            return s;
        }

        public static void CloseSession(string sessionId, int correct, int wrong, double durationMin, string heatmapPath, int attentionEvents)
        {
            if (_sessions.Count == 0) LoadSessions();
            
            foreach (var s in _sessions)
            {
                if (s.SessionId == sessionId)
                {
                    s.Correct = correct;
                    s.Wrong = wrong;
                    s.Score = correct * 10 - wrong * 2; // Arbitrary scoring
                    s.DurationMin = durationMin;
                    s.Heatmap = heatmapPath;
                    s.AttentionEvents = attentionEvents;
                    SaveSessions();
                    return;
                }
            }
        }

        public static List<SessionRecord> GetSessionsForUser(string faceId)
        {
            if (_sessions.Count == 0) LoadSessions();
            
            var result = new List<SessionRecord>();
            foreach (var s in _sessions)
            {
                if (s.FaceId == faceId) result.Add(s);
            }
            return result;
        }
    }
}
