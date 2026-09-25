# Extension contract v1

## Module boundaries

`Core.cs` defines small replaceable interfaces:

- `IGazeSource`: read a gaze sample; current implementation `OpenVrSource` uses the official eye action, never an OpenXR scene session.
- `IGazeProcessor`: process a sample and reset on invalid/reacquired data. `GazeFilter` filters in head space to avoid head-motion lag, then the engine converts back to standing space.
- `IDesktopSurfaceAdapter`: intersect a world-space gaze ray with a known desktop surface and return Windows pixels. `DesktopPlusAdapter` uses `ComputeOverlayIntersection`, runtime mouse scale and texture bounds.
- `IFrameSink`: consume a serialized frame without controlling the runtime. The v1 sink is a non-blocking latest-frame named-pipe bridge.
- `OverlayRenderer`: owns VR marker and dashboard handles. New render modules should own separate overlay keys, dispose their handles, and consume samples rather than modify source data.

The composition root is `Engine.Run`. Adapters are compiled into this version. There is deliberately no dynamic DLL loader or package manager. Other programs can integrate now through the pipe; additional desktop providers can implement the adapter once their capture metadata is known.

## Source and coordinates

An `IGazeSource.Read` sample uses **head-local OpenVR coordinates**, metres, right-handed, -Z forward, +Y up. The current source obtains a standing-space OpenVR origin/target and converts the ray with the current HMD pose. `vGazeTarget` is a point; subtract origin before normalization.

The exported world ray uses **OpenVR standing coordinates**, including current playspace/recenter transforms. Do not mix it with raw or seated coordinates. Filtering operates on local direction; world transformation uses the current head pose.

`ComputeOverlayIntersection` returns source-texture UV; its V runs bottom-to-top. The hit already includes crop bounds. Windows conversion is:

```text
x = captureLeft + floor(u * captureWidth)
y = captureTop  + floor((1 - v) * captureHeight)
```

Clamp the exact far edge to the last pixel. Reject NaN, out-of-range UV, backwards hits, invisible/transparent surfaces and gaps between physical monitors. Never apply the crop a second time. Exported `desktop.u/v` are full-source-texture normalized coordinates with a **top-left** origin, not coordinates of the cropped visible panel. Exported x/y are absolute Windows **physical pixels**, possibly negative.

SteamVR handles transformed/curved overlay intersection. For a future window adapter, use current capture bounds, window location, DPI awareness, content/client offsets, crop and capture scaling. A native window ID alone is insufficient. An ordinary desktop overlay can be captured by Desktop+ today; semantic/data integration still requires that program's API.

## Named pipe

Endpoint: `\\.\pipe\SteamGazeOverlay.v1`

UTF-8, one JSON object per line, up to approximately 30 Hz, one client at a time. The server ACL permits the current Windows account only. There is no TCP port, firewall rule or cloud transmission. Slow clients are disconnected; rendering never waits for clients. Reconnect when the pipe closes, discard frames older than 250 ms using **your own receipt time**, and clear displayed coordinates immediately on invalid/disconnected data. When exports are disabled, an explicit invalid `export_disabled` state is sent instead.

```json
{
  "schemaVersion": 1,
  "sequence": 100,
  "utc": "2026-09-25T07:00:00.0000000Z",
  "monotonicSeconds": 12.3,
  "timestampKind": "host_poll_not_sensor",
  "coordinateSpace": "OpenVR_Standing_Metres",
  "valid": true,
  "reason": "valid",
  "rawOrigin": [0, 1.6, 0],
  "rawDirection": [0, 0, -1],
  "filteredDirection": [0, 0, -1],
  "headPosition": [0, 1.6, 0],
  "mappingInput": "filtered",
  "desktop": {
    "target": "elvissteinjr.DesktopPlus0",
    "monitor": "\\\\.\\DISPLAY1",
    "u": 0.5,
    "v": 0.75,
    "x": 1280,
    "y": 720
  },
  "pollHz": 90,
  "validFraction": 0.95,
  "loopMs": 0.3
}
```

An invalid eye sample has `valid=false`, null ray fields and `desktop=null`. A valid eye ray that misses the desktop retains `valid=true` but has `desktop=null`. Consumers must handle both. Disconnect/status notifications may omit optional fields, including sequence. Timestamps are host polling timestamps, not sensor timestamps or measured sensor latency. The `pollHz` metric is not hardware sample rate. `loopMs` measures host work before output/dashboard rendering and frame wait; it is not end-to-end latency.

PowerShell read-only client (run as the same Windows account):

```powershell
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'SteamGazeOverlay.v1', [IO.Pipes.PipeDirection]::In)
try {
    $pipe.Connect(2000)
    $reader = [IO.StreamReader]::new($pipe)
    while (($line = $reader.ReadLine()) -ne $null) {
        $frame = $line | ConvertFrom-Json
        if ($frame.valid -and $null -ne $frame.desktop) {
            '{0}, {1}' -f $frame.desktop.x, $frame.desktop.y
        } else { 'No valid desktop hit' }
    }
} finally { $pipe.Dispose() }
```

This example only prints positions. Real-time consumers should add a read deadline/watchdog; a blocked `ReadLine` must not leave a stale cursor visible.

## Future compatibility

New sources/renderers must preserve validity and coordinate metadata. Use separate overlays for additional visuals and no scene application session. Expose new sink protocols as adapters instead of coupling them to gaze acquisition. Additive JSON fields are allowed within v1; incompatible coordinate or field semantics require a new version/pipe endpoint.

Mouse control must be a separate opt-in module with an explicit activation gesture. The current program never calls `SetCursorPos`, `SendInput`, or sends mouse events to third-party overlays.
