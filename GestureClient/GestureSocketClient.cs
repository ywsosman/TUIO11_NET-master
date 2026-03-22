/*
 * GestureSocketClient - TCP socket client that receives skeleton and gesture data from Python server
 * References: Lab 3 Sockets (connect, receive loop)
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GestureClient
{
    public class GestureSocketClient
    {
        private readonly string host;
        private readonly int port;
        private TcpClient client;
        private NetworkStream stream;
        private Thread receiveThread;
        private bool running;
        private readonly List<IGestureListener> listeners = new List<IGestureListener>();
        private readonly object listenerLock = new object();

        public bool IsConnected => client != null && client.Connected;
        public string Host => host;
        public int Port => port;

        public GestureSocketClient(string host = "127.0.0.1", int port = 5000)
        {
            this.host = host;
            this.port = port;
        }

        public void AddListener(IGestureListener listener)
        {
            lock (listenerLock)
            {
                if (!listeners.Contains(listener))
                    listeners.Add(listener);
            }
        }

        public void RemoveListener(IGestureListener listener)
        {
            lock (listenerLock)
            {
                listeners.Remove(listener);
            }
        }

        public void Connect()
        {
            if (running) return;
            try
            {
                client = new TcpClient(host, port);
                stream = client.GetStream();
                running = true;
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                receiveThread.Start();
                Console.WriteLine("[GestureClient] Connected to {0}:{1}", host, port);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[GestureClient] Failed to connect: {0}", ex.Message);
                throw;
            }
        }

        public void Disconnect()
        {
            running = false;
            try
            {
                if (stream != null) stream.Close();
                if (client != null) client.Close();
            }
            catch { }
            stream = null;
            client = null;
            if (receiveThread != null) receiveThread.Join(500);
            Console.WriteLine("[GestureClient] Disconnected");
        }

        private void ReceiveLoop()
        {
            var buffer = new byte[65536];
            var sb = new StringBuilder();
            while (running && client != null && client.Connected)
            {
                try
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    string chunk = Encoding.UTF8.GetString(buffer, 0, read);
                    sb.Append(chunk);
                    int idx;
                    while ((idx = sb.ToString().IndexOf('\n')) >= 0)
                    {
                        string line = sb.ToString(0, idx);
                        sb.Remove(0, idx + 1);
                        ProcessMessage(line);
                    }
                }
                catch (IOException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (running)
                        Console.WriteLine("[GestureClient] Receive error: {0}", ex.Message);
                    break;
                }
            }
        }

        private void ProcessMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                int idx = 0;
                var msg = ParseJsonObject(json.Trim(), ref idx);
                if (msg == null || msg.Count == 0) return;

                var typeVal = msg.ContainsKey("type") ? msg["type"]?.ToString() : null;
                if (typeVal == "frame")
                {
                    double timestamp = msg.ContainsKey("timestamp") ? Convert.ToDouble(msg["timestamp"]) : 0;
                    var skeleton = ParseSkeleton(msg);
                    var gesture = ParseGesture(msg);

                    lock (listenerLock)
                    {
                        foreach (var l in listeners)
                        {
                            try
                            {
                                if (skeleton != null && skeleton.Count > 0)
                                    l.OnSkeletonUpdate(timestamp, skeleton);
                                if (gesture != null)
                                    l.OnGestureRecognized(timestamp, gesture);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("[GestureClient] Listener error: {0}", ex.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[GestureClient] Parse error: {0}", ex.Message);
            }
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
            if (s[i] == '{')
            {
                i++;
                return ParseJsonObject(s, ref i);
            }
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

        private List<SkeletonLandmark> ParseSkeleton(Dictionary<string, object> msg)
        {
            if (!msg.ContainsKey("skeleton")) return null;
            var arr = msg["skeleton"] as List<object>;
            if (arr == null) return null;
            var result = new List<SkeletonLandmark>();
            foreach (var item in arr)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null) continue;
                var lm = new SkeletonLandmark
                {
                    Id = GetInt(dict, "id"),
                    Name = GetString(dict, "name"),
                    X = (float)GetDouble(dict, "x"),
                    Y = (float)GetDouble(dict, "y"),
                    Z = (float)GetDouble(dict, "z"),
                    Visibility = (float)GetDouble(dict, "visibility")
                };
                result.Add(lm);
            }
            return result;
        }

        private RecognizedGesture ParseGesture(Dictionary<string, object> msg)
        {
            if (!msg.ContainsKey("gesture") || msg["gesture"] == null) return null;
            var dict = msg["gesture"] as Dictionary<string, object>;
            if (dict == null) return null;
            return new RecognizedGesture
            {
                Name = GetString(dict, "name"),
                Confidence = GetDouble(dict, "confidence")
            };
        }

        private static int GetInt(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key)) return 0;
            var v = d[key];
            if (v is int) return (int)v;
            if (v is long) return (int)(long)v;
            if (v is double) return (int)(double)v;
            return 0;
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key)) return "";
            return d[key] != null ? d[key].ToString() : "";
        }

        private static double GetDouble(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key)) return 0;
            var v = d[key];
            if (v is double) return (double)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is float) return (float)v;
            return 0;
        }
    }
}
