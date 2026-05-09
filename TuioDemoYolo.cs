/*
 * TUIO + YOLO Integration Demo
 * Simplified version without OpenCvSharp dependency
 * Uses Python for YOLO detection
 */

using System;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using TUIO;
using System.IO;
using System.Diagnostics;
using System.Drawing.Imaging;

public class TuioDemoYolo : Form, TuioListener
{
    private TuioClient client;
    private Dictionary<long, TuioObject> objectList;
    private Dictionary<long, TuioCursor> cursorList;
    private Dictionary<long, TuioBlob> blobList;

    private bool useYolo = true;
    private List<YoloDetection> yoloDetections = new List<YoloDetection>();

    private Label statusLabel;
    private CheckBox yoloCheckBox;
    private TextBox logTextBox;
    private Panel videoPanel;
    private Bitmap videoFrame;

    public static int width, height;
    private int window_width = 800;
    private int window_height = 600;
    private bool fullscreen;
    private bool verbose;

    Font font = new Font("Arial", 10.0f);
    SolidBrush fntBrush = new SolidBrush(Color.White);
    SolidBrush bgrBrush = new SolidBrush(Color.FromArgb(0, 0, 64));
    SolidBrush curBrush = new SolidBrush(Color.FromArgb(192, 0, 192));
    SolidBrush objBrush = new SolidBrush(Color.FromArgb(64, 0, 0));
    SolidBrush blbBrush = new SolidBrush(Color.FromArgb(64, 64, 64));
    Pen curPen = new Pen(new SolidBrush(Color.Blue), 1);
    Pen yoloPen = new Pen(new SolidBrush(Color.Red), 2);

    public TuioDemoYolo(int port)
    {
        verbose = false;
        fullscreen = false;
        width = window_width;
        height = window_height;

        this.ClientSize = new System.Drawing.Size(width, height);
        this.Name = "TuioDemoYolo";
        this.Text = "TUIO + YOLO Demo";

        this.Closing += new CancelEventHandler(Form_Closing);
        this.KeyDown += new KeyEventHandler(Form_KeyDown);

        this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                        ControlStyles.UserPaint |
                        ControlStyles.DoubleBuffer, true);

        objectList = new Dictionary<long, TuioObject>(128);
        cursorList = new Dictionary<long, TuioCursor>(128);
        blobList = new Dictionary<long, TuioBlob>(128);

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        CreateUI();
        CheckYolo();
    }

    private void CreateUI()
    {
        int panelHeight = 80;

        videoPanel = new Panel();
        videoPanel.BackColor = Color.Black;
        videoPanel.Location = new Point(0, 0);
        videoPanel.Size = new Size(width, height - panelHeight);
        this.Controls.Add(videoPanel);

        Panel bottomPanel = new Panel();
        bottomPanel.BackColor = Color.FromArgb(40, 40, 45);
        bottomPanel.Location = new Point(0, height - panelHeight);
        bottomPanel.Size = new Size(width, panelHeight);
        this.Controls.Add(bottomPanel);

        yoloCheckBox = new CheckBox();
        yoloCheckBox.Text = "Enable YOLO Detection";
        yoloCheckBox.Checked = true;
        yoloCheckBox.ForeColor = Color.White;
        yoloCheckBox.Location = new Point(10, 10);
        yoloCheckBox.AutoSize = true;
        yoloCheckBox.CheckedChanged += (s, e) => { useYolo = yoloCheckBox.Checked; };
        bottomPanel.Controls.Add(yoloCheckBox);

        statusLabel = new Label();
        statusLabel.Text = "Ready - TUIO: Connected | YOLO: Checking...";
        statusLabel.ForeColor = Color.White;
        statusLabel.Location = new Point(10, 40);
        statusLabel.AutoSize = true;
        bottomPanel.Controls.Add(statusLabel);

        Button testYoloBtn = new Button();
        testYoloBtn.Text = "Test YOLO";
        testYoloBtn.Location = new Point(250, 10);
        testYoloBtn.Size = new Size(100, 25);
        testYoloBtn.Click += (s, e) => RunYoloTest();
        bottomPanel.Controls.Add(testYoloBtn);

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
                statusLabel.Text = "YOLO: Ready";
                Log("YOLO Python environment OK");
            }
            else
            {
                statusLabel.Text = "YOLO: Not available";
                Log("YOLO check failed");
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = "YOLO: Error - " + ex.Message;
            Log("Error: " + ex.Message);
        }
    }

    private void Log(string msg)
    {
        if (logTextBox != null)
        {
            logTextBox.AppendText(msg + "\r\n");
        }
    }

    private void RunYoloTest()
    {
        if (!useYolo) return;

        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string yoloDir = Path.Combine(appDir, "YOLO");
            if (!Directory.Exists(yoloDir))
                yoloDir = Path.Combine(Environment.CurrentDirectory, "YOLO");
            
            string testScript = Path.Combine(yoloDir, "test_yolo.py");
            if (!File.Exists(testScript))
            {
                Log("test_yolo.py not found at: " + testScript);
                testScript = Path.Combine("C:\\Users\\Rama2\\OneDrive\\Documents\\TUIO11_NET-master\\YOLO", "test_yolo.py");
            }
            
            if (!File.Exists(testScript))
            {
                Log("test_yolo.py not found");
                return;
            }

            Log("Running YOLO test...");

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "python";
            psi.Arguments = $"\"{testScript}\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            Process p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
                Log("Output: " + output.Substring(0, Math.Min(100, output.Length)));
            if (!string.IsNullOrWhiteSpace(error))
                Log("Error: " + error.Substring(0, Math.Min(100, error.Length)));

            if (p.ExitCode == 0)
                Log("YOLO test completed");
            else
                Log("YOLO test failed: " + p.ExitCode);
        }
        catch (Exception ex)
        {
            Log("Exception: " + ex.Message);
        }
    }

    private void Form_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyData == Keys.F1)
        {
            if (fullscreen == false)
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
                fullscreen = true;
            }
            else
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
                fullscreen = false;
            }
        }
        else if (e.KeyData == Keys.Escape)
        {
            this.Close();
        }
        else if (e.KeyData == Keys.V)
        {
            verbose = !verbose;
        }
    }

    private void Form_Closing(object sender, CancelEventArgs e)
    {
        client.removeTuioListener(this);
        client.disconnect();
        System.Environment.Exit(0);
    }

    public void addTuioObject(TuioObject o)
    {
        lock (objectList) { objectList.Add(o.SessionID, o); }
        if (verbose) Console.WriteLine("add obj " + o.SymbolID + " (" + o.SessionID + ")");
    }

    public void updateTuioObject(TuioObject o)
    {
        if (verbose) Console.WriteLine("set obj " + o.SymbolID);
    }

    public void removeTuioObject(TuioObject o)
    {
        lock (objectList) { objectList.Remove(o.SessionID); }
        if (verbose) Console.WriteLine("del obj " + o.SymbolID);
    }

    public void addTuioCursor(TuioCursor c)
    {
        lock (cursorList) { cursorList.Add(c.SessionID, c); }
        if (verbose) Console.WriteLine("add cur " + c.CursorID);
    }

    public void updateTuioCursor(TuioCursor c)
    {
        if (verbose) Console.WriteLine("set cur " + c.CursorID);
    }

    public void removeTuioCursor(TuioCursor c)
    {
        lock (cursorList) { cursorList.Remove(c.SessionID); }
        if (verbose) Console.WriteLine("del cur " + c.CursorID);
    }

    public void addTuioBlob(TuioBlob b)
    {
        lock (blobList) { blobList.Add(b.SessionID, b); }
    }

    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { lock (blobList) { blobList.Remove(b.SessionID); } }

    public void refresh(TuioTime frameTime)
    {
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.FillRectangle(bgrBrush, new Rectangle(0, 0, width, height));

        if (cursorList.Count > 0)
        {
            lock (cursorList)
            {
                foreach (TuioCursor tcur in cursorList.Values)
                {
                    List<TuioPoint> path = tcur.Path;
                    TuioPoint current_point = path[0];
                    for (int i = 0; i < path.Count; i++)
                    {
                        TuioPoint next_point = path[i];
                        g.DrawLine(curPen, current_point.getScreenX(width), current_point.getScreenY(height), next_point.getScreenX(width), next_point.getScreenY(height));
                        current_point = next_point;
                    }
                    g.FillEllipse(curBrush, current_point.getScreenX(width) - height / 100, current_point.getScreenY(height) - height / 100, height / 50, height / 50);
                }
            }
        }

        if (objectList.Count > 0)
        {
            lock (objectList)
            {
                foreach (TuioObject tobj in objectList.Values)
                {
                    int ox = tobj.getScreenX(width);
                    int oy = tobj.getScreenY(height);
                    int size = height / 10;

                    g.TranslateTransform(ox, oy);
                    g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                    g.TranslateTransform(-ox, -oy);

                    g.FillRectangle(objBrush, new Rectangle(ox - size / 2, oy - size / 2, size, size));

                    g.TranslateTransform(ox, oy);
                    g.RotateTransform(-1 * (float)(tobj.Angle / Math.PI * 180.0f));
                    g.TranslateTransform(-ox, -oy);

                    g.DrawString(tobj.SymbolID + "", font, fntBrush, new PointF(ox - 10, oy - 10));
                }
            }
        }

        if (blobList.Count > 0)
        {
            lock (blobList)
            {
                foreach (TuioBlob tblb in blobList.Values)
                {
                    int bx = tblb.getScreenX(width);
                    int by = tblb.getScreenY(height);
                    float bw = tblb.Width * width;
                    float bh = tblb.Height * height;

                    g.FillEllipse(blbBrush, bx - bw / 2, by - bh / 2, bw, bh);
                }
            }
        }
    }

    public static void Main(String[] argv)
    {
        int port = 3333;
        if (argv.Length > 0 && int.TryParse(argv[0], out int p))
            port = p;

        Console.WriteLine("TUIO + YOLO Demo");
        Console.WriteLine("Keys: F1=Fullscreen, V=Verbose, ESC=Exit");
        Application.Run(new TuioDemoYolo(port));
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