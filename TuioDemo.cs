using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows.Forms;
using HCI_Lab_codes.Models;
using TUIO;
using GestureClient;

namespace TuioDemoApp
{ 
  public class TuioDemo : Form, TuioListener, IGestureListener
  {
    private class TargetSlot
    {
        public int SymbolId;
        public string ObjectName;
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
    private string proximityWarning = "ok";

    // ── Emotion / difficulty state ────────────────────────────────────────────
    private string currentDifficultyHint = "normal";   // "easier", "harder", "normal"
    private string currentEmotionLabel   = "";
    private float currentEmotionConf     = 0.0f;
    private DateTime emotionLastUpdate   = DateTime.MinValue;

    // ── Adaptive hints from prior sessions (gaze_adapter / emotion_adapter) ──
    private AdaptiveHints adaptiveHints = new AdaptiveHints();
    private DateTime      adaptiveHintsLoadedAt = DateTime.MinValue;

    private PointF currentGazePoint = new PointF(-1, -1);
    private readonly object gazeHistoryLock = new object();
    private List<PointF> gazeHistory = new List<PointF>();
    private const int MaxGazeHistorySamples = 120000;
    private readonly DateTime sessionStartUtc = DateTime.UtcNow;

    private readonly object evalLock = new object();
    private int evalSuccessfulPlacements;
    private int evalRadialOpens;
    private int evalRadialCloses;
    private int evalLevelsCompletedEvents;
    private readonly Dictionary<string, int> evalEmotionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> evalLevelScoresHistory = new List<int>();
    private readonly List<double> evalLevelCompletionSeconds = new List<double>();
    private readonly Stopwatch levelStopwatch = new Stopwatch();
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
    private readonly System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
    private readonly Dictionary<long, TuioCursor> cursorList = new Dictionary<long, TuioCursor>(128);
    private readonly Dictionary<long, TuioBlob> blobList = new Dictionary<long, TuioBlob>(128);
    private readonly Dictionary<int, Image> objectImages = new Dictionary<int, Image>();
    private readonly Dictionary<int, Image> objectImagesAlt = new Dictionary<int, Image>();
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
    private Image fallbackBackground;
    private dynamic objectPlayer;

    private readonly Font smallFont = new Font("Arial", 12.0f, FontStyle.Bold);
    private readonly Font titleFont = new Font("Arial", 22.0f, FontStyle.Bold);
    private readonly SolidBrush whiteBrush = new SolidBrush(Color.White);
    private readonly SolidBrush darkOverlayBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
    private readonly Font radialFont = new Font("Arial", 14.0f, FontStyle.Bold);
    private readonly System.Windows.Forms.Timer radialTimer = new System.Windows.Forms.Timer();
    private readonly Dictionary<int, string> objectNames = new Dictionary<int, string>();
    private readonly Dictionary<int, string> objectColors = new Dictionary<int, string>();
    private readonly Dictionary<int, string> objectBenefits = new Dictionary<int, string>();
    private readonly Dictionary<int, string> objectColorAudio = new Dictionary<int, string>();
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
    private readonly int radialDwellMs = 1500;
    private DateTime closeGestureHoverSince = DateTime.MinValue;
    private const int CloseButtonGestureDwellMs = 900;
    private const int CloseButtonGestureInflatePx = 22;
    private bool shuttingDown;    private bool radialSelectionLocked;
    private DateTime radialLastActionAt = DateTime.MinValue;
    private readonly int radialRepeatDelayMs = 250;
    private bool speechIsPlaying;
    private BluetoothDevicePairingManager bluetoothPairingManager;
    private readonly object bluetoothUiSync = new object();
    private List<PairedBluetoothDevice> bluetoothDevices = new List<PairedBluetoothDevice>();
    private string bluetoothStatusMessage = "";
    private DateTime bluetoothStatusAt = DateTime.MinValue;
    private bool bluetoothMenuOpen;

    private Button closeGameButton;

    // ── Per-user progress / face-login profile ────────────────────────────────
    private readonly bool isTeacher;
    private readonly string userProfileKey;
    private readonly UserProgressStore progressStore;

    public TuioDemo(int port, bool useRadialGestureMode = false)
        : this(port, useRadialGestureMode, false, null, null)
    {
    }

    public TuioDemo(int port, bool useRadialGestureMode, bool isTeacher, string userProfileKey, UserProgressStore progressStore)
    {
        this.isTeacher = isTeacher;
        this.userProfileKey = string.IsNullOrEmpty(userProfileKey)
            ? (isTeacher ? "teacher:default" : "student:default")
            : userProfileKey;
        this.progressStore = progressStore ?? UserProgressStore.Load();
        // Ensure the profile row exists so subsequent saves preserve the kind.
        this.progressStore.EnsureProfile(this.userProfileKey, isTeacher ? "teacher" : "student");

        verbose = false;
        fullscreen = false;
        radialGestureMode = useRadialGestureMode;
        width = windowWidth;
        height = windowHeight;
        ClientSize = new Size(width, height);
        
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;

        Name = "TuioDemo";
        Text = "Object Learning Game";

        closeGameButton = new Button();
        closeGameButton.Text = "X";
        closeGameButton.Font = new Font("Arial", 16F, FontStyle.Bold);
        closeGameButton.ForeColor = Color.White;
        closeGameButton.BackColor = Color.FromArgb(232, 60, 60);
        closeGameButton.FlatStyle = FlatStyle.Flat;
        closeGameButton.FlatAppearance.BorderSize = 0;
        closeGameButton.FlatAppearance.BorderColor = Color.FromArgb(180, 30, 30);
        closeGameButton.Cursor = Cursors.Hand;
        closeGameButton.Size = new Size(48, 48);
        closeGameButton.TabStop = false;
        closeGameButton.AccessibleDescription = "Close application";
        closeGameButton.Margin = Padding.Empty;
        closeGameButton.Click += (s, e) => { Close(); };

        Controls.Add(closeGameButton);
        closeGameButton.BringToFront();

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
                objectPlayer = Activator.CreateInstance(wmpType);
                objectPlayer.settings.autoStart = false;
            }
        }
        catch
        {
            objectPlayer = null;
        }

        LoadAssets();
        BuildRadialObjectData();
        InitializeRadialSpeech();
        BuildLevels();
        StartLevel(0);
        InitializeRadialMenu();
        InitializeBluetoothPairing();

        // ── Apply adaptive hints derived from prior gaze + emotion sessions ──
        // Written by python/gaze_adapter.py + python/emotion_adapter.py at the
        // end of every session; consumed here on the next launch.
        // ── Apply teacher-assigned difficulty as the gameplay baseline ──
        // The teacher dashboard writes to UserManager (users.json); we read it
        // back here so changes actually affect slot scaling, hint timing, and
        // the silhouette display. Emotion can still nudge it easier mid-play
        // (via ApplyDifficultyHint) — but never override teacher "easy" into
        // "harder" without confirmation, which keeps the teacher's intent.
        ApplyTeacherDifficultyBaseline();

        adaptiveHints = AdaptiveHints.Load(this.userProfileKey);
        if (adaptiveHints.WasLoaded)
        {
            adaptiveHintsLoadedAt = DateTime.Now;
            Console.WriteLine("[TuioDemo] " + adaptiveHints.ShortSummary());
            Console.WriteLine("            source: " + adaptiveHints.SourcePath);

            // Bias the starting difficulty hint based on past emotion profile.
            string biasHint = adaptiveHints.ResolvedDifficultyHint();
            if (biasHint != "normal") ApplyDifficultyHint(biasHint);

            // If past sessions show this child often gets confused, give an
            // audio cue for the first target as soon as the game starts.
            if (adaptiveHints.StartWithAudioHint && speech != null && CurrentLevel != null)
            {
                var firstTarget = CurrentLevel.Targets.FirstOrDefault();
                if (firstTarget != null && !string.IsNullOrEmpty(firstTarget.ObjectName))
                {
                    try
                    {
                        speech.SpeakAsyncCancelAll();
                        speech.SpeakAsync("Let's find the " + firstTarget.ObjectName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[TuioDemo] Audio hint failed: " + ex.Message);
                    }
                }
            }
        }

        // ── TUIO markers (always active – handles object placement) ────────────
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

        LayoutCloseButton();
    }

    private void LayoutCloseButton()
    {
        if (closeGameButton == null || closeGameButton.IsDisposed) return;
        const int pad = 10;
        int x = Math.Max(pad, ClientSize.Width - closeGameButton.Width - pad);
        int y = pad;
        closeGameButton.Location = new Point(x, y);
        closeGameButton.BringToFront();
    }

    public void OnSkeletonUpdate(double timestamp, IList<SkeletonLandmark> landmarks)
    {
        if (!radialGestureMode || landmarks == null) return;
        var wrist = landmarks.FirstOrDefault(l => l.Name == "right_wrist" || l.Name == "left_wrist" || l.Name == "right_index" || l.Name == "left_index");
        if (wrist != null && wrist.Visibility > 0.3f)
        {
            gestureWristX = wrist.X;
            gestureWristY = wrist.Y;
            if (radialMenuOpen && !radialCursorFollowsGesture && gestureWristX >= 0 && gestureWristY >= 0)
                radialCursorFollowsGesture = true;

            bool pointerActive = !radialMenuOpen || radialCursorFollowsGesture;
            if (pointerActive && gestureWristX >= 0f && gestureWristY >= 0f &&
                !float.IsNaN(gestureWristX) && !float.IsNaN(gestureWristY))
            {
                int px = (int)(gestureWristX * width);
                int py = (int)(gestureWristY * height);
                radialCursorPoint = new Point(px, py);
            }
        }
        else
        {
            gestureWristX = -1f;
            gestureWristY = -1f;
        }
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke((MethodInvoker)(() => Invalidate()));
    }

    public void OnGestureRecognized(double timestamp, RecognizedGesture gesture)
    {
        if (!radialGestureMode || gesture == null) return;
        string g = gesture.Name.ToLowerInvariant();
        if (g == "triangle")
        {
            ToggleBluetoothMenu();
            return;
        }
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
        if (g == "tap")
        {
            // Laser dwell — activate the radial item currently under the cursor.
            if (radialMenuOpen)
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    int index = GetRadialSectorIndex(radialCursorPoint);
                    if (index >= 0)
                    {
                        radialHoveredIndex = index;
                        radialHoverSince = DateTime.Now;
                        radialLastActionAt = DateTime.Now;
                        radialSelectionLocked = true;
                        ActivateRadialSelection(index);
                        Invalidate();
                    }
                }));
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
        if (!string.IsNullOrEmpty(label))
        {
            lock (evalLock)
            {
                int n;
                evalEmotionCounts[label] = evalEmotionCounts.TryGetValue(label, out n) ? n + 1 : 1;
            }
        }
    }

    public void OnProximityUpdate(string status)
    {
        proximityWarning = status;
        // Invalidate on UI thread if needed (the timer already runs at 60 FPS though)
    }

    public void OnYoloDetection(IList<YoloObject> detections)
    {
        // YOLO is not drawn in this app; it runs on the gesture server only.
    }

    public void OnGazeUpdate(float x, float y)
    {
        currentGazePoint = new PointF(x, y);
        lock (gazeHistoryLock)
        {
            if (gazeHistory.Count >= MaxGazeHistorySamples)
                gazeHistory.RemoveAt(0);
            gazeHistory.Add(currentGazePoint);
        }
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke((MethodInvoker)(() => Invalidate()));
    }

    private string SaveEvaluationArtifacts()
    {
        try
        {
            // Exports go to Documents\TUIO_Evaluation (easy to find; not buried under bin\Debug).
            string evalDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TUIO_Evaluation");
            Directory.CreateDirectory(evalDir);
            string stamp = sessionStartUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "Z";
            string jsonPath = Path.Combine(evalDir, "evaluation_" + stamp + ".json");
            string pngPath = Path.Combine(evalDir, "gaze_heatmap_" + stamp + ".png");

            DateTime endUtc = DateTime.UtcNow;
            double durationSec;
            int placements, opens, closes, levelEvents, gazeSamples;
            Dictionary<string, int> emotionSnap;
            List<int> scoresSnap;
            List<double> levelTimesSnap;
            string diffHint;

            lock (evalLock)
            {
                durationSec = (endUtc - sessionStartUtc).TotalSeconds;
                placements = evalSuccessfulPlacements;
                opens = evalRadialOpens;
                closes = evalRadialCloses;
                levelEvents = evalLevelsCompletedEvents;
                emotionSnap = new Dictionary<string, int>(evalEmotionCounts);
                scoresSnap = new List<int>(evalLevelScoresHistory);
                levelTimesSnap = new List<double>(evalLevelCompletionSeconds);
                diffHint = currentDifficultyHint ?? "normal";
            }

            List<PointF> gazeSnap;
            lock (gazeHistoryLock)
            {
                gazeSamples = gazeHistory.Count;
                gazeSnap = new List<PointF>(gazeHistory);
            }

            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"session_start_utc\":\"").Append(EscapeJson(sessionStartUtc.ToString("o"))).Append("\",");
            sb.Append("\"session_end_utc\":\"").Append(EscapeJson(endUtc.ToString("o"))).Append("\",");
            sb.Append("\"duration_seconds\":").Append(durationSec.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"successful_placements\":").Append(placements).Append(',');
            sb.Append("\"radial_menu_opens\":").Append(opens).Append(',');
            sb.Append("\"radial_menu_closes\":").Append(closes).Append(',');
            sb.Append("\"levels_completed_events\":").Append(levelEvents).Append(',');
            sb.Append("\"gaze_samples_recorded\":").Append(gazeSamples).Append(',');
            sb.Append("\"final_difficulty_hint\":\"").Append(EscapeJson(diffHint)).Append("\",");
            sb.Append("\"level_scores\":[");
            for (int i = 0; i < scoresSnap.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(scoresSnap[i]);
            }
            sb.Append("],");
            sb.Append("\"level_completion_seconds\":[");
            for (int i = 0; i < levelTimesSnap.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(levelTimesSnap[i].ToString("F3", CultureInfo.InvariantCulture));
            }
            sb.Append("],");
            sb.Append("\"emotion_counts\":{");
            bool firstEmo = true;
            foreach (var kv in emotionSnap.OrderBy(k => k.Key))
            {
                if (!firstEmo) sb.Append(',');
                firstEmo = false;
                sb.Append('"').Append(EscapeJson(kv.Key)).Append("\":").Append(kv.Value);
            }
            sb.Append("},");
            const int gazeExportStride = 30;
            AppendGazePointsJson(sb, gazeSnap, gazeExportStride);
            sb.Append(',');
            sb.Append("\"export_directory\":\"").Append(EscapeJson(Path.GetDirectoryName(jsonPath))).Append("\"}");

            File.WriteAllText(jsonPath, sb.ToString(), Encoding.UTF8);
            SaveGazeHeatmapPng(pngPath);

            string msg = "[TuioDemo] Session evaluation saved to:\n  " + jsonPath + "\n  " + pngPath;
            Console.WriteLine(msg);
            return jsonPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TuioDemo] Evaluation export failed: " + ex.Message);
            try
            {
                MessageBox.Show(
                    this,
                    "Could not save session evaluation.\r\n\r\n" + ex.Message,
                    "Evaluation save failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch
            {
            }
            return null;
        }
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Appends "gaze_export_stride" and "gaze_points" (normalized x,y, downsampled) for JSON export.</summary>
    private static void AppendGazePointsJson(StringBuilder sb, IList<PointF> pts, int stride)
    {
        if (stride < 1) stride = 1;
        sb.Append("\"gaze_export_stride\":").Append(stride).Append(',');
        sb.Append("\"gaze_points\":[");
        bool first = true;
        for (int i = 0; i < pts.Count; i += stride)
        {
            PointF p = pts[i];
            if (float.IsNaN(p.X) || float.IsNaN(p.Y)) continue;
            if (p.X < 0 || p.Y < 0 || p.X > 1.001f || p.Y > 1.001f) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"x\":").Append(((double)p.X).ToString("F4", CultureInfo.InvariantCulture))
              .Append(",\"y\":").Append(((double)p.Y).ToString("F4", CultureInfo.InvariantCulture)).Append('}');
        }
        sb.Append(']');
    }

    private void SaveGazeHeatmapPng(string path)
    {
        List<PointF> copy;
        lock (gazeHistoryLock)
            copy = new List<PointF>(gazeHistory);

        const int gw = 160;
        const int gh = 90;
        const int scale = 6;
        int[,] bins = new int[gw, gh];
        foreach (var p in copy)
        {
            if (float.IsNaN(p.X) || float.IsNaN(p.Y)) continue;
            if (p.X < 0 || p.Y < 0 || p.X > 1.001f || p.Y > 1.001f) continue;
            int bx = (int)(p.X * gw);
            int by = (int)(p.Y * gh);
            if (bx >= gw) bx = gw - 1;
            if (by >= gh) by = gh - 1;
            if (bx < 0) bx = 0;
            if (by < 0) by = 0;
            bins[bx, by]++;
        }

        int max = 1;
        for (int x = 0; x < gw; x++)
            for (int y = 0; y < gh; y++)
                if (bins[x, y] > max) max = bins[x, y];

        using (Bitmap bmp = new Bitmap(gw * scale, gh * scale))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(24, 24, 32));
                for (int x = 0; x < gw; x++)
                {
                    for (int y = 0; y < gh; y++)
                    {
                        double t = bins[x, y] / (double)max;
                        int r = (int)(255 * Math.Min(1.0, Math.Max(0.0, (t - 0.25) * 2)));
                        int gr = (int)(40 + 215 * Math.Min(1.0, Math.Max(0.0, 1.0 - Math.Abs(t - 0.55) * 3)));
                        int b = (int)(255 * Math.Min(1.0, Math.Max(0.0, (0.72 - t) * 2)));
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, r, gr, b)))
                            g.FillRectangle(brush, x * scale, y * scale, scale, scale);
                    }
                }
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>
    /// Adjusts game difficulty based on the child's detected emotion.
    /// "easier"  → enlarge hitboxes 20% + draw hint arrows on unplaced targets.
    /// "harder"  → tighten hitboxes 10% (child is doing well).
    /// "normal"  → restore default hitboxes.
    /// </summary>
    private void ApplyDifficultyHint(string hint)
    {
        // Emotion-driven hint must respect the teacher baseline: a teacher who
        // set "hard" should never get auto-promoted to "easier" by a frown.
        // We only let emotion modulate within the bounds it logically can —
        // currently that means it can express "easier" only when teacher
        // chose easy or medium. (Frustration→"easier" matches the dashboard's
        // intent; "harder" never comes from emotion in this codebase, so the
        // ceiling isn't actually enforced today, just documented.)
        if (hint == currentDifficultyHint) return;
        currentDifficultyHint = hint;
        Console.WriteLine("[TuioDemo] Difficulty → " + hint);
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke((MethodInvoker)(() => Invalidate()));
    }

    /// <summary>
    /// Read the teacher-set difficulty for this user out of users.json (via
    /// UserManager) and translate it into the gameplay hint scale used by
    /// slot scaling, hint timing, and the silhouette overlay.
    ///
    ///   stored "easy"   → "easier"  (slot scaled 1.20x, silhouette shown)
    ///   stored "medium" → "normal"  (slot unchanged)
    ///   stored "hard"   → "harder"  (slot scaled 0.90x)
    ///
    /// Called once at construction so the game opens in the teacher's chosen
    /// difficulty rather than always "normal".
    /// </summary>
    private void ApplyTeacherDifficultyBaseline()
    {
        try
        {
            var user = HCI_Lab_codes.Models.UserManager.GetByFaceId(this.userProfileKey);
            if (user == null || string.IsNullOrWhiteSpace(user.Difficulty))
            {
                Console.WriteLine("[TuioDemo] No stored difficulty for " + this.userProfileKey + " — using 'normal'.");
                return;
            }

            string baseline;
            switch (user.Difficulty.Trim().ToLowerInvariant())
            {
                case "easy":   baseline = "easier"; break;
                case "hard":   baseline = "harder"; break;
                case "medium": baseline = "normal"; break;
                default:       baseline = "normal"; break;
            }
            currentDifficultyHint = baseline;
            Console.WriteLine($"[TuioDemo] Teacher baseline for {user.DisplayName} ({this.userProfileKey}): {user.Difficulty} → {baseline}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TuioDemo] ApplyTeacherDifficultyBaseline failed: " + ex.Message);
        }
    }

    private const float AdaptiveHintMinEmotionConfidence = 0.55f;

    // Minimum confidence on the "happy" reading before we enable rotation
    // mode for fruit placement. Anything below this is treated as neutral.
    private const float ContentEmotionMinConfidence = 0.50f;
    // Max age of an emotion sample before we treat it as stale (player looked
    // away, server reconnecting, etc.) — same window as the adaptive-hint code.
    private const double ContentEmotionMaxAgeSec = 14.0;

    /// <summary>
    /// True when the emotion server's latest reading is a confident "happy".
    /// Drives the rotated-fruit hard mode — when the player is enjoying it,
    /// all slots flip to alt images and require a 90° rotation of the marker.
    /// </summary>
    private bool IsPlayerContent()
    {
        if (string.IsNullOrEmpty(currentEmotionLabel)) return false;
        if (currentEmotionConf < ContentEmotionMinConfidence) return false;
        double ageSec = (DateTime.Now - emotionLastUpdate).TotalSeconds;
        if (ageSec > ContentEmotionMaxAgeSec) return false;
        return string.Equals(currentEmotionLabel.Trim(), "happy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFrustrationLikeEmotion(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        switch (label.Trim().ToLowerInvariant())
        {
            case "sad":
            case "angry":
            case "fear":
            case "disgust":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Contextual help when the emotion system widens difficulty (easier) or detects negative affect.
    /// Uses the next unplaced fruit and TUIO/Reactivision marker wording.
    /// </summary>
    private string BuildAdaptivePlacementHintForSlot(TargetSlot slot)
    {
        if (slot == null) return "";
        string name = string.IsNullOrWhiteSpace(slot.ObjectName) ? "fruit" : slot.ObjectName;
        // Rotation requirement is now driven by live emotion, not the cid.
        if (IsPlayerContent())
            return $"Stuck? Turn the {name} marker so it matches the rotated fruit on the board!";
        return $"Hold the {name} marker up to the camera — line it up with the {name} silhouette!";
    }

    private bool TryGetAdaptivePlacementHint(out string hint)
    {
        hint = "";
        if (CurrentLevel == null) return false;

        // Wait at least `auto_hint_threshold_sec` after the level starts before
        // showing any contextual hint — gives the student a chance to try first.
        // Defaults to 4s if no adaptive_hints file has been written yet.
        int hintDelaySec = (adaptiveHints != null && adaptiveHints.AutoHintThresholdSec > 0)
            ? adaptiveHints.AutoHintThresholdSec : 4;
        if (levelStopwatch.IsRunning &&
            levelStopwatch.Elapsed.TotalSeconds < hintDelaySec) return false;

        double emotionAgeSec = (DateTime.Now - emotionLastUpdate).TotalSeconds;
        if (emotionAgeSec > 14.0 || string.IsNullOrEmpty(currentEmotionLabel)) return false;
        if (currentEmotionConf < AdaptiveHintMinEmotionConfidence) return false;

        bool easier = string.Equals(currentDifficultyHint, "easier", StringComparison.OrdinalIgnoreCase);
        bool negative = IsFrustrationLikeEmotion(currentEmotionLabel);

        if (!easier && !negative) return false;

        TargetSlot next = CurrentLevel.Targets.FirstOrDefault(t => !t.IsPlaced);
        if (next == null) return false;

        hint = BuildAdaptivePlacementHintForSlot(next);
        return !string.IsNullOrEmpty(hint);
    }

    private void BuildLevels()
    {
        levels.Clear();


        var level1 = new LevelDefinition
        {
            Name = "Level 1",
            BoardImageName = "level 1.png"
        };

        // All 7 fruits spread across 4 levels (2/2/2/1). NOTE: with the base
        // yolo11n.pt fallback in use, only apple/banana/orange detect reliably;
        // strawberry/watermelon/mango/kiwi need a properly trained fruit_best.pt.
        level1.Targets.Add(new TargetSlot { SymbolId = 0, ObjectName = "Apple",  XNormalized = 0.38f, YNormalized = 0.50f, WidthNormalized = 0.13f, HeightNormalized = 0.31f });
        level1.Targets.Add(new TargetSlot { SymbolId = 1, ObjectName = "Banana", XNormalized = 0.62f, YNormalized = 0.50f, WidthNormalized = 0.18f, HeightNormalized = 0.26f });

        var level2 = new LevelDefinition
        {
            Name = "Level 2",
            BoardImageName = "level2.png"
        };
        level2.Targets.Add(new TargetSlot { SymbolId = 2, ObjectName = "Strawberry", XNormalized = 0.38f, YNormalized = 0.50f, WidthNormalized = 0.14f, HeightNormalized = 0.23f });
        level2.Targets.Add(new TargetSlot { SymbolId = 3, ObjectName = "Watermelon", XNormalized = 0.62f, YNormalized = 0.50f, WidthNormalized = 0.15f, HeightNormalized = 0.21f });

        var level3 = new LevelDefinition
        {
            Name = "Level 3",
            BoardImageName = "level2.png"
        };
        level3.Targets.Add(new TargetSlot { SymbolId = 4, ObjectName = "Mango",  XNormalized = 0.38f, YNormalized = 0.45f, WidthNormalized = 0.14f, HeightNormalized = 0.23f });
        level3.Targets.Add(new TargetSlot { SymbolId = 5, ObjectName = "Orange", XNormalized = 0.62f, YNormalized = 0.50f, WidthNormalized = 0.13f, HeightNormalized = 0.22f });

        var level4 = new LevelDefinition
        {
            Name = "Level 4",
            BoardImageName = "level2.png"
        };
        level4.Targets.Add(new TargetSlot { SymbolId = 6, ObjectName = "Kiwi", XNormalized = 0.50f, YNormalized = 0.50f, WidthNormalized = 0.13f, HeightNormalized = 0.22f });

        levels.Add(level1);
        levels.Add(level2);
        levels.Add(level3);
        levels.Add(level4);
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

        if (!isTeacher && progressStore != null &&
            progressStore.IsLevelLocked(userProfileKey, levelIndex, levels.Count))
        {
            // Refuse to start a locked level for a student. Stay on the highest unlocked.
            int unlocked = progressStore.UnlockedLevels(userProfileKey, levels.Count);
            int safeIndex = Math.Max(0, unlocked - 1);
            if (currentLevelIndex == safeIndex) return;
            levelIndex = safeIndex;
        }

        currentLevelIndex = levelIndex;
        pendingLevelComplete = false;
        foreach (var slot in CurrentLevel.Targets)
        {
            slot.IsPlaced = false;
        }

        levelStopwatch.Restart();

        // Trigger animations
        levelTransitionOffset = this.ClientSize.Width > 0 ? this.ClientSize.Width : 1280;
        levelTransitionAlpha = 255f;
    }

    private void LoadAssets()
    {
        fallbackBackground = LoadImageByBaseName("background");

        objectImages[0] = LoadImageByBaseName("apple");
        {
            var tmp = LoadImageByBaseName("applecut");
            if (tmp != null)
                objectImagesAlt[0] = tmp;
            else
                objectImagesAlt[0] = objectImages[0];
        }
        objectImages[1] = LoadImageByBaseName("banana");
        {
            var tmp = LoadImageByBaseName("bananacut");
            if (tmp != null)
                objectImagesAlt[1] = tmp;
            else
                objectImagesAlt[1] = objectImages[1];
        }
        objectImages[2] = LoadImageByBaseName("straw");
        {
            var tmp = LoadImageByBaseName("strawcut");
            if (tmp != null)
                objectImagesAlt[2] = tmp;
            else
                objectImagesAlt[2] = objectImages[2];
        }
        {
            var tmp = LoadImageByBaseName("watermelonwhole");
            if (tmp != null)
                objectImages[3] = tmp;
            else
                objectImages[3] = LoadImageByBaseName("watermelon");
        }
        {
            var tmp = LoadImageByBaseName("watermelon");
            if (tmp != null)
                objectImagesAlt[3] = tmp;
            else
                objectImagesAlt[3] = objectImages[3];
        }
        objectImages[4] = LoadImageByBaseName("mango");
        {
            var tmp = LoadImageByBaseName("mangocut");
            if (tmp != null)
                objectImagesAlt[4] = tmp;
            else
                objectImagesAlt[4] = objectImages[4];
        }
        objectImages[5] = LoadImageByBaseName("Orange");
        objectImagesAlt[5] = objectImages[5];
        {
            var tmp = LoadImageByBaseName("wholekiwi");
            if (tmp != null)
                objectImages[6] = tmp;
            else
                objectImages[6] = LoadImageByBaseName("kiwi");
        }
        {
            var tmp = LoadImageByBaseName("kiwi");
            if (tmp != null)
                objectImagesAlt[6] = tmp;
            else
                objectImagesAlt[6] = objectImages[6];
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
        if (radialGestureMode)
            return;
        radialCursorPoint = e.Location;
    }

    private bool IsAnotherModalBlocking()
    {
        foreach (Form f in Application.OpenForms)
        {
            if (ReferenceEquals(f, this) || f == null || f.IsDisposed || !f.Visible)
                continue;
            if (f.Modal)
                return true;
        }
        return false;
    }

    private void TickCloseButtonGestureDwell()
    {
        if (shuttingDown)
            return;
        if (!radialGestureMode || closeGameButton == null || closeGameButton.IsDisposed || !closeGameButton.Visible)
            return;
        if (IsAnotherModalBlocking())
        {
            if (closeGestureHoverSince != DateTime.MinValue)
            {
                closeGestureHoverSince = DateTime.MinValue;
                Invalidate();
            }
            return;
        }

        if (gestureWristX < 0f || gestureWristY < 0f || float.IsNaN(gestureWristX) || float.IsNaN(gestureWristY))
        {
            if (closeGestureHoverSince != DateTime.MinValue)
                Invalidate();
            closeGestureHoverSince = DateTime.MinValue;
            return;
        }

        Rectangle closeHit = closeGameButton.Bounds;
        closeHit.Inflate(CloseButtonGestureInflatePx, CloseButtonGestureInflatePx);
        var finger = new Point((int)(gestureWristX * width), (int)(gestureWristY * height));

        if (!closeHit.Contains(finger))
        {
            if (closeGestureHoverSince != DateTime.MinValue)
                Invalidate();
            closeGestureHoverSince = DateTime.MinValue;
            return;
        }

        DateTime now = DateTime.Now;
        if (closeGestureHoverSince == DateTime.MinValue)
        {
            closeGestureHoverSince = now;
            Invalidate();
            return;
        }

        if ((now - closeGestureHoverSince).TotalMilliseconds >= CloseButtonGestureDwellMs)
        {
            closeGestureHoverSince = DateTime.MinValue;
            BeginInvoke((MethodInvoker)delegate
            {
                if (!shuttingDown)
                    Close();
            });
            return;
        }

        Invalidate();
    }

    private void DrawGestureOutsideRadialOverlay(Graphics g)
    {
        if (!radialGestureMode || radialMenuOpen) return;
        if (gestureWristX < 0f || gestureWristY < 0f || float.IsNaN(gestureWristX) || float.IsNaN(gestureWristY))
            return;

        int fx = (int)(gestureWristX * width);
        int fy = (int)(gestureWristY * height);

        float ringOuter = Math.Max(16f, Math.Min((float)Math.Min(width, height) * 0.038f, 28f));

        using (var fill = new SolidBrush(Color.FromArgb(95, 0, 230, 255)))
        using (var pen = new Pen(Color.White, 2))
        {
            float d = ringOuter * 2f;
            g.FillEllipse(fill, fx - ringOuter, fy - ringOuter, d, d);
            g.DrawEllipse(pen, fx - ringOuter, fy - ringOuter, d, d);
        }

        if (closeGameButton != null && !closeGameButton.IsDisposed && closeGestureHoverSince != DateTime.MinValue)
        {
            Rectangle wb = closeGameButton.Bounds;
            wb.Inflate(12, 12);
            double p = (DateTime.Now - closeGestureHoverSince).TotalMilliseconds / (double)CloseButtonGestureDwellMs;
            p = Math.Min(1.0, Math.Max(0.0, p));

            float start = -90f;
            float sweep = (float)(360.0 * p);
            using (var penArc = new Pen(Color.FromArgb(255, 40, 200, 80), 4))
            {
                penArc.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                penArc.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawArc(penArc, wb, start, sweep);
            }
        }
    }

    private void Form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        shuttingDown = true;

        // NOTE: The C# session-evaluation export to Documents\TUIO_Evaluation
        // has been removed. The Python GazeEvaluator already writes a richer
        // report (heatmap + scanpath + markdown) to gaze_reports/session_*/
        // on the same session-end, so this was duplicate work plus a noisy
        // "open folder?" popup. Per-user progress.txt and face_login.log
        // still live in Documents\TUIO_Evaluation — those are intentional.

        animationTimer.Stop();
        radialTimer.Stop();
        if (bluetoothPairingManager != null)
        {
            bluetoothPairingManager.Dispose();
            bluetoothPairingManager = null;
        }

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
        // Difficulty token intercept (11=Easy, 12=Medium, 13=Hard)
        if (o.SymbolID >= 11 && o.SymbolID <= 13)
        {
            string newDiff = o.SymbolID == 11 ? "easy" : (o.SymbolID == 12 ? "medium" : "hard");
            HCI_Lab_codes.Models.UserManager.UpdateDifficulty(userProfileKey, newDiff);
            
            // Provide feedback via KidPopup
            BeginInvoke((MethodInvoker)delegate {
                KidPopup.Show(this.client, "Difficulty Changed", $"Difficulty set to {newDiff.ToUpper()}");
            });
            return;
        }

        lock (objectList)
        {
            objectList[o.SessionID] = o;
        }
        if (verbose) Console.WriteLine("add obj " + o.SymbolID + " (" + o.SessionID + ")");
        PlayObjectSound(o.SymbolID);
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
                lock (evalLock)
                {
                    evalSuccessfulPlacements++;
                }
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
        levelStopwatch.Stop();
        double elapsedSeconds = levelStopwatch.Elapsed.TotalSeconds;
        int score = CalculateLearningRate(CurrentLevel.Targets.Count, elapsedSeconds);
        int completedIndex = currentLevelIndex;
        levelScores.Add(score);
        lock (evalLock)
        {
            evalLevelsCompletedEvents++;
            evalLevelScoresHistory.Add(score);
            evalLevelCompletionSeconds.Add(elapsedSeconds);
        }

        // Persist progress so the next level is unlocked the next time the user signs in.
        if (progressStore != null)
        {
            progressStore.RecordLevelCompleted(
                userProfileKey,
                isTeacher ? "teacher" : "student",
                completedIndex);
        }

        KidPopup.Show(this.client, "Great job!", CurrentLevel.Name + " completed!\n\nTime: " + elapsedSeconds.ToString("0.0") + " sec\nLearning rate: " + score + "%");

        int nextLevel = completedIndex + 1;
        if (nextLevel < levels.Count)
        {
            StartLevel(nextLevel);
            return;
        }

        int average = 0;
        if (levelScores.Count > 0)
            average = (int)Math.Round(levelScores.Average());

        KidPopup.Show(this.client, "You're a Star!", "All levels completed!\nAverage learning rate: " + average + "%\n\nGame will restart from Level 1.");

        levelScores.Clear();
        StartLevel(0);
    }

    private int CalculateLearningRate(int objectCount, double elapsedSeconds)
    {
        double expectedSeconds = objectCount * 10.0;
        double safeElapsed = Math.Max(1.0, elapsedSeconds);
        double percentage = (expectedSeconds / safeElapsed) * 100.0;
        if (percentage > 100.0) percentage = 100.0;
        if (percentage < 0.0) percentage = 0.0;
        return (int)Math.Round(percentage);
    }

    private void PlayObjectSound(int symbolId)
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
        if (string.IsNullOrEmpty(fullPath) || objectPlayer == null) return;

        try
        {
            objectPlayer.controls.stop();
            objectPlayer.URL = fullPath;
            objectPlayer.controls.play();
        }
        catch
        {
        }
    }

    private void DrawPlayfulBackground(Graphics g, int w, int h)
    {
        bool isNight = DateTime.Now.Hour >= 18 || DateTime.Now.Hour < 6;
        Color skyColor1 = isNight ? Color.FromArgb(10, 20, 50) : Color.FromArgb(135, 206, 235);
        Color skyColor2 = isNight ? Color.FromArgb(40, 50, 90) : Color.FromArgb(224, 255, 255);

        // Sky Gradient
        using (System.Drawing.Drawing2D.LinearGradientBrush skyBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(0, 0, w, h), skyColor1, skyColor2, 90f))
        {
            g.FillRectangle(skyBrush, 0, 0, w, h);
        }

        // Fluffy Clouds (darker if night)
        int cloudAlpha = isNight ? 100 : 200;
        using (SolidBrush cloudBrush = new SolidBrush(Color.FromArgb(cloudAlpha, 255, 255, 255)))
        {
            g.FillEllipse(cloudBrush, w * 0.1f, h * 0.1f, w * 0.15f, h * 0.1f);
            g.FillEllipse(cloudBrush, w * 0.15f, h * 0.08f, w * 0.15f, h * 0.12f);
            g.FillEllipse(cloudBrush, w * 0.22f, h * 0.1f, w * 0.12f, h * 0.09f);

            g.FillEllipse(cloudBrush, w * 0.7f, h * 0.2f, w * 0.15f, h * 0.1f);
            g.FillEllipse(cloudBrush, w * 0.75f, h * 0.18f, w * 0.15f, h * 0.12f);
            g.FillEllipse(cloudBrush, w * 0.82f, h * 0.2f, w * 0.12f, h * 0.09f);
        }

        // Rolling Hills (darker if night)
        Color hill1 = isNight ? Color.FromArgb(30, 80, 30) : Color.FromArgb(144, 238, 144);
        Color hill2 = isNight ? Color.FromArgb(40, 90, 40) : Color.FromArgb(152, 251, 152);
        using (SolidBrush hillBrush = new SolidBrush(hill1))
        {
            g.FillEllipse(hillBrush, -w * 0.2f, h * 0.6f, w * 0.8f, h * 0.6f);
        }
        using (SolidBrush hillBrush2 = new SolidBrush(hill2))
        {
            g.FillEllipse(hillBrush2, w * 0.4f, h * 0.55f, w * 0.8f, h * 0.6f);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        bool isNight = DateTime.Now.Hour >= 18 || DateTime.Now.Hour < 6;
        g.Clear(isNight ? Color.FromArgb(10, 20, 50) : Color.FromArgb(135, 206, 235));

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
        DrawPlacedObjects(g);
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
        DrawBluetoothPairingPanel(g);
        DrawGestureOutsideRadialOverlay(g);
    }

    private void InitializeBluetoothPairing()
    {
        bluetoothPairingManager = new BluetoothDevicePairingManager();
        bluetoothPairingManager.StatusMessage += OnBluetoothStatusMessage;
        bluetoothPairingManager.PairingStateChanged += OnBluetoothPairingStateChanged;
        bluetoothPairingManager.Start();
    }

    private void OnBluetoothStatusMessage(string message)
    {
        UpdateBluetoothStatus(message);
    }

    private void OnBluetoothPairingStateChanged(List<PairedBluetoothDevice> devices)
    {
        lock (bluetoothUiSync)
        {
            bluetoothDevices = devices;
        }

        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke((MethodInvoker)(() => Invalidate()));
        }
    }

    private void UpdateBluetoothStatus(string message)
    {
        lock (bluetoothUiSync)
        {
            bluetoothStatusMessage = message;
            bluetoothStatusAt = DateTime.Now;
        }

        Console.WriteLine("[Bluetooth] " + message);

        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke((MethodInvoker)(() => Invalidate()));
        }
    }

    private void ToggleBluetoothMenu()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke((MethodInvoker)delegate
        {
            bluetoothMenuOpen = !bluetoothMenuOpen;
            Invalidate();
        });
    }

    private void DrawBluetoothPairingPanel(Graphics g)
    {
        bool shouldDraw = false;
        if (bluetoothMenuOpen)
        {
            shouldDraw = true;
        }

        string statusToDraw = "";
        DateTime statusAtToDraw = DateTime.MinValue;
        List<PairedBluetoothDevice> devicesToDraw = new List<PairedBluetoothDevice>();
        lock (bluetoothUiSync)
        {
            statusToDraw = bluetoothStatusMessage;
            statusAtToDraw = bluetoothStatusAt;
            devicesToDraw = new List<PairedBluetoothDevice>(bluetoothDevices);
        }

        if (!shouldDraw)
        {
            if (!string.IsNullOrEmpty(statusToDraw))
            {
                double seconds = (DateTime.Now - statusAtToDraw).TotalSeconds;
                if (seconds < 4.0)
                {
                    shouldDraw = true;
                }
            }
        }

        if (!shouldDraw)
        {
            return;
        }

        int panelWidth = 470;
        int panelHeight = 330;
        int panelX = width - panelWidth - 20;
        int panelY = 120;
        Rectangle panelRect = new Rectangle(panelX, panelY, panelWidth, panelHeight);

        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(225, 25, 35, 55)))
        using (Pen borderPen = new Pen(Color.FromArgb(255, 120, 190, 255), 3))
        {
            g.FillRectangle(bgBrush, panelRect);
            g.DrawRectangle(borderPen, panelRect);
        }

        string mainDeviceName = Environment.MachineName;
        if (bluetoothPairingManager != null)
        {
            mainDeviceName = bluetoothPairingManager.MainDeviceName;
        }

        using (Font title = new Font("Arial", 16.0f, FontStyle.Bold))
        using (Font lineFont = new Font("Arial", 12.0f, FontStyle.Regular))
        using (SolidBrush textBrush = new SolidBrush(Color.White))
        using (SolidBrush connectedBrush = new SolidBrush(Color.LimeGreen))
        using (SolidBrush disconnectedBrush = new SolidBrush(Color.Orange))
        {
            g.DrawString("Bluetooth Pairing Menu", title, textBrush, panelX + 12, panelY + 10);
            g.DrawString("Main Device: " + mainDeviceName, lineFont, textBrush, panelX + 12, panelY + 46);

            int startY = panelY + 86;
            int rowHeight = 26;
            int index = 0;
            while (index < devicesToDraw.Count)
            {
                PairedBluetoothDevice device = devicesToDraw[index];
                int rowY = startY + (index * rowHeight);
                if (rowY > panelY + panelHeight - 56)
                {
                    break;
                }

                string stateText = "Existing";
                SolidBrush stateBrush = disconnectedBrush;
                if (device.IsConnected)
                {
                    stateText = "Detected";
                    stateBrush = connectedBrush;
                }

                string line = device.DeviceName;
                if (string.IsNullOrEmpty(line))
                {
                    line = device.MacKey;
                }

                g.DrawString(line, lineFont, textBrush, panelX + 12, rowY);
                g.DrawString(stateText, lineFont, stateBrush, panelX + panelWidth - 90, rowY);
                index++;
            }

            if (devicesToDraw.Count == 0)
            {
                g.DrawString("No Bluetooth devices discovered yet.", lineFont, disconnectedBrush, panelX + 12, startY);
            }

            if (!string.IsNullOrEmpty(statusToDraw))
            {
                g.DrawString(statusToDraw, lineFont, textBrush, panelX + 12, panelY + panelHeight - 32);
            }
        }
    }

    private void BuildRadialObjectData()
    {
        objectNames[0] = "Apple";
        objectNames[1] = "Banana";
        objectNames[2] = "Strawberry";
        objectNames[3] = "Watermelon";
        objectNames[4] = "Mango";
        objectNames[5] = "Orange";
        objectNames[6] = "Kiwi";

        objectColors[0] = "Red";
        objectColors[1] = "Yellow";
        objectColors[2] = "Red";
        objectColors[3] = "Green";
        objectColors[4] = "Orange";
        objectColors[5] = "Orange";
        objectColors[6] = "Green";

        objectBenefits[0] = "Apple helps keep you healthy.";
        objectBenefits[1] = "Banana gives you energy.";
        objectBenefits[2] = "Strawberry helps your skin stay healthy.";
        objectBenefits[3] = "Watermelon helps you stay hydrated.";
        objectBenefits[4] = "Mango helps your eyes stay strong.";
        objectBenefits[5] = "Orange helps your body fight colds.";
        objectBenefits[6] = "Kiwi helps your tummy feel happy.";

        objectColorAudio[0] = "apple_color.mp3";
        objectColorAudio[1] = "banana_color.mp3";
        objectColorAudio[2] = "straw_color.mp3";
        objectColorAudio[3] = "waterm_color.mp3";
        objectColorAudio[4] = "mango_color.mp3";
        objectColorAudio[5] = "orange_color.mp3";
        objectColorAudio[6] = "kiwi_color.mp3";
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
            TickCloseButtonGestureDwell();
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
            if (objectPlayer == null)
            {
                return false;
            }
            int state = (int)objectPlayer.playState;
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

    private HashSet<int> GetActiveObjectIds()
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

    private List<int> GetSortedActiveObjectIds()
    {
        var ids = GetActiveObjectIds().ToList();
        ids.Sort();
        return ids;
    }

    private void OpenRadialMenuLayer1()
    {
        bool wasClosed = !radialMenuOpen;
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
        closeGestureHoverSince = DateTime.MinValue;
        if (wasClosed)
        {
            lock (evalLock)
            {
                evalRadialOpens++;
            }
        }
        Invalidate();
    }

    private void CloseRadialMenu()
    {
        if (!radialMenuOpen)
            return;
        radialMenuOpen = false;
        radialLayer = "none";
        radialSubMode = "";
        radialCursorFollowsGesture = false;
        radialLabels.Clear();
        radialHoveredIndex = -1;
        radialHoverSince = DateTime.MinValue;
        radialSelectionLocked = false;
        lock (evalLock)
        {
            evalRadialCloses++;
        }
        closeGestureHoverSince = DateTime.MinValue;
        Invalidate();
    }

    private void BuildRadialLayer2Objects()
    {
        radialLabels.Clear();
        radialLabels.Add("Back");
        foreach (int id in GetSortedActiveObjectIds())
        {
            if (objectNames.ContainsKey(id))
            {
                radialLabels.Add(objectNames[id]);
            }
        }
    }

    private void BuildRadialLayer2Level()
    {
        radialLabels.Clear();
        radialLabels.Add("Back");
        radialLabels.Add("Restart Level");

        int unlocked = isTeacher
            ? levels.Count
            : progressStore.UnlockedLevels(userProfileKey, levels.Count);

        for (int i = 0; i < levels.Count; i++)
        {
            if (i < unlocked)
                radialLabels.Add("Go to Level " + (i + 1));
            else
                radialLabels.Add("Locked - Level " + (i + 1));
        }
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
                BuildRadialLayer2Objects();
                return;
            }
            if (label == "Info")
            {
                radialLayer = "layer2";
                radialSubMode = "info";
                BuildRadialLayer2Objects();
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

        foreach (int id in GetSortedActiveObjectIds())
        {
            if (!objectNames.ContainsKey(id))
            {
                continue;
            }
            if (objectNames[id] == label)
            {
                PlayObjectColorAudio(id);
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

        foreach (int id in GetSortedActiveObjectIds())
        {
            if (!objectNames.ContainsKey(id))
            {
                continue;
            }
            if (objectNames[id] == label)
            {
                if (objectBenefits.ContainsKey(id))
                {
                    SpeakSentence(objectBenefits[id]);
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

        const string lockedPrefix = "Locked - Level ";
        if (label.StartsWith(lockedPrefix, StringComparison.Ordinal))
        {
            SpeakSentence("This level is locked. Finish the earlier levels first.");
            return;
        }

        const string gotoPrefix = "Go to Level ";
        if (label.StartsWith(gotoPrefix, StringComparison.Ordinal) &&
            int.TryParse(label.Substring(gotoPrefix.Length).Trim(), out int lvl) &&
            lvl >= 1 && lvl <= levels.Count)
        {
            StartLevel(lvl - 1);
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
            if (objectPlayer != null)
            {
                objectPlayer.controls.stop();
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

    private void PlayObjectColorAudio(int objectId)
    {
        if (radialMuted)
        {
            return;
        }
        if (!objectColorAudio.ContainsKey(objectId))
        {
            return;
        }

        string mp3Name = objectColorAudio[objectId];
        string fullPath = GetAssetsPath(mp3Name);
        if (string.IsNullOrEmpty(fullPath))
        {
            return;
        }
        if (objectPlayer == null)
        {
            return;
        }

        try
        {
            objectPlayer.controls.stop();
            objectPlayer.URL = fullPath;
            objectPlayer.controls.play();
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
            if (objectPlayer == null)
            {
                return;
            }
            try
            {
                objectPlayer.controls.stop();
                objectPlayer.URL = lastMp3Path;
                objectPlayer.controls.play();
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
                // Draw faded ghost image of the object. When the player is
                // visibly enjoying the game (emotion = happy), show the
                // rotated/cut alt asset for every fruit that has one — the
                // board telegraphs that the slot wants a rotated marker.
                Image objectImg = null;
                if (IsPlayerContent() && objectImagesAlt.ContainsKey(slot.SymbolId))
                {
                    objectImg = objectImagesAlt[slot.SymbolId];
                }
                else if (objectImages.ContainsKey(slot.SymbolId))
                {
                    objectImg = objectImages[slot.SymbolId];
                }
                
                if (objectImg != null)
                {
                    System.Drawing.Imaging.ImageAttributes attrs = new System.Drawing.Imaging.ImageAttributes();
                    System.Drawing.Imaging.ColorMatrix matrix = new System.Drawing.Imaging.ColorMatrix();
                    
                    // Make it a dark silhouette with 40% opacity
                    matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = 0.2f; // Darken
                    matrix.Matrix33 = 0.4f; // 40% alpha
                    attrs.SetColorMatrix(matrix);
                    
                    g.DrawImage(objectImg, drawRect, 0, 0, objectImg.Width, objectImg.Height, GraphicsUnit.Pixel, attrs);
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

            string checkSuffix = slot.IsPlaced ? " (placed)" : "";
            string status = slot.ObjectName + checkSuffix;
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

    private void DrawPlacedObjects(Graphics g)
    {
        if (CurrentLevel == null) return;

        foreach (var slot in CurrentLevel.Targets)
        {
            if (!slot.IsPlaced) continue;
            // Render the placed fruit using whichever asset matches the
            // emotion-driven rotation mode. Falls back to the normal sprite
            // when the alt isn't available.
            Image placedImage = null;
            if (IsPlayerContent() && objectImagesAlt.ContainsKey(slot.SymbolId))
            {
                placedImage = objectImagesAlt[slot.SymbolId];
            }
            else if (objectImages.ContainsKey(slot.SymbolId))
            {
                placedImage = objectImages[slot.SymbolId];
            }
            if (placedImage == null) continue;

            RectangleF slotRect = GetSlotBounds(slot);
            Rectangle drawRect = Rectangle.Round(slotRect);

            // Draw the colored object over the silhouette area once correctly placed.
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
                if (UseAlternateImageForSymbol(tobj.SymbolID, isRotated90) && objectImagesAlt.ContainsKey(tobj.SymbolID))
                {
                    imgToDraw = objectImagesAlt[tobj.SymbolID];
                }
                else if (objectImages.ContainsKey(tobj.SymbolID))
                {
                    imgToDraw = objectImages[tobj.SymbolID];
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
        // Emotion-driven hard mode: when the player is visibly enjoying it,
        // we show alt (rotated/cut) images for every fruit that has one.
        // The on-screen art always reflects the current required orientation
        // so the player can see what the slot is asking for.
        bool needRotated = IsPlayerContent();
        if (isRotated90 != needRotated) return false;
        return needRotated
               && objectImagesAlt.ContainsKey(symbolId)
               && objectImages.ContainsKey(symbolId)
               && objectImagesAlt[symbolId] != null;
    }

    private bool IsValidPlacementState(int symbolId, bool isRotated90)
    {
        // Placement validity follows the same live emotion probe so the rule
        // matches what the player sees on the board: happy → must be rotated,
        // otherwise → must be upright. Stale or low-confidence emotion falls
        // back to the upright requirement.
        return isRotated90 == IsPlayerContent();
    }

    private void DrawHud(Graphics g)
    {
        if (CurrentLevel == null) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int placedCount = CurrentLevel.Targets.Count(t => t.IsPlaced);
        int totalCount = CurrentLevel.Targets.Count;
        double elapsed = levelStopwatch.IsRunning ? levelStopwatch.Elapsed.TotalSeconds : 0.0;

        string line1 = CurrentLevel.Name + "  |  Stars: " + placedCount + "/" + totalCount;
        string line2 = "Time: " + elapsed.ToString("0.0") + " sec  |  Place objects in the matching silhouettes!";
        string line3 = "Magic Hands: circle=open | square=close | open_hand=select | triangle=Bluetooth menu";
        if (radialGestureMode)
            line3 += "  Corner X: hold index ~1 sec to quit.";

        string adaptiveHint = null;
        bool showAdaptiveHint = TryGetAdaptivePlacementHint(out adaptiveHint);

        float hudHeight = showAdaptiveHint ? 128f : (radialGestureMode ? 110f : 95f);

        // Draw Bubbly HUD
        RectangleF hudRect = new RectangleF(15, 15, width - 30, hudHeight);
        using (System.Drawing.Drawing2D.GraphicsPath path = GetRoundedRect(hudRect, 20))
        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
        using (Pen borderPen = new Pen(Color.FromArgb(255, 150, 200, 255), 6))
        {
            g.FillPath(bgBrush, path);
            g.DrawPath(borderPen, path);
        }

        if (showAdaptiveHint)
        {
            RectangleF strip = new RectangleF(hudRect.X + 8, hudRect.Bottom - 34, hudRect.Width - 16, 28);
            using (System.Drawing.Drawing2D.GraphicsPath stripPath = GetRoundedRect(strip, 12))
            using (SolidBrush amber = new SolidBrush(Color.FromArgb(235, 255, 248, 220)))
            using (Pen amberBorder = new Pen(Color.FromArgb(255, 230, 160, 60), 2))
            {
                g.FillPath(amber, stripPath);
                g.DrawPath(amberBorder, stripPath);
            }
            using (var hintFont = new Font("Comic Sans MS", 11F, FontStyle.Bold))
            using (var hintBrush = new SolidBrush(Color.FromArgb(255, 140, 75, 20)))
            {
                RectangleF hintTextRect = new RectangleF(strip.X + 10, strip.Y + 5, strip.Width - 20, strip.Height - 8);
                g.DrawString(adaptiveHint, hintFont, hintBrush, hintTextRect);
            }
        }

        using (var darkFont = new Font("Comic Sans MS", 16F, FontStyle.Bold))
        using (var midFont = new Font("Comic Sans MS", 12F, FontStyle.Bold))
        {
            g.DrawString(line1, darkFont, Brushes.DarkSlateBlue, 30, 22);
            g.DrawString(line2, midFont, Brushes.DarkSlateGray, 30, 52);
            g.DrawString(line3, midFont, Brushes.DimGray, 30, 77);
        }

        if (proximityWarning == "too_close")
        {
            using (var alertFont = new Font("Comic Sans MS", 24F, FontStyle.Bold))
            {
                string warn = "You are too close to the screen! Please step back.";
                SizeF sz = g.MeasureString(warn, alertFont);
                g.FillRectangle(new SolidBrush(Color.FromArgb(180, 255, 0, 0)), width / 2 - sz.Width / 2 - 20, height / 2 - sz.Height / 2 - 20, sz.Width + 40, sz.Height + 40);
                g.DrawString(warn, alertFont, Brushes.White, width / 2 - sz.Width / 2, height / 2 - sz.Height / 2);
            }
        }
        else if (proximityWarning == "too_far")
        {
            using (var alertFont = new Font("Comic Sans MS", 24F, FontStyle.Bold))
            {
                string warn = "You are too far! Please step closer.";
                SizeF sz = g.MeasureString(warn, alertFont);
                g.FillRectangle(new SolidBrush(Color.FromArgb(180, 255, 165, 0)), width / 2 - sz.Width / 2 - 20, height / 2 - sz.Height / 2 - 20, sz.Width + 40, sz.Height + 40);
                g.DrawString(warn, alertFont, Brushes.White, width / 2 - sz.Width / 2, height / 2 - sz.Height / 2);
            }
        }

        // ── Adaptive-hints indicator chip ─────────────────────────────────
        // Shows a small chip in the top-right of the HUD so the teacher /
        // student can SEE that prior-session data is shaping this run.
        if (adaptiveHints != null && adaptiveHints.WasLoaded)
        {
            string chip = adaptiveHints.ShortSummary();
            using (var chipFont = new Font("Comic Sans MS", 9.5F, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(chip, chipFont);
                float chipW = sz.Width + 22;
                float chipH = sz.Height + 8;
                float chipX = width - chipW - 70;   // leave room for the close button
                float chipY = 22;
                RectangleF chipRect = new RectangleF(chipX, chipY, chipW, chipH);
                using (System.Drawing.Drawing2D.GraphicsPath cp = GetRoundedRect(chipRect, 10))
                using (SolidBrush chipBg = new SolidBrush(Color.FromArgb(230, 60, 120, 200)))
                using (Pen chipBorder = new Pen(Color.FromArgb(255, 90, 160, 240), 2))
                {
                    g.FillPath(chipBg, cp);
                    g.DrawPath(chipBorder, cp);
                }
                g.DrawString(chip, chipFont, Brushes.White, chipX + 10, chipY + 3);
            }
        }


        // Gaze Drawing
        if (currentGazePoint.X >= 0 && currentGazePoint.Y >= 0)
        {
            int gx = (int)(currentGazePoint.X * width);
            int gy = (int)(currentGazePoint.Y * height);
            using (Pen gazePen = new Pen(Color.FromArgb(100, 255, 255, 0), 4))
            {
                g.DrawEllipse(gazePen, gx - 15, gy - 15, 30, 30);
            }
            
            // Highlight silhouette nearest to gaze
            TargetSlot nearestSlot = null;
            float minDist = float.MaxValue;
            foreach (var slot in CurrentLevel.Targets)
            {
                if (slot.IsPlaced) continue;
                float dx = (slot.XNormalized * width) - gx;
                float dy = (slot.YNormalized * height) - gy;
                float dist = (dx * dx) + (dy * dy);
                if (dist < minDist) { minDist = dist; nearestSlot = slot; }
            }
            if (nearestSlot != null && minDist < 30000) // approx 170px radius
            {
                int sx = (int)(nearestSlot.XNormalized * width);
                int sy = (int)(nearestSlot.YNormalized * height);
                using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 0)))
                {
                    g.FillEllipse(glowBrush, sx - 60, sy - 60, 120, 120);
                }
            }
        }

        // Emotion Indicator (Bubbly corner)
        bool emotionRecent = (DateTime.Now - emotionLastUpdate).TotalSeconds < 14.0;
        if (emotionRecent && !string.IsNullOrEmpty(currentEmotionLabel))
        {
            Color hintColor;
            switch (currentDifficultyHint)
            {
                case "easier": hintColor = Color.FromArgb(255, 200, 50); break;  // Gold/Orange
                case "harder": hintColor = Color.FromArgb(80, 200, 80); break;   // Green
                default:       hintColor = Color.FromArgb(180, 180, 180); break; // Grey
            }

            string hintMsg = "";
            string em = currentEmotionLabel.Trim().ToLowerInvariant();
            if (em == "happy") { hintMsg = "Keep it up!"; }
            else if (em == "sad") { hintMsg = "Cheer up, you got this!"; }
            else if (em == "angry") { hintMsg = "Take a deep breath, we'll help."; }
            else if (em == "surprise") { hintMsg = "Wow! Great job!"; }
            else if (em == "fear") { hintMsg = "It's okay, try again!"; }
            else if (em == "disgust") { hintMsg = "Let's make this simpler, check the hint."; }
            else { hintMsg = "You're doing fine!"; }

            string emotionText = "Mood: " + currentEmotionLabel.ToUpper() + "\n" + hintMsg;
            string cornerTip = "";
            if (TryGetAdaptivePlacementHint(out cornerTip))
                emotionText += "\n\nTip: " + cornerTip;

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

        LayoutCloseButton();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutCloseButton();
        if (this.ClientSize.Width > 0 && this.ClientSize.Height > 0)
        {
            width = this.ClientSize.Width;
            height = this.ClientSize.Height;
            this.Invalidate();
        }
    }
    [STAThread]
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
            string profileKey = string.IsNullOrWhiteSpace(login.UserProfileKey)
                ? (login.IsTeacher ? "teacher:default" : "student:default")
                : login.UserProfileKey.Trim();

            if (login.IsTeacher)
            {
                // Teachers see the zero-touch dashboard, not the game
                Application.Run(new TeacherDashboardForm(port, profileKey));
            }
            else
            {
                var store = UserProgressStore.Load();
                store.EnsureProfile(profileKey, "student");
                Application.Run(new TuioDemo(port, login.UseRadialGestureMode, false, profileKey, store));
            }
        }
    }
}

// Custom kid-friendly popup to replace boring MessageBox
public class KidPopup : Form, TuioListener
{
    private Label lblTitle;
    private Label lblMessage;
    private Button btnOk;
    private TuioClient _client;
    // Minimum visible time before any TUIO event can dismiss the popup.
    // Without this, the bridge's IoU tracker drops a fruit's sid every ~1s
    // and re-creates it, which fires addTuioObject and instantly closes the
    // "Great job!" celebration the user came to see.
    private DateTime _shownAt;
    private static readonly TimeSpan _MinVisible = TimeSpan.FromSeconds(2.0);
    // Backstop auto-close so the user isn't required to scan a marker — the
    // popup will close itself after this window even if no event fires.
    private static readonly TimeSpan _AutoClose  = TimeSpan.FromSeconds(5.0);

    public KidPopup(TuioClient client, string title, string message)
    {
        _client = client;
        if (_client != null)
        {
            _client.addTuioListener(this);
        }

        this.FormBorderStyle = FormBorderStyle.None;
        this.ClientSize = new Size(400, 250);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.White;
        this.DoubleBuffered = true;

        // Auto-close after _AutoClose. Use the Shown event to start the timer
        // so we don't begin counting before the form actually paints.
        this.Shown += (s, e) =>
        {
            _shownAt = DateTime.Now;
            // Fully-qualified to disambiguate from System.Threading.Timer,
            // which is also in scope via other usings in this file.
            var timer = new System.Windows.Forms.Timer { Interval = (int)_AutoClose.TotalMilliseconds };
            timer.Tick += (ts, te) =>
            {
                timer.Stop();
                timer.Dispose();
                if (this.IsHandleCreated && !this.IsDisposed)
                    this.Close();
            };
            timer.Start();
        };

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
        lblMessage.Text = message + "\n\n(Scan any marker to continue)";
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
        
        // Hide button since we want marker scan only
        btnOk.Visible = false;

        this.Controls.Add(lblTitle);
        this.Controls.Add(lblMessage);
        this.Controls.Add(btnOk);
    }
    
    protected override void OnFormClosed(FormClosedEventArgs e)
    {

        if (_client != null)
        {
            _client.removeTuioListener(this);
        }
        base.OnFormClosed(e);
    }

    public void addTuioObject(TuioObject tobj)
    {
        // Ignore any TUIO event in the first _MinVisible seconds — the IoU
        // tracker in yolo_tuio_bridge re-issues sids when fruits drift in/out
        // of frame, so the very same fruit that finished the level would
        // otherwise instantly dismiss the celebration popup.
        if (_shownAt == DateTime.MinValue || (DateTime.Now - _shownAt) < _MinVisible)
            return;
        if (this.IsHandleCreated && !this.IsDisposed)
        {
            this.Invoke((MethodInvoker)delegate {
                this.Close();
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

    public static void Show(TuioClient client, string title, string message)
    {
        using (var form = new KidPopup(client, title, message))
        {
            form.ShowDialog();
        }
    }
}
}