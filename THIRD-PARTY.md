# Third-party components and references

## Valve OpenVR SDK

- https://github.com/ValveSoftware/openvr
- Pinned revision: `0924064316de3effbcd1acf1e309182a2deb1c05` (2.15.6).
- Redistributed unmodified: `headers/openvr_api.cs`, Windows x64 `openvr_api.dll`.
- License: Valve BSD-style license, included in `vendor/openvr/LICENSE` and distribution `OpenVR-LICENSE.txt`.
- Used for eye input, compositor overlays, ray intersection and application registration.

## Desktop+ (existing external application)

- https://github.com/elvissteinjr/DesktopPlus
- Source consulted: `031d74d69f08411a9a9b1030d3d428ac02b93549`.
- License: GPL-3.0.
- Desktop+ performs desktop capture, presentation, curvature and positioning. This project connects to its existing runtime surfaces; Desktop+ binaries are not modified or redistributed.
- `VRInput.cpp`, `LaserPointer.cpp`, `Overlays.cpp`, and `OutputManager.cpp` were inspected to establish eye-action bindings, surface keys, source-texture UV semantics, desktop union offsets and capture-mode limitations.
- This app declares its own `/actions/gaze/in/eyes` action; it does not modify Desktop+ input bindings or configuration.

## PSVR2Toolkit (external gaze provider)

- https://github.com/BnuuySolutions/PSVR2Toolkit
- Tested with `v1.0.0-experimental-2` on PSVR2.
- Installed and calibrated separately; no PSVR2Toolkit or Sony driver files are redistributed here.

No Unity engine, web UI framework, third-party smoothing package or custom desktop-capture engine is bundled.
