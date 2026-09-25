# Hotscreen UDP bridge

`steamgaze_hotscreen_bridge.py` converts Steam Gaze's local named-pipe output
into Hotscreen's gaze input protocol:

- Input: `\\.\pipe\SteamGazeOverlay.v1`, UTF-8 NDJSON
- Output: `UDP 127.0.0.1:7779`, two little-endian float32 values
- Valid gaze: normalized x/y on the selected Windows monitor
- Invalid, stale or missing desktop hit: `(-1.0, -1.0)`

No Python packages are required. The launcher supports the standard `py`
launcher, a working `python` command, and installed pyenv-win versions. Start SteamVR, Desktop+, Steam Gaze and
Hotscreen, keep the relevant Desktop+ desktop visible in VR, then double-click
`Start Steam Gaze to Hotscreen Bridge.cmd`.

The default target is the Windows primary monitor. To use another monitor:

```powershell
py -3 .\steamgaze_hotscreen_bridge.py --list-monitors
py -3 .\steamgaze_hotscreen_bridge.py --monitor "\\.\DISPLAY2"
```

Steam Gaze desktop mapping and local export must both be enabled. The named
pipe supports one client at a time.
