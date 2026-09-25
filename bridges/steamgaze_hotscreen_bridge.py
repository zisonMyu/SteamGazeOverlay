#!/usr/bin/env python3
r"""Bridge Steam Gaze desktop hits to the Hotscreen gaze UDP protocol.

Input:  \\.\pipe\SteamGazeOverlay.v1 (UTF-8 NDJSON)
Output: UDP 127.0.0.1:7779, two little-endian float32 values (x, y)

Coordinates sent to Hotscreen are normalized to the selected Windows monitor.
Invalid/stale/no-desktop-hit samples are represented as (-1.0, -1.0).
The script uses only the Python standard library.
"""

from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
import json
import math
import socket
import struct
import sys
import threading
import time
from dataclasses import dataclass
from typing import Any


DEFAULT_PIPE = "SteamGazeOverlay.v1"
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 7779
INVALID_PACKET = struct.pack("<ff", -1.0, -1.0)
MONITORINFOF_PRIMARY = 1


class MONITORINFOEXW(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("rcMonitor", wintypes.RECT),
        ("rcWork", wintypes.RECT),
        ("dwFlags", wintypes.DWORD),
        ("szDevice", wintypes.WCHAR * 32),
    ]


@dataclass(frozen=True)
class Monitor:
    device: str
    left: int
    top: int
    right: int
    bottom: int
    primary: bool = False

    @property
    def width(self) -> int:
        return self.right - self.left

    @property
    def height(self) -> int:
        return self.bottom - self.top


class SharedState:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.connected = False
        self.frame: dict[str, Any] | None = None
        self.received_at = 0.0
        self.reader_status = "waiting for Steam Gaze"

    def connection(self, connected: bool, status: str) -> None:
        with self.lock:
            self.connected = connected
            self.reader_status = status
            if not connected:
                self.frame = None
                self.received_at = 0.0

    def update(self, frame: dict[str, Any]) -> None:
        with self.lock:
            self.connected = True
            self.frame = frame
            self.received_at = time.monotonic()
            self.reader_status = "connected"

    def snapshot(self) -> tuple[bool, dict[str, Any] | None, float, str]:
        with self.lock:
            return self.connected, self.frame, self.received_at, self.reader_status


def enable_dpi_awareness() -> None:
    try:
        ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    except Exception:
        try:
            ctypes.windll.user32.SetProcessDPIAware()
        except Exception:
            pass


def enumerate_monitors() -> list[Monitor]:
    if sys.platform != "win32":
        raise RuntimeError("This bridge requires Windows")

    monitors: list[Monitor] = []
    user32 = ctypes.windll.user32
    user32.GetMonitorInfoW.argtypes = [wintypes.HMONITOR, ctypes.POINTER(MONITORINFOEXW)]
    user32.GetMonitorInfoW.restype = wintypes.BOOL
    callback_type = ctypes.WINFUNCTYPE(
        wintypes.BOOL,
        wintypes.HMONITOR,
        wintypes.HDC,
        ctypes.POINTER(wintypes.RECT),
        wintypes.LPARAM,
    )

    def callback(handle: int, _dc: int, _rect: Any, _data: int) -> bool:
        info = MONITORINFOEXW()
        info.cbSize = ctypes.sizeof(info)
        if not user32.GetMonitorInfoW(handle, ctypes.byref(info)):
            return True
        rect = info.rcMonitor
        monitors.append(
            Monitor(
                device=info.szDevice,
                left=rect.left,
                top=rect.top,
                right=rect.right,
                bottom=rect.bottom,
                primary=bool(info.dwFlags & MONITORINFOF_PRIMARY),
            )
        )
        return True

    callback_ref = callback_type(callback)
    if not user32.EnumDisplayMonitors(0, 0, callback_ref, 0):
        raise ctypes.WinError()
    return monitors


def select_monitor(monitors: list[Monitor], selector: str) -> Monitor:
    if not monitors:
        raise RuntimeError("Windows reported no active monitors")
    if selector.lower() == "primary":
        return next((monitor for monitor in monitors if monitor.primary), monitors[0])
    wanted = selector.upper()
    for monitor in monitors:
        if monitor.device.upper() == wanted:
            return monitor
    choices = ", ".join(monitor.device for monitor in monitors)
    raise ValueError(f"monitor {selector!r} was not found; active monitors: {choices}")


def pipe_path(pipe_name: str) -> str:
    if pipe_name.startswith("\\\\.\\pipe\\"):
        return pipe_name
    return rf"\\.\pipe\{pipe_name}"


def pipe_reader(state: SharedState, stop: threading.Event, name: str) -> None:
    path = pipe_path(name)
    while not stop.is_set():
        try:
            state.connection(False, "waiting for Steam Gaze pipe")
            with open(path, "r", encoding="utf-8", errors="replace", buffering=1) as stream:
                state.connection(True, "connected; waiting for first frame")
                for line in stream:
                    if stop.is_set():
                        return
                    try:
                        frame = json.loads(line)
                        if isinstance(frame, dict):
                            state.update(frame)
                        else:
                            state.connection(True, "ignored non-object pipe message")
                    except json.JSONDecodeError:
                        state.connection(True, "ignored malformed JSON from pipe")
            state.connection(False, "Steam Gaze pipe disconnected")
        except OSError:
            state.connection(False, "waiting for Steam Gaze pipe")
        stop.wait(0.5)


def frame_to_normalized(frame: dict[str, Any], monitor: Monitor) -> tuple[tuple[float, float] | None, str]:
    if frame.get("schemaVersion", 1) != 1:
        return None, "unsupported Steam Gaze schema"
    if frame.get("valid") is not True:
        return None, f"eye invalid: {frame.get('reason', 'unknown')}"

    desktop = frame.get("desktop")
    if not isinstance(desktop, dict):
        return None, "valid eye ray, but no Desktop+ surface hit"

    source_monitor = desktop.get("monitor")
    if isinstance(source_monitor, str) and source_monitor.upper() != monitor.device.upper():
        return None, f"hit is on {source_monitor}; target is {monitor.device}"

    x = desktop.get("x")
    y = desktop.get("y")
    if not isinstance(x, (int, float)) or isinstance(x, bool):
        return None, "desktop x coordinate is missing"
    if not isinstance(y, (int, float)) or isinstance(y, bool):
        return None, "desktop y coordinate is missing"
    if not math.isfinite(float(x)) or not math.isfinite(float(y)):
        return None, "desktop coordinates are not finite"
    if not (monitor.left <= x < monitor.right and monitor.top <= y < monitor.bottom):
        return None, f"desktop point ({x}, {y}) is outside {monitor.device}"

    normalized_x = (float(x) - monitor.left) / monitor.width
    normalized_y = (float(y) - monitor.top) / monitor.height
    return (normalized_x, normalized_y), "valid desktop gaze"


def current_packet(
    state: SharedState,
    monitor: Monitor,
    stale_seconds: float,
) -> tuple[bytes, str, tuple[float, float] | None]:
    connected, frame, received_at, reader_status = state.snapshot()
    if not connected or frame is None:
        return INVALID_PACKET, reader_status, None
    age = time.monotonic() - received_at
    if age > stale_seconds:
        return INVALID_PACKET, f"Steam Gaze frame stale ({age * 1000:.0f} ms)", None
    point, reason = frame_to_normalized(frame, monitor)
    if point is None:
        return INVALID_PACKET, reason, None
    return struct.pack("<ff", *point), reason, point


def print_monitors(monitors: list[Monitor]) -> None:
    for monitor in monitors:
        kind = " primary" if monitor.primary else ""
        print(
            f"{monitor.device}: {monitor.width}x{monitor.height} "
            f"at ({monitor.left},{monitor.top}){kind}"
        )


def self_test() -> int:
    primary = Monitor(r"\\.\DISPLAY1", 0, 0, 2560, 1440, True)
    upper = Monitor(r"\\.\DISPLAY2", 0, -1440, 2560, 0, False)

    point, reason = frame_to_normalized(
        {
            "schemaVersion": 1,
            "valid": True,
            "desktop": {"monitor": primary.device, "x": 1280, "y": 720},
        },
        primary,
    )
    assert reason == "valid desktop gaze" and point == (0.5, 0.5)
    assert struct.unpack("<ff", struct.pack("<ff", *point)) == (0.5, 0.5)

    point, _ = frame_to_normalized(
        {
            "schemaVersion": 1,
            "valid": True,
            "desktop": {"monitor": upper.device, "x": 640, "y": -720},
        },
        upper,
    )
    assert point == (0.25, 0.5)
    assert frame_to_normalized({"valid": False, "desktop": None}, primary)[0] is None
    assert frame_to_normalized({"valid": True, "desktop": None}, primary)[0] is None
    assert frame_to_normalized(
        {"valid": True, "desktop": {"monitor": upper.device, "x": 10, "y": -10}},
        primary,
    )[0] is None
    assert len(INVALID_PACKET) == 8 and struct.unpack("<ff", INVALID_PACKET) == (-1.0, -1.0)
    print("PASS: Steam Gaze -> Hotscreen bridge self-test")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Forward Steam Gaze Desktop+ hits to Hotscreen's UDP gaze input"
    )
    parser.add_argument("--pipe", default=DEFAULT_PIPE, help="Steam Gaze named pipe name")
    parser.add_argument("--host", default=DEFAULT_HOST, help="Hotscreen UDP host")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="Hotscreen UDP port")
    parser.add_argument(
        "--monitor",
        default="primary",
        help=r"Hotscreen monitor: primary or a device such as \\.\DISPLAY1",
    )
    parser.add_argument("--send-hz", type=float, default=60.0, help="UDP send rate")
    parser.add_argument(
        "--stale-ms",
        type=float,
        default=250.0,
        help="invalidate a pipe frame after this many milliseconds",
    )
    parser.add_argument("--list-monitors", action="store_true", help="show active monitor geometry")
    parser.add_argument("--self-test", action="store_true", help="run offline protocol tests")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.self_test:
        return self_test()
    if not (1 <= args.port <= 65535):
        raise ValueError("port must be between 1 and 65535")
    if args.send_hz <= 0 or args.stale_ms <= 0:
        raise ValueError("send-hz and stale-ms must be positive")

    enable_dpi_awareness()
    monitors = enumerate_monitors()
    if args.list_monitors:
        print_monitors(monitors)
        return 0
    monitor = select_monitor(monitors, args.monitor)

    print("Steam Gaze -> Hotscreen bridge")
    print(f"  input : {pipe_path(args.pipe)}")
    print(f"  output: UDP {args.host}:{args.port}, {args.send_hz:g} Hz")
    print(
        f"  screen: {monitor.device} {monitor.width}x{monitor.height} "
        f"at ({monitor.left},{monitor.top})"
    )
    print("  Ctrl+C stops the bridge and clears the gaze signal.")

    state = SharedState()
    stop = threading.Event()
    reader = threading.Thread(
        target=pipe_reader,
        args=(state, stop, args.pipe),
        name="Steam Gaze pipe reader",
        daemon=True,
    )
    reader.start()

    destination = (args.host, args.port)
    delay = 1.0 / args.send_hz
    stale_seconds = args.stale_ms / 1000.0
    previous_status = ""
    next_status = 0.0

    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
        try:
            while True:
                started = time.monotonic()
                packet, status, point = current_packet(state, monitor, stale_seconds)
                udp.sendto(packet, destination)

                if status != previous_status or started >= next_status:
                    if point is None:
                        print(f"  no gaze: {status}")
                    else:
                        print(f"  gaze: x={point[0]:.4f} y={point[1]:.4f}")
                    previous_status = status
                    next_status = started + 2.0

                remaining = delay - (time.monotonic() - started)
                if remaining > 0:
                    time.sleep(remaining)
        except KeyboardInterrupt:
            print("\nStopping bridge.")
        finally:
            stop.set()
            try:
                udp.sendto(INVALID_PACKET, destination)
            except OSError:
                pass
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"ERROR: {error}", file=sys.stderr)
        raise SystemExit(1)
