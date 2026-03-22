using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TUIO;

public class TuioDemo : Form, TuioListener
{
    private class TargetSlot
    {
        public int SymbolId;
        public string FruitName;
        public float XNormalized;
        public float YNormalized;
        public float WidthNormalized;
        public float HeightNormalized;
        public bool IsPlaced;
    }

    private class LevelDefinition
    {
        public string Name;
        public string BoardImageName;
        public List<TargetSlot> Targets = new List<TargetSlot>();
    }

    private TuioClient client;
    private readonly Dictionary<long, TuioObject> objectList = new Dictionary<long, TuioObject>(128);
    private readonly Dictionary<long, TuioCursor> cursorList = new Dictionary<long, TuioCursor>(128);
    private readonly Dictionary<long, TuioBlob> blobList = new Dictionary<long, TuioBlob>(128);
    private readonly Dictionary<int, Image> fruitImages = new Dictionary<int, Image>();
    private readonly Dictionary<int, Image> fruitImagesAlt = new Dictionary<int, Image>();
    private readonly Dictionary<string, Image> boardImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
    private readonly List<LevelDefinition> levels = new List<LevelDefinition>();
    private readonly List<int> levelScores = new List<int>();

    public static int width, height;
    private readonly int windowWidth = 1280;
    private readonly int windowHeight = 720;
    private int windowLeft;
    private int windowTop;
    private readonly int screenWidth = Screen.PrimaryScreen.Bounds.Width;
    private readonly int screenHeight = Screen.PrimaryScreen.Bounds.Height;

    private bool fullscreen;
    private bool verbose;
    private bool pendingLevelComplete;
    private int currentLevelIndex;
    private DateTime levelStartTime;
    private Image fallbackBackground;
    private dynamic fruitPlayer;

    private readonly Font smallFont = new Font("Arial", 12.0f, FontStyle.Bold);
    private readonly Font titleFont = new Font("Arial", 22.0f, FontStyle.Bold);
    private readonly SolidBrush whiteBrush = new SolidBrush(Color.White);
    private readonly SolidBrush darkOverlayBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));

    public TuioDemo(int port)
    {
        verbose = false;
        fullscreen = false;
        width = windowWidth;
        height = windowHeight;
        ClientSize = new Size(width, height);
        Name = "TuioDemo";
        Text = "Fruit Learning Game";

        Closing += Form_Closing;
        KeyDown += Form_KeyDown;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);

        try
        {
            Type wmpType = Type.GetTypeFromProgID("WMPlayer.OCX.7");
            if (wmpType != null)
            {
                fruitPlayer = Activator.CreateInstance(wmpType);
                fruitPlayer.settings.autoStart = false;
            }
        }
        catch
        {
            fruitPlayer = null;
        }

        LoadAssets();
        BuildLevels();
        StartLevel(0);

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();
    }

    private void BuildLevels()
    {
        levels.Clear();

        var level1 = new LevelDefinition
        {
            Name = "Level 1",
            BoardImageName = "level 1.png"
        };

        level1.Targets.Add(new TargetSlot { SymbolId = 0, FruitName = "Apple", XNormalized = 0.23f, YNormalized = 0.50f, WidthNormalized = 0.13f, HeightNormalized = 0.31f });
        level1.Targets.Add(new TargetSlot { SymbolId = 1, FruitName = "Banana", XNormalized = 0.75f, YNormalized = 0.50f, WidthNormalized = 0.18f, HeightNormalized = 0.26f });

        var level2 = new LevelDefinition
        {
            Name = "Level 2",
            BoardImageName = "level2.png"
        };
        level2.Targets.Add(new TargetSlot { SymbolId = 2, FruitName = "Strawberry", XNormalized = 0.11f, YNormalized = 0.50f, WidthNormalized = 0.14f, HeightNormalized = 0.23f });
        level2.Targets.Add(new TargetSlot { SymbolId = 3, FruitName = "Watermelon", XNormalized = 0.30f, YNormalized = 0.50f, WidthNormalized = 0.15f, HeightNormalized = 0.21f });
        level2.Targets.Add(new TargetSlot { SymbolId = 4, FruitName = "Mango", XNormalized = 0.50f, YNormalized = 0.45f, WidthNormalized = 0.14f, HeightNormalized = 0.23f });
        level2.Targets.Add(new TargetSlot { SymbolId = 5, FruitName = "Orange", XNormalized = 0.69f, YNormalized = 0.50f, WidthNormalized = 0.13f, HeightNormalized = 0.22f });
        level2.Targets.Add(new TargetSlot { SymbolId = 6, FruitName = "Kiwi", XNormalized = 0.88f, YNormalized = 0.50f, WidthNormalized = 0.13f, HeightNormalized = 0.22f });

        levels.Add(level1);
        levels.Add(level2);
    }

    private LevelDefinition CurrentLevel
    {
        get
        {
            if (currentLevelIndex >= 0 && currentLevelIndex < levels.Count)
                return levels[currentLevelIndex];
            return null;
        }
    }

    private void StartLevel(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= levels.Count) return;

        currentLevelIndex = levelIndex;
        pendingLevelComplete = false;
        foreach (var slot in CurrentLevel.Targets)
        {
            slot.IsPlaced = false;
        }

        levelStartTime = DateTime.Now;
    }

    private void LoadAssets()
    {
        fallbackBackground = LoadImageByBaseName("background");

        fruitImages[0] = LoadImageByBaseName("apple");
        {
            var tmp = LoadImageByBaseName("applecut");
            if (tmp != null)
                fruitImagesAlt[0] = tmp;
            else
                fruitImagesAlt[0] = fruitImages[0];
        }
        fruitImages[1] = LoadImageByBaseName("banana");
        {
            var tmp = LoadImageByBaseName("bananacut");
            if (tmp != null)
                fruitImagesAlt[1] = tmp;
            else
                fruitImagesAlt[1] = fruitImages[1];
        }
        fruitImages[2] = LoadImageByBaseName("straw");
        {
            var tmp = LoadImageByBaseName("strawcut");
            if (tmp != null)
                fruitImagesAlt[2] = tmp;
            else
                fruitImagesAlt[2] = fruitImages[2];
        }
        {
            var tmp = LoadImageByBaseName("watermelonwhole");
            if (tmp != null)
                fruitImages[3] = tmp;
            else
                fruitImages[3] = LoadImageByBaseName("watermelon");
        }
        {
            var tmp = LoadImageByBaseName("watermelon");
            if (tmp != null)
                fruitImagesAlt[3] = tmp;
            else
                fruitImagesAlt[3] = fruitImages[3];
        }
        fruitImages[4] = LoadImageByBaseName("mango");
        {
            var tmp = LoadImageByBaseName("mangocut");
            if (tmp != null)
                fruitImagesAlt[4] = tmp;
            else
                fruitImagesAlt[4] = fruitImages[4];
        }
        fruitImages[5] = LoadImageByBaseName("Orange");
        fruitImagesAlt[5] = fruitImages[5];
        {
            var tmp = LoadImageByBaseName("wholekiwi");
            if (tmp != null)
                fruitImages[6] = tmp;
            else
                fruitImages[6] = LoadImageByBaseName("kiwi");
        }
        {
            var tmp = LoadImageByBaseName("kiwi");
            if (tmp != null)
                fruitImagesAlt[6] = tmp;
            else
                fruitImagesAlt[6] = fruitImages[6];
        }

        var level1Board = LoadImageByExactName("level 1.png");
        if (level1Board == null)
            level1Board = LoadImageByExactName("level1.png");
        if (level1Board != null) boardImages["level 1.png"] = level1Board;

        var level2Board = LoadImageByExactName("level2.png");
        if (level2Board != null) boardImages["level2.png"] = level2Board;
    }

    private Image LoadImageByBaseName(string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return null;
        string[] extensions = { ".png", ".jpg", ".jpeg" };
        foreach (var ext in extensions)
        {
            var path = GetAssetsPath(baseName + ext);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return Image.FromFile(path);
            }
        }
        return null;
    }

    private Image LoadImageByExactName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        var path = GetAssetsPath(fileName);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return Image.FromFile(path);
        return null;
    }

    private string GetAssetsPath(string fileName)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
        if (File.Exists(path)) return path;

        string rootPath = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 4; i++)
        {
            rootPath = Path.GetDirectoryName(rootPath);
            if (string.IsNullOrEmpty(rootPath)) break;
            path = Path.Combine(rootPath, "Assets", fileName);
            if (File.Exists(path)) return path;
        }

        return string.Empty;
    }

    private void Form_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyData == Keys.F1)
        {
            if (!fullscreen)
            {
                width = screenWidth;
                height = screenHeight;
                windowLeft = Left;
                windowTop = Top;
                FormBorderStyle = FormBorderStyle.None;
                Left = 0;
                Top = 0;
                Width = screenWidth;
                Height = screenHeight;
                fullscreen = true;
            }
            else
            {
                width = windowWidth;
                height = windowHeight;
                FormBorderStyle = FormBorderStyle.Sizable;
                Left = windowLeft;
                Top = windowTop;
                Width = windowWidth;
                Height = windowHeight;
                fullscreen = false;
            }
        }
        else if (e.KeyData == Keys.Escape)
        {
            Close();
        }
        else if (e.KeyData == Keys.V)
        {
            verbose = !verbose;
        }
    }

    private void Form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (client != null)
        {
            client.removeTuioListener(this);
            client.disconnect();
        }
        Environment.Exit(0);
    }

    public void addTuioObject(TuioObject o)
    {
        lock (objectList)
        {
            objectList[o.SessionID] = o;
        }
        if (verbose) Console.WriteLine("add obj " + o.SymbolID + " (" + o.SessionID + ")");
        PlayFruitSound(o.SymbolID);
        EvaluateObjectPlacement(o);
    }

    public void updateTuioObject(TuioObject o)
    {
        EvaluateObjectPlacement(o);
        if (verbose) Console.WriteLine("set obj " + o.SymbolID + " (" + o.SessionID + ")");
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
        lock (cursorList) { cursorList[c.SessionID] = c; }
    }

    public void updateTuioCursor(TuioCursor c) { }

    public void removeTuioCursor(TuioCursor c)
    {
        lock (cursorList) { cursorList.Remove(c.SessionID); }
    }

    public void addTuioBlob(TuioBlob b)
    {
        lock (blobList) { blobList[b.SessionID] = b; }
    }

    public void updateTuioBlob(TuioBlob b) { }

    public void removeTuioBlob(TuioBlob b)
    {
        lock (blobList) { blobList.Remove(b.SessionID); }
    }

    public void refresh(TuioTime frameTime)
    {
        Invalidate();
    }

    private void EvaluateObjectPlacement(TuioObject o)
    {
        var level = CurrentLevel;
        if (level == null || pendingLevelComplete) return;

        foreach (var slot in level.Targets)
        {
            if (slot.IsPlaced || slot.SymbolId != o.SymbolID) continue;

            RectangleF slotRect = GetSlotBounds(slot);
            PointF markerPoint = new PointF(o.getScreenX(width), o.getScreenY(height));

            bool isRotated90 = Math.Abs(Math.Cos(o.Angle)) < 0.707;
            if (slotRect.Contains(markerPoint) && IsValidPlacementState(o.SymbolID, isRotated90))
            {
                slot.IsPlaced = true;
            }
        }

        if (level.Targets.All(t => t.IsPlaced))
        {
            pendingLevelComplete = true;
            BeginInvoke((MethodInvoker)HandleLevelCompleted);
        }
    }

    private void HandleLevelCompleted()
    {
        var elapsedSeconds = (DateTime.Now - levelStartTime).TotalSeconds;
        int score = CalculateLearningRate(CurrentLevel.Targets.Count, elapsedSeconds);
        levelScores.Add(score);

        MessageBox.Show(
            CurrentLevel.Name + " completed!\nTime: " + elapsedSeconds.ToString("0.0") + " sec\nLearning rate: " + score + "%",
            "Great job",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        int nextLevel = currentLevelIndex + 1;
        if (nextLevel < levels.Count)
        {
            StartLevel(nextLevel);
            return;
        }

        int average;
        if (levelScores.Count == 0)
            average = 0;
        else
            average = (int)Math.Round(levelScores.Average());
        MessageBox.Show(
            "All levels completed!\nAverage learning rate: " + average + "%\n\nGame will restart from Level 1.",
            "Finished",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        levelScores.Clear();
        StartLevel(0);
    }

    private int CalculateLearningRate(int fruitCount, double elapsedSeconds)
    {
        double expectedSeconds = fruitCount * 10.0;
        double safeElapsed = Math.Max(1.0, elapsedSeconds);
        double percentage = (expectedSeconds / safeElapsed) * 100.0;
        if (percentage > 100.0) percentage = 100.0;
        if (percentage < 0.0) percentage = 0.0;
        return (int)Math.Round(percentage);
    }

    private void PlayFruitSound(int symbolId)
    {
        string soundFile;
        switch (symbolId)
        {
            case 0: soundFile = "apple.mp3"; break;
            case 1: soundFile = "banana.mp3"; break;
            case 2: soundFile = "straw.mp3"; break;
            case 3: soundFile = "watermelon.mp3"; break;
            case 4: soundFile = "mango.mp3"; break;
            case 5: soundFile = "orange.mp3"; break;
            case 6: soundFile = "kiwi.mp3"; break;
            default: return;
        }

        string fullPath = GetAssetsPath(soundFile);
        if (string.IsNullOrEmpty(fullPath) || fruitPlayer == null) return;

        try
        {
            fruitPlayer.controls.stop();
            fruitPlayer.URL = fullPath;
            fruitPlayer.controls.play();
        }
        catch
        {
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.Clear(Color.Black);

        Image board = null;
        var level = CurrentLevel;
        if (level != null && boardImages.ContainsKey(level.BoardImageName))
        {
            board = boardImages[level.BoardImageName];
        }
        else
        {
            board = fallbackBackground;
        }

        if (board != null)
        {
            g.DrawImage(board, new Rectangle(0, 0, width, height));
        }
        else
        {
            g.FillRectangle(new SolidBrush(Color.FromArgb(240, 240, 40)), new Rectangle(0, 0, width, height));
        }

        DrawTargetZones(g);
        DrawPlacedFruits(g);
        DrawObjects(g);
        DrawHud(g);
    }

    private void DrawTargetZones(Graphics g)
    {
        if (CurrentLevel == null) return;

        foreach (var slot in CurrentLevel.Targets)
        {
            float tx = slot.XNormalized * width;
            float ty = slot.YNormalized * height;
            RectangleF slotRect = GetSlotBounds(slot);

            Color ringColor;
            if (slot.IsPlaced)
                ringColor = Color.LimeGreen;
            else
                ringColor = Color.White;
            using (var pen = new Pen(ringColor, 3))
            {
                g.DrawRectangle(pen, slotRect.X, slotRect.Y, slotRect.Width, slotRect.Height);
            }

            string checkSuffix;
            if (slot.IsPlaced)
                checkSuffix = " ✓";
            else
                checkSuffix = "";
            string status = slot.FruitName + checkSuffix;
            SizeF labelSize = g.MeasureString(status, smallFont);
            g.FillRectangle(darkOverlayBrush, tx - labelSize.Width / 2 - 6, slotRect.Bottom + 4, labelSize.Width + 12, labelSize.Height + 4);
            g.DrawString(status, smallFont, whiteBrush, tx - labelSize.Width / 2, slotRect.Bottom + 6);
        }
    }

    private void DrawPlacedFruits(Graphics g)
    {
        if (CurrentLevel == null) return;

        foreach (var slot in CurrentLevel.Targets)
        {
            if (!slot.IsPlaced) continue;
            Image placedImage;
            if (slot.SymbolId == 3 || slot.SymbolId == 6)
            {
                if (fruitImagesAlt.ContainsKey(slot.SymbolId))
                    placedImage = fruitImagesAlt[slot.SymbolId];
                else
                    placedImage = null;
            }
            else
            {
                if (fruitImages.ContainsKey(slot.SymbolId))
                    placedImage = fruitImages[slot.SymbolId];
                else
                    placedImage = null;
            }
            if (placedImage == null) continue;

            RectangleF slotRect = GetSlotBounds(slot);
            Rectangle drawRect = Rectangle.Round(slotRect);

            // Draw the colored fruit over the silhouette area once correctly placed.
            g.DrawImage(placedImage, drawRect);
        }
    }

    private RectangleF GetSlotBounds(TargetSlot slot)
    {
        float centerX = slot.XNormalized * width;
        float centerY = slot.YNormalized * height;
        float slotWidth = slot.WidthNormalized * width;
        float slotHeight = slot.HeightNormalized * height;
        return new RectangleF(centerX - (slotWidth / 2f), centerY - (slotHeight / 2f), slotWidth, slotHeight);
    }

    private void DrawObjects(Graphics g)
    {
        HashSet<int> activeIds = new HashSet<int>(CurrentLevel.Targets.Select(t => t.SymbolId));

        lock (objectList)
        {
            foreach (TuioObject tobj in objectList.Values)
            {
                if (!activeIds.Contains(tobj.SymbolID)) continue;
                if (CurrentLevel.Targets.Any(t => t.SymbolId == tobj.SymbolID && t.IsPlaced)) continue;

                int ox = tobj.getScreenX(width);
                int oy = tobj.getScreenY(height);
                bool isRotated90 = Math.Abs(Math.Cos(tobj.Angle)) < 0.707;

                Image imgToDraw = null;
                if (UseAlternateImageForSymbol(tobj.SymbolID, isRotated90) && fruitImagesAlt.ContainsKey(tobj.SymbolID))
                {
                    imgToDraw = fruitImagesAlt[tobj.SymbolID];
                }
                else if (fruitImages.ContainsKey(tobj.SymbolID))
                {
                    imgToDraw = fruitImages[tobj.SymbolID];
                }

                if (imgToDraw != null)
                {
                    int imageSize = height / 4;
                    g.FillEllipse(new SolidBrush(Color.FromArgb(110, 255, 255, 255)), ox - imageSize / 2 - 12, oy - imageSize / 2 - 12, imageSize + 24, imageSize + 24);
                    g.DrawImage(imgToDraw, new Rectangle(ox - imageSize / 2, oy - imageSize / 2, imageSize, imageSize));
                }
                else
                {
                    int size = height / 9;
                    g.FillRectangle(Brushes.DarkRed, new Rectangle(ox - size / 2, oy - size / 2, size, size));
                    g.DrawString(tobj.SymbolID.ToString(), smallFont, Brushes.White, new PointF(ox - 10, oy - 10));
                }
            }
        }
    }

    private bool UseAlternateImageForSymbol(int symbolId, bool isRotated90)
    {
        if (!isRotated90) return false;
        return fruitImagesAlt.ContainsKey(symbolId) && fruitImages.ContainsKey(symbolId) && fruitImagesAlt[symbolId] != null;
    }

    private bool IsValidPlacementState(int symbolId, bool isRotated90)
    {
        // Kiwi (6) and watermelon (3) are valid only in cut/rotated state.
        if (symbolId == 3 || symbolId == 6) return isRotated90;

        // Other fruits must be whole (not rotated to cut state) to be accepted.
        return !isRotated90;
    }

    private void DrawHud(Graphics g)
    {
        if (CurrentLevel == null) return;

        int placedCount = CurrentLevel.Targets.Count(t => t.IsPlaced);
        int totalCount = CurrentLevel.Targets.Count;
        double elapsed = (DateTime.Now - levelStartTime).TotalSeconds;

        string line1 = CurrentLevel.Name + "  |  Progress: " + placedCount + "/" + totalCount;
        string line2 = "Time: " + elapsed.ToString("0.0") + " sec  |  Place fruits in the matching silhouettes";
        string line3;
        if (currentLevelIndex == 0)
            line3 = "Level 1 markers: Apple=0, Banana=1";
        else
            line3 = "Level 2 markers: Strawberry=2, Watermelon=3, Mango=4, Orange=5, Kiwi=6";

        g.FillRectangle(darkOverlayBrush, 10, 10, width - 20, 88);
        g.DrawString(line1, titleFont, whiteBrush, 20, 14);
        g.DrawString(line2, smallFont, whiteBrush, 20, 48);
        g.DrawString(line3, smallFont, whiteBrush, 20, 68);
    }

    private void InitializeComponent()
    {
            this.SuspendLayout();
            // 
            // TuioDemo
            // 
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Name = "TuioDemo";
            this.Load += new System.EventHandler(this.TuioDemo_Load);
            this.ResumeLayout(false);

    }

    private void TuioDemo_Load(object sender, EventArgs e)
    {

    }

    public static void Main(string[] argv)
    {
        int port;
        switch (argv.Length)
        {
            case 1:
                port = int.Parse(argv[0], null);
                if (port == 0)
                {
                    Console.WriteLine("usage: mono TuioDemo [port]");
                    return;
                }
                break;
            case 0:
                port = 3333;
                break;
            default:
                Console.WriteLine("usage: mono TuioDemo [port]");
                return;
        }

        LoginForm login = new LoginForm(port);
        if (login.ShowDialog() == DialogResult.OK)
        {
            Application.Run(new TuioDemo(port));
        }
    }
}
