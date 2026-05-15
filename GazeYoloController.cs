/*
 * GazeYoloController - Unified Gaze + YOLO Integration
 * Coordinates gaze tracking with object detection
 * Implements gaze-contingent object highlighting
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TUIO;

namespace TUIO
{
    public class GazePoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float RawX { get; set; }
        public float RawY { get; set; }
        public float SmoothX { get; set; }
        public float SmoothY { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float Confidence { get; set; }
        public bool FaceDetected { get; set; }
        public DateTime Timestamp { get; set; }

        public GazePoint()
        {
            X = 0.5f;
            Y = 0.5f;
            RawX = 0.5f;
            RawY = 0.5f;
            SmoothX = 0.5f;
            SmoothY = 0.5f;
            Confidence = 0;
            FaceDetected = false;
            Timestamp = DateTime.Now;
        }

        public GazePoint(float x, float y)
        {
            X = x;
            Y = y;
            RawX = x;
            RawY = y;
            SmoothX = x;
            SmoothY = y;
            Confidence = 1.0f;
            FaceDetected = true;
            Timestamp = DateTime.Now;
        }
    }

    public class GazeTargetObject
    {
        public int TrackId { get; set; }
        public string ClassName { get; set; }
        public float Confidence { get; set; }
        public RectangleF Bounds { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float DistanceToGaze { get; set; }
        public bool IsGazeTarget { get; set; }
        public int FocusFrames { get; set; }
        public float FocusScore { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class GazeYoloController
    {
        public event EventHandler<GazePoint> OnGazeUpdated;
        public event EventHandler<GazeTargetObject> OnTargetChanged;
        public event EventHandler<List<GazeTargetObject>> OnObjectsUpdated;

        private GazePoint currentGaze = new GazePoint();
        private List<GazeTargetObject> trackedObjects = new List<GazeTargetObject>();
        private GazeTargetObject currentTarget = null;

        private float screenWidth;
        private float screenHeight;
        private float gazeThreshold = 0.15f;
        private float focusThreshold = 0.08f;
        private int minFocusFrames = 10;
        private float focusScoreDecay = 0.95f;

        private bool isEnabled = true;
        private int gazeFocusRadius = 80;
        private bool highlightGazeTargets = true;
        private bool dimNonTargets = true;

        private Queue<GazePoint> gazeHistory = new Queue<GazePoint>();
        private const int gazeHistorySize = 30;

        private Random random = new Random();

        public GazePoint CurrentGaze => currentGaze;
        public List<GazeTargetObject> TrackedObjects => trackedObjects;
        public GazeTargetObject CurrentTarget => currentTarget;
        public bool IsEnabled
        {
            get => isEnabled;
            set => isEnabled = value;
        }

        public float GazeThreshold
        {
            get => gazeThreshold;
            set => gazeThreshold = Math.Max(0.01f, Math.Min(0.5f, value));
        }

        public float FocusThreshold
        {
            get => focusThreshold;
            set => focusThreshold = Math.Max(0.01f, Math.Min(0.3f, value));
        }

        public int MinFocusFrames
        {
            get => minFocusFrames;
            set => minFocusFrames = Math.Max(1, value);
        }

        public bool HighlightGazeTargets
        {
            get => highlightGazeTargets;
            set => highlightGazeTargets = value;
        }

        public bool DimNonTargets
        {
            get => dimNonTargets;
            set => dimNonTargets = value;
        }

        public int GazeFocusRadius
        {
            get => gazeFocusRadius;
            set => gazeFocusRadius = Math.Max(20, Math.Min(200, value));
        }

        public GazeYoloController(float screenWidth, float screenHeight)
        {
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
        }

        public void UpdateScreenSize(float width, float height)
        {
            screenWidth = width;
            screenHeight = height;
        }

        public void UpdateGaze(float normalizedX, float normalizedY, float rawX = -1, float rawY = -1)
        {
            if (!isEnabled) return;

            GazePoint newGaze = new GazePoint
            {
                X = normalizedX,
                Y = normalizedY,
                RawX = rawX >= 0 ? rawX : normalizedX,
                RawY = rawY >= 0 ? rawY : normalizedY,
                Confidence = 1.0f,
                FaceDetected = true,
                Timestamp = DateTime.Now
            };

            float smoothedX = ApplySmoothing(newGaze.X, gazeHistory, true);
            float smoothedY = ApplySmoothing(newGaze.Y, gazeHistory, false);

            newGaze.SmoothX = smoothedX;
            newGaze.SmoothY = smoothedY;

            if (gazeHistory.Count > 0)
            {
                var lastGaze = gazeHistory.Last();
                float dt = (float)(newGaze.Timestamp - lastGaze.Timestamp).TotalSeconds;
                if (dt > 0)
                {
                    newGaze.VelocityX = (smoothedX - lastGaze.SmoothX) / dt;
                    newGaze.VelocityY = (smoothedY - lastGaze.SmoothY) / dt;
                }
            }

            gazeHistory.Enqueue(newGaze);
            if (gazeHistory.Count > gazeHistorySize)
            {
                var old = gazeHistory.Dequeue();
            }

            currentGaze = newGaze;
            OnGazeUpdated?.Invoke(this, currentGaze);

            UpdateObjectFocus();
            OnObjectsUpdated?.Invoke(this, trackedObjects);
        }

        private float ApplySmoothing(float value, Queue<GazePoint> history, bool isX)
        {
            if (history.Count == 0)
                return value;

            float alpha = 0.3f;
            float lastValue = isX ? history.Last().SmoothX : history.Last().SmoothY;

            return alpha * value + (1 - alpha) * lastValue;
        }

        public void UpdateDetections(List<DetectionResult> detections)
        {
            if (!isEnabled) return;

            DateTime now = DateTime.Now;
            var existingTargets = trackedObjects.ToDictionary(t => t.TrackId);

            trackedObjects.Clear();

            foreach (var det in detections)
            {
                float centerX = det.BBox.CenterX / screenWidth;
                float centerY = det.BBox.CenterY / screenHeight;

                float gazeX = currentGaze.SmoothX;
                float gazeY = currentGaze.SmoothY;

                float dx = centerX - gazeX;
                float dy = centerY - gazeY;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                GazeTargetObject target;

                if (existingTargets.ContainsKey(det.TrackId))
                {
                    target = existingTargets[det.TrackId];
                    target.FocusFrames++;
                    target.FocusScore = Math.Min(1.0f, target.FocusScore + 0.1f);
                }
                else
                {
                    target = new GazeTargetObject
                    {
                        TrackId = det.TrackId,
                        ClassName = det.ClassName,
                        Confidence = det.Confidence,
                        FocusFrames = 1,
                        FocusScore = 0.1f,
                        FirstSeen = now
                    };
                }

                target.Bounds = new RectangleF(
                    det.BBox.X1, det.BBox.Y1,
                    det.BBox.Width, det.BBox.Height
                );
                target.CenterX = det.BBox.CenterX;
                target.CenterY = det.BBox.CenterY;
                target.DistanceToGaze = distance;
                target.LastSeen = now;
                target.IsGazeTarget = distance < gazeThreshold && target.FocusFrames >= minFocusFrames;

                trackedObjects.Add(target);
            }

            foreach (var obj in trackedObjects)
            {
                obj.FocusScore *= focusScoreDecay;

                if (obj.FocusScore < 0.05f)
                {
                    obj.IsGazeTarget = false;
                }
            }

            var newTarget = trackedObjects
                .Where(t => t.IsGazeTarget)
                .OrderBy(t => t.DistanceToGaze)
                .ThenByDescending(t => t.FocusScore)
                .FirstOrDefault();

            if (newTarget != currentTarget)
            {
                currentTarget = newTarget;
                OnTargetChanged?.Invoke(this, currentTarget);
            }
        }

        private void UpdateObjectFocus()
        {
            float gazeX = currentGaze.SmoothX;
            float gazeY = currentGaze.SmoothY;

            foreach (var obj in trackedObjects)
            {
                float objCenterX = obj.CenterX / screenWidth;
                float objCenterY = obj.CenterY / screenHeight;

                float dx = objCenterX - gazeX;
                float dy = objCenterY - gazeY;
                obj.DistanceToGaze = (float)Math.Sqrt(dx * dx + dy * dy);

                obj.IsGazeTarget = obj.DistanceToGaze < focusThreshold && obj.FocusFrames >= minFocusFrames;
            }
        }

        public Point GetGazeScreenPosition()
        {
            return new Point(
                (int)(currentGaze.SmoothX * screenWidth),
                (int)(currentGaze.SmoothY * screenHeight)
            );
        }

        public GazeTargetObject GetObjectAtGaze()
        {
            if (currentTarget != null && currentTarget.IsGazeTarget)
            {
                return currentTarget;
            }

            return trackedObjects
                .OrderBy(t => t.DistanceToGaze)
                .FirstOrDefault();
        }

        public List<GazeTargetObject> GetObjectsInGazeVicinity(float radius = 0.15f)
        {
            return trackedObjects
                .Where(t => t.DistanceToGaze < radius)
                .OrderBy(t => t.DistanceToGaze)
                .ToList();
        }

        public void DrawGazeOverlay(Graphics g, int canvasWidth, int canvasHeight)
        {
            if (!isEnabled) return;

            float scaleX = (float)canvasWidth / screenWidth;
            float scaleY = (float)canvasHeight / screenHeight;

            int gazeX = (int)(currentGaze.SmoothX * canvasWidth);
            int gazeY = (int)(currentGaze.SmoothY * canvasHeight);

            int rawX = (int)(currentGaze.RawX * canvasWidth);
            int rawY = (int)(currentGaze.RawY * canvasHeight);

            using (Pen gazePen = new Pen(Color.Lime, 2))
            using (Pen rawPen = new Pen(Color.Orange, 1))
            using (Pen focusPen = new Pen(Color.Cyan, 2))
            {
                g.DrawEllipse(rawPen, gazeX - 8, gazeY - 8, 16, 16);

                g.DrawLine(gazePen, gazeX - 20, gazeY, gazeX - 8, gazeY);
                g.DrawLine(gazePen, gazeX + 8, gazeY, gazeX + 20, gazeY);
                g.DrawLine(gazePen, gazeX, gazeY - 20, gazeX, gazeY - 8);
                g.DrawLine(gazePen, gazeX, gazeY + 8, gazeX, gazeY + 20);

                if (currentTarget != null && currentTarget.IsGazeTarget)
                {
                    int radius = gazeFocusRadius;
                    using (Brush focusBrush = new SolidBrush(Color.FromArgb(30, 0, 255, 255)))
                    {
                        g.FillEllipse(focusBrush, gazeX - radius, gazeY - radius, radius * 2, radius * 2);
                    }
                    g.DrawEllipse(focusPen, gazeX - radius, gazeY - radius, radius * 2, radius * 2);
                }
            }

            if (currentGaze.VelocityX != 0 || currentGaze.VelocityY != 0)
            {
                int vx = (int)(currentGaze.VelocityX * 100);
                int vy = (int)(currentGaze.VelocityY * 100);
                
                using (Pen velPen = new Pen(Color.Yellow, 1))
                {
                    g.DrawLine(velPen, gazeX, gazeY, gazeX + vx, gazeY + vy);
                }
            }
        }

        public void DrawObjectOverlay(Graphics g, int canvasWidth, int canvasHeight)
        {
            if (!isEnabled) return;

            foreach (var obj in trackedObjects)
            {
                float scaleX = (float)canvasWidth / screenWidth;
                float scaleY = (float)canvasHeight / screenHeight;

                RectangleF scaledBounds = new RectangleF(
                    obj.Bounds.X * scaleX,
                    obj.Bounds.Y * scaleY,
                    obj.Bounds.Width * scaleX,
                    obj.Bounds.Height * scaleY
                );

                Color boxColor;
                float alpha;

                if (obj.IsGazeTarget && highlightGazeTargets)
                {
                    boxColor = Color.Lime;
                    alpha = 1.0f;
                }
                else if (dimNonTargets)
                {
                    boxColor = Color.Gray;
                    alpha = 0.5f;
                }
                else
                {
                    boxColor = GetClassColor(obj.ClassName);
                    alpha = 0.8f;
                }

                using (Pen boxPen = new Pen(Color.FromArgb((int)(alpha * 255), boxColor), obj.IsGazeTarget ? 3 : 2))
                {
                    g.DrawRectangle(boxPen, scaledBounds.X, scaledBounds.Y, scaledBounds.Width, scaledBounds.Height);
                }

                string label = $"{obj.ClassName} {obj.Confidence:P0}";
                if (obj.TrackId >= 0)
                {
                    label = $"[ID:{obj.TrackId}] {label}";
                }

                if (obj.IsGazeTarget)
                {
                    label = $"★ {label}";
                }

                using (Font labelFont = new Font("Consolas", 10, FontStyle.Bold))
                using (Brush labelBrush = new SolidBrush(Color.FromArgb((int)(alpha * 255), 255, 255, 255)))
                {
                    SizeF labelSize = g.MeasureString(label, labelFont);
                    float labelX = scaledBounds.X;
                    float labelY = scaledBounds.Y - labelSize.Height - 4;

                    if (labelY < 0) labelY = scaledBounds.Y + 2;

                    using (Brush bgBrush = new SolidBrush(Color.FromArgb((int)(alpha * 200), 0, 0, 0)))
                    {
                        g.FillRectangle(bgBrush, labelX, labelY, labelSize.Width + 10, labelSize.Height + 4);
                    }

                    g.DrawString(label, labelFont, labelBrush, labelX + 5, labelY + 2);
                }

                float centerX = scaledBounds.X + scaledBounds.Width / 2;
                float centerY = scaledBounds.Y + scaledBounds.Height / 2;

                using (Pen crossPen = new Pen(boxColor, 1))
                {
                    g.DrawLine(crossPen, centerX - 8, centerY, centerX + 8, centerY);
                    g.DrawLine(crossPen, centerX, centerY - 8, centerX, centerY + 8);
                }

                if (obj.FocusScore > 0.3f)
                {
                    int focusBarWidth = (int)(scaledBounds.Width * obj.FocusScore);
                    using (Brush focusBrush = new SolidBrush(Color.FromArgb((int)(alpha * 200), 0, 255, 0)))
                    {
                        g.FillRectangle(focusBrush, scaledBounds.X, scaledBounds.Bottom + 2, focusBarWidth, 4);
                    }
                }
            }
        }

        private Color GetClassColor(string className)
        {
            int hash = className.GetHashCode();
            int r = (hash & 0xFF0000) >> 16;
            int g = (hash & 0x00FF00) >> 8;
            int b = (hash & 0x0000FF);

            r = Math.Max(80, r);
            g = Math.Max(80, g);
            b = Math.Max(80, b);

            return Color.FromArgb(r, g, b);
        }

        public string GetDebugInfo()
        {
            var info = new System.Text.StringBuilder();
            
            info.AppendLine($"Gaze: ({currentGaze.SmoothX:F3}, {currentGaze.SmoothY:F3})");
            info.AppendLine($"Raw: ({currentGaze.RawX:F3}, {currentGaze.RawY:F3})");
            info.AppendLine($"Velocity: ({currentGaze.VelocityX:F2}, {currentGaze.VelocityY:F2})");
            info.AppendLine($"Face: {(currentGaze.FaceDetected ? "Yes" : "No")}");
            info.AppendLine($"Objects: {trackedObjects.Count}");
            
            if (currentTarget != null)
            {
                info.AppendLine($"Target: ID:{currentTarget.TrackId} {currentTarget.ClassName}");
                info.AppendLine($"Target Dist: {currentTarget.DistanceToGaze:F3}");
                info.AppendLine($"Focus: {currentTarget.FocusFrames} frames, score {currentTarget.FocusScore:F2}");
            }
            else
            {
                info.AppendLine("Target: None");
            }

            return info.ToString();
        }

        public void Reset()
        {
            currentGaze = new GazePoint();
            trackedObjects.Clear();
            currentTarget = null;
            gazeHistory.Clear();
        }
    }

    public class GazeYoloForm : Form, TuioListener
    {
        private YoloPipeClient yoloClient;
        private Thread gazeThread;
        private Thread captureThread;
        private bool isRunning;

        private GazeYoloController gazeYoloController;
        private Bitmap currentFrame;
        private readonly object frameLock = new object();

        private Panel mainPanel;
        private PictureBox displayBox;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel gazeLabel;
        private ToolStripStatusLabel targetLabel;
        private ToolStripStatusLabel fpsLabel;
        private ToolStripStatusLabel statusLabel;

        private CheckBox enableGazeCheck;
        private CheckBox highlightCheck;
        private TrackBar thresholdSlider;
        private Label thresholdLabel;

        private int frameCount = 0;
        private DateTime lastFpsUpdate = DateTime.Now;
        private int currentFps = 0;

        public GazeYoloForm()
        {
            this.Text = "Gaze-YOLO Controller";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(30, 30, 35);

            gazeYoloController = new GazeYoloController(800, 600);
            gazeYoloController.OnGazeUpdated += (s, e) => UpdateGazeDisplay(e);
            gazeYoloController.OnTargetChanged += (s, e) => UpdateTargetDisplay(e);

            InitializeComponent();

            yoloClient = new YoloPipeClient("YoloDetectorPipe");
            yoloClient.MaxFps = 15;
            yoloClient.OnDetectionReceived += YoloClient_OnDetectionReceived;
            yoloClient.OnConnectionChanged += YoloClient_OnConnectionChanged;

            statusLabel.Text = "Ready - Press START";
        }

        private void InitializeComponent()
        {
            mainPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            this.Controls.Add(mainPanel);

            displayBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            displayBox.Paint += DisplayBox_Paint;
            mainPanel.Controls.Add(displayBox);

            statusStrip = new StatusStrip();

            gazeLabel = new ToolStripStatusLabel("Gaze: (0.50, 0.50)");
            gazeLabel.ForeColor = Color.Lime;
            statusStrip.Items.Add(gazeLabel);

            targetLabel = new ToolStripStatusLabel("Target: None");
            targetLabel.ForeColor = Color.Cyan;
            statusStrip.Items.Add(targetLabel);

            fpsLabel = new ToolStripStatusLabel("FPS: 0");
            statusStrip.Items.Add(fpsLabel);

            statusLabel = new ToolStripStatusLabel("Ready");
            statusStrip.Items.Add(statusLabel);

            this.Controls.Add(statusStrip);
        }

        private void DisplayBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            lock (frameLock)
            {
                if (currentFrame != null)
                {
                    g.DrawImage(currentFrame, 0, 0, displayBox.Width, displayBox.Height);
                }
            }

            gazeYoloController.DrawGazeOverlay(g, displayBox.Width, displayBox.Height);
            gazeYoloController.DrawObjectOverlay(g, displayBox.Width, displayBox.Height);
        }

        private void UpdateGazeDisplay(GazePoint gaze)
        {
            if (gazeLabel.InvokeRequired)
            {
                gazeLabel.BeginInvoke(new Action(() => UpdateGazeDisplay(gaze)));
                return;
            }

            gazeLabel.Text = $"Gaze: ({gaze.SmoothX:F2}, {gaze.SmoothY:F2})";
        }

        private void UpdateTargetDisplay(GazeTargetObject target)
        {
            if (targetLabel.InvokeRequired)
            {
                targetLabel.BeginInvoke(new Action(() => UpdateTargetDisplay(target)));
                return;
            }

            if (target != null)
            {
                targetLabel.Text = $"Target: {target.ClassName} [ID:{target.TrackId}]";
                targetLabel.ForeColor = Color.Lime;
            }
            else
            {
                targetLabel.Text = "Target: None";
                targetLabel.ForeColor = Color.Gray;
            }
        }

        private void YoloClient_OnDetectionReceived(object sender, DetectionResponse e)
        {
            gazeYoloController.UpdateDetections(e.Detections);
            displayBox.BeginInvoke(new Action(() => displayBox.Invalidate()));
        }

        private void YoloClient_OnConnectionChanged(object sender, bool connected)
        {
            this.BeginInvoke(new Action(() =>
            {
                statusLabel.Text = connected ? "YOLO Connected" : "YOLO Disconnected";
                statusLabel.ForeColor = connected ? Color.Lime : Color.Red;
            }));
        }

        private void UpdateFps()
        {
            frameCount++;
            var elapsed = DateTime.Now - lastFpsUpdate;
            if (elapsed.TotalMilliseconds >= 1000)
            {
                currentFps = frameCount;
                frameCount = 0;
                lastFpsUpdate = DateTime.Now;
                fpsLabel.Text = $"FPS: {currentFps}";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isRunning = false;
            yoloClient?.Dispose();
            base.OnFormClosing(e);
        }

        public static void Main(string[] argv)
        {
            Application.Run(new GazeYoloForm());
        }
    }
}