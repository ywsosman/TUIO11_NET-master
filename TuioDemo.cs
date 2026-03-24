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
        Name = "TuioDemo";
        Text = useRadialGestureMode ? "Fruit Learning Game (Radial Gesture)" : "Fruit Learning Game";

        Closing += Form_Closing;
        KeyDown += Form_KeyDown;
        MouseMove += Form_MouseMove;
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
        BuildRadialFruitData();
        InitializeRadialSpeech();
        BuildLevels();
        StartLevel(0);
        InitializeRadialMenu();

        if (radialGestureMode)
        {
            gestureClient = new GestureSocketClient("127.0.0.1", 5000);
            gestureClient.AddListener(this);
            try
            {
                gestureClient.Connect();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not connect to gesture server.\nStart: python radial_gesture_server.py\n" + ex.Message, "Radial Gesture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        else
        {
            client = new TuioClient(port);
            client.addTuioListener(this);
            client.connect();
        }
    }

    public void OnSkeletonUpdate(double timestamp, IList<SkeletonLandmark> landmarks)
    {
        if (!radialGestureMode || landmarks == null) return;
        // Prefer index tip over wrist for cursor (gesture server sends index tip as wrist in hand-only mode)
        var wrist = landmarks.FirstOrDefault(l => l.Name == "right_wrist" || l.Name == "left_wrist" || l.Name == "right_index" || l.Name == "left_index");
        if (wrist != null && wrist.Visibility > 0.3f)
        {
            gestureWristX = wrist.X;
            gestureWristY = wrist.Y;
            // When menu is open and index finger is visible, auto-enable cursor following (no open_hand needed)
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
        if (!radialMenuOpen)
        {
            return;
        }
        if (radialLabels.Count == 0)
        {
            return;
        }

        using (var overlayBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            g.FillRectangle(overlayBrush, new Rectangle(0, 0, width, height));
        }

        Point center = new Point(width / 2, height / 2);
        float minSide = Math.Min(width, height);
        float innerRadius = minSide * 0.12f;
        float outerRadius = minSide * 0.44f;
        Rectangle outerRect = new Rectangle(
            (int)(center.X - outerRadius),
            (int)(center.Y - outerRadius),
            (int)(outerRadius * 2.0f),
            (int)(outerRadius * 2.0f)
        );
        float span = 360.0f / radialLabels.Count;
        float start = -90.0f;

        for (int i = 0; i < radialLabels.Count; i++)
        {
            Color fillColor;
            if (i == radialHoveredIndex)
            {
                fillColor = Color.FromArgb(220, 80, 150, 230);
            }
            else
            {
                if ((i % 2) == 0)
                {
                    fillColor = Color.FromArgb(200, 40, 90, 160);
                }
                else
                {
                    fillColor = Color.FromArgb(200, 30, 70, 130);
                }
            }

            using (var brush = new SolidBrush(fillColor))
            using (var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 2))
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
            var sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            g.DrawString(radialLabels[i], radialFont, Brushes.White, labelRect, sf);
        }

        using (var holeBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            g.FillEllipse(
                holeBrush,
                new Rectangle(
                    (int)(center.X - innerRadius),
                    (int)(center.Y - innerRadius),
                    (int)(innerRadius * 2.0f),
                    (int)(innerRadius * 2.0f)
                )
            );
        }

        using (var tapBrush = new SolidBrush(Color.FromArgb(140, 255, 210, 80)))
        using (var tapPen = new Pen(Color.FromArgb(230, 255, 255, 255), 2))
        {
            float tapRadius = minSide * 0.035f;
            if (tapRadius < 12.0f)
            {
                tapRadius = 12.0f;
            }
            if (tapRadius > 32.0f)
            {
                tapRadius = 32.0f;
            }
            Rectangle tapRect = new Rectangle(
                (int)(radialCursorPoint.X - tapRadius),
                (int)(radialCursorPoint.Y - tapRadius),
                (int)(tapRadius * 2.0f),
                (int)(tapRadius * 2.0f)
            );
            g.FillEllipse(tapBrush, tapRect);
            g.DrawEllipse(tapPen, tapRect);
        }
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
        if (radialGestureMode)
            line3 = "Radial: circle=open | square=close | open_hand=select (O key also opens)";
        else if (currentLevelIndex == 0)
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
            Application.Run(new TuioDemo(port, login.UseRadialGestureMode));
        }
    }
}
