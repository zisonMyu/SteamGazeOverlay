# Changelog

## 0.1.1 — experimental

Initial public snapshot.

- Combined OpenVR gaze visualization with raw/smoothed modes and configurable markers.
- Desktop+ intersection to Windows physical pixels and a local named-pipe bridge.
- Windows settings, tray mode, SteamVR Dashboard and optional autostart.
- A 30-second mapping check that separates receiving eye data, observing a desktop hit, and human accuracy validation.
- Wait for a new SteamVR server after its Quit event; clear outputs on disconnect.
- Portable build and clean ZIP packaging with pinned OpenVR files and license notices.

Tested on one PSVR2/PSVR2Toolkit setup. Broader headset compatibility, quantified accuracy and in-game performance remain open validation work. Desktop+ capture failures during display changes have not been resolved upstream by this release.
