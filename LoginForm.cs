using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GestureClient;
using TUIO;

public class LoginForm : Form, TuioListener
{
    private TuioClient client;
    private RoundedButton btnScan;
    private RoundedButton btnRadialGesture;
    private Label labelTitle;
    private Label labelStatus;
    private Label label1;
    private Button btnClose;
    private bool isScanning = false;
    private int port;
    private RoundedButton btnAdminPanel;
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
        
        labelStatus = new Label();
        labelStatus.BackColor = Color.Transparent;
        labelStatus.ForeColor = Color.White;
        labelStatus.TextAlign = ContentAlignment.MiddleCenter;
        labelStatus.Text = "Please show your marker to the camera!";
        labelStatus.Font = new Font("Comic Sans MS", 16F, FontStyle.Bold);
        labelStatus.Size = new Size(450, 40);
        labelStatus.Location = new Point(0, 150);
        this.Controls.Add(labelStatus);

        try 
        {
            if (!client.isConnected())
                client.connect();
        } 
        catch { }
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

    // TuioListener implementation
    public void addTuioObject(TuioObject tobj)
    {
        if (isScanning && tobj.SymbolID >= 0 && tobj.SymbolID <= 7)
        {
            this.Invoke((MethodInvoker)delegate {
                if (labelStatus != null) labelStatus.Text = "Logged in! Let's play!";
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
                if (labelStatus != null) labelStatus.Text = "Hmm, I don't recognize that marker. Try another!";
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
