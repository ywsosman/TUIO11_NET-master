using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TUIO;
using GestureClient;

public class LoginForm : Form, TuioListener
{
    private TuioClient client;
    private RoundedButton btnScan;
    private RoundedButton btnRadialGesture;
    private Label labelTitle;
    private Label labelStatus;
    private Button btnClose;
    private bool isScanning = false;
    private int port;

    // For dragging the frameless window
    private bool dragging = false;
    private Point dragCursorPoint;
    private Point dragFormPoint;

    public bool UseRadialGestureMode { get; private set; }

    public LoginForm(int port)
    {
        this.port = port;
        InitializeComponent();
        
        client = new TuioClient(port);
        client.addTuioListener(this);

        // Auto-start scanning and gesture mode
        UseRadialGestureMode = true;
        isScanning = true;
        
        btnScan.Visible = false;
        btnRadialGesture.Visible = false;
        
        labelStatus.Text = "Please show your marker to the camera!";
        labelStatus.Font = new Font("Comic Sans MS", 16F, FontStyle.Bold);
        labelStatus.Size = new Size(500, 40);
        labelStatus.Location = new Point(0, 150);

        try 
        {
            if (!client.isConnected())
                client.connect();
        } 
        catch { }
    }

    private void InitializeComponent()
    {
        this.btnScan = new RoundedButton();
        this.btnRadialGesture = new RoundedButton();
        this.labelTitle = new Label();
        this.labelStatus = new Label();
        this.btnClose = new Button();
        this.SuspendLayout();

        // Form settings
        this.FormBorderStyle = FormBorderStyle.None;
        this.ClientSize = new Size(500, 320);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.White;
        this.DoubleBuffered = true;

        // Window dragging events
        this.MouseDown += LoginForm_MouseDown;
        this.MouseMove += LoginForm_MouseMove;
        this.MouseUp += LoginForm_MouseUp;

        // Custom Close Button
        this.btnClose.Text = "X";
        this.btnClose.Font = new Font("Comic Sans MS", 14F, FontStyle.Bold);
        this.btnClose.ForeColor = Color.White;
        this.btnClose.BackColor = Color.FromArgb(255, 80, 80);
        this.btnClose.FlatStyle = FlatStyle.Flat;
        this.btnClose.FlatAppearance.BorderSize = 0;
        this.btnClose.Size = new Size(40, 40);
        this.btnClose.Location = new Point(455, 5); // 500 - 45
        this.btnClose.Cursor = Cursors.Hand;
        this.btnClose.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

        // Title Label
        this.labelTitle.Text = "Welcome to Fruit Learning!";
        this.labelTitle.Font = new Font("Comic Sans MS", 18F, FontStyle.Bold);
        this.labelTitle.ForeColor = Color.White;
        this.labelTitle.BackColor = Color.Transparent;
        this.labelTitle.AutoSize = false;
        this.labelTitle.Size = new Size(500, 40);
        this.labelTitle.TextAlign = ContentAlignment.MiddleCenter;
        this.labelTitle.Location = new Point(0, 30);
        this.labelTitle.MouseDown += LoginForm_MouseDown; // Allow dragging by title
        this.labelTitle.MouseMove += LoginForm_MouseMove;

        // Status Label
        this.labelStatus.Text = "Select how you want to play today!";
        this.labelStatus.Font = new Font("Comic Sans MS", 12F);
        this.labelStatus.ForeColor = Color.White;
        this.labelStatus.BackColor = Color.Transparent;
        this.labelStatus.AutoSize = false;
        this.labelStatus.Size = new Size(500, 30);
        this.labelStatus.TextAlign = ContentAlignment.MiddleCenter;
        this.labelStatus.Location = new Point(0, 75);

        // btnScan
        this.btnScan.Location = new Point(60, 140);
        this.btnScan.Name = "btnScan";
        this.btnScan.Size = new Size(380, 60);
        this.btnScan.TabIndex = 0;
        this.btnScan.Text = "Scan Marker to Login";
        this.btnScan.Font = new Font("Comic Sans MS", 14F, FontStyle.Bold);
        this.btnScan.ButtonColor = Color.FromArgb(100, 200, 100); // Kid friendly green
        this.btnScan.HoverColor = Color.FromArgb(120, 220, 120);
        this.btnScan.Click += new EventHandler(this.btnScan_Click);

        // btnRadialGesture
        this.btnRadialGesture.Location = new Point(60, 220);
        this.btnRadialGesture.Name = "btnRadialGesture";
        this.btnRadialGesture.Size = new Size(380, 60);
        this.btnRadialGesture.TabIndex = 1;
        this.btnRadialGesture.Text = "Play with Magic Hands";
        this.btnRadialGesture.Font = new Font("Comic Sans MS", 14F, FontStyle.Bold);
        this.btnRadialGesture.ButtonColor = Color.FromArgb(255, 160, 60); // Friendly orange
        this.btnRadialGesture.HoverColor = Color.FromArgb(255, 180, 80);
        this.btnRadialGesture.Click += new EventHandler(this.btnRadialGesture_Click);

        // Add controls
        this.Controls.Add(this.btnClose);
        this.Controls.Add(this.labelTitle);
        this.Controls.Add(this.labelStatus);
        this.Controls.Add(this.btnRadialGesture);
        this.Controls.Add(this.btnScan);
        this.ResumeLayout(false);
        this.PerformLayout();
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
            UseRadialGestureMode = true;
            client.removeTuioListener(this);
            if (client.isConnected())
                client.disconnect();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch
        {
            MessageBox.Show("Could not connect to the Magic Hands server!\nMake sure 'python gesture_server.py' is running.", "Oops!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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

    // TuioListener implementation
    public void addTuioObject(TuioObject tobj)
    {
        if (isScanning && tobj.SymbolID >= 0 && tobj.SymbolID <= 7)
        {
            this.Invoke((MethodInvoker)delegate {
                labelStatus.Text = "Logged in! Let's play!";
                isScanning = false;
                client.removeTuioListener(this);
                client.disconnect();
                
                this.DialogResult = DialogResult.OK;
                this.Close();
            });
        }
        else if (isScanning)
        {
            this.Invoke((MethodInvoker)delegate {
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
