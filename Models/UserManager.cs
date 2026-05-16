using System;
using System.Collections.Generic;
using System.IO;

namespace HCI_Lab_codes.Models
{
    public class User
    {
        public string FaceId { get; set; }
        public string Role { get; set; } // "child", "teacher", "admin"
        public string DisplayName { get; set; }
        public int Age { get; set; }
        public string Difficulty { get; set; } // "easy", "medium", "hard"
        public string EnrolledAt { get; set; }

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                { "face_id", FaceId },
                { "role", Role },
                { "display_name", DisplayName },
                { "age", Age },
                { "difficulty", Difficulty },
                { "enrolled_at", EnrolledAt }
            };
        }

        public static User FromDict(Dictionary<string, object> dict)
        {
            return new User
            {
                FaceId = dict.ContainsKey("face_id") ? dict["face_id"].ToString() : "",
                Role = dict.ContainsKey("role") ? dict["role"].ToString() : "child",
                DisplayName = dict.ContainsKey("display_name") ? dict["display_name"].ToString() : "Unknown",
                Age = dict.ContainsKey("age") ? Convert.ToInt32(dict["age"]) : 5,
                Difficulty = dict.ContainsKey("difficulty") ? dict["difficulty"].ToString() : "easy",
                EnrolledAt = dict.ContainsKey("enrolled_at") ? dict["enrolled_at"].ToString() : DateTime.Now.ToString("yyyy-MM-dd")
            };
        }
    }

    public static class UserManager
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "users.json");
        private static List<User> _users = new List<User>();

        public static void LoadUsers()
        {
            if (!File.Exists(FilePath))
            {
                // Create with a default teacher for testing
                _users.Add(new User { FaceId = "teacher_default", Role = "teacher", DisplayName = "Ms. Sara", Age = 30, Difficulty = "medium", EnrolledAt = DateTime.Now.ToString("yyyy-MM-dd") });
                SaveUsers();
                return;
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                var dict = SimpleJson.Parse(json);
                _users.Clear();
                
                if (dict.ContainsKey("users") && dict["users"] is List<object> userList)
                {
                    foreach (var obj in userList)
                    {
                        if (obj is Dictionary<string, object> userDict)
                        {
                            _users.Add(User.FromDict(userDict));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UserManager] Load Error: " + ex.Message);
            }
        }

        public static void SaveUsers()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var userList = new List<object>();
                foreach (var u in _users)
                {
                    userList.Add(u.ToDict());
                }

                var root = new Dictionary<string, object> { { "users", userList } };
                string json = SimpleJson.Serialize(root);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UserManager] Save Error: " + ex.Message);
            }
        }

        public static User GetByFaceId(string faceId)
        {
            if (_users.Count == 0) LoadUsers();
            foreach (var u in _users)
            {
                if (u.FaceId == faceId) return u;
            }
            return null;
        }

        public static User CreateUser(string faceId, string role = "child", string displayName = null)
        {
            // Derive a readable name from the profile key (e.g. "student:youssef" → "Youssef")
            if (string.IsNullOrWhiteSpace(displayName))
            {
                int colon = faceId.IndexOf(':');
                string slug = colon >= 0 ? faceId.Substring(colon + 1) : faceId;
                // Convert slug: "dr-ayman" → "Dr Ayman", "youssef" → "Youssef"
                var parts = slug.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                    if (parts[i].Length > 0)
                        parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
                displayName = string.Join(" ", parts);
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = (role == "teacher" ? "Teacher" : "Student") + " " + (_users.Count + 1);
            }

            var u = new User
            {
                FaceId = faceId,
                Role = role,
                DisplayName = displayName,
                Age = 5,
                Difficulty = "easy",
                EnrolledAt = DateTime.Now.ToString("yyyy-MM-dd")
            };
            _users.Add(u);
            SaveUsers();
            return u;
        }

        public static void UpdateDifficulty(string faceId, string newDifficulty)
        {
            var u = GetByFaceId(faceId);
            if (u != null)
            {
                u.Difficulty = newDifficulty;
                SaveUsers();
                Console.WriteLine($"[UserManager] Updated {faceId} difficulty to {newDifficulty}");
            }
        }

        /// <summary>
        /// Full update: replaces every editable field for the matched user.
        /// Returns true if the user was found and saved, false otherwise.
        /// </summary>
        public static bool UpdateUser(User updated)
        {
            if (updated == null || string.IsNullOrEmpty(updated.FaceId)) return false;
            if (_users.Count == 0) LoadUsers();

            for (int i = 0; i < _users.Count; i++)
            {
                if (_users[i].FaceId == updated.FaceId)
                {
                    _users[i].DisplayName = updated.DisplayName;
                    _users[i].Role        = updated.Role;
                    _users[i].Age         = updated.Age;
                    _users[i].Difficulty  = updated.Difficulty;
                    SaveUsers();
                    Console.WriteLine($"[UserManager] Updated user {updated.FaceId}");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes a user record. Returns true if the user existed and was deleted.
        /// Note: this does NOT delete adaptive_hints_*.json or sessions.json
        /// rows — those are intentionally kept as analytics history.
        /// </summary>
        public static bool DeleteUser(string faceId)
        {
            if (string.IsNullOrEmpty(faceId)) return false;
            if (_users.Count == 0) LoadUsers();

            for (int i = 0; i < _users.Count; i++)
            {
                if (_users[i].FaceId == faceId)
                {
                    _users.RemoveAt(i);
                    SaveUsers();
                    Console.WriteLine($"[UserManager] Deleted user {faceId}");
                    return true;
                }
            }
            return false;
        }

        public static List<User> GetAllUsers()
        {
            if (_users.Count == 0) LoadUsers();
            return _users;
        }
    }
}
