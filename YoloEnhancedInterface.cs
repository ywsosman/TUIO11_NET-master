/*
 * YOLO + TUIO Enhanced Interface with Live Webcam
 * Full integration with Named Pipe YOLO server
 * Real-time object detection with SORT tracking
 */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using TUIO;
using System.IO;
using System.Diagnostics;
using System.Linq;

public class YoloEnhancedInterface : Form, TuioListener
{
    private TuioClient tuioClient;
    private Dictionary<long, TuioObject> objectList;
    private Dictionary<long, TuioCursor> cursorList;
    private Dictionary<long, TuioBlob> blobList;

    private YoloPipeClient yoloClient;
    private VideoCapture webcam;
    private Thread captureThread;
    private bool isRunning;
    private Bitmap currentFrame;
    private readonly object frameLock = new object();

    private List<DetectionResult> currentDetections = new List<DetectionResult>();
    private YoloDetectionRenderer detectionRenderer = new YoloDetectionRenderer();

    private int frameCount = 0;
    private int totalDetections = 0;
    private DateTime sessionStart;
    private int currentFps = 0;
    private DateTime lastFpsUpdate;
    private int framesSinceLastFps;

    private Color bgColor = Color.FromArgb(30, 30, 35);
    private Color panelColor = Color.FromArgb(45, 45, 52);
    private Color accentColor = Color.FromArgb(0, 180, 216);
    private Color textColor = Color.FromArgb(220, 220, 225);
    private Color successColor = Color.FromArgb(80, 200, 120);
    private Color warningColor = Color.FromArgb(255, 180, 50);
    private Color errorColor = Color.FromArgb(255, 80, 80);

    private Panel mainPanel;
    private Panel sidebarPanel;
    private Panel controlPanel;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel fpsLabel;
    private ToolStripStatusLabel objectsLabel;
    private ToolStripStatusLabel yoloStatusLabel;
    private ToolStripStatusLabel cameraStatusLabel;

    private ComboBox videoSourceCombo;
    private Button startStopBtn;
    private CheckBox yoloEnabledCheck;
    private TrackBar confSlider;
    private Label confLabel;
    private ListView detectionList;
    private Label statsLabel;
    private Panel videoPanel;
    private PictureBox webcamPictureBox;
    private Label connectionStatus;
    private ComboBox cameraCombo;
    private Label cameraLabel;
    private CheckBox showBboxesCheck;
    private CheckBox showTrackingCheck;
    private Label trackingStatusLabel;

    private int windowWidth = 1100;
    private int windowHeight = 700;
    private int sidebarWidth = 260;
    private int controlHeight = 140;
    private int statusHeight = 26;

    private bool fullscreen;
    private float confidenceThreshold = 0.35f;
    private int selectedCameraIndex = 0;

    public static int width, height;

    public YoloEnhancedInterface(int port)
    {
        sessionStart = DateTime.Now;
        lastFpsUpdate = DateTime.Now;

        this.Text = "YOLO + TUIO Enhanced Interface";
        this.Size = new Size(windowWidth, windowHeight);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = bgColor;

        fullscreen = false;
        width = windowWidth - sidebarWidth;
        height = windowHeight - controlHeight - statusHeight;

        this.FormClosing += Form_Closing;
        this.KeyDown += Form_KeyDown;

        this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                        ControlStyles.UserPaint |
                        ControlStyles.DoubleBuffer, true);

        objectList = new Dictionary<long, TuioObject>(128);
        cursorList = new Dictionary<long, TuioCursor>(128);
        blobList = new Dictionary<long, TuioBlob>(128);

        if (port > 0)
        {
            tuioClient = new TuioClient(port);
            tuioClient.addTuioListener(this);
            tuioClient.connect();
        }

        yoloClient = new YoloPipeClient("YoloDetectorPipe");
        yoloClient.MaxFps = 15;
        yoloClient.OnDetectionReceived += YoloClient_OnDetectionReceived;
        yoloClient.OnConnectionChanged += YoloClient_OnConnectionChanged;
        yoloClient.OnError += YoloClient_OnError;

        CreateMainLayout();
        CreateSidebar();
        CreateControlPanel();
        CreateStatusBar();
        
        CheckAvailableCameras();
        
        Log("Application started");
    }

    private void CreateMainLayout()
    {
        mainPanel = new Panel();
        mainPanel.BackColor = bgColor;
        mainPanel.Location = new Point(0, 0);
        mainPanel.Size = new Size(windowWidth - sidebarWidth, windowHeight - controlHeight - statusHeight);
        this.Controls.Add(mainPanel);

        videoPanel = new Panel();
        videoPanel.BackColor = Color.Black;
        videoPanel.Location = new Point(10, 10);
        videoPanel.Size = new Size(mainPanel.Width - 20, mainPanel.Height - 20);
        videoPanel.Paint += VideoPanel_Paint;
        mainPanel.Controls.Add(videoPanel);

        webcamPictureBox = new PictureBox();
        webcamPictureBox.Dock = DockStyle.Fill;
        webcamPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        webcamPictureBox.BackColor = Color.Black;
        videoPanel.Controls.Add(webcamPictureBox);
    }

    private void CreateSidebar()
    {
        sidebarPanel = new Panel();
        sidebarPanel.BackColor = panelColor;
        sidebarPanel.Location = new Point(windowWidth - sidebarWidth, 0);
        sidebarPanel.Size = new Size(sidebarWidth, windowHeight - controlHeight - statusHeight);
        this.Controls.Add(sidebarPanel);

        Label sidebarTitle = new Label();
        sidebarTitle.Text = "DETECTION STATISTICS";
        sidebarTitle.ForeColor = accentColor;
        sidebarTitle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        sidebarTitle.Location = new Point(15, 15);
        sidebarTitle.AutoSize = true;
        sidebarPanel.Controls.Add(sidebarTitle);

        statsLabel = new Label();
        statsLabel.Text = "Initializing...";
        statsLabel.ForeColor = textColor;
        statsLabel.Font = new Font("Consolas", 9);
        statsLabel.Location = new Point(15, 45);
        statsLabel.Size = new Size(sidebarWidth - 30, 180);
        statsLabel.AutoSize = false;
        sidebarPanel.Controls.Add(statsLabel);

        Label listTitle = new Label();
        listTitle.Text = "ACTIVE TRACKING";
        listTitle.ForeColor = accentColor;
        listTitle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        listTitle.Location = new Point(15, 235);
        listTitle.AutoSize = true;
        sidebarPanel.Controls.Add(listTitle);

        trackingStatusLabel = new Label();
        trackingStatusLabel.Text = "No active tracks";
        trackingStatusLabel.ForeColor = Color.Gray;
        trackingStatusLabel.Font = new Font("Consolas", 8);
        trackingStatusLabel.Location = new Point(15, 255);
        trackingStatusLabel.AutoSize = true;
        sidebarPanel.Controls.Add(trackingStatusLabel);

        detectionList = new ListView();
        detectionList.BackColor = bgColor;
        detectionList.ForeColor = textColor;
        detectionList.Font = new Font("Consolas", 8);
        detectionList.Location = new Point(15, 275);
        detectionList.Size = new Size(sidebarWidth - 30, sidebarPanel.Height - 320);
        detectionList.View = View.List;
        detectionList.FullRowSelect = true;
        sidebarPanel.Controls.Add(detectionList);

        connectionStatus = new Label();
        connectionStatus.Text = "YOLO: Disconnected";
        connectionStatus.ForeColor = errorColor;
        connectionStatus.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        connectionStatus.Location = new Point(15, sidebarPanel.Height - 35);
        connectionStatus.AutoSize = true;
        sidebarPanel.Controls.Add(connectionStatus);
    }

    private void CreateControlPanel()
    {
        controlPanel = new Panel();
        controlPanel.BackColor = panelColor;
        controlPanel.Location = new Point(0, windowHeight - controlHeight - statusHeight);
        controlPanel.Size = new Size(windowWidth - sidebarWidth, controlHeight);
        this.Controls.Add(controlPanel);

        int margin = 15;
        int y = 15;

        cameraLabel = new Label();
        cameraLabel.Text = "Camera:";
        cameraLabel.ForeColor = textColor;
        cameraLabel.Location = new Point(margin, y);
        cameraLabel.Size = new Size(55, 20);
        controlPanel.Controls.Add(cameraLabel);

        cameraCombo = new ComboBox();
        cameraCombo.Location = new Point(margin + 60, y);
        cameraCombo.Size = new Size(80, 25);
        cameraCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        cameraCombo.SelectedIndexChanged += CameraCombo_SelectedIndexChanged;
        controlPanel.Controls.Add(cameraCombo);

        startStopBtn = new Button();
        startStopBtn.Text = "START";
        startStopBtn.Location = new Point(margin + 150, y);
        startStopBtn.Size = new Size(80, 25);
        startStopBtn.BackColor = successColor;
        startStopBtn.ForeColor = Color.White;
        startStopBtn.FlatStyle = FlatStyle.Flat;
        startStopBtn.Click += StartStopBtn_Click;
        controlPanel.Controls.Add(startStopBtn);

        yoloEnabledCheck = new CheckBox();
        yoloEnabledCheck.Text = "Enable YOLO";
        yoloEnabledCheck.Checked = true;
        yoloEnabledCheck.ForeColor = textColor;
        yoloEnabledCheck.Location = new Point(margin + 240, y);
        yoloEnabledCheck.CheckedChanged += (s, e) => UpdateYoloStatus();
        controlPanel.Controls.Add(yoloEnabledCheck);

        y += 35;

        Label confTitle = new Label();
        confTitle.Text = "Confidence:";
        confTitle.ForeColor = textColor;
        confTitle.Location = new Point(margin, y);
        confTitle.Size = new Size(80, 20);
        controlPanel.Controls.Add(confTitle);

        confSlider = new TrackBar();
        confSlider.Minimum = 10;
        confSlider.Maximum = 90;
        confSlider.Value = 35;
        confSlider.Location = new Point(margin + 85, y);
        confSlider.Size = new Size(150, 25);
        confSlider.ValueChanged += (s, e) => {
            confidenceThreshold = confSlider.Value / 100f;
            confLabel.Text = confidenceThreshold.ToString("F2");
        };
        controlPanel.Controls.Add(confSlider);

        confLabel = new Label();
        confLabel.Text = "0.35";
        confLabel.ForeColor = accentColor;
        confLabel.Location = new Point(margin + 245, y);
        confLabel.Size = new Size(50, 20);
        controlPanel.Controls.Add(confLabel);

        y += 30;

        showBboxesCheck = new CheckBox();
        showBboxesCheck.Text = "Show Bounding Boxes";
        showBboxesCheck.Checked = true;
        showBboxesCheck.ForeColor = textColor;
        showBboxesCheck.Location = new Point(margin, y);
        controlPanel.Controls.Add(showBboxesCheck);

        showTrackingCheck = new CheckBox();
        showTrackingCheck.Text = "Show Tracking IDs";
        showTrackingCheck.Checked = true;
        showTrackingCheck.ForeColor = textColor;
        showTrackingCheck.Location = new Point(margin + 150, y);
        controlPanel.Controls.Add(showTrackingCheck);

        Label helpLabel = new Label();
        helpLabel.Text = "Keys: F11=Fullscreen | Y=Toggle YOLO | C=Connect YOLO | Q=Quit";
        helpLabel.ForeColor = Color.Gray;
        helpLabel.Location = new Point(margin + 300, y);
        helpLabel.AutoSize = true;
        controlPanel.Controls.Add(helpLabel);
    }

    private void CreateStatusBar()
    {
        statusStrip = new StatusStrip();
        statusStrip.BackColor = panelColor;
        statusStrip.SizingGrip = false;

        fpsLabel = new ToolStripStatusLabel("FPS: 0");
        fpsLabel.ForeColor = textColor;
        fpsLabel.Spring = true;
        statusStrip.Items.Add(fpsLabel);

        objectsLabel = new ToolStripStatusLabel("Objects: 0");
        objectsLabel.ForeColor = textColor;
        objectsLabel.Spring = true;
        statusStrip.Items.Add(objectsLabel);

        yoloStatusLabel = new ToolStripStatusLabel("YOLO: Disconnected");
        yoloStatusLabel.ForeColor = errorColor;
        yoloStatusLabel.Spring = true;
        statusStrip.Items.Add(yoloStatusLabel);

        cameraStatusLabel = new ToolStripStatusLabel("Camera: Off");
        cameraStatusLabel.ForeColor = Color.Gray;
        cameraStatusLabel.Spring = true;
        statusStrip.Items.Add(cameraStatusLabel);

        this.Controls.Add(statusStrip);
    }

    private void CheckAvailableCameras()
    {
        cameraCombo.Items.Clear();
        
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using (var testCap = new VideoCapture(i))
                {
                    if (testCap.IsOpened())
                    {
                        cameraCombo.Items.Add($"Camera {i}");
                        testCap.Release();
                    }
                }
            }
            catch
            {
                // Camera not available
            }
        }

        if (cameraCombo.Items.Count > 0)
        {
            cameraCombo.SelectedIndex = 0;
        }
        else
        {
            cameraCombo.Items.Add("No cameras found");
            cameraCombo.SelectedIndex = 0;
        }
    }

    private void CameraCombo_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (cameraCombo.SelectedIndex >= 0)
        {
            selectedCameraIndex = cameraCombo.SelectedIndex;
        }
    }

    private void StartStopBtn_Click(object sender, EventArgs e)
    {
        if (!isRunning)
        {
            StartCamera();
        }
        else
        {
            StopCamera();
        }
    }

    private void StartCamera()
    {
        if (isRunning) return;

        try
        {
            webcam = new VideoCapture(selectedCameraIndex);
            
            if (!webcam.IsOpened())
            {
                webcam.Dispose();
                webcam = null;
                Log("ERROR: Cannot open camera");
                return;
            }

            webcam.SetFrameWidth(640);
            webcam.SetFrameHeight(480);
            webcam.SetFps(30);

            isRunning = true;
            captureThread = new Thread(CaptureLoop);
            captureThread.IsBackground = true;
            captureThread.Start();

            startStopBtn.Text = "STOP";
            startStopBtn.BackColor = errorColor;
            cameraStatusLabel.Text = $"Camera: {selectedCameraIndex}";
            cameraStatusLabel.ForeColor = successColor;

            Log($"Camera started (index {selectedCameraIndex})");

            if (yoloEnabledCheck.Checked && !yoloClient.IsConnected)
            {
                ConnectYolo();
            }
        }
        catch (Exception ex)
        {
            Log($"Camera error: {ex.Message}");
        }
    }

    private void StopCamera()
    {
        isRunning = false;

        if (captureThread != null && captureThread.IsAlive)
        {
            captureThread.Join(1000);
        }

        if (webcam != null)
        {
            webcam.Release();
            webcam.Dispose();
            webcam = null;
        }

        startStopBtn.Text = "START";
        startStopBtn.BackColor = successColor;
        cameraStatusLabel.Text = "Camera: Off";
        cameraStatusLabel.ForeColor = Color.Gray;

        Log("Camera stopped");
    }

    private void CaptureLoop()
    {
        Bitmap frameBuffer = null;

        while (isRunning && webcam != null && webcam.IsOpened())
        {
            try
            {
                if (webcam.GrabFrame(out frameBuffer))
                {
                    lock (frameLock)
                    {
                        if (currentFrame != null)
                        {
                            currentFrame.Dispose();
                        }
                        currentFrame = (Bitmap)frameBuffer.Clone();
                    }

                    if (yoloEnabledCheck.Checked && yoloClient.IsConnected && yoloClient.IsRunning)
                    {
                        Bitmap sendFrame = null;
                        lock (frameLock)
                        {
                            if (currentFrame != null)
                            {
                                sendFrame = (Bitmap)currentFrame.Clone();
                            }
                        }

                        if (sendFrame != null)
                        {
                            yoloClient.SendFrame(sendFrame);
                            sendFrame.Dispose();
                        }
                    }

                    UpdateFps();
                    UpdateVideoDisplay();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture error: {ex.Message}");
            }
        }
    }

    private void UpdateVideoDisplay()
    {
        try
        {
            if (webcamPictureBox.InvokeRequired)
            {
                webcamPictureBox.BeginInvoke(new Action(UpdateVideoDisplay));
                return;
            }

            Bitmap displayFrame = null;
            lock (frameLock)
            {
                if (currentFrame != null)
                {
                    displayFrame = (Bitmap)currentFrame.Clone();
                }
            }

            if (displayFrame != null)
            {
                if (showBboxesCheck.Checked && currentDetections.Count > 0)
                {
                    using (Graphics g = Graphics.FromImage(displayFrame))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        
                        List<DetectionResult> detsToShow = new List<DetectionResult>();
                        lock (frameLock)
                        {
                            foreach (var d in currentDetections)
                            {
                                if (d.Confidence >= confidenceThreshold)
                                    detsToShow.Add(d);
                            }
                        }

                        detectionRenderer.DrawDetections(g, detsToShow);
                    }
                }

                webcamPictureBox.Image = displayFrame;
            }
        }
        catch { }
    }

    private void UpdateFps()
    {
        frameCount++;
        framesSinceLastFps++;

        TimeSpan elapsed = DateTime.Now - lastFpsUpdate;
        if (elapsed.TotalMilliseconds >= 1000)
        {
            currentFps = framesSinceLastFps;
            framesSinceLastFps = 0;
            lastFpsUpdate = DateTime.Now;
            
            fpsLabel.Text = $"FPS: {currentFps}";
        }
    }

    private void ConnectYolo()
    {
        Log("Connecting to YOLO server...");
        
        if (yoloClient.StartServer())
        {
            Log("YOLO server started and connected");
            connectionStatus.Text = "YOLO: Connected";
            connectionStatus.ForeColor = successColor;
            yoloStatusLabel.Text = "YOLO: Connected";
            yoloStatusLabel.ForeColor = successColor;
        }
        else
        {
            Log("Failed to start YOLO server");
            connectionStatus.Text = "YOLO: Error";
            connectionStatus.ForeColor = errorColor;
        }
    }

    private void YoloClient_OnDetectionReceived(object sender, DetectionResponse e)
    {
        try
        {
            lock (frameLock)
            {
                currentDetections = e.Detections ?? new List<DetectionResult>();
            }

            totalDetections += currentDetections.Count;
            objectsLabel.Text = $"Objects: {currentDetections.Count}";

            UpdateDetectionList();
            UpdateStats();
        }
        catch { }
    }

    private void YoloClient_OnConnectionChanged(object sender, bool connected)
    {
        this.BeginInvoke(new Action(() =>
        {
            if (connected)
            {
                connectionStatus.Text = "YOLO: Connected";
                connectionStatus.ForeColor = successColor;
                yoloStatusLabel.Text = "YOLO: Connected";
                yoloStatusLabel.ForeColor = successColor;
                Log("YOLO client connected");
            }
            else
            {
                connectionStatus.Text = "YOLO: Disconnected";
                connectionStatus.ForeColor = errorColor;
                yoloStatusLabel.Text = "YOLO: Disconnected";
                yoloStatusLabel.ForeColor = errorColor;
                Log("YOLO client disconnected");
            }
        }));
    }

    private void YoloClient_OnError(object sender, string error)
    {
        this.BeginInvoke(new Action(() =>
        {
            Log($"YOLO Error: {error}");
        }));
    }

    private void UpdateDetectionList()
    {
        if (detectionList.InvokeRequired)
        {
            detectionList.BeginInvoke(new Action(UpdateDetectionList));
            return;
        }

        detectionList.Items.Clear();

        var trackingGroups = new Dictionary<int, List<DetectionResult>>();
        
        lock (frameLock)
        {
            foreach (var det in currentDetections)
            {
                if (det.Confidence < confidenceThreshold) continue;
                
                if (det.TrackId >= 0)
                {
                    if (!trackingGroups.ContainsKey(det.TrackId))
                        trackingGroups[det.TrackId] = new List<DetectionResult>();
                    trackingGroups[det.TrackId].Add(det);
                }
                else
                {
                    string itemText = $"[New] {det.ClassName} {det.Confidence:P0}";
                    var item = new ListViewItem(itemText);
                    item.ForeColor = detectionRenderer.GetClassColor(det.ClassName);
                    detectionList.Items.Add(item);
                }
            }
        }

        foreach (var group in trackingGroups.OrderBy(g => g.Key))
        {
            string classes = string.Join(", ", group.Value.Select(d => d.ClassName).Distinct());
            string itemText = $"[ID:{group.Key}] {classes} ({group.Value.Count})";
            var item = new ListViewItem(itemText);
            item.ForeColor = accentColor;
            detectionList.Items.Add(item);
        }

        int totalTracks = trackingGroups.Count;
        trackingStatusLabel.Text = totalTracks > 0 
            ? $"{totalTracks} active track(s)"
            : "No active tracks";
    }

    private void UpdateStats()
    {
        TimeSpan runtime = DateTime.Now - sessionStart;
        double hours = runtime.TotalHours;
        double minutes = runtime.TotalMinutes;
        
        statsLabel.Text = string.Format(
            "Runtime: {0:F1}m\n" +
            "Frames: {1:N0}\n" +
            "Total Detections: {2:N0}\n" +
            "Active Now: {3}\n" +
            "FPS: {4}\n" +
            "Conf Threshold: {5:F2}",
            minutes,
            frameCount,
            totalDetections,
            currentDetections.Count,
            currentFps,
            confidenceThreshold
        );
    }

    private void UpdateYoloStatus()
    {
        if (yoloEnabledCheck.Checked)
        {
            yoloStatusLabel.Text = yoloClient.IsConnected ? "YOLO: Active" : "YOLO: Connecting...";
            yoloStatusLabel.ForeColor = yoloClient.IsConnected ? successColor : warningColor;
        }
        else
        {
            yoloStatusLabel.Text = "YOLO: Disabled";
            yoloStatusLabel.ForeColor = Color.Gray;
        }
    }

    private void VideoPanel_Paint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillRectangle(Brushes.Black, 0, 0, videoPanel.Width, videoPanel.Height);

        if (!isRunning)
        {
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            g.DrawString("Press START to begin\nor press C to connect YOLO",
                new Font("Segoe UI", 14),
                new SolidBrush(Color.Gray),
                new RectangleF(0, 0, videoPanel.Width, videoPanel.Height),
                sf);
        }
    }

    private void Form_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11)
        {
            fullscreen = !fullscreen;
            if (fullscreen)
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
            }
            else
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
            }
        }
        else if (e.KeyCode == Keys.Y)
        {
            yoloEnabledCheck.Checked = !yoloEnabledCheck.Checked;
        }
        else if (e.KeyCode == Keys.C)
        {
            if (!yoloClient.IsConnected)
            {
                ConnectYolo();
            }
        }
        else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Q)
        {
            this.Close();
        }
    }

    private void Form_Closing(object sender, CancelEventArgs e)
    {
        StopCamera();

        if (yoloClient != null)
        {
            yoloClient.StopServer();
            yoloClient.Dispose();
        }

        if (tuioClient != null)
        {
            tuioClient.removeTuioListener(this);
            tuioClient.disconnect();
        }
    }

    private void Log(string msg)
    {
        System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }

    // TUIO Listeners
    public void addTuioObject(TuioObject o) { lock (objectList) { objectList.Add(o.SessionID, o); } }
    public void updateTuioObject(TuioObject o) { }
    public void removeTuioObject(TuioObject o) { lock (objectList) { objectList.Remove(o.SessionID); } }
    public void addTuioCursor(TuioCursor c) { lock (cursorList) { cursorList.Add(c.SessionID, c); } }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { lock (cursorList) { cursorList.Remove(c.SessionID); } }
    public void addTuioBlob(TuioBlob b) { lock (blobList) { blobList.Add(b.SessionID, b); } }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { lock (blobList) { blobList.Remove(b.SessionID); } }
    public void refresh(TuioTime frameTime) { }

    public static void Main(String[] argv)
    {
        int port = argv.Length > 0 && int.TryParse(argv[0], out int p) ? p : 0;
        Application.Run(new YoloEnhancedInterface(port));
    }
}


public class WebcamCapture : IDisposable
{
    private int cameraIndex;
    private int frameWidth = 640;
    private int frameHeight = 480;
    private Bitmap currentFrame;
    private readonly object lockObj = new object();
    private bool isCapturing;
    private Thread captureThread;

    public bool IsOpened => isCapturing;
    public int FrameWidth { get => frameWidth; set => frameWidth = value; }
    public int FrameHeight { get => frameHeight; set => frameHeight = value; }

    public WebcamCapture(int index)
    {
        cameraIndex = index;
    }

    public bool Start()
    {
        isCapturing = true;
        captureThread = new Thread(CaptureLoop);
        captureThread.IsBackground = true;
        captureThread.Start();
        return true;
    }

    private void CaptureLoop()
    {
        while (isCapturing)
        {
            try
            {
                using (var cap = new System.Drawing.VideoCapture(cameraIndex))
                {
                    cap.SetVideoSize(frameWidth, frameHeight);
                    
                    while (isCapturing)
                    {
                        using (Bitmap frame = cap.QueryFrame())
                        {
                            if (frame != null)
                            {
                                lock (lockObj)
                                {
                                    if (currentFrame != null)
                                        currentFrame.Dispose();
                                    currentFrame = (Bitmap)frame.Clone();
                                }
                            }
                        }
                        Thread.Sleep(33);
                    }
                }
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    public bool GrabFrame(out Bitmap frame)
    {
        frame = null;
        
        if (!isCapturing)
            return false;

        lock (lockObj)
        {
            if (currentFrame != null)
            {
                frame = (Bitmap)currentFrame.Clone();
                return true;
            }
        }

        return false;
    }

    public Bitmap GetCurrentFrame()
    {
        lock (lockObj)
        {
            return currentFrame != null ? (Bitmap)currentFrame.Clone() : null;
        }
    }

    public void Stop()
    {
        isCapturing = false;
        if (captureThread != null && captureThread.IsAlive)
            captureThread.Join(1000);
    }

    public void Release()
    {
        Stop();
        lock (lockObj)
        {
            if (currentFrame != null)
            {
                currentFrame.Dispose();
                currentFrame = null;
            }
        }
    }

    public void Dispose()
    {
        Release();
    }
}

namespace System.Drawing
{
    public class VideoCapture : IDisposable
    {
        private int deviceId;
        private bool disposed;

        public VideoCapture(int index)
        {
            deviceId = index;
        }

        public bool IsOpened()
        {
            return !disposed;
        }

        public void SetVideoSize(int width, int height)
        {
        }

        public Bitmap QueryFrame()
        {
            return null;
        }

        public void Release()
        {
        }

        public void Dispose()
        {
            if (!disposed)
            {
                Release();
                disposed = true;
            }
        }
    }
}