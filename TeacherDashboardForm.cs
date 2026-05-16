using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GestureClient;
using HCI_Lab_codes.Models;
using TUIO;

namespace TuioDemoApp
{
    /// <summary>
    /// Zero-touch teacher dashboard.
    /// PRIMARY cursor  : skeleton hand (index finger tip) — point + 1s dwell or pinch to select.
    /// FALLBACK cursor : gaze dot (when hand is not in frame).
    /// TUIO tokens 11 (Easy) / 12 (Medium) / 13 (Hard) update the selected student's difficulty.
    /// Press Escape to close.
    /// </summary>
    public class TeacherDashboardForm : Form, IGestureListener, TuioListener
    {
        // ── Data ──────────────────────────────────────────────────────────────
        private readonly string teacherProfileKey;
        private List<User> students = new List<User>();
        private User selectedStudent  = null;
        private User hoveredStudent   = null;   // updated each paint; used by pinch-select

        // ── TUIO ──────────────────────────────────────────────────────────────
        private TuioClient tuioClient;

        // ── Gesture / cursor ─────────────────────────────────────────────────
        private GestureSocketClient gestureClient;

        // Unified cursor position (skeleton primary, gaze fallback).
        private int cursorX = -1;
        private int cursorY = -1;

        // Gaze stored separately so we can draw it as a secondary indicator.
        private int gazeX = -1;
        private int gazeY = -1;

        // Skeleton times out after this many ms → fall back to gaze cursor.
        private DateTime   lastSkeletonTime  = DateTime.MinValue;
        private const int  SKELETON_TIMEOUT_MS = 600;
        private bool       cursorFromSkeleton  = false;

        private string proximityStatus = "ok";

        // ── Dwell ─────────────────────────────────────────────────────────────
        private DateTime dwellStartTime  = DateTime.MinValue;
        private string   currentHoveredId = null;
        private const double DWELL_SECONDS = 1.0;   // shorter than gaze — hand is precise

        // ── Feedback banner ───────────────────────────────────────────────────
        private string   bannerText   = "";
        private DateTime bannerExpiry = DateTime.MinValue;

        // ── Delete-arming state ───────────────────────────────────────────────
        // Marker 17 places → enters "armed" state for DELETE_WINDOW_SECONDS.
        // Within the window, a PINCH gesture confirms the deletion.
        // The marker being lifted, the window expiring, or any other action
        // cancels the armed state.
        private bool       deleteArmed       = false;
        private DateTime   deleteArmedAt     = DateTime.MinValue;
        private User       deleteArmedTarget = null;
        private const double DELETE_WINDOW_SECONDS = 5.0;

        // ── Layout constants ──────────────────────────────────────────────────
        private const int TILE_W   = 280;
        private const int TILE_H   = 180;
        private const int TILE_GAP = 20;
        private const int TILE_X0  = 40;
        private const int TILE_Y0  = 130;
        private const int DETAIL_X = 900;

        // ── Fonts ─────────────────────────────────────────────────────────────
        private readonly Font fontTitle   = new Font("Segoe UI", 22F, FontStyle.Bold);
        private readonly Font fontHint    = new Font("Segoe UI", 11F);
        private readonly Font fontTile    = new Font("Segoe UI", 14F, FontStyle.Bold);
        private readonly Font fontSub     = new Font("Segoe UI", 10F);
        private readonly Font fontDetail  = new Font("Segoe UI", 13F, FontStyle.Bold);
        private readonly Font fontDetail2 = new Font("Segoe UI", 11F);

        // ─────────────────────────────────────────────────────────────────────
        public TeacherDashboardForm(int tuioPort, string teacherKey)
        {
            teacherProfileKey = teacherKey;
            Text            = "Teacher Dashboard";
            WindowState     = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor       = Color.FromArgb(28, 32, 40);
            DoubleBuffered  = true;
            KeyPreview      = true;

            UserManager.LoadUsers();
            students = UserManager.GetAllUsers();

            // TUIO — difficulty tokens
            try
            {
                tuioClient = new TuioClient(tuioPort);
                tuioClient.addTuioListener(this);
                tuioClient.connect();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Dashboard] TUIO connect failed: " + ex.Message);
            }

            // Gesture socket — skeleton hand + gaze fallback
            gestureClient = new GestureSocketClient("127.0.0.1", 5000);
            gestureClient.AddListener(this);
            try { gestureClient.Connect(); }
            catch { Console.WriteLine("[Dashboard] GestureServer not running — hand cursor disabled."); }

            // Repaint at ~60 fps for smooth dwell arc and cursor movement
            var timer = new Timer { Interval = 16 };
            timer.Tick += (s, e) =>
            {
                // Expire skeleton cursor if hand leaves frame
                if (cursorFromSkeleton &&
                    (DateTime.Now - lastSkeletonTime).TotalMilliseconds > SKELETON_TIMEOUT_MS)
                {
                    cursorFromSkeleton = false;
                    // Fall back to gaze position
                    if (gazeX > 0) { cursorX = gazeX; cursorY = gazeY; }
                    else           { cursorX = -1;    cursorY = -1;    }
                }
                TickDeleteArmedTimeout();
                Invalidate();
            };
            timer.Start();

            // ESC is the only keyboard shortcut (emergency close). All CRUD is
            // performed via TUIO markers + hand gestures — see ApplyDifficulty,
            // BumpAge, CycleRole, ArmDelete below.
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosing += OnFormClosing;
        }

        // ── Painting ──────────────────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DrawHeader(g);
            DrawStudentGrid(g);
            DrawDetailPanel(g);
            DrawProximityBanner(g);
            DrawFeedbackBanner(g);
            DrawDeleteArmedOverlay(g);
            DrawCursor(g);
        }

        /// <summary>
        /// Full-width red banner shown while a deletion is armed.
        /// Shows the target name and a shrinking countdown bar so the
        /// teacher can see how long they have to pinch-confirm.
        /// </summary>
        private void DrawDeleteArmedOverlay(Graphics g)
        {
            if (!deleteArmed || deleteArmedTarget == null) return;

            double elapsed   = (DateTime.Now - deleteArmedAt).TotalSeconds;
            double remaining = Math.Max(0, DELETE_WINDOW_SECONDS - elapsed);
            double frac      = remaining / DELETE_WINDOW_SECONDS;

            // Banner background (translucent red across the top)
            var bannerRect = new Rectangle(0, 105, Width, 80);
            using (var br = new SolidBrush(Color.FromArgb(220, 180, 30, 30)))
                g.FillRectangle(br, bannerRect);

            // Main message
            string msg = $"⚠  DELETE {deleteArmedTarget.DisplayName}?  PINCH TO CONFIRM  ({remaining:0.0}s)";
            var sz = TextRenderer.MeasureText(msg, fontDetail);
            TextRenderer.DrawText(g, msg, fontDetail,
                new Point(Width / 2 - sz.Width / 2, 122), Color.White);

            // Countdown bar
            int barH = 8;
            int barW = (int)(Width * frac);
            using (var br = new SolidBrush(Color.White))
                g.FillRectangle(br, 0, bannerRect.Bottom - barH, barW, barH);
        }

        private void DrawHeader(Graphics g)
        {
            TextRenderer.DrawText(g, "TEACHER DASHBOARD", fontTitle,
                new Point(TILE_X0, 20), Color.White);
            TextRenderer.DrawText(g,
                "Point at tile + hold 1s (or pinch) to select.   " +
                "Markers:  11/12/13 = Easy/Med/Hard   14/15 = Age ±1   16 = Cycle role   " +
                "17 = Delete (then pinch to confirm).   ESC exit",
                fontHint, new Point(TILE_X0, 68), Color.FromArgb(160, 170, 190));

            // Cursor mode indicator (top-right)
            string modeLabel = cursorFromSkeleton ? "[ HAND CURSOR ]" : "[ GAZE CURSOR ]";
            Color  modeColor = cursorFromSkeleton ? Color.Cyan : Color.FromArgb(255, 100, 100);
            TextRenderer.DrawText(g, modeLabel, fontHint,
                new Point(Width - 200, 20), modeColor);

            g.DrawLine(new Pen(Color.FromArgb(60, 70, 90), 1), TILE_X0, 105, Width - 40, 105);
        }

        private void DrawStudentGrid(Graphics g)
        {
            string newHoveredId  = null;
            User   newHoveredUser = null;

            int childIndex = 0;
            foreach (var s in students)
            {
                if (s.Role != "child") continue;

                int col  = childIndex % 2;
                int row  = childIndex / 2;
                int x    = TILE_X0 + col * (TILE_W + TILE_GAP);
                int y    = TILE_Y0 + row * (TILE_H + TILE_GAP);
                var rect = new Rectangle(x, y, TILE_W, TILE_H);

                bool isHovered  = cursorX >= x && cursorX <= x + TILE_W &&
                                  cursorY >= y && cursorY <= y + TILE_H;
                bool isSelected = selectedStudent != null && selectedStudent.FaceId == s.FaceId;
                if (isHovered) { newHoveredId = s.FaceId; newHoveredUser = s; }

                // Tile background
                Color bgCol = isSelected ? Color.FromArgb(30, 80, 50) : Color.FromArgb(48, 54, 66);
                using (var br = new SolidBrush(bgCol)) g.FillRectangle(br, rect);

                // Dwell progress arc rising from bottom
                if (isHovered && currentHoveredId == s.FaceId && dwellStartTime != DateTime.MinValue)
                {
                    double progress = Math.Min(1.0,
                        (DateTime.Now - dwellStartTime).TotalSeconds / DWELL_SECONDS);
                    int fillH = (int)(TILE_H * progress);
                    var fillRect = new Rectangle(x, y + TILE_H - fillH, TILE_W, fillH);
                    using (var br = new SolidBrush(Color.FromArgb(80, 80, 220, 120)))
                        g.FillRectangle(br, fillRect);

                    if (progress >= 1.0)
                        SelectStudent(s);
                }

                // Border
                Color borderCol = isSelected ? Color.Lime
                                : isHovered  ? Color.Cyan
                                :              Color.FromArgb(70, 80, 100);
                int borderW = (isHovered || isSelected) ? 3 : 1;
                using (var pen = new Pen(borderCol, borderW)) g.DrawRectangle(pen, rect);

                // Difficulty dot
                Color diffCol = DifficultyColor(s.Difficulty);
                g.FillEllipse(new SolidBrush(diffCol), x + TILE_W - 28, y + 10, 18, 18);

                // Text
                TextRenderer.DrawText(g, s.DisplayName.ToUpper(), fontTile,
                    new Point(x + 14, y + 14), Color.White);
                TextRenderer.DrawText(g,
                    $"Difficulty: {s.Difficulty.ToUpper()}\nAge: {s.Age}",
                    fontSub, new Point(x + 14, y + 50), Color.FromArgb(200, 210, 225));

                var sessions = SessionManager.GetSessionsForUser(s.FaceId);
                TextRenderer.DrawText(g, $"{sessions.Count} session{(sessions.Count == 1 ? "" : "s")}",
                    fontSub, new Point(x + 14, y + TILE_H - 32), Color.FromArgb(140, 150, 170));

                childIndex++;
            }

            // Update hover tracking
            if (newHoveredId != currentHoveredId)
            {
                currentHoveredId = newHoveredId;
                dwellStartTime   = newHoveredId != null ? DateTime.Now : DateTime.MinValue;
            }
            hoveredStudent = newHoveredUser;
        }

        private void DrawDetailPanel(Graphics g)
        {
            int panelW   = Width - DETAIL_X - 30;
            var panelRect = new Rectangle(DETAIL_X, 115, panelW, Height - 145);

            using (var br = new SolidBrush(Color.FromArgb(38, 44, 56)))
                g.FillRectangle(br, panelRect);
            using (var pen = new Pen(Color.FromArgb(60, 70, 90), 1))
                g.DrawRectangle(pen, panelRect);

            if (selectedStudent == null)
            {
                string hint = "Point at a student tile\nand hold 1 s (or pinch)\nto see their details here.";
                TextRenderer.DrawText(g, hint, fontDetail2,
                    new Rectangle(DETAIL_X + 20, 250, panelW - 40, 200),
                    Color.FromArgb(100, 110, 130),
                    TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);
                return;
            }

            var s   = selectedStudent;
            int tx  = DETAIL_X + 20;
            int ty  = 130;

            TextRenderer.DrawText(g, s.DisplayName,              fontDetail,  new Point(tx, ty),      Color.White);
            TextRenderer.DrawText(g, $"Role: {s.Role}",          fontDetail2, new Point(tx, ty + 34),  Color.FromArgb(170, 180, 200));
            TextRenderer.DrawText(g, $"Age: {s.Age}",            fontDetail2, new Point(tx, ty + 58),  Color.FromArgb(170, 180, 200));
            TextRenderer.DrawText(g, $"Face ID: {s.FaceId}",     fontDetail2, new Point(tx, ty + 82),  Color.FromArgb(120, 130, 150));

            TextRenderer.DrawText(g, "Difficulty:", fontDetail2, new Point(tx, ty + 116), Color.FromArgb(170, 180, 200));
            string diff = s.Difficulty?.ToUpper() ?? "EASY";
            using (var br = new SolidBrush(DifficultyColor(s.Difficulty)))
                g.FillRectangle(br, new Rectangle(tx + 90, ty + 120, 90, 22));
            TextRenderer.DrawText(g, diff, fontDetail2,
                new Rectangle(tx + 90, ty + 120, 90, 22),
                Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, "── Recent Sessions ──", fontDetail2,
                new Point(tx, ty + 158), Color.FromArgb(100, 110, 130));

            var sessions = SessionManager.GetSessionsForUser(s.FaceId);
            if (sessions.Count == 0)
            {
                TextRenderer.DrawText(g, "No sessions yet.", fontDetail2,
                    new Point(tx, ty + 182), Color.FromArgb(90, 100, 120));
            }
            else
            {
                int sy = ty + 182, shown = 0;
                for (int i = sessions.Count - 1; i >= 0 && shown < 5; i--, shown++)
                {
                    var sess = sessions[i];
                    string line = $"• {sess.Date}  ✓{sess.Correct} ✗{sess.Wrong}  {sess.DurationMin:0.0}min";
                    TextRenderer.DrawText(g, line, fontDetail2, new Point(tx, sy),
                        Color.FromArgb(170, 180, 200));
                    sy += 26;
                }
            }

            TextRenderer.DrawText(g, "Place TUIO token to change difficulty:", fontDetail2,
                new Point(tx, Height - 130), Color.FromArgb(130, 140, 160));
            DrawDifficultyLegend(g, tx, Height - 105);
        }

        private void DrawDifficultyLegend(Graphics g, int x, int y)
        {
            string[] labels = { "11 = Easy", "12 = Medium", "13 = Hard" };
            Color[]  cols   = { Color.LimeGreen, Color.Gold, Color.OrangeRed };
            for (int i = 0; i < 3; i++)
            {
                g.FillEllipse(new SolidBrush(cols[i]), x + i * 120, y, 14, 14);
                TextRenderer.DrawText(g, labels[i], fontSub,
                    new Point(x + i * 120 + 18, y - 1), cols[i]);
            }
        }

        private void DrawProximityBanner(Graphics g)
        {
            if (proximityStatus == "ok" || proximityStatus == "unknown") return;
            string msg = proximityStatus == "too_close" ? "📏  Please step back!" : "👋  Come a little closer!";
            Color  col = proximityStatus == "too_close"
                ? Color.FromArgb(200, 200, 0, 0) : Color.FromArgb(200, 200, 130, 0);
            var sz   = TextRenderer.MeasureText(msg, fontDetail);
            var rect = new Rectangle(Width / 2 - sz.Width / 2 - 20, Height - 80, sz.Width + 40, sz.Height + 16);
            using (var br = new SolidBrush(col)) g.FillRectangle(br, rect);
            TextRenderer.DrawText(g, msg, fontDetail, new Point(rect.X + 20, rect.Y + 8), Color.White);
        }

        private void DrawFeedbackBanner(Graphics g)
        {
            if (string.IsNullOrEmpty(bannerText) || DateTime.Now > bannerExpiry) return;
            var sz   = TextRenderer.MeasureText(bannerText, fontDetail);
            var rect = new Rectangle(Width / 2 - sz.Width / 2 - 24, 115, sz.Width + 48, sz.Height + 18);
            using (var br = new SolidBrush(Color.FromArgb(210, 30, 90, 40))) g.FillRectangle(br, rect);
            g.DrawRectangle(new Pen(Color.LimeGreen, 2), rect);
            TextRenderer.DrawText(g, bannerText, fontDetail, new Point(rect.X + 24, rect.Y + 9), Color.LimeGreen);
        }

        /// <summary>
        /// Draws the active cursor.
        /// Skeleton hand = cyan crosshair + ring (primary, precise).
        /// Gaze fallback = small semi-transparent red dot.
        /// </summary>
        private void DrawCursor(Graphics g)
        {
            // Show gaze as a small secondary dot when skeleton hand is active (so teacher can see both)
            if (gazeX > 0 && gazeY > 0 && cursorFromSkeleton)
            {
                // tiny gaze dot (secondary, behind hand cursor)
                g.FillEllipse(new SolidBrush(Color.FromArgb(90, 255, 80, 80)),
                    gazeX - 6, gazeY - 6, 12, 12);
            }

            if (cursorX <= 0 || cursorY <= 0) return;

            if (cursorFromSkeleton)
            {
                // ── Skeleton hand cursor: cyan ring + crosshair ──
                int r = 16;
                g.FillEllipse(new SolidBrush(Color.FromArgb(60, 0, 255, 255)),
                    cursorX - r, cursorY - r, r * 2, r * 2);
                g.DrawEllipse(new Pen(Color.Cyan, 2),
                    cursorX - r, cursorY - r, r * 2, r * 2);
                // crosshair lines
                int arm = 24;
                var p = new Pen(Color.Cyan, 2);
                g.DrawLine(p, cursorX - arm, cursorY, cursorX - r - 2, cursorY);
                g.DrawLine(p, cursorX + r + 2, cursorY, cursorX + arm, cursorY);
                g.DrawLine(p, cursorX, cursorY - arm, cursorX, cursorY - r - 2);
                g.DrawLine(p, cursorX, cursorY + r + 2, cursorX, cursorY + arm);
                // dwell ring shows progress
                if (currentHoveredId != null && dwellStartTime != DateTime.MinValue)
                {
                    double progress = Math.Min(1.0,
                        (DateTime.Now - dwellStartTime).TotalSeconds / DWELL_SECONDS);
                    int sweepAngle = (int)(360 * progress);
                    using (var arcPen = new Pen(Color.LimeGreen, 4))
                        g.DrawArc(arcPen, cursorX - r - 6, cursorY - r - 6,
                            (r + 6) * 2, (r + 6) * 2, -90, sweepAngle);
                }
            }
            else
            {
                // ── Gaze fallback cursor: red dot + ring ──
                int r = 10;
                g.FillEllipse(new SolidBrush(Color.FromArgb(160, 255, 80, 80)),
                    cursorX - r, cursorY - r, r * 2, r * 2);
                g.DrawEllipse(new Pen(Color.White, 1),
                    cursorX - r, cursorY - r, r * 2, r * 2);
                if (currentHoveredId != null && dwellStartTime != DateTime.MinValue)
                {
                    double progress = Math.Min(1.0,
                        (DateTime.Now - dwellStartTime).TotalSeconds / DWELL_SECONDS);
                    int sweepAngle = (int)(360 * progress);
                    using (var arcPen = new Pen(Color.LimeGreen, 3))
                        g.DrawArc(arcPen, cursorX - r - 5, cursorY - r - 5,
                            (r + 5) * 2, (r + 5) * 2, -90, sweepAngle);
                }
            }
        }

        // ── Student selection ─────────────────────────────────────────────────
        private void SelectStudent(User s)
        {
            if (selectedStudent?.FaceId == s.FaceId) return;

            // Auto-cancel any armed deletion when the selection changes,
            // so the next pinch never deletes the wrong student.
            if (deleteArmed) CancelDelete("Delete cancelled (selection changed)");

            selectedStudent = s;
            dwellStartTime  = DateTime.MinValue;
            ShowBanner($"Selected: {s.DisplayName}");
        }

        /// <summary>Called by pinch gesture — immediately selects the hovered tile.</summary>
        private void InstantSelect()
        {
            if (hoveredStudent != null)
                SelectStudent(hoveredStudent);
            else
                ShowBanner("Point at a student tile first");
        }

        // ── UPDATE: difficulty (marker 11 / 12 / 13) ──────────────────────────
        private void ApplyDifficulty(string difficulty)
        {
            if (selectedStudent == null)
            {
                ShowBanner("Select a student first (point + hold or pinch)");
                return;
            }
            UserManager.UpdateDifficulty(selectedStudent.FaceId, difficulty);
            selectedStudent.Difficulty = difficulty;
            ShowBanner($"{selectedStudent.DisplayName} → {difficulty.ToUpper()}");
        }

        // ── UPDATE: age (marker 14 = +1, marker 15 = −1) ─────────────────────
        private void BumpAge(int delta)
        {
            if (selectedStudent == null)
            {
                ShowBanner("Select a student first (point + hold or pinch)");
                return;
            }
            int newAge = Math.Max(1, Math.Min(120, selectedStudent.Age + delta));
            if (newAge == selectedStudent.Age) return;
            selectedStudent.Age = newAge;
            UserManager.UpdateUser(selectedStudent);
            ShowBanner($"{selectedStudent.DisplayName}: age → {newAge}");
        }

        // ── UPDATE: role cycle (marker 16 → child → teacher → admin → …) ─────
        private void CycleRole()
        {
            if (selectedStudent == null)
            {
                ShowBanner("Select a student first (point + hold or pinch)");
                return;
            }
            string next;
            switch ((selectedStudent.Role ?? "child").ToLower())
            {
                case "child":   next = "teacher"; break;
                case "teacher": next = "admin";   break;
                default:        next = "child";   break;
            }
            selectedStudent.Role = next;
            UserManager.UpdateUser(selectedStudent);
            ShowBanner($"{selectedStudent.DisplayName} → role: {next.ToUpper()}");
        }

        // ── DELETE: two-step touchless confirmation ──────────────────────────
        // Step 1: marker 17 placed → arms the delete for DELETE_WINDOW_SECONDS.
        // Step 2: teacher pinches within the window → deletion is committed.
        // Step 3 (any of): marker lifted, window expires, or different student
        //                  selected → deletion is cancelled.
        private void ArmDelete()
        {
            if (selectedStudent == null)
            {
                ShowBanner("Select a student first, then place marker 17");
                return;
            }
            deleteArmed       = true;
            deleteArmedAt     = DateTime.Now;
            deleteArmedTarget = selectedStudent;
            ShowBanner($"⚠ DELETE {selectedStudent.DisplayName}? Pinch to confirm.");
        }

        private void ConfirmDelete()
        {
            if (!deleteArmed || deleteArmedTarget == null) return;
            string name = deleteArmedTarget.DisplayName;
            UserManager.DeleteUser(deleteArmedTarget.FaceId);

            // Refresh the roster and clear selection
            UserManager.LoadUsers();
            students = UserManager.GetAllUsers();
            if (selectedStudent != null && selectedStudent.FaceId == deleteArmedTarget.FaceId)
                selectedStudent = null;

            deleteArmed       = false;
            deleteArmedTarget = null;
            ShowBanner($"🗑  {name} deleted");
        }

        private void CancelDelete(string reason)
        {
            if (!deleteArmed) return;
            deleteArmed       = false;
            deleteArmedTarget = null;
            ShowBanner(reason);
        }

        /// <summary>Called once per repaint to expire stale armed deletions.</summary>
        private void TickDeleteArmedTimeout()
        {
            if (!deleteArmed) return;
            if ((DateTime.Now - deleteArmedAt).TotalSeconds > DELETE_WINDOW_SECONDS)
                CancelDelete("Delete cancelled (timed out)");
        }

        private void ShowBanner(string text)
        {
            bannerText   = text;
            bannerExpiry = DateTime.Now.AddSeconds(3);
        }

        // ── TUIO listener ─────────────────────────────────────────────────────
        // All CRUD operations on the selected student are driven by TUIO markers.
        // Placing a marker fires the action once; lifting it does nothing
        // (except cancel an armed deletion — see removeTuioObject below).
        public void addTuioObject(TuioObject o)
        {
            switch (o.SymbolID)
            {
                case 11: BeginInvoke((MethodInvoker)(() => ApplyDifficulty("easy")));   break;
                case 12: BeginInvoke((MethodInvoker)(() => ApplyDifficulty("medium"))); break;
                case 13: BeginInvoke((MethodInvoker)(() => ApplyDifficulty("hard")));   break;
                case 14: BeginInvoke((MethodInvoker)(() => BumpAge(+1)));               break;
                case 15: BeginInvoke((MethodInvoker)(() => BumpAge(-1)));               break;
                case 16: BeginInvoke((MethodInvoker)(() => CycleRole()));               break;
                case 17: BeginInvoke((MethodInvoker)(() => ArmDelete()));               break;
            }
        }
        public void updateTuioObject(TuioObject o) { }
        public void removeTuioObject(TuioObject o)
        {
            // Lifting the delete marker cancels the armed state.
            if (o.SymbolID == 17 && deleteArmed)
                BeginInvoke((MethodInvoker)(() => CancelDelete("Delete cancelled (marker lifted)")));
        }
        public void addTuioCursor(TuioCursor c)    { }
        public void updateTuioCursor(TuioCursor c) { }
        public void removeTuioCursor(TuioCursor c) { }
        public void addTuioBlob(TuioBlob b)        { }
        public void updateTuioBlob(TuioBlob b)     { }
        public void removeTuioBlob(TuioBlob b)     { }
        public void refresh(TuioTime t)            { }

        // ── IGestureListener ──────────────────────────────────────────────────

        /// <summary>
        /// Skeleton hand update — right_wrist (id 16) = right index tip = primary cursor.
        /// Falls back to left_wrist (id 15) if right is not visible.
        /// </summary>
        public void OnSkeletonUpdate(double t, IList<SkeletonLandmark> landmarks)
        {
            SkeletonLandmark best = null;
            foreach (var lm in landmarks)
            {
                if (lm.Id == 16) { best = lm; break; }          // right index tip
                if (lm.Id == 15 && best == null) best = lm;     // left index tip fallback
            }
            if (best == null) return;

            cursorX           = (int)(best.X * Width);
            cursorY           = (int)(best.Y * Height);
            cursorFromSkeleton = true;
            lastSkeletonTime   = DateTime.Now;
        }

        /// <summary>
        /// Pinch gesture (name == "enter") has two roles depending on context:
        ///  - When a deletion is armed (marker 17 present): CONFIRM the delete.
        ///  - Otherwise: instant-select the currently hovered student tile.
        /// </summary>
        public void OnGestureRecognized(double t, RecognizedGesture g)
        {
            if (g == null) return;
            bool isPinch = (g.Name == "enter" || g.Name == "pinch");
            if (!isPinch) return;

            BeginInvoke((MethodInvoker)(() =>
            {
                if (deleteArmed) ConfirmDelete();
                else             InstantSelect();
            }));
        }

        /// <summary>
        /// Gaze update — becomes the cursor only when no skeleton hand is active.
        /// Always stored for the secondary gaze dot overlay.
        /// </summary>
        public void OnGazeUpdate(float x, float y)
        {
            gazeX = (int)(x * Width);
            gazeY = (int)(y * Height);
            if (!cursorFromSkeleton)
            {
                cursorX = gazeX;
                cursorY = gazeY;
            }
        }

        public void OnProximityUpdate(string status) { proximityStatus = status; }
        public void OnEmotionUpdate(string label, float conf, string hint) { }
        public void OnYoloDetection(IList<YoloObject> objects)             { }

        // ── Cleanup ───────────────────────────────────────────────────────────
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try { tuioClient?.removeTuioListener(this); tuioClient?.disconnect(); } catch { }
            try { gestureClient?.RemoveListener(this); gestureClient?.Disconnect(); } catch { }
        }

        // ── Helper ────────────────────────────────────────────────────────────
        private static Color DifficultyColor(string diff)
        {
            switch ((diff ?? "easy").ToLower())
            {
                case "easy":   return Color.LimeGreen;
                case "medium": return Color.Gold;
                case "hard":   return Color.OrangeRed;
                default:       return Color.Gray;
            }
        }
    }
}
