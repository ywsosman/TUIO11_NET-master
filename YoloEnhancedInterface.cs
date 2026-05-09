/*
 * TUIO + YOLO Enhanced Interface
 * Simplified version without OpenCvSharp
 * Uses Python backend for YOLO detection
 */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using TUIO;
using System.IO;
using System.Diagnostics;
using System.Linq;

public class YoloEnhancedInterface : Form, TuioListener
{
    private TuioClient client;
    private Dictionary<long, TuioObject> objectList;
    private Dictionary<long, TuioCursor> cursorList;
    private Dictionary<long, TuioBlob> blobList;

    private bool useYolo = true;
    private float confidence = 0.35f;
    private List<YoloDetection> yoloDetections = new List<YoloDetection>();
    private Dictionary<string, int> classStats = new Dictionary<string, int>();

    private int frameCount = 0;
    private int totalDetections = 0;
    private DateTime sessionStart;
    private int fps = 0;
    private DateTime lastFpsUpdate;

    private Color bgColor = Color.FromArgb(30, 30, 35);
    private Color panelColor = Color.FromArgb(45, 45, 52);
    private Color accentColor = Color.FromArgb(0, 180, 216);
    private Color textColor = Color.FromArgb(220, 220, 225);
    private Color successColor = Color.FromArgb(80, 200, 120);

    private Panel mainPanel;
    private Panel sidebarPanel;
    private Panel controlPanel;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel fpsLabel;
    private ToolStripStatusLabel objectsLabel;
    private ToolStripStatusLabel yoloStatusLabel;

    private ComboBox videoSourceCombo;
    private Button startStopBtn;
    private CheckBox yoloEnabledCheck;
    private TrackBar confSlider;
    private Label confLabel;
    private ListView detectionList;
    private Label statsLabel;
    private Panel videoPanel;

    private int window_width = 1000;
    private int window_height = 650;
    private int sidebarWidth = 250;
    private int controlHeight = 120;
    private int statusHeight = 26;

    private bool fullscreen;

    public YoloEnhancedInterface(int port)
    {
        sessionStart = DateTime.Now;
        lastFpsUpdate = DateTime.Now;

        this.Text = "YOLO + TUIO Enhanced Interface";
        this.Size = new Size(window_width, window_height);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = bgColor;

        fullscreen = false;
        width = window_width - sidebarWidth;
        height = window_height - controlHeight - statusHeight;

        this.FormClosing += Form_Closing;
        this.KeyDown += Form_KeyDown;

        this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                        ControlStyles.UserPaint |
                        ControlStyles.DoubleBuffer, true);

        objectList = new Dictionary<long, TuioObject>(128);
        cursorList = new Dictionary<long, TuioCursor>(128);
        blobList = new Dictionary<long, TuioBlob>(128);

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        CreateMainLayout();
        CreateSidebar();
        CreateControlPanel();
        CreateStatusBar();
        CheckYolo();
    }

    private void CreateMainLayout()
    {
        mainPanel = new Panel();
        mainPanel.BackColor = bgColor;
        mainPanel.Location = new Point(0, 0);
        mainPanel.Size = new Size(window_width - sidebarWidth, window_height - controlHeight - statusHeight);
        this.Controls.Add(mainPanel);

        videoPanel = new Panel();
        videoPanel.BackColor = Color.Black;
        videoPanel.Location = new Point(10, 10);
        videoPanel.Size = new Size(mainPanel.Width - 20, mainPanel.Height - 20);
        videoPanel.Paint += VideoPanel_Paint;
        mainPanel.Controls.Add(videoPanel);
    }

    private void CreateSidebar()
    {
        sidebarPanel = new Panel();
        sidebarPanel.BackColor = panelColor;
        sidebarPanel.Location = new Point(window_width - sidebarWidth, 0);
        sidebarPanel.Size = new Size(sidebarWidth, window_height - controlHeight - statusHeight);
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
        statsLabel.Size = new Size(sidebarWidth - 30, 150);
        statsLabel.AutoSize = false;
        sidebarPanel.Controls.Add(statsLabel);

        Label listTitle = new Label();
        listTitle.Text = "RECENT DETECTIONS";
        listTitle.ForeColor = accentColor;
        listTitle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        listTitle.Location = new Point(15, 210);
        listTitle.AutoSize = true;
        sidebarPanel.Controls.Add(listTitle);

        detectionList = new ListView();
        detectionList.BackColor = bgColor;
        detectionList.ForeColor = textColor;
        detectionList.Font = new Font("Consolas", 8);
        detectionList.Location = new Point(15, 235);
        detectionList.Size = new Size(sidebarWidth - 30, sidebarPanel.Height - 280);
        detectionList.View = View.List;
        sidebarPanel.Controls.Add(detectionList);
    }

    private void CreateControlPanel()
    {
        controlPanel = new Panel();
        controlPanel.BackColor = panelColor;
        controlPanel.Location = new Point(0, window_height - controlHeight - statusHeight);
        controlPanel.Size = new Size(window_width - sidebarWidth, controlHeight);
        this.Controls.Add(controlPanel);

        int margin = 15;
        int y = 15;

        Label sourceLabel = new Label();
        sourceLabel.Text = "Video:";
        sourceLabel.ForeColor = textColor;
        sourceLabel.Location = new Point(margin, y);
        sourceLabel.Size = new Size(50, 20);
        controlPanel.Controls.Add(sourceLabel);

        videoSourceCombo = new ComboBox();
        videoSourceCombo.Items.AddRange(new object[] { "Webcam", "Test Image" });
        videoSourceCombo.SelectedIndex = 0;
        videoSourceCombo.Location = new Point(margin + 55, y);
        videoSourceCombo.Size = new Size(120, 25);
        controlPanel.Controls.Add(videoSourceCombo);

        startStopBtn = new Button();
        startStopBtn.Text = "START";
        startStopBtn.Location = new Point(margin + 185, y);
        startStopBtn.Size = new Size(80, 25);
        startStopBtn.BackColor = accentColor;
        startStopBtn.ForeColor = Color.White;
        startStopBtn.FlatStyle = FlatStyle.Flat;
        startStopBtn.Click += (s, e) => { };
        controlPanel.Controls.Add(startStopBtn);

        yoloEnabledCheck = new CheckBox();
        yoloEnabledCheck.Text = "Enable YOLO";
        yoloEnabledCheck.Checked = true;
        yoloEnabledCheck.ForeColor = textColor;
        yoloEnabledCheck.Location = new Point(margin + 280, y);
        yoloEnabledCheck.CheckedChanged += (s, e) => { useYolo = yoloEnabledCheck.Checked; };
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
        confSlider.ValueChanged += (s, e) => { confidence = confSlider.Value / 100f; confLabel.Text = confidence.ToString("F2"); };
        controlPanel.Controls.Add(confSlider);

        confLabel = new Label();
        confLabel.Text = "0.35";
        confLabel.ForeColor = accentColor;
        confLabel.Location = new Point(margin + 245, y);
        confLabel.Size = new Size(50, 20);
        controlPanel.Controls.Add(confLabel);

        Label helpLabel = new Label();
        helpLabel.Text = "Keys: F11=Fullscreen | Y=Toggle YOLO | ESC=Exit";
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

        yoloStatusLabel = new ToolStripStatusLabel("YOLO: Ready");
        yoloStatusLabel.ForeColor = successColor;
        yoloStatusLabel.Spring = true;
        statusStrip.Items.Add(yoloStatusLabel);

        this.Controls.Add(statusStrip);
    }

    private void CheckYolo()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "python";
            psi.Arguments = "-c \"from ultralytics import YOLO; print('OK')\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.CreateNoWindow = true;

            Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode == 0 && output.Trim().Contains("OK"))
            {
                yoloStatusLabel.Text = "YOLO: Ready";
                statsLabel.Text = "YOLO: Python ready\n";
            }
            else
            {
                yoloStatusLabel.Text = "YOLO: Not found";
                statsLabel.Text = "YOLO: Check Python\n";
            }
        }
        catch (Exception ex)
        {
            yoloStatusLabel.Text = "YOLO: Error";
            statsLabel.Text = "Error: " + ex.Message;
        }
    }

    private void VideoPanel_Paint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillRectangle(Brushes.Black, 0, 0, videoPanel.Width, videoPanel.Height);

        StringFormat sf = new StringFormat();
        sf.Alignment = StringAlignment.Center;
        sf.LineAlignment = StringAlignment.Center;
        g.DrawString("TUIO + YOLO Interface\nPress START or use keyboard", new Font("Segoe UI", 14), new SolidBrush(Color.Gray),
            new RectangleF(0, 0, videoPanel.Width, videoPanel.Height), sf);
    }

    private void Form_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11)
        {
            if (fullscreen)
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
                fullscreen = false;
            }
            else
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
                fullscreen = true;
            }
        }
        else if (e.KeyCode == Keys.Y)
        {
            useYolo = !useYolo;
            yoloEnabledCheck.Checked = useYolo;
            yoloStatusLabel.Text = useYolo ? "YOLO: Active" : "YOLO: Off";
        }
        else if (e.KeyCode == Keys.Escape)
        {
            this.Close();
        }
    }

    private void Form_Closing(object sender, CancelEventArgs e)
    {
        client.removeTuioListener(this);
        client.disconnect();
    }

    public void addTuioObject(TuioObject o) { lock (objectList) { objectList.Add(o.SessionID, o); } }
    public void updateTuioObject(TuioObject o) { }
    public void removeTuioObject(TuioObject o) { lock (objectList) { objectList.Remove(o.SessionID); } }
    public void addTuioCursor(TuioCursor c) { lock (cursorList) { cursorList.Add(c.SessionID, c); } }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { lock (cursorList) { cursorList.Remove(c.SessionID); } }
    public void addTuioBlob(TuioBlob b) { lock (blobList) { blobList.Add(b.SessionID, b); } }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { lock (blobList) { blobList.Remove(b.SessionID); } }
    public void refresh(TuioTime frameTime) { videoPanel.Invalidate(); }

    public static void Main(String[] argv)
    {
        int port = argv.Length > 0 && int.TryParse(argv[0], out int p) ? p : 3333;
        Application.Run(new YoloEnhancedInterface(port));
    }
}

public class YoloDetection
{
    public string class_name { get; set; }
    public float confidence { get; set; }
    public YoloBBox bbox { get; set; }
}

public class YoloBBox
{
    public float x1 { get; set; }
    public float y1 { get; set; }
    public float x2 { get; set; }
    public float y2 { get; set; }
}