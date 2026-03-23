using System;
using System.Drawing;
using System.Windows.Forms;
using TUIO;
using GestureClient;

public class LoginForm : Form, TuioListener
{
    private TuioClient client;
    private Button btnScan;
    private Button btnRadialGesture;
    private Label label1;
    private bool isScanning = false;
    private int port;

    public bool UseRadialGestureMode { get; private set; }

    public LoginForm(int port)
    {
        this.port = port;
        InitializeComponent();
        
        client = new TuioClient(port);
        client.addTuioListener(this);
    }

    private void InitializeComponent()
    {
        this.btnScan = new Button();
        this.btnRadialGesture = new Button();
        this.label1 = new Label();
        this.SuspendLayout();

        // btnScan
        this.btnScan.Location = new Point(75, 40);
        this.btnScan.Name = "btnScan";
        this.btnScan.Size = new Size(200, 50);
        this.btnScan.TabIndex = 0;
        this.btnScan.Text = "Scan to Login";
        this.btnScan.UseVisualStyleBackColor = true;
        this.btnScan.Click += new EventHandler(this.btnScan_Click);

        // btnRadialGesture
        this.btnRadialGesture.Location = new Point(75, 100);
        this.btnRadialGesture.Name = "btnRadialGesture";
        this.btnRadialGesture.Size = new Size(200, 50);
        this.btnRadialGesture.TabIndex = 1;
        this.btnRadialGesture.Text = "Radial Menu (Gesture)";
        this.btnRadialGesture.UseVisualStyleBackColor = true;
        this.btnRadialGesture.Click += new EventHandler(this.btnRadialGesture_Click);

        // label1
        this.label1.AutoSize = true;
        this.label1.Location = new Point(75, 165);
        this.label1.Name = "label1";
        this.label1.Size = new Size(200, 20);
        this.label1.TabIndex = 2;
        this.label1.Text = "Don't have an account? Signup";
        this.label1.TextAlign = ContentAlignment.MiddleCenter;

        // LoginForm
        this.ClientSize = new Size(350, 240);
        this.Controls.Add(this.label1);
        this.Controls.Add(this.btnRadialGesture);
        this.Controls.Add(this.btnScan);
        this.Name = "LoginForm";
        this.Text = "Login";
        this.ResumeLayout(false);
        this.PerformLayout();
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
            MessageBox.Show("Could not connect to gesture server.\nStart: python gesture_server.py\n(Requires gesture_templates.json in project root)", "Radial Gesture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void btnScan_Click(object sender, EventArgs e)
    {
        if (!isScanning)
        {
            btnScan.Text = "Scanning...";
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
        // Only login if the marker ID is between 0 and 7
        if (isScanning && tobj.SymbolID >= 0 && tobj.SymbolID <= 7)
        {
            this.Invoke((MethodInvoker)delegate {
                btnScan.Text = "Logged in (ID: " + tobj.SymbolID + ")!";
                isScanning = false;
                client.removeTuioListener(this);
                client.disconnect();
                
                // Signal success and close the form
                this.DialogResult = DialogResult.OK;
                this.Close();
            });
        }
        else if (isScanning)
        {
            // Optional: You could update the button text to show an invalid marker was scanned
            this.Invoke((MethodInvoker)delegate {
                btnScan.Text = "Invalid Marker (" + tobj.SymbolID + ")";
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
