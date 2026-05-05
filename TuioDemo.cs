using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Windows.Forms;
using TUIO;
using GestureClient;

public class TuioDemo : Form, TuioListener, IGestureListener
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
    private GestureSocketClient gestureClient;
    private bool radialGestureMode;
    private bool radialCursorFollowsGesture;
    private float gestureWristX = -1f, gestureWristY = -1f;

    // ── Emotion / difficulty state ────────────────────────────────────────────
    private string currentDifficultyHint = "normal";   // "easier", "harder", "normal"
    private string currentEmotionLabel   = "";
    private float  currentEmotionConf    = 0f;
    private DateTime emotionLastUpdate   = DateTime.MinValue;
    // Pulse animation tick counter for hint arrows
    private int    hintPulseTick         = 0;
    
    // --- Transition State ---
    private float levelTransitionOffset = 0f;
    private float levelTransitionAlpha = 0f;

    private readonly Dictionary<long, TuioObject> objectList = new Dictionary<long, TuioObject>(128);

    // --- Smooth Animation State ---
    private class VisualState
    {
        public float X;
        public float Y;
    }
    private readonly Dictionary<long, VisualState> visualStates = new Dictionary<long, VisualState>();
    private readonly Timer animationTimer = new Timer();
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
    private readonly Font radialFont = new Font("Arial", 14.0f, FontStyle.Bold);
    private readonly Timer radialTimer = new Timer();
    private readonly Dictionary<int, string> fruitNames = new Dictionary<int, string>();
    private readonly Dictionary<int, string> fruitColors = new Dictionary<int, string>();
    private readonly Dictionary<int, string> fruitBenefits = new Dictionary<int, string>();
    private readonly Dictionary<int, string> fruitColorAudio = new Dictionary<int, string>();
    private readonly List<string> radialLabels = new List<string>();
    private SpeechSynthesizer speech;
    private bool radialMenuOpen;
    private bool radialMuted;
    private string radialLayer = "none";
    private string radialSubMode = "";
    private string lastAudioKind = "";
    private string lastSpokenSentence = "";
    private string lastMp3Path = "";
    private Point radialCursorPoint = Point.Empty;
    private int radialHoveredIndex = -1;
    private DateTime radialHoverSince = DateTime.MinValue;
    private readonly int radialDwellMs = 500;
    private bool radialSelectionLocked;
    private DateTime radialLastActionAt = DateTime.MinValue;
    private readonly int radialRepeatDelayMs = 250;
    private bool speechIsPlaying;

    public TuioDemo(int port, bool useRadialGestureMode = false)
    {
        verbose = false;
        fullscreen = false;
        radialGestureMode = useRadialGestureMode;
        width = windowWidth;
        height = windowHeight;
        ClientSize = new Size(width, height);
        
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;

        Name = "TuioDemo";
        Text = "Fruit Learning Game";

        Button btnClose = new Button();
        btnClose.Text = "X";
        btnClose.Font = new Font("Comic Sans MS", 14F, FontStyle.Bold);
        btnClose.ForeColor = Color.White;
        btnClose.BackColor = Color.FromArgb(255, 80, 80);
        btnClose.FlatStyle = FlatStyle.Flat;
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Size = new Size(40, 40);
        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(Screen.PrimaryScreen.Bounds.Width - 45, 5);
        btnClose.Cursor = Cursors.Hand;
        btnClose.Click += (s, e) => { this.Close(); };
        this.Controls.Add(btnClose);

        Closing += Form_Closing;
        KeyDown += Form_KeyDown;
        MouseMove += Form_MouseMove;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);

        // Smooth Animation Loop (~60 FPS)
        animationTimer.Interval = 16;
        animationTimer.Tick += (s, e) => { this.Invalidate(); };
        animationTimer.Start();

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
        BuildRadialFruitData();
        InitializeRadialSpeech();
        BuildLevels();
        StartLevel(0);
        InitializeRadialMenu();

        // ── TUIO markers (always active – handles fruit placement) ────────────
        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        // ── Gesture client (always active – handles radial menu & emotion) ────
        gestureClient = new GestureSocketClient("127.0.0.1", 5000);
        gestureClient.AddListener(this);
        try
        {
            gestureClient.Connect();
            Console.WriteLine("[TuioDemo] Gesture server connected (port 5000)");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TuioDemo] Gesture server not available: " + ex.Message);
        }
    }

    public void OnSkeletonUpdate(double timestamp, IList<SkeletonLandmark> landmarks)
    {
        if (!radialGestureMode || landmarks == null) return;
        // Prefer index tip over wrist for cursor
        var wrist = landmarks.FirstOrDefault(l => l.Name == "right_wrist" || l.Name == "left_wrist" || l.Name == "right_index" || l.Name == "left_index");
        if (wrist != null && wrist.Visibility > 0.3f)
        {
            gestureWristX = wrist.X;
            gestureWristY = wrist.Y;
            // index cursor
            if (radialMenuOpen && !radialCursorFollowsGesture && gestureWristX >= 0 && gestureWristY >= 0)
            {
                radialCursorFollowsGesture = true;
            }
        }
        else
        {
            gestureWristX = -1f;
            gestureWristY = -1f;
        }
        if (radialMenuOpen && radialCursorFollowsGesture && gestureWristX >= 0 && gestureWristY >= 0)
        {
            int px = (int)(gestureWristX * width);
            int py = (int)(gestureWristY * height);
            radialCursorPoint = new Point(px, py);
        }
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke((MethodInvoker)(() => Invalidate()));
    }

    public void OnGestureRecognized(double timestamp, RecognizedGesture gesture)
    {
        if (!radialGestureMode || gesture == null) return;
        string g = gesture.Name.ToLowerInvariant();
        if (g == "pointer_up")
        {
            if (!radialMenuOpen)
                BeginInvoke((MethodInvoker)OpenRadialMenuLayer1);
            return;
        }
        if (g == "fist")
        {
            if (radialMenuOpen)
                BeginInvoke((MethodInvoker)CloseRadialMenu);
            return;
        }
        if (g == "open_hand")
        {
            if (radialMenuOpen)
            {
                radialCursorFollowsGesture = true;
                int cx = width / 2;
                int cy = height / 2;
                radialCursorPoint = new Point(cx, cy);
            }
            return;
        }
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke((MethodInvoker)(() => Invalidate()));
    }

    // ── Emotion / Difficulty ──────────────────────────────────────────────────

    public void OnEmotionUpdate(string label, float confidence, string difficultyHint)
    {
        // Called on the receive thread – marshal UI changes to the UI thread.
        currentEmotionLabel  = label ?? "";
        currentEmotionConf   = confidence;
        emotionLastUpdate    = DateTime.Now;
        ApplyDifficultyHint(difficultyHint);
    }

    /// <summary>
    /// Adjusts game difficulty based on the child's detected emotion.
    /// "easier"  → enlarge hitboxes 20% + draw hint arrows on unplaced targets.
    /// "harder"  → tighten hitboxes 10% (child is doing well).
    /// "normal"  → restore default hitboxes.
    /// </summary>
    private void ApplyDifficultyHint(string hint)
    {
        if (hint == currentDifficultyHint) return;
        currentDifficultyHint = hint;
        Console.WriteLine("[TuioDemo] Difficulty → " + hint);
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke((MethodInvoker)(() => Invalidate()));
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

        // Trigger animations
        levelTransitionOffset = this.ClientSize.Width > 0 ? this.ClientSize.Width : 1280;
        levelTransitionAlpha = 255f;
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
        else if (e.KeyData == Keys.O)
        {
            OpenRadialMenuLayer1();
        }
        else if (e.KeyData == Keys.X)
        {
            CloseRadialMenu();
        }
    }

    private void Form_MouseMove(object sender, MouseEventArgs e)
    {
        if (!radialGestureMode || !radialCursorFollowsGesture)
            radialCursorPoint = e.Location;
    }

    private void Form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        animationTimer.Stop();
        radialTimer.Stop();

        if (gestureClient != null)
        {
            gestureClient.RemoveListener(this);
            gestureClient.Disconnect();
        }

        if (speech != null)
        {
            try
            {
                speech.SpeakAsyncCancelAll();
                speech.Dispose();
            }
            catch
            {
            }
        }

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
        lock (visualStates)
        {
            visualStates.Remove(o.SessionID);
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
        // Rendering is now handled by the continuous 60 FPS animationTimer loop
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

        KidPopup.Show("Great job!", CurrentLevel.Name + " completed!\n\nTime: " + elapsedSeconds.ToString("0.0") + " sec\nLearning rate: " + score + "%");

        int nextLevel = currentLevelIndex + 1;
        if (nextLevel < levels.Count)
        {
            StartLevel(nextLevel);
            return;
        }

        int average = 0;
        if (levelScores.Count > 0)
            average = (int)Math.Round(levelScores.Average());
            
        KidPopup.Show("You're a Star!", "All levels completed!\nAverage learning rate: " + average + "%\n\nGame will restart from Level 1.");

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

    private void DrawPlayfulBackground(Graphics g, int w, int h)
    {
        // Sky Gradient
        using (System.Drawing.Drawing2D.LinearGradientBrush skyBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(0, 0, w, h), Color.FromArgb(135, 206, 235), Color.FromArgb(224, 255, 255), 90f))
        {
            g.FillRectangle(skyBrush, 0, 0, w, h);
        }

        // Fluffy Clouds
        using (SolidBrush cloudBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
        {
            g.FillEllipse(cloudBrush, w * 0.1f, h * 0.1f, w * 0.15f, h * 0.1f);
            g.FillEllipse(cloudBrush, w * 0.15f, h * 0.08f, w * 0.15f, h * 0.12f);
            g.FillEllipse(cloudBrush, w * 0.22f, h * 0.1f, w * 0.12f, h * 0.09f);

            g.FillEllipse(cloudBrush, w * 0.7f, h * 0.2f, w * 0.15f, h * 0.1f);
            g.FillEllipse(cloudBrush, w * 0.75f, h * 0.18f, w * 0.15f, h * 0.12f);
            g.FillEllipse(cloudBrush, w * 0.82f, h * 0.2f, w * 0.12f, h * 0.09f);
        }

        // Rolling Hills
        using (SolidBrush hillBrush = new SolidBrush(Color.FromArgb(144, 238, 144))) // LightGreen
        {
            g.FillEllipse(hillBrush, -w * 0.2f, h * 0.6f, w * 0.8f, h * 0.6f);
        }
        using (SolidBrush hillBrush2 = new SolidBrush(Color.FromArgb(152, 251, 152))) // PaleGreen
        {
            g.FillEllipse(hillBrush2, w * 0.4f, h * 0.55f, w * 0.8f, h * 0.6f);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Clear whole screen with sky color first so edges don't smear during slide
        g.Clear(Color.FromArgb(135, 206, 235));

        if (levelTransitionOffset > 0.5f)
            levelTransitionOffset += (0 - levelTransitionOffset) * 0.1f;
        else
            levelTransitionOffset = 0;

        if (levelTransitionAlpha > 0)
        {
            levelTransitionAlpha -= 8f;
        }

        // --- SLIDING GAME BOARD ---
        var state = g.Save();
        g.TranslateTransform(levelTransitionOffset, 0);

        // Draw playful scenery instead of a generic static board image
        DrawPlayfulBackground(g, this.ClientSize.Width, this.ClientSize.Height);

        // Update width/height to match client size for perfect responsiveness
        width = this.ClientSize.Width;
        height = this.ClientSize.Height;

        DrawTargetZones(g);
        DrawPlacedFruits(g);
        DrawObjects(g);

        g.Restore(state);
        // --------------------------

        // Draw white overlay for fade flash
        if (levelTransitionAlpha > 0)
        {
            int alpha = Math.Min(255, Math.Max(0, (int)levelTransitionAlpha));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            {
                g.FillRectangle(b, 0, 0, width, height);
            }
        }

        DrawHud(g);
        DrawRadialMenu(g);
    }

    private void BuildRadialFruitData()
    {
        fruitNames[0] = "Apple";
        fruitNames[1] = "Banana";
        fruitNames[2] = "Strawberry";
        fruitNames[3] = "Watermelon";
        fruitNames[4] = "Mango";
        fruitNames[5] = "Orange";
        fruitNames[6] = "Kiwi";

        fruitColors[0] = "Red";
        fruitColors[1] = "Yellow";
        fruitColors[2] = "Red";
        fruitColors[3] = "Green";
        fruitColors[4] = "Orange";
        fruitColors[5] = "Orange";
        fruitColors[6] = "Green";

        fruitBenefits[0] = "Apple helps keep you healthy.";
        fruitBenefits[1] = "Banana gives you energy.";
        fruitBenefits[2] = "Strawberry helps your skin stay healthy.";
        fruitBenefits[3] = "Watermelon helps you stay hydrated.";
        fruitBenefits[4] = "Mango helps your eyes stay strong.";
        fruitBenefits[5] = "Orange helps your body fight colds.";
        fruitBenefits[6] = "Kiwi helps your tummy feel happy.";

        fruitColorAudio[0] = "apple_color.mp3";
        fruitColorAudio[1] = "banana_color.mp3";
        fruitColorAudio[2] = "straw_color.mp3";
        fruitColorAudio[3] = "waterm_color.mp3";
        fruitColorAudio[4] = "mango_color.mp3";
        fruitColorAudio[5] = "orange_color.mp3";
        fruitColorAudio[6] = "kiwi_color.mp3";
    }

    private void InitializeRadialSpeech()
    {
        try
        {
            speech = new SpeechSynthesizer();
            speech.Rate = 0;
            speech.Volume = 100;
            speech.SpeakCompleted += Speech_SpeakCompleted;
        }
        catch
        {
            speech = null;
        }
    }

    private void Speech_SpeakCompleted(object sender, SpeakCompletedEventArgs e)
    {
        speechIsPlaying = false;
    }

    private void InitializeRadialMenu()
    {
        radialTimer.Interval = 33;
        radialTimer.Tick += RadialTimer_Tick;
        radialTimer.Start();
    }

    private void RadialTimer_Tick(object sender, EventArgs e)
    {
        if (!radialMenuOpen)
        {
            return;
        }

        int index = GetRadialSectorIndex(radialCursorPoint);
        if (index < 0)
        {
            radialHoveredIndex = -1;
            radialHoverSince = DateTime.MinValue;
            radialSelectionLocked = false;
            Invalidate();
            return;
        }

        if (index != radialHoveredIndex)
        {
            radialHoveredIndex = index;
            radialHoverSince = DateTime.Now;
            radialSelectionLocked = false;
            Invalidate();
            return;
        }

        if (radialHoverSince == DateTime.MinValue)
        {
            radialHoverSince = DateTime.Now;
            Invalidate();
            return;
        }

        double elapsed = (DateTime.Now - radialHoverSince).TotalMilliseconds;
        if (radialSelectionLocked)
        {
            if (CanRepeatCurrentHover(index))
            {
                double repeatElapsed = (DateTime.Now - radialLastActionAt).TotalMilliseconds;
                if (repeatElapsed >= radialRepeatDelayMs)
                {
                    radialLastActionAt = DateTime.Now;
                    ActivateRadialSelection(index);
                }
            }
            Invalidate();
            return;
        }

        if (elapsed >= radialDwellMs)
        {
            radialHoverSince = DateTime.Now;
            radialLastActionAt = DateTime.Now;
            radialSelectionLocked = true;
            ActivateRadialSelection(index);
        }
        Invalidate();
    }

    private bool CanRepeatCurrentHover(int index)
    {
        if (index < 0 || index >= radialLabels.Count)
        {
            return false;
        }
        if (radialMuted)
        {
            return false;
        }

        string label = radialLabels[index];
        if (radialLayer != "layer2")
        {
            return false;
        }

        if (radialSubMode == "colors")
        {
            if (label == "Back")
            {
                return false;
            }
            return !IsAnyAudioPlaying();
        }
        if (radialSubMode == "info")
        {
            if (label == "Back")
            {
                return false;
            }
            return !IsAnyAudioPlaying();
        }
        if (radialSubMode == "audio")
        {
            if (label == "Repeat Last Audio")
            {
                if (lastAudioKind == "tts" || lastAudioKind == "mp3")
                {
                    return !IsAnyAudioPlaying();
                }
            }
        }
        return false;
    }

    private bool IsAnyAudioPlaying()
    {
        if (speechIsPlaying)
        {
            return true;
        }
        if (IsMediaPlayerPlaying())
        {
            return true;
        }
        return false;
    }

    private bool IsMediaPlayerPlaying()
    {
        try
        {
            if (fruitPlayer == null)
            {
                return false;
            }
            int state = (int)fruitPlayer.playState;
            if (state == 3)
            {
                return true;
            }
            if (state == 6)
            {
                return true;
            }
            if (state == 9)
            {
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private HashSet<int> GetActiveFruitIds()
    {
        HashSet<int> ids = new HashSet<int>();
        if (CurrentLevel == null)
        {
            return ids;
        }

        foreach (var slot in CurrentLevel.Targets)
        {
            ids.Add(slot.SymbolId);
        }
        return ids;
    }

    private List<int> GetSortedActiveFruitIds()
    {
        var ids = GetActiveFruitIds().ToList();
        ids.Sort();
        return ids;
    }

    private void OpenRadialMenuLayer1()
    {
        radialMenuOpen = true;
        radialLayer = "layer1";
        radialSubMode = "";
        radialCursorFollowsGesture = false;
        radialLabels.Clear();
        radialLabels.Add("Colors");
        radialLabels.Add("Info");
        radialLabels.Add("Level Control");
        radialLabels.Add("Audio");
        radialLabels.Add("Exit");
        radialCursorPoint = radialGestureMode ? new Point(width / 2, height / 2) : PointToClient(Cursor.Position);
        radialHoveredIndex = -1;
        radialHoverSince = DateTime.MinValue;
        radialSelectionLocked = false;
        Invalidate();
    }

    private void CloseRadialMenu()
    {
        radialMenuOpen = false;
        radialLayer = "none";
        radialSubMode = "";
        radialCursorFollowsGesture = false;
        radialLabels.Clear();
        radialHoveredIndex = -1;
        radialHoverSince = DateTime.MinValue;
        radialSelectionLocked = false;
        Invalidate();
    }

    private void BuildRadialLayer2Fruits()
    {
        radialLabels.Clear();
        radialLabels.Add("Back");
        foreach (int id in GetSortedActiveFruitIds())
        {
            if (fruitNames.ContainsKey(id))
            {
                radialLabels.Add(fruitNames[id]);
            }
        }
    }

    private void BuildRadialLayer2Level()
    {
        radialLabels.Clear();
        radialLabels.Add("Back");
        radialLabels.Add("Restart Level");
        radialLabels.Add("Go to Level 1");
        radialLabels.Add("Go to Level 2");
    }

    private void BuildRadialLayer2Audio()
    {
        radialLabels.Clear();
        radialLabels.Add("Back");
        radialLabels.Add("Repeat Last Audio");
        if (radialMuted)
        {
            radialLabels.Add("Unmute");
        }
        else
        {
            radialLabels.Add("Mute");
        }
    }

    private void ActivateRadialSelection(int index)
    {
        if (index < 0 || index >= radialLabels.Count)
        {
            return;
        }

        string label = radialLabels[index];
        if (radialLayer == "layer1")
        {
            if (label == "Colors")
            {
                radialLayer = "layer2";
                radialSubMode = "colors";
                BuildRadialLayer2Fruits();
                return;
            }
            if (label == "Info")
            {
                radialLayer = "layer2";
                radialSubMode = "info";
                BuildRadialLayer2Fruits();
                return;
            }
            if (label == "Level Control")
            {
                radialLayer = "layer2";
                radialSubMode = "level";
                BuildRadialLayer2Level();
                return;
            }
            if (label == "Audio")
            {
                radialLayer = "layer2";
                radialSubMode = "audio";
                BuildRadialLayer2Audio();
                return;
            }
            if (label == "Exit")
            {
                CloseRadialMenu();
                return;
            }
            return;
        }

        if (radialLayer == "layer2")
        {
            if (radialSubMode == "colors")
            {
                HandleRadialColors(label);
                return;
            }
            if (radialSubMode == "info")
            {
                HandleRadialInfo(label);
                return;
            }
            if (radialSubMode == "level")
            {
                HandleRadialLevel(label);
                return;
            }
            if (radialSubMode == "audio")
            {
                HandleRadialAudio(label);
                return;
            }
        }
    }

    private void HandleRadialColors(string label)
    {
        if (label == "Back")
        {
            OpenRadialMenuLayer1();
            return;
        }

        foreach (int id in GetSortedActiveFruitIds())
        {
            if (!fruitNames.ContainsKey(id))
            {
                continue;
            }
            if (fruitNames[id] == label)
            {
                PlayFruitColorAudio(id);
                return;
            }
        }
    }

    private void HandleRadialInfo(string label)
    {
        if (label == "Back")
        {
            OpenRadialMenuLayer1();
            return;
        }

        foreach (int id in GetSortedActiveFruitIds())
        {
            if (!fruitNames.ContainsKey(id))
            {
                continue;
            }
            if (fruitNames[id] == label)
            {
                if (fruitBenefits.ContainsKey(id))
                {
                    SpeakSentence(fruitBenefits[id]);
                }
                return;
            }
        }
    }

    private void HandleRadialLevel(string label)
    {
        if (label == "Back")
        {
            OpenRadialMenuLayer1();
            return;
        }

        if (label == "Restart Level")
        {
            StartLevel(currentLevelIndex);
            return;
        }
        if (label == "Go to Level 1")
        {
            StartLevel(0);
            return;
        }
        if (label == "Go to Level 2")
        {
            StartLevel(1);
            return;
        }
    }

    private void HandleRadialAudio(string label)
    {
        if (label == "Back")
        {
            OpenRadialMenuLayer1();
            return;
        }

        if (label == "Repeat Last Audio")
        {
            RepeatLastAudio();
            return;
        }
        if (label == "Mute")
        {
            radialMuted = true;
            StopAllRadialAudio();
            BuildRadialLayer2Audio();
            return;
        }
        if (label == "Unmute")
        {
            radialMuted = false;
            BuildRadialLayer2Audio();
            return;
        }
    }

    private void StopAllRadialAudio()
    {
        try
        {
            if (fruitPlayer != null)
            {
                fruitPlayer.controls.stop();
            }
        }
        catch
        {
        }

        speechIsPlaying = false;

        try
        {
            if (speech != null)
            {
                speech.SpeakAsyncCancelAll();
            }
        }
        catch
        {
        }
    }

    private void PlayFruitColorAudio(int fruitId)
    {
        if (radialMuted)
        {
            return;
        }
        if (!fruitColorAudio.ContainsKey(fruitId))
        {
            return;
        }

        string mp3Name = fruitColorAudio[fruitId];
        string fullPath = GetAssetsPath(mp3Name);
        if (string.IsNullOrEmpty(fullPath))
        {
            return;
        }
        if (fruitPlayer == null)
        {
            return;
        }

        try
        {
            fruitPlayer.controls.stop();
            fruitPlayer.URL = fullPath;
            fruitPlayer.controls.play();
            lastAudioKind = "mp3";
            lastMp3Path = fullPath;
            lastSpokenSentence = "";
        }
        catch
        {
        }
    }

    private void SpeakSentence(string sentence)
    {
        if (radialMuted)
        {
            return;
        }
        if (string.IsNullOrEmpty(sentence))
        {
            return;
        }
        if (speech == null)
        {
            return;
        }

        try
        {
            speech.SpeakAsyncCancelAll();
            speechIsPlaying = true;
            speech.SpeakAsync(sentence);
            lastAudioKind = "tts";
            lastSpokenSentence = sentence;
            lastMp3Path = "";
        }
        catch
        {
        }
    }

    private void RepeatLastAudio()
    {
        if (radialMuted)
        {
            return;
        }

        if (lastAudioKind == "tts")
        {
            if (!string.IsNullOrEmpty(lastSpokenSentence))
            {
                SpeakSentence(lastSpokenSentence);
            }
            return;
        }

        if (lastAudioKind == "mp3")
        {
            if (string.IsNullOrEmpty(lastMp3Path))
            {
                return;
            }
            if (fruitPlayer == null)
            {
                return;
            }
            try
            {
                fruitPlayer.controls.stop();
                fruitPlayer.URL = lastMp3Path;
                fruitPlayer.controls.play();
            }
            catch
            {
            }
        }
    }

    private int GetRadialSectorIndex(Point cursor)
    {
        if (!radialMenuOpen)
        {
            return -1;
        }
        if (radialLabels.Count == 0)
        {
            return -1;
        }

        Point center = new Point(width / 2, height / 2);
        float minSide = Math.Min(width, height);
        float innerRadius = minSide * 0.12f;
        float outerRadius = minSide * 0.44f;
        float dx = cursor.X - center.X;
        float dy = cursor.Y - center.Y;
        float dist = (float)Math.Sqrt((dx * dx) + (dy * dy));

        if (dist < innerRadius)
        {
            return -1;
        }
        if (dist > outerRadius)
        {
            return -1;
        }

        float angle = (float)(Math.Atan2(dy, dx) * (180.0 / Math.PI));
        float adjusted = angle + 90.0f;
        while (adjusted < 0.0f)
        {
            adjusted = adjusted + 360.0f;
        }
        while (adjusted >= 360.0f)
        {
            adjusted = adjusted - 360.0f;
        }
        float sectorSpan = 360.0f / radialLabels.Count;
        int index = (int)(adjusted / sectorSpan);

        if (index < 0)
        {
            index = 0;
        }
        if (index >= radialLabels.Count)
        {
            index = radialLabels.Count - 1;
        }
        return index;
    }

    private void DrawRadialMenu(Graphics g)
    {
        if (!radialMenuOpen) return;
        if (radialLabels.Count == 0) return;

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var overlayBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255))) // Lighter overlay
        {
            g.FillRectangle(overlayBrush, new Rectangle(0, 0, width, height));
        }

        Point center = new Point(width / 2, height / 2);
        float minSide = Math.Min(width, height);
        float innerRadius = minSide * 0.12f;
        float outerRadius = minSide * 0.44f;
        Rectangle outerRect = new Rectangle(
            (int)(center.X - outerRadius), (int)(center.Y - outerRadius),
            (int)(outerRadius * 2.0f), (int)(outerRadius * 2.0f)
        );
        float span = 360.0f / radialLabels.Count;
        float start = -90.0f;

        Color[] pastels = new Color[] {
            Color.FromArgb(240, 255, 150, 150), // Pastel Red
            Color.FromArgb(240, 150, 255, 150), // Pastel Green
            Color.FromArgb(240, 150, 200, 255), // Pastel Blue
            Color.FromArgb(240, 255, 255, 150), // Pastel Yellow
            Color.FromArgb(240, 220, 150, 255)  // Pastel Purple
        };

        for (int i = 0; i < radialLabels.Count; i++)
        {
            Color fillColor = (i == radialHoveredIndex) ? Color.FromArgb(255, 255, 215, 0) : pastels[i % pastels.Length];

            using (var brush = new SolidBrush(fillColor))
            using (var pen = new Pen(Color.White, 5)) // Thick white border
            {
                g.FillPie(brush, outerRect, start + (span * i), span);
                g.DrawPie(pen, outerRect, start + (span * i), span);
            }

            float mid = start + (span * i) + (span / 2.0f);
            float midRad = (float)(Math.PI / 180.0) * mid;
            float labelRadius = (innerRadius + outerRadius) / 2.0f;
            float tx = center.X + (labelRadius * (float)Math.Cos(midRad));
            float ty = center.Y + (labelRadius * (float)Math.Sin(midRad));
            RectangleF labelRect = new RectangleF(tx - 90.0f, ty - 20.0f, 180.0f, 40.0f);
            
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            
            // Draw text with a subtle white shadow for readability
            labelRect.Offset(2, 2);
            g.DrawString(radialLabels[i], radialFont, Brushes.White, labelRect, sf);
            labelRect.Offset(-2, -2);
            g.DrawString(radialLabels[i], radialFont, Brushes.DarkSlateGray, labelRect, sf);
        }

        using (var holeBrush = new SolidBrush(Color.White))
        using (var holePen = new Pen(Color.LightGray, 3))
        {
            Rectangle innerRect = new Rectangle(
                (int)(center.X - innerRadius), (int)(center.Y - innerRadius),
                (int)(innerRadius * 2.0f), (int)(innerRadius * 2.0f));
            g.FillEllipse(holeBrush, innerRect);
            g.DrawEllipse(holePen, innerRect);
        }

        using (var tapBrush = new SolidBrush(Color.FromArgb(200, 255, 100, 100)))
        using (var tapPen = new Pen(Color.White, 3))
        {
            float tapRadius = Math.Max(12.0f, Math.Min(32.0f, minSide * 0.035f));
            Rectangle tapRect = new Rectangle(
                (int)(radialCursorPoint.X - tapRadius), (int)(radialCursorPoint.Y - tapRadius),
                (int)(tapRadius * 2.0f), (int)(tapRadius * 2.0f)
            );
            g.FillEllipse(tapBrush, tapRect);
            g.DrawEllipse(tapPen, tapRect);
        }
    }

    private void DrawTargetZones(Graphics g)
    {
        if (CurrentLevel == null) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        hintPulseTick++;
        bool pulseOn = (hintPulseTick / 15) % 2 == 0;
        
        // Bounce offset for the hint arrow
        float bounceY = (float)Math.Sin(hintPulseTick * 0.2) * 8f;

        foreach (var slot in CurrentLevel.Targets)
        {
            float tx = slot.XNormalized * width;
            float ty = slot.YNormalized * height;
            RectangleF slotRect = GetSlotBounds(slot);
            Rectangle drawRect = Rectangle.Round(slotRect);

            // Draw a playful puzzle slot background (recessed hole)
            using (System.Drawing.Drawing2D.GraphicsPath path = GetRoundedRect(slotRect, 20))
            {
                // Inner dark shadow for recessed look
                g.FillPath(new SolidBrush(Color.FromArgb(40, 0, 0, 0)), path);
                
                // Thick glowing border
                Color ringColor = Color.White;
                float ringWidth = 6f;

                if (currentDifficultyHint == "easier" && !slot.IsPlaced)
                {
                    ringColor = Color.FromArgb(255, 255, 230, 50);
                    ringWidth = 8f;
                }
                else if (slot.IsPlaced)
                {
                    ringColor = Color.LimeGreen;
                }

                using (var pen = new Pen(ringColor, ringWidth))
                {
                    if (!slot.IsPlaced && currentDifficultyHint != "easier")
                    {
                        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        pen.DashPattern = new float[] { 3.0F, 3.0F };
                    }
                    g.DrawPath(pen, path);
                }
            }

            if (!slot.IsPlaced)
            {
                // Draw faded ghost image of the fruit!
                Image fruitImg = null;
                if ((slot.SymbolId == 3 || slot.SymbolId == 6) && fruitImagesAlt.ContainsKey(slot.SymbolId))
                {
                    fruitImg = fruitImagesAlt[slot.SymbolId];
                }
                else if (fruitImages.ContainsKey(slot.SymbolId))
                {
                    fruitImg = fruitImages[slot.SymbolId];
                }
                
                if (fruitImg != null)
                {
                    System.Drawing.Imaging.ImageAttributes attrs = new System.Drawing.Imaging.ImageAttributes();
                    System.Drawing.Imaging.ColorMatrix matrix = new System.Drawing.Imaging.ColorMatrix();
                    
                    // Make it a dark silhouette with 40% opacity
                    matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = 0.2f; // Darken
                    matrix.Matrix33 = 0.4f; // 40% alpha
                    attrs.SetColorMatrix(matrix);
                    
                    g.DrawImage(fruitImg, drawRect, 0, 0, fruitImg.Width, fruitImg.Height, GraphicsUnit.Pixel, attrs);
                }

                if (currentDifficultyHint == "easier")
                {
                using (var glowPen = new Pen(Color.FromArgb(180, 255, 230, 50), 6))
                {
                    float expand = 8f;
                    g.DrawEllipse(glowPen, slotRect.X - expand, slotRect.Y - expand, slotRect.Width + expand * 2, slotRect.Height + expand * 2);
                }
                
                // Bouncy cartoon arrow
                float arrowX = slotRect.X + slotRect.Width / 2f;
                float arrowY = slotRect.Y - 35f + bounceY;
                Point[] arrow = {
                    new Point((int)arrowX,          (int)(arrowY + 28)),
                    new Point((int)(arrowX - 16f),  (int)arrowY),
                    new Point((int)(arrowX - 6f),   (int)arrowY),
                    new Point((int)(arrowX - 6f),   (int)(arrowY - 15)),
                    new Point((int)(arrowX + 6f),   (int)(arrowY - 15)),
                    new Point((int)(arrowX + 6f),   (int)arrowY),
                    new Point((int)(arrowX + 16f),  (int)arrowY),
                };
                using (var arrowBrush = new SolidBrush(Color.FromArgb(255, 230, 50)))
                using (var arrowPen = new Pen(Color.White, 2))
                {
                    g.FillPolygon(arrowBrush, arrow);
                    g.DrawPolygon(arrowPen, arrow);
                }
                } // End if easier
            } // End if !slot.IsPlaced

            string checkSuffix = slot.IsPlaced ? " ✓" : "";
            string status = slot.FruitName + checkSuffix;
            SizeF labelSize = g.MeasureString(status, smallFont);

            RectangleF labelBg = new RectangleF(tx - labelSize.Width / 2 - 10, slotRect.Bottom + 8, labelSize.Width + 20, labelSize.Height + 8);
            using (System.Drawing.Drawing2D.GraphicsPath path = GetRoundedRect(labelBg, 10))
            using (SolidBrush bgBrush = new SolidBrush(slot.IsPlaced ? Color.LimeGreen : Color.FromArgb(200, 50, 50, 50)))
            {
                g.FillPath(bgBrush, path);
            }
            g.DrawString(status, smallFont, whiteBrush, tx - labelSize.Width / 2, slotRect.Bottom + 12);
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
        float centerX    = slot.XNormalized * width;
        float centerY    = slot.YNormalized * height;
        float slotWidth  = slot.WidthNormalized  * width;
        float slotHeight = slot.HeightNormalized * height;

        float scale = 1.0f;
        if      (currentDifficultyHint == "easier") scale = 1.20f;   // +20%
        else if (currentDifficultyHint == "harder") scale = 0.90f;   // -10%

        slotWidth  *= scale;
        slotHeight *= scale;

        return new RectangleF(
            centerX - (slotWidth  / 2f),
            centerY - (slotHeight / 2f),
            slotWidth,
            slotHeight);
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

                int targetX = tobj.getScreenX(width);
                int targetY = tobj.getScreenY(height);

                lock (visualStates)
                {
                    if (!visualStates.ContainsKey(tobj.SessionID))
                    {
                        visualStates[tobj.SessionID] = new VisualState { X = targetX, Y = targetY };
                    }
                    
                    VisualState vs = visualStates[tobj.SessionID];
                    // Smooth interpolation (Lerp)
                    vs.X += (targetX - vs.X) * 0.15f;
                    vs.Y += (targetY - vs.Y) * 0.15f;

                    int ox = (int)vs.X;
                    int oy = (int)vs.Y;
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
                } // End lock visualStates
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
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int placedCount = CurrentLevel.Targets.Count(t => t.IsPlaced);
        int totalCount = CurrentLevel.Targets.Count;
        double elapsed = (DateTime.Now - levelStartTime).TotalSeconds;

        string line1 = CurrentLevel.Name + "  |  Stars: " + placedCount + "/" + totalCount;
        string line2 = "Time: " + elapsed.ToString("0.0") + " sec  |  Place fruits in the matching silhouettes!";
        string line3 = "Magic Hands: circle=open | square=close | open_hand=select";

        // Draw Bubbly HUD
        RectangleF hudRect = new RectangleF(15, 15, width - 30, 95);
        using (System.Drawing.Drawing2D.GraphicsPath path = GetRoundedRect(hudRect, 20))
        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
        using (Pen borderPen = new Pen(Color.FromArgb(255, 150, 200, 255), 6))
        {
            g.FillPath(bgBrush, path);
            g.DrawPath(borderPen, path);
        }

        using (var darkFont = new Font("Comic Sans MS", 16F, FontStyle.Bold))
        using (var midFont = new Font("Comic Sans MS", 12F, FontStyle.Bold))
        {
            g.DrawString(line1, darkFont, Brushes.DarkSlateBlue, 30, 22);
            g.DrawString(line2, midFont, Brushes.DarkSlateGray, 30, 55);
            g.DrawString(line3, midFont, Brushes.DimGray, 30, 80);
        }

        // Emotion Indicator (Bubbly corner)
        bool emotionRecent = (DateTime.Now - emotionLastUpdate).TotalSeconds < 10.0;
        if (emotionRecent && !string.IsNullOrEmpty(currentEmotionLabel))
        {
            Color hintColor;
            switch (currentDifficultyHint)
            {
                case "easier": hintColor = Color.FromArgb(255, 200, 50); break;  // Gold/Orange
                case "harder": hintColor = Color.FromArgb(80, 200, 80); break;   // Green
                default:       hintColor = Color.FromArgb(180, 180, 180); break; // Grey
            }

            string emoji = "😐";
            if (currentEmotionLabel == "happy") emoji = "😄";
            if (currentEmotionLabel == "sad") emoji = "😢";
            if (currentEmotionLabel == "angry") emoji = "😡";
            if (currentEmotionLabel == "surprise") emoji = "😲";
            
            string emotionText = $"{emoji} {currentEmotionLabel.ToUpper()}";

            using (var emotionFont = new Font("Comic Sans MS", 14F, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(emotionText, emotionFont);
                RectangleF emoRect = new RectangleF(width - sz.Width - 30, height - sz.Height - 30, sz.Width + 20, sz.Height + 16);
                
                using (System.Drawing.Drawing2D.GraphicsPath path = GetRoundedRect(emoRect, 15))
                using (SolidBrush bgBrush = new SolidBrush(Color.White))
                using (Pen borderPen = new Pen(hintColor, 4))
                using (SolidBrush textBrush = new SolidBrush(hintColor))
                {
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);
                    g.DrawString(emotionText, emotionFont, textBrush, emoRect.X + 10, emoRect.Y + 8);
                }
            }
        }
    }

    // Helper to draw rounded rectangles
    private System.Drawing.Drawing2D.GraphicsPath GetRoundedRect(RectangleF rect, float radius)
    {
        System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void InitializeComponent()
    {
            this.SuspendLayout();
            // 
            // TuioDemo
            // 
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.Name = "TuioDemo";
            this.Load += new System.EventHandler(this.TuioDemo_Load);
            this.ResumeLayout(false);

    }

    private void TuioDemo_Load(object sender, EventArgs e)
    {
        if (this.ClientSize.Width > 0 && this.ClientSize.Height > 0)
        {
            width = this.ClientSize.Width;
            height = this.ClientSize.Height;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (this.ClientSize.Width > 0 && this.ClientSize.Height > 0)
        {
            width = this.ClientSize.Width;
            height = this.ClientSize.Height;
            this.Invalidate();
        }
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
            Application.Run(new TuioDemo(port, login.UseRadialGestureMode));
        }
    }
}

// Custom kid-friendly popup to replace boring MessageBox
public class KidPopup : Form
{
    private Label lblTitle;
    private Label lblMessage;
    private Button btnOk;

    public KidPopup(string title, string message)
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ClientSize = new Size(400, 250);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.White;
        this.DoubleBuffered = true;

        lblTitle = new Label();
        lblTitle.Text = title;
        lblTitle.Font = new Font("Comic Sans MS", 18F, FontStyle.Bold);
        lblTitle.ForeColor = Color.White;
        lblTitle.BackColor = Color.Transparent;
        lblTitle.AutoSize = false;
        lblTitle.Size = new Size(400, 40);
        lblTitle.TextAlign = ContentAlignment.MiddleCenter;
        lblTitle.Location = new Point(0, 20);

        lblMessage = new Label();
        lblMessage.Text = message;
        lblMessage.Font = new Font("Comic Sans MS", 12F, FontStyle.Bold);
        lblMessage.ForeColor = Color.DarkSlateGray;
        lblMessage.BackColor = Color.Transparent;
        lblMessage.AutoSize = false;
        lblMessage.Size = new Size(360, 100);
        lblMessage.TextAlign = ContentAlignment.MiddleCenter;
        lblMessage.Location = new Point(20, 70);

        btnOk = new Button();
        btnOk.Text = "Yay! Let's Go!";
        btnOk.Font = new Font("Comic Sans MS", 14F, FontStyle.Bold);
        btnOk.BackColor = Color.LimeGreen;
        btnOk.ForeColor = Color.White;
        btnOk.FlatStyle = FlatStyle.Flat;
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Size = new Size(200, 50);
        btnOk.Location = new Point(100, 180);
        btnOk.Cursor = Cursors.Hand;
        btnOk.Click += (s, e) => this.Close();

        this.Controls.Add(lblTitle);
        this.Controls.Add(lblMessage);
        this.Controls.Add(btnOk);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);
        using (System.Drawing.Drawing2D.LinearGradientBrush brush = new System.Drawing.Drawing2D.LinearGradientBrush(rect, Color.FromArgb(100, 200, 255), Color.FromArgb(150, 100, 255), 45f))
        {
            g.FillRectangle(brush, rect);
        }

        // Message background bubble
        using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            int r = 20;
            Rectangle msgRect = new Rectangle(15, 65, 370, 105);
            path.AddArc(msgRect.X, msgRect.Y, r, r, 180, 90);
            path.AddArc(msgRect.Right - r, msgRect.Y, r, r, 270, 90);
            path.AddArc(msgRect.Right - r, msgRect.Bottom - r, r, r, 0, 90);
            path.AddArc(msgRect.X, msgRect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            g.FillPath(Brushes.White, path);
        }

        // Border
        using (Pen pen = new Pen(Color.White, 6))
        {
            g.DrawRectangle(pen, 3, 3, this.Width - 6, this.Height - 6);
        }
    }

    public static void Show(string title, string message)
    {
        using (var form = new KidPopup(title, message))
        {
            form.ShowDialog();
        }
    }
}
