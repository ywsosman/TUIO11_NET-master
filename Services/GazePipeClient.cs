/*
 * Gaze Tracking Named Pipe Client for C#
 * Communicates with Python gaze server via Windows Named Pipes
 */

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Drawing;

namespace TUIO
{
    [System.Runtime.Serialization.DataContract]
    public class GazeData
    {
        [System.Runtime.Serialization.DataMember(Name = "x")]
        public float X { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "y")]
        public float Y { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "raw_x")]
        public float RawX { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "raw_y")]
        public float RawY { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "yaw")]
        public float Yaw { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "pitch")]
        public float Pitch { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "face_detected")]
        public bool FaceDetected { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "confidence")]
        public float Confidence { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "timestamp")]
        public double Timestamp { get; set; }
    }

    [System.Runtime.Serialization.DataContract]
    public class GazeStats
    {
        [System.Runtime.Serialization.DataMember(Name = "frames_processed")]
        public int FramesProcessed { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "uptime_seconds")]
        public double UptimeSeconds { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "calibrated")]
        public bool Calibrated { get; set; }
    }

    public class GazePipeClient : IDisposable
    {
        private string pipeName;
        private NamedPipeClientStream pipe;
        private bool isConnected;
        private bool isRunning;
        private Thread receiveThread;
        private GazeData lastGaze;
        private readonly object gazeLock = new object();

        public event EventHandler<GazeData> OnGazeUpdated;
        public event EventHandler<string> OnError;
        public event EventHandler<bool> OnConnectionChanged;

        public bool IsConnected => isConnected;
        public GazeData LastGaze
        {
            get
            {
                lock (gazeLock)
                {
                    return lastGaze;
                }
            }
        }

        public GazePipeClient(string pipeName = "GazeTrackerPipe")
        {
            this.pipeName = pipeName;
        }

        public bool Connect(int timeoutMs = 5000)
        {
            try
            {
                pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe.Connect(timeoutMs);

                isConnected = pipe.IsConnected;

                if (isConnected)
                {
                    StartReceiveLoop();
                    OnConnectionChanged?.Invoke(this, true);
                }

                return isConnected;
            }
            catch (Exception ex)
            {
                FireError($"Connection failed: {ex.Message}");
                isConnected = false;
                OnConnectionChanged?.Invoke(this, false);
                return false;
            }
        }

        private void StartReceiveLoop()
        {
            isRunning = true;
            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();
        }

        private void ReceiveLoop()
        {
            byte[] buffer = new byte[4096];
            StringBuilder sb = new StringBuilder();

            while (isRunning && pipe != null && pipe.IsConnected)
            {
                try
                {
                    int bytesRead = pipe.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        sb.Append(data);

                        string content = sb.ToString();
                        int jsonEnd = FindJsonEnd(content);

                        if (jsonEnd >= 0)
                        {
                            string json = content.Substring(0, jsonEnd + 1);
                            sb.Remove(0, jsonEnd + 1);

                            ProcessJsonGaze(json);
                        }
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
                catch (IOException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    FireError($"Receive error: {ex.Message}");
                    break;
                }
            }

            isConnected = false;
            isRunning = false;
            OnConnectionChanged?.Invoke(this, false);
        }

        private int FindJsonEnd(string content)
        {
            int braceCount = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                }
                else if (!inString)
                {
                    if (c == '{')
                        braceCount++;
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                            return i;
                    }
                }
            }

            return -1;
        }

        private void ProcessJsonGaze(string json)
        {
            try
            {
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(GazeData));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var gaze = (GazeData)serializer.ReadObject(ms);

                    lock (gazeLock)
                    {
                        lastGaze = gaze;
                    }

                    OnGazeUpdated?.Invoke(this, gaze);
                }
            }
            catch (Exception ex)
            {
                FireError($"JSON parse error: {ex.Message}");
            }
        }

        public void SendCommand(string command)
        {
            if (!isConnected)
                return;

            try
            {
                byte[] requestBytes = Encoding.UTF8.GetBytes(command);
                pipe.Write(requestBytes, 0, requestBytes.Length);
                pipe.Flush();
            }
            catch (Exception ex)
            {
                FireError($"Send command error: {ex.Message}");
            }
        }

        public void RequestGaze()
        {
            SendCommand("GET_GAZE");
        }

        public GazeStats GetStats()
        {
            if (!isConnected)
                return null;

            try
            {
                SendCommand("GET_STATS");

                byte[] buffer = new byte[4096];
                int bytesRead = pipe.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(GazeStats));
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        return (GazeStats)serializer.ReadObject(ms);
                    }
                }
            }
            catch { }

            return null;
        }

        public bool StartCalibration()
        {
            if (!isConnected)
                return false;

            SendCommand("CALIBRATE");

            return true;
        }

        public bool SaveCalibration(string path = "gaze_calibration.json")
        {
            if (!isConnected)
                return false;

            SendCommand("SAVE_CALIBRATION");

            return true;
        }

        public bool LoadCalibration(string path = "gaze_calibration.json")
        {
            if (!isConnected)
                return false;

            SendCommand("LOAD_CALIBRATION");

            return true;
        }

        public void Disconnect()
        {
            try
            {
                SendCommand("QUIT");
            }
            catch { }

            isConnected = false;
            isRunning = false;

            if (pipe != null)
            {
                pipe.Close();
                pipe = null;
            }

            OnConnectionChanged?.Invoke(this, false);
        }

        private void FireError(string message)
        {
            OnError?.Invoke(this, message);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    public class GazeCursorRenderer
    {
        private int cursorSize = 40;
        private Color cursorColor = Color.Lime;
        private int crossSize = 20;

        public Color GazeColor { get; set; }
        public int Size { get; set; }

        public GazeCursorRenderer()
        {
            GazeColor = Color.Lime;
            Size = 40;
        }

        public void DrawGazeCursor(Graphics g, float normalizedX, float normalizedY, int canvasWidth, int canvasHeight)
        {
            int x = (int)(normalizedX * canvasWidth);
            int y = (int)(normalizedY * canvasHeight);

            using (Pen pen = new Pen(GazeColor, 2))
            {
                g.DrawEllipse(pen, x - 6, y - 6, 12, 12);

                g.DrawLine(pen, x - crossSize, y, x - 8, y);
                g.DrawLine(pen, x + 8, y, x + crossSize, y);
                g.DrawLine(pen, x, y - crossSize, x, y - 8);
                g.DrawLine(pen, x, y + 8, x, y + crossSize);
            }

            string coordText = $"({normalizedX:F2}, {normalizedY:F2})";
            using (Font font = new Font("Consolas", 9))
            using (Brush brush = new SolidBrush(GazeColor))
            {
                g.DrawString(coordText, font, brush, x + 15, y - 10);
            }
        }

        public void DrawGazeWithTarget(Graphics g, float gazeX, float gazeY, int canvasWidth, int canvasHeight, GazeTargetObject target)
        {
            DrawGazeCursor(g, gazeX, gazeY, canvasWidth, canvasHeight);

            if (target != null && target.IsGazeTarget)
            {
                int x = (int)(gazeX * canvasWidth);
                int y = (int)(gazeY * canvasHeight);
                int radius = 60;

                using (Pen targetPen = new Pen(Color.Cyan, 2))
                {
                    targetPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawEllipse(targetPen, x - radius, y - radius, radius * 2, radius * 2);
                }
            }
        }
    }
}