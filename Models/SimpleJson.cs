using System;
using System.Collections.Generic;
using System.Text;

namespace HCI_Lab_codes.Models
{
    public static class SimpleJson
    {
        public static Dictionary<string, object> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();
            int idx = 0;
            return ParseJsonObject(json.Trim(), ref idx);
        }

        private static int SkipWhitespace(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        private static Dictionary<string, object> ParseJsonObject(string s, ref int i)
        {
            var result = new Dictionary<string, object>();
            i = SkipWhitespace(s, i);
            if (i >= s.Length || s[i] != '{') return result;
            i++;
            i = SkipWhitespace(s, i);
            while (i < s.Length && s[i] != '}')
            {
                string key = ParseJsonString(s, ref i);
                if (key == null) break;
                i = SkipWhitespace(s, i);
                if (i < s.Length && s[i] == ':') i++;
                i = SkipWhitespace(s, i);
                result[key] = ParseJsonValue(s, ref i);
                i = SkipWhitespace(s, i);
                if (i < s.Length && s[i] == ',') { i++; i = SkipWhitespace(s, i); }
            }
            if (i < s.Length && s[i] == '}') i++;
            return result;
        }

        private static string ParseJsonString(string s, ref int i)
        {
            i = SkipWhitespace(s, i);
            if (i >= s.Length || s[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\') { i++; if (i < s.Length) sb.Append(s[i++]); }
                else sb.Append(s[i++]);
            }
            if (i < s.Length) i++;
            return sb.ToString();
        }

        private static object ParseJsonValue(string s, ref int i)
        {
            i = SkipWhitespace(s, i);
            if (i >= s.Length) return null;
            if (s[i] == '"') return ParseJsonString(s, ref i);
            if (s[i] == '{') return ParseJsonObject(s, ref i);
            if (s[i] == '[')
            {
                i++;
                i = SkipWhitespace(s, i);
                var list = new List<object>();
                while (i < s.Length && s[i] != ']')
                {
                    object item = ParseJsonValue(s, ref i);
                    list.Add(item);
                    i = SkipWhitespace(s, i);
                    if (i < s.Length && s[i] == ',') { i++; i = SkipWhitespace(s, i); }
                }
                if (i < s.Length && s[i] == ']') i++;
                return list;
            }
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }

            var num = new StringBuilder();
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == '-' || s[i] == 'e' || s[i] == 'E'))
                num.Append(s[i++]);
            string numStr = num.ToString();
            if (numStr.Contains("."))
            {
                double d;
                return double.TryParse(numStr, out d) ? (object)d : (object)0.0;
            }
            int n;
            return int.TryParse(numStr, out n) ? (object)n : 0;
        }

        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            if (obj is string s) return "\"" + s.Replace("\"", "\\\"") + "\"";
            if (obj is bool b) return b ? "true" : "false";
            if (obj is int || obj is long || obj is float || obj is double) return obj.ToString();
            
            if (obj is Dictionary<string, object> dict)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                bool first = true;
                foreach (var kvp in dict)
                {
                    if (!first) sb.Append(",");
                    sb.Append("\"" + kvp.Key + "\":");
                    sb.Append(Serialize(kvp.Value));
                    first = false;
                }
                sb.Append("}");
                return sb.ToString();
            }
            
            if (obj is List<object> list)
            {
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(",");
                    sb.Append(Serialize(item));
                    first = false;
                }
                sb.Append("]");
                return sb.ToString();
            }
            
            return "\"" + obj.ToString() + "\"";
        }
    }
}
