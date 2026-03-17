/*
	TUIO C# Demo - part of the reacTIVision project
	Copyright (c) 2005-2016 Martin Kaltenbrunner <martin@tuio.org>

	This program is free software; you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation; either version 2 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program; if not, write to the Free Software
	Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
*/

using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using TUIO;
using System.IO;

public class TuioDemo : Form, TuioListener
{
    private TuioClient client;
    private readonly Dictionary<long, TuioObject> objectList;
    private readonly Dictionary<long, TuioCursor> cursorList;
    private readonly Dictionary<long, TuioBlob> blobList;

    public static int width, height;
    private readonly int window_width = 640;
    private readonly int window_height = 480;
    private int window_left = 0;
    private int window_top = 0;
    private readonly int screen_width = Screen.PrimaryScreen.Bounds.Width;
    private readonly int screen_height = Screen.PrimaryScreen.Bounds.Height;

    private bool fullscreen;
    private bool verbose;

    readonly Font font = new Font("Arial", 10.0f);
    readonly SolidBrush fntBrush = new SolidBrush(Color.White);
    readonly SolidBrush bgrBrush = new SolidBrush(Color.White);
    readonly SolidBrush curBrush = new SolidBrush(Color.FromArgb(192, 0, 192));
    readonly SolidBrush objBrush = new SolidBrush(Color.FromArgb(64, 0, 0));
    readonly SolidBrush blbBrush = new SolidBrush(Color.FromArgb(64, 64, 64));
    readonly Pen curPen = new Pen(new SolidBrush(Color.Blue), 1);

    // Media player used to play fruit sounds
    private dynamic fruitPlayer;
    private Image backgroundImage;
    private List<Image> fruitImages = new List<Image>(); 
    private List<Image> fruitImagesAlt = new List<Image>(); 

    private Image LoadFruitImage(string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return null;

        string p1 = GetAssetsPath(baseName);
        if (File.Exists(p1)) return Image.FromFile(p1);

        string[] exts = { ".png", ".jpg", ".jpeg" };
        foreach (var ext in exts)
        {
            string p2 = GetAssetsPath(baseName + ext);
            if (File.Exists(p2)) return Image.FromFile(p2);
        }
        return null;
    }

    private void LoadAssets()
    {
        string bgPath = GetAssetsPath("background.jpg");
        if (File.Exists(bgPath)) backgroundImage = Image.FromFile(bgPath);

        for (int i = 0; i <= 7; i++)
        {
            string name1 = string.Empty;
            string name2 = string.Empty;
            switch (i)
            {
                case 0: name1 = "apple"; name2 = "applecut"; break;
                case 1: name1 = "banana"; name2 = "bananacut"; break;
                case 2: name1 = "straw"; name2 = "strawcut"; break;
                case 3: name1 = "watermelonwhole"; name2 = "watermelon"; break;
                case 4: name1 = "mango"; name2 = "mangocut"; break;
                case 5: name1 = "Orange"; name2 = "Orange"; break;
                case 6: name1 = "wholekiwi"; name2 = "kiwi"; break;
                case 7: name1 = "straw"; name2 = "strawcut"; break;
            }

            Image primary = LoadFruitImage(name1);
            Image alternate = LoadFruitImage(name2);

          
            if (primary == null)
            {
                if (i == 3) primary = LoadFruitImage("watermelon");
                if (i == 6) primary = LoadFruitImage("kiwi");
            }
            if (alternate == null)
            {
                alternate = primary; // Default to primary if alternate image doesn't exist
            }

            fruitImages.Add(primary);
            fruitImagesAlt.Add(alternate);
        }
    }

    private string GetAssetsPath(string fileName)
    {
        // Try to find the file in the current directory
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
        if (File.Exists(path)) return path;

        // Try to find the file in the project root (going up from bin/Debug or bin/Release)
        string rootPath = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 3; i++) // Try up to 3 levels up
        {
            rootPath = Path.GetDirectoryName(rootPath);
            if (string.IsNullOrEmpty(rootPath)) break;
            path = Path.Combine(rootPath, "Assets", fileName);
            if (File.Exists(path)) return path;
        }

        return string.Empty; // Not found
    }

    public TuioDemo(int port)
    {

        verbose = false;
        fullscreen = false;
        width = window_width;
        height = window_height;
        this.ClientSize = new Size(width, height);
        this.Name = "TuioDemo";
        this.Text = "TuioDemo";

        this.Closing += (sender, e) => Form_Closing(sender, e);
        this.KeyDown += (sender, e) => Form_KeyDown(sender, e);

        this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                        ControlStyles.UserPaint |
                        ControlStyles.DoubleBuffer, true);

        objectList = new Dictionary<long, TuioObject>(128);
        cursorList = new Dictionary<long, TuioCursor>(128);
        blobList = new Dictionary<long, TuioBlob>(128);

        // Initialize media player for fruit sounds (MP3) using late binding to avoid WMPLib dependency
        try {
            Type wmpType = Type.GetTypeFromProgID("WMPlayer.OCX.7");
            if (wmpType != null) {
                fruitPlayer = Activator.CreateInstance(wmpType);
                fruitPlayer.settings.autoStart = false;
            }
        } catch {
            fruitPlayer = null;
        }

        LoadAssets(); // Cache images for fast and responsive rendering

        client = new TuioClient(port);
        client.addTuioListener(this);

        client.connect();
    }

    private void Form_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
    {

        if (e.KeyData == Keys.F1)
        {
            if (fullscreen == false)
            {

                width = screen_width;
                height = screen_height;

                window_left = this.Left;
                window_top = this.Top;

                this.FormBorderStyle = FormBorderStyle.None;
                this.Left = 0;
                this.Top = 0;
                this.Width = screen_width;
                this.Height = screen_height;

                fullscreen = true;
            }
            else
            {

                width = window_width;
                height = window_height;

                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.Left = window_left;
                this.Top = window_top;
                this.Width = window_width;
                this.Height = window_height;

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

    private void Form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        client.removeTuioListener(this);

        client.disconnect();
        System.Environment.Exit(0);
    }

    public void addTuioObject(TuioObject o)
    {
        lock (objectList)
        {
            objectList.Add(o.SessionID, o);
        }
        if (verbose) Console.WriteLine("add obj " + o.SymbolID + " (" + o.SessionID + ") " + o.X + " " + o.Y + " " + o.Angle);

        // Play sound once when a new object (fruit) appears
        PlayFruitSound(o.SymbolID);
    }

    public void updateTuioObject(TuioObject o)
    {

        if (verbose) Console.WriteLine("set obj " + o.SymbolID + " " + o.SessionID + " " + o.X + " " + o.Y + " " + o.Angle + " " + o.MotionSpeed + " " + o.RotationSpeed + " " + o.MotionAccel + " " + o.RotationAccel);
    }

    public void removeTuioObject(TuioObject o)
    {
        lock (objectList)
        {
            objectList.Remove(o.SessionID);
        }
        if (verbose) Console.WriteLine("del obj " + o.SymbolID + " (" + o.SessionID + ")");
    }

    public void addTuioCursor(TuioCursor c)
    {
        lock (cursorList)
        {
            cursorList.Add(c.SessionID, c);
        }
        if (verbose) Console.WriteLine("add cur " + c.CursorID + " (" + c.SessionID + ") " + c.X + " " + c.Y);
    }

    public void updateTuioCursor(TuioCursor c)
    {
        if (verbose) Console.WriteLine("set cur " + c.CursorID + " (" + c.SessionID + ") " + c.X + " " + c.Y + " " + c.MotionSpeed + " " + c.MotionAccel);
    }

    public void removeTuioCursor(TuioCursor c)
    {
        lock (cursorList)
        {
            cursorList.Remove(c.SessionID);
        }
        if (verbose) Console.WriteLine("del cur " + c.CursorID + " (" + c.SessionID + ")");
    }

    public void addTuioBlob(TuioBlob b)
    {
        lock (blobList)
        {
            blobList.Add(b.SessionID, b);
        }
        if (verbose) Console.WriteLine("add blb " + b.BlobID + " (" + b.SessionID + ") " + b.X + " " + b.Y + " " + b.Angle + " " + b.Width + " " + b.Height + " " + b.Area);
    }

    public void updateTuioBlob(TuioBlob b)
    {

        if (verbose) Console.WriteLine("set blb " + b.BlobID + " (" + b.SessionID + ") " + b.X + " " + b.Y + " " + b.Angle + " " + b.Width + " " + b.Height + " " + b.Area + " " + b.MotionSpeed + " " + b.RotationSpeed + " " + b.MotionAccel + " " + b.RotationAccel);
    }

    public void removeTuioBlob(TuioBlob b)
    {
        lock (blobList)
        {
            blobList.Remove(b.SessionID);
        }
        if (verbose) Console.WriteLine("del blb " + b.BlobID + " (" + b.SessionID + ")");
    }

    public void refresh(TuioTime frameTime)
    {
        Invalidate();
    }

    /// <summary>
    /// Plays the corresponding fruit sound (MP3) for a given symbol ID.
    /// Expects files like "apple.mp3", "banana.mp3", etc. in the current directory.
    /// </summary>
    /// <param name="symbolId">The TUIO object SymbolID.</param>
    private void PlayFruitSound(int symbolId)
    {
        string soundFile;

        switch (symbolId)
        {
            case 0:
                soundFile = "apple.mp3";
                break;
            case 1:
                soundFile = "banana.mp3";
                break;
            case 2:
                soundFile = "straw.mp3";
                break;
            case 3:
                soundFile = "watermelon.mp3";
                break;
            case 4:
                soundFile = "mango.mp3";
                break;
            case 5:
                soundFile = "orange.mp3";
                break;
            case 6:
                soundFile = "kiwi.mp3";
                break;
            case 7:
                soundFile = "straw.mp3"; // Placeholder for 7
                break;
            default:
                return; // no sound for other IDs
        }

        // Look for sounds inside an "Assets" folder next to the project
        string fullPath = GetAssetsPath(soundFile);
        if (string.IsNullOrEmpty(fullPath)) {
            Console.WriteLine("Sound file not found: " + soundFile);
            return;
        }
        if (fruitPlayer == null) {
            Console.WriteLine("fruitPlayer is null");
            return;
        }

        try
        {
            Console.WriteLine("Playing sound: " + fullPath);
            // Stop any currently playing sound, set new file, and play
            fruitPlayer.controls.stop();
            fruitPlayer.URL = fullPath;
            fruitPlayer.controls.play();
        }
        catch
        {
            // swallow exceptions – you can add logging here if needed
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Getting the graphics object
        Graphics g = pevent.Graphics;
        g.FillRectangle(bgrBrush, new Rectangle(0, 0, width, height));

        // Draw global background image (Black.jpeg) if it exists
        if (backgroundImage != null)
        {
            g.DrawImage(backgroundImage, new Rectangle(0, 0, width, height));
        }

        // --- INTERACTIVE UI ELEMENTS ---
        if (objectList.Count == 0)
        {
            string prompt = "Hold a fruit!";
            Font titleFont = new Font("Arial", 28.0f, FontStyle.Bold);
            SizeF textSize = g.MeasureString(prompt, titleFont);

            // Draw a translucent box behind the text
            g.FillRectangle(new SolidBrush(Color.FromArgb(150, 0, 0, 0)), 
                (width - textSize.Width) / 2 - 20, (height - textSize.Height) / 2 - 10, 
                textSize.Width + 40, textSize.Height + 20);

            g.DrawString(prompt, titleFont, Brushes.Yellow, new PointF((width - textSize.Width) / 2, (height - textSize.Height) / 2));
        }

        // draw the cursor path
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
                    g.DrawString(tcur.CursorID + "", font, fntBrush, new PointF(tcur.getScreenX(width) - 10, tcur.getScreenY(height) - 10));
                }
            }
        }

        // draw the objects
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

                    // Determine if the marker is rotated approximately 90 or 270 degrees
                    // Cosine of 90/270 degrees is 0. If it falls below ~0.707 (45 degrees), we treat it as rotated.
                    bool isRotated90 = Math.Abs(Math.Cos(tobj.Angle)) < 0.707;
                    Image imgToDraw = null;
                    if (tobj.SymbolID >= 0 && tobj.SymbolID < fruitImages.Count)
                    {
                        imgToDraw = isRotated90 ? fruitImagesAlt[(int)tobj.SymbolID] : fruitImages[(int)tobj.SymbolID];
                    }

                    // Interactive image rendering without using a dictionary lookup
                    if (imgToDraw != null)
                    {
                        int imageSize = height / 3; // Make images larger and more visible

                        // Draw a nice highlight frame behind the fruit
                        g.FillEllipse(new SolidBrush(Color.FromArgb(120, 255, 255, 255)), ox - imageSize / 2 - 15, oy - imageSize / 2 - 15, imageSize + 30, imageSize + 30);
                        g.DrawEllipse(new Pen(Color.LimeGreen, 4), ox - imageSize / 2 - 15, oy - imageSize / 2 - 15, imageSize + 30, imageSize + 30);

                        // Draw the cached image
                        g.DrawImage(imgToDraw, new Rectangle(ox - imageSize / 2, oy - imageSize / 2, imageSize, imageSize));

                        // Get the name of the fruit using a switch statement instead of dictionary
                        string fName = string.Empty;
                        switch (tobj.SymbolID)
                        {
                            case 0: fName = "Apple"; break;
                            case 1: fName = "Banana"; break;
                            case 2: fName = "Strawberry"; break;
                            case 3: fName = "Watermelon"; break;
                            case 4: fName = "Mango"; break;
                            case 5: fName = "Orange"; break;
                            case 6: fName = "Kiwi"; break;
                            case 7: fName = "Strawberry"; break;
                        }

                        if (!string.IsNullOrEmpty(fName))
                        {
                            Font fruitFont = new Font("Arial", 20.0f, FontStyle.Bold);
                            SizeF nameSize = g.MeasureString(fName, fruitFont);

                            // Draw nice background for text
                            g.FillRectangle(new SolidBrush(Color.FromArgb(180, 0, 0, 0)), ox - nameSize.Width / 2 - 10, oy + imageSize / 2 + 10, nameSize.Width + 20, nameSize.Height + 10);
                            g.DrawString(fName, fruitFont, Brushes.LightGreen, new PointF(ox - nameSize.Width / 2, oy + imageSize / 2 + 15));
                        }
                    }
                }
            }
        }

        // draw the blobs
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

                    g.TranslateTransform(bx, by);
                    g.RotateTransform((float)(tblb.Angle / Math.PI * 180.0f));
                    g.TranslateTransform(-bx, -by);

                    g.FillEllipse(blbBrush, bx - bw / 2, by - bh / 2, bw, bh);

                    g.TranslateTransform(bx, by);
                    g.RotateTransform(-1 * (float)(tblb.Angle / Math.PI * 180.0f));
                    g.TranslateTransform(-bx, -by);

                    g.DrawString(tblb.BlobID + "", font, fntBrush, new PointF(bx, by));
                }
            }
        }
    }

    public static void Main(String[] argv)
    {
        int port = 0;
        switch (argv.Length)
        {
            case 1:
                port = int.Parse(argv[0], null);
                if (port == 0) goto default;
                break;
            case 0:
                port = 3333;
                break;
            default:
                Console.WriteLine("usage: mono TuioDemo [port]");
                System.Environment.Exit(0);
                break;
        }

        LoginForm login = new LoginForm(port);
        if (login.ShowDialog() == DialogResult.OK)
        {
            Application.Run(new TuioDemo(port));
        }
    }
}
