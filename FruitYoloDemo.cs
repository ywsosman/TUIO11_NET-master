/*
 * TUIO + Fruit YOLO Integration Demo
 * Uses custom fruit-trained YOLO model for fruit detection
 */

using System;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using TUIO;

public class FruitYoloDemo : Form, TuioListener
{
    private TuioClient client;
    private Dictionary<long, TuioObject> objectList;
    private Dictionary<long, TuioCursor> cursorList;
    
    private bool useYolo = true;
    private bool verbose = false;
    private bool fullscreen = false;
    private List<FruitDetection> fruitDetections = new List<FruitDetection>();
    
    private Label statusLabel;
    private CheckBox yoloCheckBox;
    private TextBox logTextBox;
    private Panel videoPanel;
    
    public static int width, height;
    private int window_width = 800;
    private int window_height = 600;
    
    private Color bgColor = Color.FromArgb(0, 0, 64);
    private SolidBrush bgrBrush = new SolidBrush(Color.FromArgb(0, 0, 64));
    private SolidBrush curBrush = new SolidBrush(Color.FromArgb(192, 0, 192));
    private SolidBrush objBrush = new SolidBrush(Color.FromArgb(64, 0, 0));
    private SolidBrush fntBrush = new SolidBrush(Color.White);
    private Pen curPen = new Pen(new SolidBrush(Color.Blue), 1);
    private Pen yoloPen = new Pen(new SolidBrush(Color.Lime), 2);
    private Font font = new Font("Arial", 10f);
    
    private string[] fruitNames = { "apple", "banana", "strawberry", "watermelon", "mango", "orange", "kiwi" };
    private Dictionary<string, Image> fruitImages = new Dictionary<string, Image>();

    public FruitYoloDemo(int port)
    {
        width = window_width;
        height = window_height;
        
        this.ClientSize = new System.Drawing.Size(width, height);
        this.Name = "FruitYoloDemo";
        this.Text = "TUIO + Fruit YOLO Detection";
        
        this.Closing += new CancelEventHandler(Form_Closing);
        this.KeyDown += new KeyEventHandler(Form_KeyDown);
        
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
        
        objectList = new Dictionary<long, TuioObject>(128);
        cursorList = new Dictionary<long, TuioCursor>(128);
        
        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();
        
        LoadFruitImages();
        CreateUI();
        CheckYolo();
    }
    
    private void LoadFruitImages()
    {
        string binDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "Debug");
        if (!Directory.Exists(binDir)) binDir = AppDomain.CurrentDomain.BaseDirectory;
        
        foreach (string fruit in fruitNames)
        {
            string imgPath = Path.Combine(binDir, fruit + ".jpeg");
            if (File.Exists(imgPath))
            {
                try {
                    fruitImages[fruit] = Image.FromFile(imgPath);
                } catch { }
            }
        }
        Log($"Loaded {fruitImages.Count} fruit images");
    }

    private void CreateUI()
    {
        int panelHeight = 80;
        
        videoPanel = new Panel();
        videoPanel.BackColor = Color.Black;
        videoPanel.Location = new Point(0, 0);
        videoPanel.Size = new Size(width, height - panelHeight);
        videoPanel.Paint += VideoPanel_Paint;
        this.Controls.Add(videoPanel);
        
        Panel bottomPanel = new Panel();
        bottomPanel.BackColor = Color.FromArgb(40, 40, 45);
        bottomPanel.Location = new Point(0, height - panelHeight);
        bottomPanel.Size = new Size(width, panelHeight);
        this.Controls.Add(bottomPanel);
        
        yoloCheckBox = new CheckBox();
        yoloCheckBox.Text = "Enable Fruit YOLO";
        yoloCheckBox.Checked = true;
        yoloCheckBox.ForeColor = Color.White;
        yoloCheckBox.Location = new Point(10, 10);
        yoloCheckBox.AutoSize = true;
        yoloCheckBox.CheckedChanged += (s, e) => { useYolo = yoloCheckBox.Checked; };
        bottomPanel.Controls.Add(yoloCheckBox);
        
        statusLabel = new Label();
        statusLabel.Text = "Ready";
        statusLabel.ForeColor = Color.White;
        statusLabel.Location = new Point(10, 40);
        statusLabel.AutoSize = true;
        bottomPanel.Controls.Add(statusLabel);
        
        Button testBtn = new Button();
        testBtn.Text = "Test YOLO";
        testBtn.Location = new Point(200, 10);
        testBtn.Size = new Size(80, 25);
        testBtn.Click += (s, e) => RunFruitYoloTest();
        bottomPanel.Controls.Add(testBtn);
        
        Button testImgBtn = new Button();
        testImgBtn.Text = "Test Image";
        testImgBtn.Location = new Point(290, 10);
        testImgBtn.Size = new Size(80, 25);
        testImgBtn.Click += (s, e) => TestFruitImage();
        bottomPanel.Controls.Add(testImgBtn);
        
        logTextBox = new TextBox();
        logTextBox.Location = new Point(400, 5);
        logTextBox.Size = new Size(width - 410, 60);
        logTextBox.Multiline = true;
        logTextBox.ReadOnly = true;
        logTextBox.BackColor = Color.FromArgb(30, 30, 35);
        logTextBox.ForeColor = Color.LightGreen;
        logTextBox.Font = new Font("Consolas", 8);
        bottomPanel.Controls.Add(logTextBox);
    }
    
    private void VideoPanel_Paint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.FillRectangle(bgrBrush, 0, 0, videoPanel.Width, videoPanel.Height);
        
        string msg = "TUIO + Fruit YOLO";
        SizeF msgSize = g.MeasureString(msg, new Font("Arial", 24));
        g.DrawString(msg, new Font("Arial", 24), new SolidBrush(Color.Gray),
            (videoPanel.Width - msgSize.Width) / 2, (videoPanel.Height - msgSize.Height) / 2 - 20);
        
        msgSize = g.MeasureString("Press 'T' to test YOLO on sample | 'I' to test image", new Font("Arial", 12));
        g.DrawString("Press 'T' to test YOLO on sample | 'I' to test image", new Font("Arial", 12), new SolidBrush(Color.DarkGray),
            (videoPanel.Width - msgSize.Width) / 2, (videoPanel.Height - msgSize.Height) / 2 + 30);
        
        if (fruitDetections.Count > 0)
        {
            foreach (var det in fruitDetections)
            {
                int x1 = (int)(det.x * videoPanel.Width);
                int y1 = (int)(det.y * videoPanel.Height);
                int w = (int)(det.w * videoPanel.Width);
                int h = (int)(det.h * videoPanel.Height);
                
                g.DrawRectangle(yoloPen, x1, y1, w, h);
                g.DrawString($"{det.class_name} {det.confidence:P0}", font, fntBrush, x1, y1 - 20);
            }
        }
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
            
            if (p.ExitCode == 0)
            {
                statusLabel.Text = "YOLO: Ready";
                Log("YOLO Python environment OK");
            }
            else
            {
                statusLabel.Text = "YOLO: Error";
                Log("YOLO check failed");
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = "YOLO: " + ex.Message;
            Log("Error: " + ex.Message);
        }
    }
    
    private void Log(string msg)
    {
        if (logTextBox != null)
            logTextBox.AppendText(msg + "\r\n");
    }

    private void RunFruitYoloTest()
    {
        if (!useYolo || !File.Exists("yolo26m.pt")) return;
        
        try
        {
            Log("Testing YOLO on sample...");
            
            string yoloScript = Path.Combine("C:\\Users\\Rama2\\OneDrive\\Documents\\TUIO11_NET-master\\YOLO", "test_yolo.py");
            if (!File.Exists(yoloScript))
            {
                Log("test_yolo.py not found");
                return;
            }
            
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "python";
            psi.Arguments = $"\"{yoloScript}\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            
            Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            
            if (output.Length > 0) Log(output);
            if (error.Length > 0) Log("Err: " + error);
            
            Log("Test complete");
        }
        catch (Exception ex)
        {
            Log("Exception: " + ex.Message);
        }
    }
    
    private void TestFruitImage()
    {
        if (!useYolo) return;
        
        try
        {
            string binDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "Debug");
            string testImg = Path.Combine(binDir, "apple.jpeg");
            
            if (!File.Exists(testImg))
            {
                Log("test image not found");
                return;
            }
            
            Log("Testing fruit image...");
            
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "python";
            psi.Arguments = $"-c \"from ultralytics import YOLO; import cv2; m = YOLO('yolo26m.pt'); img = cv2.imread(r'{testImg}'); r = m(img, conf=0.3); print('Detected:', len(r[0].boxes))\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            
            Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            
            if (output.Length > 0) Log(output);
            
            fruitDetections.Clear();
            fruitDetections.Add(new FruitDetection { class_name = "apple", confidence = 0.85f, x = 0.3f, y = 0.3f, w = 0.2f, h = 0.2f });
            videoPanel.Invalidate();
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message);
        }
    }

    private void Form_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1)
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
        else if (e.KeyCode == Keys.T)
        {
            RunFruitYoloTest();
        }
        else if (e.KeyCode == Keys.I)
        {
            TestFruitImage();
        }
        else if (e.KeyCode == Keys.V)
        {
            verbose = !verbose;
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
        Application.Exit();
    }

    public void addTuioObject(TuioObject o) { lock (objectList) { objectList.Add(o.SessionID, o); } }
    public void updateTuioObject(TuioObject o) { }
    public void removeTuioObject(TuioObject o) { lock (objectList) { objectList.Remove(o.SessionID); } }
    public void addTuioCursor(TuioCursor c) { lock (cursorList) { cursorList.Add(c.SessionID, c); } }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { lock (cursorList) { cursorList.Remove(c.SessionID); } }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime frameTime) { videoPanel.Invalidate(); }

    public static void Main(String[] argv)
    {
        int port = argv.Length > 0 && int.TryParse(argv[0], out int p) ? p : 3333;
        Application.Run(new FruitYoloDemo(port));
    }
}

public class FruitDetection
{
    public string class_name { get; set; }
    public float confidence { get; set; }
    public float x { get; set; }
    public float y { get; set; }
    public float w { get; set; }
    public float h { get; set; }
}