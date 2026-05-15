/*
 * Eye Tracker C# Client
 * Receives eye data from Python via Named Pipe
 * Provides eye state + TUIO output for C# applications
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
    public enum EyeBlinkState
    {
        Open,
        Closed,
        Blinking
    }

    public class EyeData
    {
        public bool FaceDetected { get; set; }
        public int LeftX { get; set; }
        public int LeftY { get; set; }
        public int RightX { get; set; }
        public int RightY { get; set; }
        public float LeftOpenness { get; set; }
        public float RightOpenness { get; set; }
        public string BlinkState { get; set; }
        public string Quadrant { get; set; }
        public float GazeX { get; set; }
        public float GazeY { get; set; }
        public double Timestamp { get; set; }
    }

    public class EyePipeClient : IDisposable
    {
        private string pipeName;
        private NamedPipeClientStream pipe;
        private bool isConnected;
        private bool isRunning;
        private Thread receiveThread;

        public event EventHandler<EyeData> OnEyeDataReceived;
        public event EventHandler<bool> OnConnectionChanged;
        public event EventHandler<string> OnError;

        public bool IsConnected => isConnected;

        public EyePipeClient(string pipeName = "EyeTrackerPipe")
        {
            this.pipeName = pipeName;
        }

        public bool Connect(int timeoutMs = 3000)
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

                            ProcessEyeData(json);
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

        private void ProcessEyeData(string json)
        {
            try
            {
                var data = System.Web.Script.Serialization.JavaScriptSerializer.DeserializeObject(json) 
                    as System.Collections.Generic.Dictionary<string, object>;

                var eyeData = new EyeData
                {
                    FaceDetected = data.ContainsKey("face_detected") && data["face_detected"] is bool b && b,
                    LeftX = data.ContainsKey("left_x") ? Convert.ToInt32(data["left_x"]) : 0,
                    LeftY = data.ContainsKey("left_y") ? Convert.ToInt32(data["left_y"]) : 0,
                    RightX = data.ContainsKey("right_x") ? Convert.ToInt32(data["right_x"]) : 0,
                    RightY = data.ContainsKey("right_y") ? Convert.ToInt32(data["right_y"]) : 0,
                    LeftOpenness = data.ContainsKey("left_openness") ? (float)(double)data["left_openness"] : 0f,
                    RightOpenness = data.ContainsKey("right_openness") ? (float)(double)data["right_openness"] : 0f,
                    BlinkState = data.ContainsKey("blink_state") ? data["blink_state"].ToString() : "open",
                    Quadrant = data.ContainsKey("quadrant") ? data["quadrant"].ToString() : "none",
                    GazeX = data.ContainsKey("gaze_x") ? (float)(double)data["gaze_x"] : 0.5f,
                    GazeY = data.ContainsKey("gaze_y") ? (float)(double)data["gaze_y"] : 0.5f,
                    Timestamp = data.ContainsKey("timestamp") ? (double)data["timestamp"] : 0
                };

                OnEyeDataReceived?.Invoke(this, eyeData);
            }
            catch (Exception ex)
            {
                FireError($"Parse error: {ex.Message}");
            }
        }

        public void RequestEyeData()
        {
            if (!isConnected) return;

            try
            {
                byte[] requestBytes = Encoding.UTF8.GetBytes("GET");
                pipe.Write(requestBytes, 0, requestBytes.Length);
                pipe.Flush();
            }
            catch { }
        }

        public void Ping()
        {
            if (!isConnected) return;

            try
            {
                byte[] requestBytes = Encoding.UTF8.GetBytes("PING");
                pipe.Write(requestBytes, 0, requestBytes.Length);
                pipe.Flush();
            }
            catch { }
        }

        public void Disconnect()
        {
            if (isConnected && pipe != null)
            {
                try
                {
                    byte[] quitBytes = Encoding.UTF8.GetBytes("QUIT");
                    pipe.Write(quitBytes, 0, quitBytes.Length);
                    pipe.Flush();
                }
                catch { }

                pipe.Close();
                pipe = null;
            }

            isConnected = false;
            isRunning = false;
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

    public class EyeVisualRenderer
    {
        public void DrawEyeOverlay(Graphics g, EyeData eye, int canvasWidth, int canvasHeight)
        {
            if (!eye.FaceDetected)
            {
                using (Font f = new Font("Arial", 16))
                {
                    string text = "NO FACE";
                    SizeF size = g.MeasureString(text, f);
                    float x = (canvasWidth - size.Width) / 2;
                    float y = (canvasHeight - size.Height) / 2;
                    g.DrawString(text, f, Brushes.Red, x, y);
                }
                return;
            }

            float scaleX = (float)canvasWidth / 640;
            float scaleY = (float)canvasHeight / 480;

            int lx = (int)(eye.LeftX * scaleX);
            int ly = (int)(eye.LeftY * scaleY);
            int rx = (int)(eye.RightX * scaleX);
            int ry = (int)(eye.RightY * scaleY);

            using (Brush eyeBrush = new SolidBrush(Color.Lime))
            {
                g.FillEllipse(eyeBrush, lx - 8, ly - 8, 16, 16);
                g.FillEllipse(eyeBrush, rx - 8, ry - 8, 16, 16);
            }

            if (eye.BlinkState != "closed")
            {
                int gx = (int)(eye.GazeX * canvasWidth);
                int gy = (int)(eye.GazeY * canvasHeight);

                using (Pen gazePen = new Pen(Color.Red, 2))
                {
                    g.DrawLine(gazePen, gx - 20, gy, gx - 5, gy);
                    g.DrawLine(gazePen, gx + 5, gy, gx + 20, gy);
                    g.DrawLine(gazePen, gx, gy - 20, gx, gy - 5);
                    g.DrawLine(gazePen, gx, gy + 5, gx, gy + 20);
                }

                int midX = (lx + rx) / 2;
                int midY = (ly + ry) / 2;
                using (Pen arrowPen = new Pen(Color.Yellow, 1))
                {
                    g.DrawLine(arrowPen, midX, midY, gx, gy);
                }
            }

            using (Font f = new Font("Consolas", 12, FontStyle.Bold))
            {
                Color qColor = eye.Quadrant == "blink" ? Color.Gray : Color.LimeGreen;
                g.DrawString(eye.Quadrant.ToUpper(), f, new SolidBrush(qColor), 10, 30);
            }

            using (Font f = new Font("Consolas", 9))
            {
                string info = $"Gaze: ({eye.GazeX:F2}, {eye.GazeY:F2})";
                g.DrawString(info, f, Brushes.White, 10, 60);

                info = $"L: {eye.LeftOpenness:F2}  R: {eye.RightOpenness:F2}";
                g.DrawString(info, f, Brushes.White, 10, 80);
            }

            int barY = canvasHeight - 50;
            int lBarW = (int)(eye.LeftOpenness * 60);
            int rBarW = (int)(eye.RightOpenness * 60);

            g.FillRectangle(Brushes.Gray, 10, barY, 60, 15);
            g.FillRectangle(Brushes.Lime, 10, barY, lBarW, 15);
            g.FillRectangle(Brushes.Gray, 10, barY + 20, 60, 15);
            g.FillRectangle(Brushes.Lime, 10, barY + 20, rBarW, 15);

            g.DrawString("L", new Font("Arial", 8), Brushes.White, 12, barY - 5);
            g.DrawString("R", new Font("Arial", 8), Brushes.White, 12, barY + 15);

            if (eye.BlinkState == "closed")
            {
                using (Font f = new Font("Arial", 20, FontStyle.Bold))
                {
                    string text = "EYES CLOSED";
                    SizeF size = g.MeasureString(text, f);
                    float x = (canvasWidth - size.Width) / 2;
                    float y = (canvasHeight - size.Height) / 2;
                    g.DrawString(text, f, Brushes.Red, x, y);
                }
            }
        }
    }
}