/*
 * YOLO Named Pipe Client for C#
 * Communicates with Python YOLO server via Windows Named Pipes
 * Provides real-time object detection to the C# application
 */

using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;

namespace TUIO
{
    [DataContract]
    public class DetectionResult
    {
        [DataMember(Name = "track_id")]
        public int TrackId { get; set; }

        [DataMember(Name = "class")]
        public string ClassName { get; set; }

        [DataMember(Name = "class_id")]
        public int ClassId { get; set; }

        [DataMember(Name = "confidence")]
        public float Confidence { get; set; }

        [DataMember(Name = "bbox")]
        public BoundingBox BBox { get; set; }

        [DataMember(Name = "source")]
        public string Source { get; set; }
    }

    [DataContract]
    public class BoundingBox
    {
        [DataMember(Name = "x1")]
        public float X1 { get; set; }

        [DataMember(Name = "y1")]
        public float Y1 { get; set; }

        [DataMember(Name = "x2")]
        public float X2 { get; set; }

        [DataMember(Name = "y2")]
        public float Y2 { get; set; }

        public float Width => X2 - X1;
        public float Height => Y2 - Y1;
        public float CenterX => (X1 + X2) / 2;
        public float CenterY => (Y1 + Y2) / 2;
    }

    [DataContract]
    public class DetectionResponse
    {
        [DataMember(Name = "frame_id")]
        public int FrameId { get; set; }

        [DataMember(Name = "timestamp")]
        public double Timestamp { get; set; }

        [DataMember(Name = "count")]
        public int Count { get; set; }

        [DataMember(Name = "processing_ms")]
        public double ProcessingMs { get; set; }

        [DataMember(Name = "tracking")]
        public bool Tracking { get; set; }

        [DataMember(Name = "detections")]
        public List<DetectionResult> Detections { get; set; }

        [DataMember(Name = "error")]
        public string Error { get; set; }
    }

    public class YoloPipeClient : IDisposable
    {
        private string pipeName;
        private NamedPipeClientStream pipe;
        private Process serverProcess;
        private bool isConnected;
        private bool isRunning;
        private int maxFps;
        private DateTime lastFrameTime;
        private int frameId;
        private Thread receiveThread;
        private AutoResetEvent dataAvailable;
        private DetectionResponse lastResponse;
        private readonly object responseLock = new object();

        public event EventHandler<DetectionResponse> OnDetectionReceived;
        public event EventHandler<string> OnError;
        public event EventHandler<bool> OnConnectionChanged;

        public bool IsConnected => isConnected;
        public bool IsRunning => isRunning;
        public int MaxFps
        {
            get => maxFps;
            set => maxFps = Math.Max(1, Math.Min(60, value));
        }

        public YoloPipeClient(string pipeName = "YoloDetectorPipe")
        {
            this.pipeName = pipeName;
            this.maxFps = 15;
            this.isConnected = false;
            this.isRunning = false;
            this.frameId = 0;
            this.dataAvailable = new AutoResetEvent(false);
        }

        public bool StartServer(string pythonPath = "python", string serverScript = null)
        {
            try
            {
                string yoloDir = GetYoloDirectory();
                
                if (serverScript == null)
                {
                    serverScript = Path.Combine(yoloDir, "yolo_pipe_server.py");
                }

                if (!File.Exists(serverScript))
                {
                    FireError($"Server script not found: {serverScript}");
                    return false;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = pythonPath;
                psi.Arguments = $"\"{serverScript}\" --pipe {pipeName}";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = yoloDir;

                serverProcess = Process.Start(psi);
                
                Thread.Sleep(500);

                if (serverProcess.HasExited)
                {
                    FireError($"Server process exited immediately with code: {serverProcess.ExitCode}");
                    return false;
                }

                return Connect();
            }
            catch (Exception ex)
            {
                FireError($"Failed to start server: {ex.Message}");
                return false;
            }
        }

        public bool Connect()
        {
            try
            {
                pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                
                pipe.Connect(5000);

                isConnected = pipe.IsConnected;
                OnConnectionChanged?.Invoke(this, isConnected);

                if (isConnected)
                {
                    StartReceiveLoop();
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
            byte[] buffer = new byte[65536];
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

                            ProcessJsonResponse(json);
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

        private void ProcessJsonResponse(string json)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(DetectionResponse));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var response = (DetectionResponse)serializer.ReadObject(ms);
                    
                    lock (responseLock)
                    {
                        lastResponse = response;
                    }

                    if (response.Error == null)
                    {
                        OnDetectionReceived?.Invoke(this, response);
                    }
                    else
                    {
                        FireError($"Detection error: {response.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                FireError($"JSON parse error: {ex.Message}");
            }
        }

        public bool SendFrame(Bitmap frame)
        {
            if (!isConnected || !isRunning)
                return false;

            TimeSpan elapsed = DateTime.Now - lastFrameTime;
            double minInterval = 1000.0 / maxFps;
            
            if (elapsed.TotalMilliseconds < minInterval)
                return false;

            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    frame.Save(ms, ImageFormat.Jpeg);
                    byte[] imageBytes = ms.ToArray();
                    string base64 = Convert.ToBase64String(imageBytes);

                    string request = $"{{\"command\":\"detect\",\"image\":\"{base64}\"}}";
                    
                    byte[] requestBytes = Encoding.UTF8.GetBytes(request);
                    pipe.Write(requestBytes, 0, requestBytes.Length);
                    pipe.Flush();

                    lastFrameTime = DateTime.Now;
                    return true;
                }
            }
            catch (Exception ex)
            {
                FireError($"Send frame error: {ex.Message}");
                return false;
            }
        }

        public bool SendFrameRaw(byte[] imageData)
        {
            if (!isConnected || !isRunning)
                return false;

            try
            {
                string base64 = Convert.ToBase64String(imageData);
                string request = $"{{\"command\":\"detect\",\"image\":\"{base64}\"}}";
                
                byte[] requestBytes = Encoding.UTF8.GetBytes(request);
                pipe.Write(requestBytes, 0, requestBytes.Length);
                pipe.Flush();

                return true;
            }
            catch (Exception ex)
            {
                FireError($"Send frame error: {ex.Message}");
                return false;
            }
        }

        public DetectionResponse GetLastResponse()
        {
            lock (responseLock)
            {
                return lastResponse;
            }
        }

        public List<DetectionResult> GetDetections()
        {
            var response = GetLastResponse();
            return response?.Detections ?? new List<DetectionResult>();
        }

        public void Ping()
        {
            if (!isConnected)
                return;

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
            try
            {
                if (pipe != null && pipe.IsConnected)
                {
                    byte[] quitBytes = Encoding.UTF8.GetBytes("QUIT");
                    pipe.Write(quitBytes, 0, quitBytes.Length);
                    pipe.Flush();
                    pipe.Close();
                }
            }
            catch { }

            isConnected = false;
            isRunning = false;
            OnConnectionChanged?.Invoke(this, false);
        }

        public void StopServer()
        {
            Disconnect();

            if (serverProcess != null && !serverProcess.HasExited)
            {
                try
                {
                    serverProcess.Kill();
                    serverProcess.WaitForExit(2000);
                }
                catch { }
                serverProcess.Dispose();
                serverProcess = null;
            }
        }

        private string GetYoloDirectory()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string yoloDir = Path.Combine(appDir, "YOLO");
            
            if (!Directory.Exists(yoloDir))
                yoloDir = Path.Combine(Environment.CurrentDirectory, "YOLO");
            
            if (!Directory.Exists(yoloDir))
            {
                string projectRoot = @"C:\Users\Rama2\OneDrive\Desktop\TUIO11_NET-master";
                yoloDir = Path.Combine(projectRoot, "YOLO");
            }

            return yoloDir;
        }

        private void FireError(string message)
        {
            OnError?.Invoke(this, message);
        }

        public void Dispose()
        {
            StopServer();
            dataAvailable?.Dispose();
        }
    }

    public class YoloDetectionRenderer
    {
        private readonly Dictionary<string, Color> classColors = new Dictionary<string, Color>();
        private readonly Random random = new Random();
        private int colorIndex = 0;

        public Color GetClassColor(string className)
        {
            if (!classColors.ContainsKey(className))
            {
                byte[] rgb = new byte[3];
                random.NextBytes(rgb);
                rgb[0] = (byte)Math.Max(80, rgb[0]);
                rgb[1] = (byte)Math.Max(80, rgb[1]);
                rgb[2] = (byte)Math.Max(80, rgb[2]);
                classColors[className] = Color.FromArgb(rgb[0], rgb[1], rgb[2]);
            }
            return classColors[className];
        }

        public void DrawDetections(Graphics g, List<DetectionResult> detections, float scaleX = 1.0f, float scaleY = 1.0f)
        {
            foreach (var det in detections)
            {
                float x1 = det.BBox.X1 * scaleX;
                float y1 = det.BBox.Y1 * scaleY;
                float x2 = det.BBox.X2 * scaleX;
                float y2 = det.BBox.Y2 * scaleY;

                Color color = GetClassColor(det.ClassName);

                using (Pen pen = new Pen(color, 2))
                {
                    g.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);
                }

                string label = $"{det.ClassName} {det.Confidence:P0}";
                if (det.TrackId >= 0)
                    label = $"[ID:{det.TrackId}] {label}";

                using (Font font = new Font("Arial", 9))
                using (Brush brush = new SolidBrush(color))
                {
                    SizeF textSize = g.MeasureString(label, font);
                    
                    using (Brush bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                    {
                        g.FillRectangle(bgBrush, x1, y1 - textSize.Height - 4, textSize.Width + 10, textSize.Height + 4);
                    }
                    
                    g.DrawString(label, font, Brushes.White, x1 + 5, y1 - textSize.Height - 2);
                }

                float centerX = (x1 + x2) / 2;
                float centerY = (y1 + y2) / 2;
                using (Pen crossPen = new Pen(color, 1))
                {
                    g.DrawLine(crossPen, centerX - 5, centerY, centerX + 5, centerY);
                    g.DrawLine(crossPen, centerX, centerY - 5, centerX, centerY + 5);
                }
            }
        }

        public Bitmap DrawDetectionsToBitmap(Bitmap source, List<DetectionResult> detections)
        {
            Bitmap result = (Bitmap)source.Clone();
            
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                DrawDetections(g, detections, 1.0f, 1.0f);
            }

            return result;
        }
    }
}