"""
HCI radial menu — PyQt5 double-layer radial UI for the fruit learning project.

Gesture protocol: same JSON lines as python/gesture_server.py (TCP port 5000).
Connect TUIO / level logic later via LevelGameState.set_active_fruit_ids(...).
"""

from __future__ import annotations

import json
import math
import os
import socket
import threading
import time
from dataclasses import dataclass
from typing import Dict, List, Optional, Set

from PyQt5.QtCore import QObject, QPointF, QRectF, Qt, QThread, QTimer, QUrl, pyqtSignal
from PyQt5.QtGui import QColor, QFont, QPainter, QPen, QBrush, QPolygonF
from PyQt5.QtMultimedia import QMediaContent, QMediaPlayer
from PyQt5.QtWidgets import QApplication, QLabel, QMainWindow, QVBoxLayout, QWidget

import pyttsx3


# -----------------------------------------------------------------------------
# Fruit data (IDs match TuioDemo TargetSlot.SymbolId: 0..6)
# -----------------------------------------------------------------------------


@dataclass(frozen=True)
class Fruit:
    """One fruit row: id, display name, color label, child-friendly benefit."""

    id: int
    name: str
    color: str
    benefit: str


# All seven fruits used by the project.
ALL_FRUITS: Dict[int, Fruit] = {
    0: Fruit(0, "Apple", "Red", "Apple helps keep you healthy."),
    1: Fruit(1, "Banana", "Yellow", "Banana gives you energy."),
    2: Fruit(2, "Strawberry", "Red", "Strawberries help your skin feel good."),
    3: Fruit(3, "Watermelon", "Green", "Watermelon helps you stay hydrated."),
    4: Fruit(4, "Mango", "Orange", "Mango helps your eyes stay strong."),
    5: Fruit(5, "Orange", "Orange", "Oranges help your body fight colds."),
    6: Fruit(6, "Kiwi", "Green", "Kiwi helps your tummy feel happy."),
}

# Level -> active fruit IDs (same as TuioDemo: level 1 has 2 fruits, level 2 has 5).
LEVEL_TO_ACTIVE_IDS: Dict[int, Set[int]] = {
    1: {0, 1},
    2: {2, 3, 4, 5, 6},
}

# Assets folder: *_color.mp3 filenames by fruit id (matches project Assets/).
FRUIT_ID_TO_COLOR_AUDIO_BASENAME: Dict[int, str] = {
    0: "apple_color.mp3",
    1: "banana_color.mp3",
    2: "straw_color.mp3",
    3: "waterm_color.mp3",
    4: "mango_color.mp3",
    5: "orange_color.mp3",
    6: "kiwi_color.mp3",
}


def project_assets_dir() -> str:
    """Folder that contains the *_color.mp3 files."""
    base = os.path.dirname(os.path.abspath(__file__))
    return os.path.normpath(os.path.join(base, "..", "Assets"))


def color_audio_path_for_fruit_id(fruit_id: int) -> str:
    """Full path to the color clip for this fruit id."""
    name = FRUIT_ID_TO_COLOR_AUDIO_BASENAME[fruit_id]
    return os.path.join(project_assets_dir(), name)


# -----------------------------------------------------------------------------
# Level / active fruit set (connect TUIO here later)
# -----------------------------------------------------------------------------


class LevelGameState(QObject):
    """
    Holds current level and the active fruit id set.
    The radial menu reads active_ids only from this object — never hardcodes layer-2 fruits.
    """

    active_ids_changed = pyqtSignal()

    def __init__(self):
        super().__init__()
        self.current_level: int = 1
        self.active_fruit_ids: Set[int] = set()
        self.apply_level(1)

    def apply_level(self, level_number: int) -> None:
        """Set level and replace active_fruit_ids from LEVEL_TO_ACTIVE_IDS."""
        if level_number in LEVEL_TO_ACTIVE_IDS:
            self.current_level = level_number
        else:
            self.current_level = 1
        self.active_fruit_ids = set(LEVEL_TO_ACTIVE_IDS[self.current_level])
        self.active_ids_changed.emit()

    def set_active_fruit_ids(self, ids: Set[int]) -> None:
        """
        Call from TUIO / game logic when markers or level state change.
        Only ids present in ALL_FRUITS are kept.
        """
        cleaned: Set[int] = set()
        for fid in ids:
            if fid in ALL_FRUITS:
                cleaned.add(fid)
        self.active_fruit_ids = cleaned
        self.active_ids_changed.emit()

    def sorted_active_fruits(self) -> List[Fruit]:
        """Active fruits as Fruit rows, sorted by id for stable menu order."""
        out: List[Fruit] = []
        for fid in sorted(self.active_fruit_ids):
            if fid in ALL_FRUITS:
                out.append(ALL_FRUITS[fid])
        return out


# -----------------------------------------------------------------------------
# Audio: TTS + MP3 color clips + mute + repeat last
# -----------------------------------------------------------------------------


class AudioManager(QObject):
    """
    pyttsx3 for spoken benefits; QMediaPlayer for *_color.mp3 from Assets.
    Stores last played content for Repeat Last Audio.
    """

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self.muted: bool = False
        self.last_kind: str = ""
        self.last_tts_text: str = ""
        self.last_mp3_path: str = ""
        self._tts_engine = None
        self._player = QMediaPlayer(self)
        try:
            self._tts_engine = pyttsx3.init()
        except Exception:
            self._tts_engine = None

    def stop_media_player(self) -> None:
        self._player.stop()

    def speak(self, text: str) -> None:
        """Speak text with pyttsx3 (runs in a worker thread to avoid blocking UI)."""
        if self.muted:
            return
        cleaned = text.strip()
        if len(cleaned) == 0:
            return
        self.last_kind = "tts"
        self.last_tts_text = cleaned
        self.last_mp3_path = ""

        def run_tts() -> None:
            if self._tts_engine is None:
                return
            try:
                self._tts_engine.say(cleaned)
                self._tts_engine.runAndWait()
            except Exception:
                pass

        t = threading.Thread(target=run_tts, daemon=True)
        t.start()

    def play_color_mp3(self, absolute_path: str) -> None:
        """Play one color audio file; path must exist."""
        if self.muted:
            return
        if not os.path.isfile(absolute_path):
            return
        self.stop_media_player()
        self.last_kind = "mp3"
        self.last_tts_text = ""
        self.last_mp3_path = absolute_path
        url = QUrl.fromLocalFile(absolute_path)
        self._player.setMedia(QMediaContent(url))
        self._player.play()

    def repeat_last(self) -> None:
        """Repeat last TTS or last MP3."""
        if self.muted:
            return
        if self.last_kind == "tts":
            if len(self.last_tts_text) > 0:
                self.speak(self.last_tts_text)
            return
        if self.last_kind == "mp3":
            if len(self.last_mp3_path) > 0:
                self.play_color_mp3(self.last_mp3_path)
            return

    def set_muted(self, value: bool) -> None:
        self.muted = value
        if self.muted:
            self.stop_media_player()


# -----------------------------------------------------------------------------
# Gesture TCP client (same wire format as GestureSocketClient.cs)
# -----------------------------------------------------------------------------


class GestureThread(QThread):
    """Background thread: reads newline-delimited JSON from gesture_server."""

    skeleton_updated = pyqtSignal(list)
    gesture_recognized = pyqtSignal(str, float)
    connection_changed = pyqtSignal(bool)
    parse_error = pyqtSignal(str)

    def __init__(self, host: str, port: int):
        super().__init__()
        self.host = host
        self.port = port
        self._running = False
        self._sock: Optional[socket.socket] = None

    def stop_thread(self) -> None:
        self._running = False
        if self._sock is not None:
            try:
                self._sock.close()
            except Exception:
                pass

    def run(self) -> None:
        self._running = True
        buffer = ""
        while self._running:
            if self._sock is None:
                try:
                    self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                    self._sock.settimeout(1.0)
                    self._sock.connect((self.host, self.port))
                    self._sock.settimeout(None)
                    self.connection_changed.emit(True)
                except Exception:
                    self._sock = None
                    self.connection_changed.emit(False)
                    time.sleep(0.5)
                    continue

            try:
                chunk = self._sock.recv(65536)
                if not chunk:
                    raise ConnectionError("closed")
                buffer = buffer + chunk.decode("utf-8", errors="replace")
                while True:
                    idx = buffer.find("\n")
                    if idx < 0:
                        break
                    line = buffer[0:idx].strip()
                    buffer = buffer[idx + 1 :]
                    self._handle_line(line)
            except Exception:
                try:
                    if self._sock is not None:
                        self._sock.close()
                except Exception:
                    pass
                self._sock = None
                self.connection_changed.emit(False)
                time.sleep(0.3)

    def _handle_line(self, line: str) -> None:
        if len(line) == 0:
            return
        try:
            msg = json.loads(line)
        except Exception as exc:
            self.parse_error.emit(str(exc))
            return
        mtype = msg.get("type")
        if mtype != "frame":
            return
        sk = msg.get("skeleton")
        if sk is not None:
            if isinstance(sk, list):
                self.skeleton_updated.emit(sk)
        g = msg.get("gesture")
        if g is None:
            return
        if isinstance(g, dict):
            name = g.get("name")
            conf = g.get("confidence")
            if name is None:
                return
            cval = 0.0
            if isinstance(conf, (int, float)):
                cval = float(conf)
            self.gesture_recognized.emit(str(name), cval)


def pick_cursor_normalized(skeleton: List[dict]) -> Optional[tuple]:
    """
    Map skeleton JSON to normalized (x, y) in 0..1 for the menu cursor.
    Prefers index fingertip landmarks, then wrist proxies.
    """
    if not skeleton:
        return None
    by_name: Dict[str, dict] = {}
    by_id: Dict[int, dict] = {}
    for lm in skeleton:
        if not isinstance(lm, dict):
            continue
        nid = lm.get("id")
        if isinstance(nid, int):
            by_id[nid] = lm
        nm = lm.get("name")
        if isinstance(nm, str):
            by_name[nm] = lm

    pick_order = ["right_index", "left_index", "right_wrist", "left_wrist"]
    for key in pick_order:
        if key in by_name:
            lm = by_name[key]
            x = lm.get("x")
            y = lm.get("y")
            if isinstance(x, (int, float)) and isinstance(y, (int, float)):
                return (float(x), float(y))

    if 19 in by_id:
        lm = by_id[19]
        x = lm.get("x")
        y = lm.get("y")
        if isinstance(x, (int, float)) and isinstance(y, (int, float)):
            return (float(x), float(y))
    if 20 in by_id:
        lm = by_id[20]
        x = lm.get("x")
        y = lm.get("y")
        if isinstance(x, (int, float)) and isinstance(y, (int, float)):
            return (float(x), float(y))
    if 16 in by_id:
        lm = by_id[16]
        x = lm.get("x")
        y = lm.get("y")
        if isinstance(x, (int, float)) and isinstance(y, (int, float)):
            return (float(x), float(y))
    if 15 in by_id:
        lm = by_id[15]
        x = lm.get("x")
        y = lm.get("y")
        if isinstance(x, (int, float)) and isinstance(y, (int, float)):
            return (float(x), float(y))
    return None


# -----------------------------------------------------------------------------
# Radial menu widget (custom paint + sector hit test)
# -----------------------------------------------------------------------------


class RadialMenuWidget(QWidget):
    """
    Double-layer radial menu: set labels, then paint sectors.
    Cursor is drawn here as a semi-transparent circle (menu cursor).
    """

    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.setAttribute(Qt.WA_TranslucentBackground, True)
        self.labels: List[str] = []
        self.cursor_pos = QPointF(0.0, 0.0)
        self.cursor_visible: bool = False
        self.inner_ratio: float = 0.12
        self.outer_ratio: float = 0.44

    def set_labels(self, labels: List[str]) -> None:
        self.labels = list(labels)
        self.update()

    def set_cursor_pos(self, x: float, y: float) -> None:
        self.cursor_pos = QPointF(x, y)
        self.update()

    def set_cursor_visible(self, visible: bool) -> None:
        self.cursor_visible = visible
        self.update()

    def paintEvent(self, event) -> None:
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)
        w = float(self.width())
        h = float(self.height())
        cx = w / 2.0
        cy = h / 2.0
        m = min(w, h)
        inner_r = m * self.inner_ratio
        outer_r = m * self.outer_ratio

        painter.fillRect(self.rect(), QColor(0, 0, 0, 120))

        n = len(self.labels)
        if n <= 0:
            painter.end()
            return

        span = 360.0 / float(n)
        font = QFont("Arial", 14, QFont.Bold)
        painter.setFont(font)

        for i in range(n):
            d0 = float(i) * span - 90.0
            d1 = float(i + 1) * span - 90.0
            color = QColor(40, 90, 160, 200)
            if i % 2 == 1:
                color = QColor(30, 70, 130, 200)
            painter.setBrush(QBrush(color))
            painter.setPen(QPen(QColor(255, 255, 255, 220), 2))
            wedge_pts = []
            wedge_pts.append(QPointF(cx, cy))
            arc_steps = 36
            for s in range(arc_steps + 1):
                t = d0 + (d1 - d0) * (float(s) / float(arc_steps))
                tr = math.radians(t)
                px = cx + outer_r * math.cos(tr)
                py = cy + outer_r * math.sin(tr)
                wedge_pts.append(QPointF(px, py))
            painter.drawPolygon(QPolygonF(wedge_pts))

            mid_deg = d0 + span / 2.0
            mid_rad = math.radians(mid_deg)
            label_r = (inner_r + outer_r) / 2.0
            tx = cx + label_r * math.cos(mid_rad)
            ty = cy + label_r * math.sin(mid_rad)
            painter.setPen(QPen(QColor(255, 255, 255)))
            painter.drawText(
                int(tx - 80),
                int(ty - 12),
                160,
                48,
                Qt.AlignCenter,
                self.labels[i],
            )

        painter.setPen(Qt.NoPen)
        painter.setBrush(QBrush(QColor(0, 0, 0, 120)))
        painter.drawEllipse(QRectF(cx - inner_r, cy - inner_r, 2.0 * inner_r, 2.0 * inner_r))

        if self.cursor_visible:
            cr = m * 0.06
            if cr < 12.0:
                cr = 12.0
            if cr > 36.0:
                cr = 36.0
            painter.setBrush(QBrush(QColor(255, 200, 80, 140)))
            painter.setPen(QPen(QColor(255, 255, 255, 200), 2))
            painter.drawEllipse(self.cursor_pos, cr, cr)

        painter.end()

    def sector_index_at(self, x: float, y: float) -> int:
        """Return sector index for point, or -1 if outside the ring."""
        w = float(self.width())
        h = float(self.height())
        cx = w / 2.0
        cy = h / 2.0
        m = min(w, h)
        inner_r = m * self.inner_ratio
        outer_r = m * self.outer_ratio
        dx = x - cx
        dy = y - cy
        dist = math.hypot(dx, dy)
        if dist < inner_r:
            return -1
        if dist > outer_r:
            return -1
        n = len(self.labels)
        if n <= 0:
            return -1
        deg = math.degrees(math.atan2(dy, dx))
        adj = deg + 90.0
        while adj < 0.0:
            adj = adj + 360.0
        while adj >= 360.0:
            adj = adj - 360.0
        span = 360.0 / float(n)
        idx = int(adj / span)
        if idx < 0:
            idx = 0
        if idx >= n:
            idx = n - 1
        return idx


# -----------------------------------------------------------------------------
# Main window: state machine + gesture handlers + placeholders
# -----------------------------------------------------------------------------


class MainWindow(QMainWindow):
    """Main screen with overlay radial menu."""

    def __init__(self):
        super().__init__()
        self.setWindowTitle("HCI Fruit Menu (PyQt5)")
        self.resize(1280, 720)

        self.game_state = LevelGameState()
        self.audio = AudioManager(self)

        self.menu_open: bool = False
        self.layer: str = "none"
        self.layer1_labels = ["Colors", "Info", "Level Control", "Audio", "Exit"]
        self.sub_mode: str = ""
        self.cursor_follows_skeleton: bool = False
        self._dwell_index: int = -1
        self._dwell_since: float = 0.0
        self._dwell_ms: int = 450

        self._last_skeleton: List[dict] = []

        central = QWidget(self)
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)

        self.status_label = QLabel(
            "Start gesture_server.py, then run this app. "
            "Raise finger (pointer_up) to open menu. "
            "Use open_hand to unlock cursor, fist or Exit to close."
        )
        self.status_label.setWordWrap(True)
        self.status_label.setStyleSheet("font-size: 16px; color: #222; padding: 12px;")
        layout.addWidget(self.status_label)

        self.level_label = QLabel("")
        self.level_label.setStyleSheet("font-size: 20px; font-weight: bold;")
        layout.addWidget(self.level_label)

        self.conn_label = QLabel("Gesture: disconnected")
        self.conn_label.setStyleSheet("font-size: 14px; color: #666;")
        layout.addWidget(self.conn_label)

        self.overlay = RadialMenuWidget(self)
        self.overlay.setGeometry(0, 0, self.width(), self.height())
        self.overlay.hide()

        self.gesture_thread = GestureThread("127.0.0.1", 5000)
        self.gesture_thread.skeleton_updated.connect(self.on_skeleton)
        self.gesture_thread.gesture_recognized.connect(self.on_gesture)
        self.gesture_thread.connection_changed.connect(self.on_gesture_connection)
        self.gesture_thread.start()

        self.dwell_timer = QTimer(self)
        self.dwell_timer.setInterval(33)
        self.dwell_timer.timeout.connect(self.on_dwell_tick)
        self.dwell_timer.start()

        self.refresh_level_label()
        self.game_state.active_ids_changed.connect(self.on_active_ids_changed)

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        self.overlay.setGeometry(0, 0, self.width(), self.height())

    def on_active_ids_changed(self) -> None:
        """When TUIO updates active ids, rebuild open layer-2 if needed."""
        self.refresh_level_label()
        if self.menu_open and self.layer == "layer2":
            if self.sub_mode == "colors" or self.sub_mode == "info":
                self.build_layer2_fruit_menu()

    def refresh_level_label(self) -> None:
        ids = sorted(self.game_state.active_fruit_ids)
        text = "Level " + str(self.game_state.current_level)
        text = text + " — active fruit ids: " + str(ids)
        self.level_label.setText(text)

    def on_gesture_connection(self, ok: bool) -> None:
        if ok:
            self.conn_label.setText("Gesture: connected to 127.0.0.1:5000")
        else:
            self.conn_label.setText("Gesture: disconnected (start gesture_server.py)")

    def on_skeleton(self, skeleton: list) -> None:
        self._last_skeleton = skeleton
        if not self.menu_open:
            return
        if not self.cursor_follows_skeleton:
            return
        pos = pick_cursor_normalized(skeleton)
        if pos is None:
            return
        nx = pos[0]
        ny = pos[1]
        x = nx * float(self.width())
        y = ny * float(self.height())
        self.overlay.set_cursor_pos(x, y)
        self.overlay.set_cursor_visible(True)

    def on_gesture(self, name: str, confidence: float) -> None:
        _ = confidence
        if name == "pointer_up":
            if not self.menu_open:
                self.open_menu_layer1()
            return
        if name == "fist":
            if self.menu_open:
                self.close_menu()
            return
        if name == "open_hand":
            if self.menu_open:
                self.cursor_follows_skeleton = True
                cx = float(self.width()) / 2.0
                cy = float(self.height()) / 2.0
                self.overlay.set_cursor_pos(cx, cy)
                self.overlay.set_cursor_visible(True)
            return

    def open_menu_layer1(self) -> None:
        self.menu_open = True
        self.layer = "layer1"
        self.sub_mode = ""
        self.cursor_follows_skeleton = False
        self.overlay.set_labels(self.layer1_labels)
        cx = float(self.width()) / 2.0
        cy = float(self.height()) / 2.0
        self.overlay.set_cursor_pos(cx, cy)
        self.overlay.set_cursor_visible(False)
        self.overlay.show()
        self.overlay.raise_()

    def close_menu(self) -> None:
        self.menu_open = False
        self.layer = "none"
        self.sub_mode = ""
        self.cursor_follows_skeleton = False
        self.overlay.hide()
        self._dwell_index = -1
        self._dwell_since = 0.0

    def build_layer2_fruit_menu(self) -> None:
        fruits = self.game_state.sorted_active_fruits()
        labels: List[str] = ["Back"]
        for fr in fruits:
            labels.append(fr.name)
        self.overlay.set_labels(labels)

    def build_layer2_level_menu(self) -> None:
        self.overlay.set_labels(["Back", "Restart Level", "Go to Level 1", "Go to Level 2"])

    def build_layer2_audio_menu(self) -> None:
        mute_word = "Mute"
        if self.audio.muted:
            mute_word = "Unmute"
        self.overlay.set_labels(["Back", "Repeat Last Audio", mute_word])

    def on_dwell_tick(self) -> None:
        if not self.menu_open:
            return
        if not self.cursor_follows_skeleton:
            return
        x = self.overlay.cursor_pos.x()
        y = self.overlay.cursor_pos.y()
        idx = self.overlay.sector_index_at(x, y)
        if idx < 0:
            self._dwell_index = -1
            self._dwell_since = 0.0
            return
        now = time.time()
        if idx != self._dwell_index:
            self._dwell_index = idx
            self._dwell_since = now
            return
        elapsed_ms = int((now - self._dwell_since) * 1000.0)
        if elapsed_ms < self._dwell_ms:
            return
        self._dwell_since = now
        self.activate_sector_index(idx)

    def activate_sector_index(self, idx: int) -> None:
        labels = self.overlay.labels
        if idx < 0 or idx >= len(labels):
            return

        if self.layer == "layer1":
            label = labels[idx]
            if label == "Colors":
                self.sub_mode = "colors"
                self.layer = "layer2"
                self.build_layer2_fruit_menu()
                return
            if label == "Info":
                self.sub_mode = "info"
                self.layer = "layer2"
                self.build_layer2_fruit_menu()
                return
            if label == "Level Control":
                self.sub_mode = "level"
                self.layer = "layer2"
                self.build_layer2_level_menu()
                return
            if label == "Audio":
                self.sub_mode = "audio"
                self.layer = "layer2"
                self.build_layer2_audio_menu()
                return
            if label == "Exit":
                self.close_menu()
                return
            return

        if self.layer == "layer2":
            if self.sub_mode == "colors":
                self.handle_colors_layer(idx, labels)
                return
            if self.sub_mode == "info":
                self.handle_info_layer(idx, labels)
                return
            if self.sub_mode == "level":
                self.handle_level_layer(idx, labels)
                return
            if self.sub_mode == "audio":
                self.handle_audio_layer(idx, labels)
                return

    def handle_colors_layer(self, idx: int, labels: List[str]) -> None:
        choice = labels[idx]
        if choice == "Back":
            self.open_menu_layer1()
            return
        for fr in self.game_state.sorted_active_fruits():
            if fr.name == choice:
                path = color_audio_path_for_fruit_id(fr.id)
                self.audio.play_color_mp3(path)
                return

    def handle_info_layer(self, idx: int, labels: List[str]) -> None:
        choice = labels[idx]
        if choice == "Back":
            self.open_menu_layer1()
            return
        for fr in self.game_state.sorted_active_fruits():
            if fr.name == choice:
                self.audio.speak(fr.benefit)
                return

    def handle_level_layer(self, idx: int, labels: List[str]) -> None:
        choice = labels[idx]
        if choice == "Back":
            self.open_menu_layer1()
            return
        if choice == "Restart Level":
            self.on_restart_level()
            return
        if choice == "Go to Level 1":
            self.on_go_to_level(1)
            return
        if choice == "Go to Level 2":
            self.on_go_to_level(2)
            return

    def handle_audio_layer(self, idx: int, labels: List[str]) -> None:
        choice = labels[idx]
        if choice == "Back":
            self.open_menu_layer1()
            return
        if choice == "Repeat Last Audio":
            self.audio.repeat_last()
            return
        if choice == "Mute" or choice == "Unmute":
            new_val = not self.audio.muted
            self.audio.set_muted(new_val)
            self.build_layer2_audio_menu()
            return

    # --- Placeholders: wire to TUIO / TuioDemo-equivalent logic later ---

    def on_restart_level(self) -> None:
        """Placeholder: reset current level in the real game."""
        self.status_label.setText("Placeholder: restart level (connect your game loop here).")

    def on_go_to_level(self, level_number: int) -> None:
        """Placeholder: switch level and refresh active fruit ids from LEVEL_TO_ACTIVE_IDS."""
        self.game_state.apply_level(level_number)
        msg = "Placeholder: go to level " + str(level_number) + " (active ids updated for menu)."
        self.status_label.setText(msg)

    def closeEvent(self, event) -> None:
        self.gesture_thread.stop_thread()
        self.gesture_thread.wait(2000)
        super().closeEvent(event)


def main() -> None:
    import sys

    app = QApplication(sys.argv)
    win = MainWindow()
    win.show()
    sys.exit(app.exec_())


if __name__ == "__main__":
    main()
