using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GestureClient;
using HCI_Lab_codes.Models;
using TUIO;
using TuioDemoApp;

public class LoginForm : Form, TuioListener
{
    private TuioClient client;
    private RoundedButton btnScan;
    private RoundedButton btnRadialGesture;
    private Label labelTitle;
    private Label labelStatus;
    private Label labelBluetooth;
    private Label label1;
    private Button btnClose;
    private bool isScanning = false;
    private int port;
    private RoundedButton btnAdminPanel;
    // For dragging the frameless window
    private bool dragging = false;
    private Point dragCursorPoint;
    private Point dragFormPoint;

    // Face login state
    private Thread faceLoginThread;
    private CancellationTokenSource faceLoginCts;
    private bool loginCompleted;
    private readonly object loginLock = new object();

    // Bluetooth login state
    private BluetoothDevicePairingManager btManager;
    private readonly HashSet<string> btKnownAtStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool btBaselineCaptured;

    // Face-priority race control. While true, BT/marker detections are queued
    // rather than completing the login; this gives enrolment Face ID time to finish
    private bool faceLoginActive = true;
    private PairedBluetoothDevice queuedBtDevice;
    /// <summary>Marker symbol ID held while Face ID loads; completes sign-in if face fails.</summary>
    private int? queuedMarkerSymbolId;

    public bool UseRadialGestureMode { get; private set; }
    public bool IsTeacher { get; private set; }
    public double? IdentifiedAgeYears { get; private set; }
    /// <summary>Resolved progress key (<c>student:youssef</c>, <c>teacher:dr-ayman</c>, <c>student:marker-5</c>, …).</summary>
    public string UserProfileKey { get; private set; }
    /// <summary>Display name when identified via enrolled face (<see cref="FaceLogin.LoginResult.PersonDisplayName"/>).</summary>
    public string IdentifiedPersonName { get; private set; }

    public LoginForm(int port)
    {
        this.port = port;
        InitializeComponent();

        client = new TuioClient(port);
        client.addTuioListener(this);

        // Three parallel sign-in paths: face ID (Python camera), TUIO marker, Bluetooth tag.
        UseRadialGestureMode = true;
        IsTeacher = false;
        isScanning = true;

        // No buttons: hide every control that InitializeComponent created.
        btnScan.Visible = false;
        btnRadialGesture.Visible = false;
        if (btnAdminPanel != null) btnAdminPanel.Visible = false;
        if (label1 != null) label1.Visible = false;

        labelStatus = new Label();
        labelStatus.BackColor = Color.Transparent;
        labelStatus.ForeColor = Color.White;
        labelStatus.TextAlign = ContentAlignment.MiddleCenter;
        labelStatus.Text = "A separate camera window will open for face sign-in. Look at the camera when it appears. Marker or Bluetooth work if face login fails.";
        labelStatus.Font = new Font("Comic Sans MS", 13F, FontStyle.Bold);
        labelStatus.Size = new Size(450, 110);
        labelStatus.Location = new Point(0, 90);
        this.Controls.Add(labelStatus);

        labelBluetooth = new Label();
        labelBluetooth.BackColor = Color.Transparent;
        labelBluetooth.ForeColor = Color.FromArgb(220, 240, 255);
        labelBluetooth.TextAlign = ContentAlignment.MiddleCenter;
        labelBluetooth.Text = "Bluetooth: searching...";
        labelBluetooth.Font = new Font("Comic Sans MS", 11F, FontStyle.Bold);
        labelBluetooth.Size = new Size(450, 30);
        labelBluetooth.Location = new Point(0, 225);
        this.Controls.Add(labelBluetooth);

        try
        {
            if (!client.isConnected())
                client.connect();
        }
        catch { }

        StartBluetoothLogin();

        this.Shown += LoginForm_Shown;
        this.FormClosing += LoginForm_FormClosing;
    }

    private void LoginForm_Shown(object sender, EventArgs e)
    {
        StartFaceLogin();
    }

    private void LoginForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        try { faceLoginCts?.Cancel(); } catch { }
        StopBluetoothLogin();
    }

    private void StartFaceLogin()
    {
        faceLoginCts = new CancellationTokenSource();
        var token = faceLoginCts.Token;
        faceLoginThread = new Thread(() =>
        {
            FaceLogin.LoginResult res;
            try { res = FaceLogin.Run(cancellation: token); }
            catch (Exception ex)
            {
                res = new FaceLogin.LoginResult
                {
                    Kind = FaceLogin.LoginKind.Error,
                    ErrorMessage = ex.Message
                };
            }
            OnFaceLoginCompleted(res);
        });
        faceLoginThread.IsBackground = true;
        faceLoginThread.Start();
    }

    private void OnFaceLoginCompleted(FaceLogin.LoginResult result)
    {
        if (this.IsDisposed) return;
        try
        {
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (this.IsDisposed) return;
                lock (loginLock)
                {
                    if (loginCompleted) return;

                    // Face has finished racing — release the BT/marker queue
                    // regardless of outcome so a fallback can take over.
                    faceLoginActive = false;

                    if (result.Kind == FaceLogin.LoginKind.Student ||
                        result.Kind == FaceLogin.LoginKind.Teacher)
                    {
                        IsTeacher = (result.Kind == FaceLogin.LoginKind.Teacher);
                        IdentifiedAgeYears = result.AgeYears;
                        IdentifiedPersonName = result.PersonDisplayName ?? "";

                        var user = HCI_Lab_codes.Models.UserManager.GetByFaceId(result.ProfileKey);
                        if (user == null) {
                            user = HCI_Lab_codes.Models.UserManager.CreateUser(
                                result.ProfileKey,
                                IsTeacher ? "teacher" : "child",
                                string.IsNullOrWhiteSpace(result.PersonDisplayName) ? null : result.PersonDisplayName);
                        } else {
                            IsTeacher = (user.Role == "teacher" || user.Role == "admin");
                        }

                        FinishLogin(DialogResult.OK,
                            IsTeacher
                                ? ("Welcome" + (
                                    string.IsNullOrEmpty(IdentifiedPersonName) ? ", teacher!" : ", " + IdentifiedPersonName + "!"))
                                : ("Welcome, " +
                                    (string.IsNullOrEmpty(IdentifiedPersonName) ? "Let's play!" : IdentifiedPersonName + "!")),
                            result.ProfileKey);
                        return;
                    }

                    // Either user pressed Q on the preview window (MODE:CANCEL) or Python exited with error.
                    // Don't close the form; give the held-back BT/marker their turn.
                    if (TryUseQueuedSignIn()) return;

                    if (result.Kind == FaceLogin.LoginKind.Cancelled)
                    {
                        if (labelStatus != null)
                            labelStatus.Text = "Face ID skipped. Show a marker or connect a Bluetooth tag.";
                        return;
                    }

                    // Error: surface the friendly message, keep marker + Bluetooth listening.
                    if (labelStatus != null)
                    {
                        string detail = string.IsNullOrEmpty(result.ErrorMessage)
                            ? "Face login unavailable."
                            : result.ErrorMessage;
                        if (detail.Length > 180) detail = detail.Substring(0, 180) + "...";
                        labelStatus.Text =
                            "Face login failed: " + detail +
                            Environment.NewLine +
                            "Show a marker or connect a Bluetooth tag. (see Documents/TUIO_Evaluation/face_login.log)";
                    }
                }
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Called from <see cref="OnFaceLoginCompleted"/> after the face race ends
    /// without a positive identification. If the user already showed a marker
    /// or brought a Bluetooth tag close while face login was loading, sign in
    /// with that path now. Caller must hold <see cref="loginLock"/>.
    /// </summary>
    private bool TryUseQueuedSignIn()
    {
        if (loginCompleted) return true;

        if (queuedBtDevice != null)
        {
            var dev = queuedBtDevice;
            queuedBtDevice = null;
            IsTeacher = false;
            IdentifiedAgeYears = null;
            string name = string.IsNullOrEmpty(dev.DeviceName) ? dev.MacKey : dev.DeviceName;
            FinishLogin(DialogResult.OK, "Welcome! " + name + " connected.", ProfileKeyForBluetooth(dev));
            return true;
        }

        if (queuedMarkerSymbolId.HasValue)
        {
            int sid = queuedMarkerSymbolId.Value;
            queuedMarkerSymbolId = null;
            isScanning = false;
            IsTeacher = false;
            FinishLogin(DialogResult.OK, "Logged in! Let's play!", "student:marker-" + sid);
            return true;
        }

        return false;
    }

    private static string ProfileKeyForBluetooth(PairedBluetoothDevice dev)
    {
        if (dev == null || string.IsNullOrEmpty(dev.MacKey))
            return "student:ble-unknown";
        var hexChars = dev.MacKey.Where(c =>
            char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')).ToArray();
        string hex = new string(hexChars).ToUpperInvariant();
        if (hex.Length >= 8)
            hex = hex.Substring(hex.Length - 8);
        if (!string.IsNullOrEmpty(hex))
            return "student:ble-" + hex.ToLowerInvariant();
        string slug = FaceLogin.ProfileSlug(dev.DeviceName ?? dev.MacKey);
        return "student:" + (string.IsNullOrEmpty(slug) ? "ble-unknown" : "ble-" + slug);
    }

    private void FinishLogin(DialogResult dr, string statusText, string userProfileKey = null)
    {
        loginCompleted = true;
        if (!string.IsNullOrWhiteSpace(userProfileKey))
            UserProfileKey = userProfileKey.Trim();
        if (labelStatus != null) labelStatus.Text = statusText ?? "";
        try { client?.removeTuioListener(this); } catch { }
        try { if (client != null && client.isConnected()) client.disconnect(); } catch { }
        try { faceLoginCts?.Cancel(); } catch { }
        StopBluetoothLogin();
        this.DialogResult = dr;
        this.Close();
    }

    // ── Bluetooth sign-in ─────────────────────────────────────────────────────

    private void StartBluetoothLogin()
    {
        try
        {
            btManager = new BluetoothDevicePairingManager();
            btManager.PairingStateChanged += OnBluetoothPairingChanged;
            btManager.StatusMessage += OnBluetoothStatusMessage;
            btManager.Start();
        }
        catch (Exception ex)
        {
            if (labelBluetooth != null)
                labelBluetooth.Text = "Bluetooth unavailable (" + ex.Message + ")";
        }
    }

    private void StopBluetoothLogin()
    {
        var mgr = btManager;
        btManager = null;
        if (mgr == null) return;
        try { mgr.PairingStateChanged -= OnBluetoothPairingChanged; } catch { }
        try { mgr.StatusMessage -= OnBluetoothStatusMessage; } catch { }
        try { mgr.Dispose(); } catch { }
    }

    private void OnBluetoothStatusMessage(string message)
    {
        if (string.IsNullOrEmpty(message) || this.IsDisposed) return;
        try
        {
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (this.IsDisposed || loginCompleted) return;
                if (labelBluetooth != null)
                    labelBluetooth.Text = "Bluetooth: " + message;
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void OnBluetoothPairingChanged(List<PairedBluetoothDevice> devices)
    {
        if (devices == null || this.IsDisposed) return;
        try
        {
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (this.IsDisposed) return;
                lock (loginLock)
                {
                    if (loginCompleted) return;

                    if (!btBaselineCaptured)
                    {
                        // First scan: remember whatever is already advertising so we don't
                        // sign the user in for devices that were sitting there before login.
                        foreach (var d in devices)
                            if (d != null && !string.IsNullOrEmpty(d.MacKey))
                                btKnownAtStart.Add(d.MacKey);
                        btBaselineCaptured = true;
                        UpdateBluetoothLabel(devices, null);
                        return;
                    }

                    PairedBluetoothDevice newKey = null;
                    foreach (var d in devices)
                    {
                        if (d == null || !d.IsConnected) continue;
                        if (string.IsNullOrEmpty(d.MacKey)) continue;
                        if (!btKnownAtStart.Contains(d.MacKey))
                        {
                            newKey = d;
                            break;
                        }
                    }

                    UpdateBluetoothLabel(devices, newKey);

                    if (newKey != null)
                    {
                        if (faceLoginActive)
                        {
                            // Face has priority; remember the device but don't sign in yet.
                            queuedBtDevice = newKey;
                            if (labelBluetooth != null)
                            {
                                string name = string.IsNullOrEmpty(newKey.DeviceName) ? newKey.MacKey : newKey.DeviceName;
                                labelBluetooth.Text = "Bluetooth: " + name + " ready (waiting for Face ID...)";
                            }
                            return;
                        }

                        IsTeacher = false; // Bluetooth tag = student sign-in
                        IdentifiedAgeYears = null;
                        string deviceName = string.IsNullOrEmpty(newKey.DeviceName) ? newKey.MacKey : newKey.DeviceName;
                        FinishLogin(DialogResult.OK, "Welcome! " + deviceName + " connected.",
                            ProfileKeyForBluetooth(newKey));
                    }
                }
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void UpdateBluetoothLabel(List<PairedBluetoothDevice> devices, PairedBluetoothDevice signInDevice)
    {
        if (labelBluetooth == null) return;
        if (signInDevice != null)
        {
            labelBluetooth.Text = "Bluetooth: signing in via " + (signInDevice.DeviceName ?? signInDevice.MacKey);
            return;
        }
        int connected = 0;
        foreach (var d in devices)
            if (d != null && d.IsConnected) connected++;
        labelBluetooth.Text = connected == 0
            ? "Bluetooth: searching for a tag..."
            : "Bluetooth: " + connected + " device(s) seen. Bring a NEW tag close to sign in.";
    }

    private void InitializeComponent()
    {
            this.label1 = new System.Windows.Forms.Label();
            this.btnRadialGesture = new RoundedButton();
            this.btnScan = new RoundedButton();
            this.btnAdminPanel = new RoundedButton();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.BackColor = System.Drawing.Color.Transparent;
            this.label1.Cursor = System.Windows.Forms.Cursors.Hand;
            this.label1.Font = new System.Drawing.Font("Arial", 7F, System.Drawing.FontStyle.Bold);
            this.label1.ForeColor = System.Drawing.Color.White;
            this.label1.Location = new System.Drawing.Point(152, 217);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(134, 50);
            this.label1.TabIndex = 2;
            this.label1.Text = "Don\'t have an account? Sign up";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btnRadialGesture
            // 
            this.btnRadialGesture.BackColor = System.Drawing.Color.Transparent;
            this.btnRadialGesture.ButtonColor = System.Drawing.Color.DodgerBlue;
            this.btnRadialGesture.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRadialGesture.HoverColor = System.Drawing.Color.DeepSkyBlue;
            this.btnRadialGesture.Location = new System.Drawing.Point(125, 150);
            this.btnRadialGesture.Name = "btnRadialGesture";
            this.btnRadialGesture.Size = new System.Drawing.Size(200, 50);
            this.btnRadialGesture.TabIndex = 1;
            this.btnRadialGesture.Text = "Radial Menu (Gesture)";
            this.btnRadialGesture.Click += new System.EventHandler(this.btnRadialGesture_Click);
            // 
            // btnScan
            // 
            this.btnScan.BackColor = System.Drawing.Color.Transparent;
            this.btnScan.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.btnScan.ButtonColor = System.Drawing.Color.DodgerBlue;
            this.btnScan.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnScan.HoverColor = System.Drawing.Color.DeepSkyBlue;
            this.btnScan.Location = new System.Drawing.Point(125, 90);
            this.btnScan.Name = "btnScan";
            this.btnScan.Size = new System.Drawing.Size(200, 50);
            this.btnScan.TabIndex = 0;
            this.btnScan.Text = "Scan to Login";
            this.btnScan.Click += new System.EventHandler(this.btnScan_Click);
            // 
            // btnAdminPanel
            // 
            this.btnAdminPanel.BackColor = System.Drawing.Color.Transparent;
            this.btnAdminPanel.ButtonColor = System.Drawing.Color.DodgerBlue;
            this.btnAdminPanel.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAdminPanel.HoverColor = System.Drawing.Color.DeepSkyBlue;
            this.btnAdminPanel.Location = new System.Drawing.Point(125, 270);
            this.btnAdminPanel.Name = "btnAdminPanel";
            this.btnAdminPanel.Size = new System.Drawing.Size(200, 30);
            this.btnAdminPanel.TabIndex = 3;
            this.btnAdminPanel.Text = "Admin Panel";
            this.btnAdminPanel.Click += new System.EventHandler(this.btnAdminPanel_Click);
            // 
            // LoginForm
            // 
            this.ClientSize = new System.Drawing.Size(450, 340);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnRadialGesture);
            this.Controls.Add(this.btnScan);
            this.Controls.Add(this.btnAdminPanel);
            this.Name = "LoginForm";
            this.Text = "Login";
            this.ResumeLayout(false);

    }

    

    
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw kid-friendly gradient background
        Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);
        using (LinearGradientBrush brush = new LinearGradientBrush(rect, Color.FromArgb(80, 190, 255), Color.FromArgb(160, 100, 255), 45f))
        {
            g.FillRectangle(brush, rect);
        }

        // Draw cute cloud-like blobs in the background
        using (SolidBrush cloudBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
        {
            g.FillEllipse(cloudBrush, -50, -20, 200, 150);
            g.FillEllipse(cloudBrush, this.Width - 120, this.Height - 100, 180, 160);
            g.FillEllipse(cloudBrush, 50, this.Height - 60, 120, 100);
        }

        // Draw form border
        using (Pen pen = new Pen(Color.White, 6))
        {
            g.DrawRectangle(pen, 3, 3, this.Width - 6, this.Height - 6);
        }
    }

    private void LoginForm_MouseDown(object sender, MouseEventArgs e)
    {
        dragging = true;
        dragCursorPoint = Cursor.Position;
        dragFormPoint = this.Location;
    }

    private void LoginForm_MouseMove(object sender, MouseEventArgs e)
    {
        if (dragging)
        {
            Point dif = Point.Subtract(Cursor.Position, new Size(dragCursorPoint));
            this.Location = Point.Add(dragFormPoint, new Size(dif));
        }
    }

    private void LoginForm_MouseUp(object sender, MouseEventArgs e)
    {
        dragging = false;
    }

    private void btnRadialGesture_Click(object sender, EventArgs e)
    {
        var gc = new GestureSocketClient("127.0.0.1", 5000);
        try
        {
            gc.Connect();
            gc.Disconnect();
            lock (loginLock)
            {
                if (loginCompleted) return;
                UseRadialGestureMode = true;
                IsTeacher = true; // manual gesture login = teacher / unrestricted
                FinishLogin(DialogResult.OK, "Welcome, teacher!", "teacher:default");
            }
        }
        catch
        {
            MessageBox.Show("Could not connect to the Magic Hands server!\nMake sure 'python gesture_server.py' is running.", "Oops!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void btnAdminPanel_Click(object sender, EventArgs e)
    {
        var adminForm = new AdminPanelForm();
        adminForm.Show();
    }

    private void btnScan_Click(object sender, EventArgs e)
    {
        if (!isScanning)
        {
            btnScan.Text = "Show marker to camera!";
            isScanning = true;
            btnScan.Enabled = false;

            if (!client.isConnected())
            {
                client.connect();
            }
        }
    }

    // TuioListener implementation. Marker sign-in is held back while Face ID
    // is still loading; if Face ID fails, a previously-shown marker can complete the login.
    public void addTuioObject(TuioObject tobj)
    {
        if (isScanning && tobj.SymbolID >= 0 && tobj.SymbolID <= 7)
        {
            this.Invoke((MethodInvoker)delegate {
                lock (loginLock)
                {
                    if (loginCompleted) return;

                    if (faceLoginActive)
                    {
                        queuedMarkerSymbolId = tobj.SymbolID;
                        if (labelStatus != null)
                            labelStatus.Text = "Marker recognised - holding while Face ID tries to identify you...";
                        return;
                    }

                    isScanning = false;
                    IsTeacher = false; // marker scan keeps the student profile
                    FinishLogin(DialogResult.OK, "Logged in! Let's play!", "student:marker-" + tobj.SymbolID);
                }
            });
        }
        else if (isScanning)
        {
            this.Invoke((MethodInvoker)delegate {
                if (labelStatus != null && !loginCompleted && !faceLoginActive)
                    labelStatus.Text = "Hmm, I don't recognize that marker. Try another!";
            });
        }
    }

    public void updateTuioObject(TuioObject tobj) { }
    public void removeTuioObject(TuioObject tobj) { }
    public void addTuioCursor(TuioCursor tcur) { }
    public void updateTuioCursor(TuioCursor tcur) { }
    public void removeTuioCursor(TuioCursor tcur) { }
    public void addTuioBlob(TuioBlob tblb) { }
    public void updateTuioBlob(TuioBlob tblb) { }
    public void removeTuioBlob(TuioBlob tblb) { }
    public void refresh(TuioTime frameTime) { }
}

// Custom Rounded Button Class
public class RoundedButton : Control
{
    public Color ButtonColor { get; set; } = Color.DodgerBlue;
    public Color HoverColor { get; set; } = Color.DeepSkyBlue;
    private bool isHovered = false;
    private bool isPressed = false;

    public RoundedButton()
    {
        this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        this.BackColor = Color.Transparent;
        this.DoubleBuffered = true;
        this.Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        isHovered = true;
        this.Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        isHovered = false;
        this.Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        isPressed = true;
        this.Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        isPressed = false;
        this.Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color currentColor = isPressed ? Color.FromArgb(Math.Max(0, ButtonColor.R - 30), Math.Max(0, ButtonColor.G - 30), Math.Max(0, ButtonColor.B - 30)) : (isHovered ? HoverColor : ButtonColor);

        int radius = 25;
        Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();

        // Drop shadow effect
        if (!isPressed)
        {
            Rectangle shadowRect = new Rectangle(0, 4, this.Width - 1, this.Height - 1);
            GraphicsPath shadowPath = new GraphicsPath();
            shadowPath.AddArc(shadowRect.X, shadowRect.Y, radius, radius, 180, 90);
            shadowPath.AddArc(shadowRect.Right - radius, shadowRect.Y, radius, radius, 270, 90);
            shadowPath.AddArc(shadowRect.Right - radius, shadowRect.Bottom - radius, radius, radius, 0, 90);
            shadowPath.AddArc(shadowRect.X, shadowRect.Bottom - radius, radius, radius, 90, 90);
            shadowPath.CloseFigure();
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
            {
                g.FillPath(shadowBrush, shadowPath);
            }
        }

        // Button body
        int yOffset = isPressed ? 4 : 0;
        using (Matrix m = new Matrix())
        {
            m.Translate(0, yOffset);
            g.Transform = m;
        }

        using (SolidBrush brush = new SolidBrush(currentColor))
        {
            g.FillPath(brush, path);
        }

        using (Pen pen = new Pen(Color.White, 3))
        {
            g.DrawPath(pen, path);
        }

        // Draw Text
        TextRenderer.DrawText(g, this.Text, this.Font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
