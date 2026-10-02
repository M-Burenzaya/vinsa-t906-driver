# Vinsa T906 driver

Vinsa Studio is a .NET 10 / WPF Windows configuration app, backed by an evidence-based reconstruction of the tablet's mapping protocol. Research scripts preserve the extraction workflow and independent protocol verification. REA/Ghidra was used to analyze the supplied native Delphi `TabletService.exe`.

This is new implementation source, not the original vendor source or a complete replacement for the vendor application. Mapping and mode reports are reconstructed; raw pen input, device pressure curves, raw tilt decoding, hotkey dispatch, Wintab/Windows Ink integration, and full device initialization are not implemented. Windows continues to provide the tablet's existing HID input support.

## Windows desktop app

```powershell
./scripts/Start-VinsaStudio.ps1 -Rebuild
```

This builds into the ignored `build/VinsaStudio/` directory and opens the native Windows app. Building requires the .NET 10 SDK; running the published app requires the .NET 10 Windows Desktop runtime. It does not require Python. Run the launcher again without `-Rebuild` to reuse the build. It reads the installed vendor INI when available, otherwise the supplied snapshot; `-SettingsPath` selects another file.

The main window keeps the original MyTablet layout with a monochrome dark theme and native Windows minimize, maximize and close buttons: the original pen bitmap and its connector lines sit beside custom-key and pressure controls, with the pressure graph on the left and the drawing tester on the right. Screen Mapping and Defined Keys open separate owned windows. The bitmap is embedded directly from the preserved vendor file and displayed with a transparent background in grayscale; no extra image copy is stored in source.

- Mapping: a desktop snapshot in the actual monitor layout, display selection, movable regions with eight resize handles, a tablet illustration, exact coordinates, presets, and saving a new settings copy. Ink/handedness stay on the main window. Refresh preview takes a new snapshot; it stays in memory during normal use. Existing files are never overwritten.
- Device: read-only HID detection. Applying mapping is a separate explicit action: only three reconstructed reports, not full vendor initialization. Writes are not yet hardware-validated; automatic startup/reconnection writes are not enabled.
- Pressure & ink: drawing canvas and live history from Windows stylus pressure. A toggle above the graph selects pen-event history or a continuously scrolling ten-second timeline (20 Hz), including idle time. Mouse/hover input is excluded from the graph and Windows must report pressure support. Actual pen testing is still needed; unavailable pressure is not replaced with simulated values. The drawing cursor follows supported Windows X/Y tilt properties; devices without exposed tilt show only a tip marker. Actual T906 tilt availability still needs pen testing.
- Buttons: the original ten-key dialog layout displays vendor default assignments for reference, with click-to-select highlighting. Current device assignments are not decoded. Pen button customization saves a global draft profile with separate upper/lower actions (shortcut, mouse click or Disabled). Shortcut capture supports Ctrl/Shift/Alt/Win. Profiles live at `%LOCALAPPDATA%/VinsaStudio/pen-profile.json`; Save profile does not apply them to devices or other apps. The editor includes a tester input monitor; keyboard/mouse events without a verified device identity are labelled accordingly. Global interception and device application are not implemented. The pressure fine-tune slider is a local preview from −255 to +255 with reset to zero; it does not change device pressure or the Windows pressure graph. Restore default resets only the reconstructed mapping/mode fields locally.

The app manifest uses `asInvoker`: it does not request administrator privileges. This is separate from publisher signing/reputation; the current build is not code-signed.

## Verify

Verify the .NET backend and read device metadata:

```powershell
dotnet run --project tests/VinsaStudio.CoreChecks -- --devices
dotnet build src/VinsaStudio -c Release
./build/VinsaStudio/VinsaStudio.exe --smoke-test --settings downloads/windows/installed/MyTablet/TSetting.ini
```

The backend passes 47 checks, including 9,000 drag/resize scenarios and fixed-rate pressure-history tests. The GUI smoke test renders the main window and both dialogs, checks slider bounds/reset, both graph modes, preset/key selection and reopening, and does not apply settings. Automated tests do not verify real pen pressure or hardware writes.

The optional independent oracle compares .NET encoders against bounded emulation of the vendor's actual x86 packing instructions. It requires Python and Unicorn only for research verification:

```powershell
uv run --with unicorn python scripts/verify_protocol_oracle.py
```

This passes 2,000 comparisons across 1,000 deterministic rectangles, covering both screen commands. The former Python app, CLI, packaging and tests have been removed; the Windows app uses only .NET.

See [analysis and limitations](docs/research/analysis.md) for target hashes, procedure addresses, REA Evidence IDs, and coverage.

## Layout

```text
src/VinsaStudio/     .NET Windows desktop app and native backend
tests/               Implementation tests and synthetic fixtures
scripts/             Development and artifact inspection tools
docs/research/       Evidence, findings, and artifact hashes
docs/vendor/         Original vendor manuals and instructions
downloads/           Original vendor software, included in Git
  vendor-media/      Installation-media files in their original relative layout
  windows/installed/ Installed MyTablet files supplied for investigation
  windows/installers/4.8.1.905/  Separately downloaded Windows installer
artifacts/           Local extraction, decompilation, and REA output
build/               Generated build output
```

Keep vendor binaries separate from implementation code. Preserve package-relative paths when extracting software: configuration and resource files can depend on those paths. Record observed behavior and its evidence before implementing it. Vendor inputs in `downloads/` and `docs/vendor/` are included in Git; generated `artifacts/` and `build/` output are ignored.

`docs/research/vendor-artifacts.json` records the 25 supplied vendor files, their original paths, current paths, sizes, and SHA-256 hashes. The installation-media `Setup.exe` and downloaded 4.8.1.905 `Setup.exe` have different hashes and are both retained. No exact duplicate files were found. The manifest records the supplied snapshots, not a claim that an installer produced the supplied installed files.

## Artifact inventory

Run from the repository root:

```powershell
./scripts/Get-ArtifactInventory.ps1
```

The script lists files under `downloads/`, records their SHA-256 hashes, and reports exact duplicate files. It reads files without executing them. To save an inventory locally (the output file must not already exist):

```powershell
New-Item -ItemType Directory -Path artifacts -Force
./scripts/Get-ArtifactInventory.ps1 -OutputPath artifacts/inventory.json
```

REA is installed globally, with user-level Ghidra 12.1.4 and Java 21 configured for native analysis. `rea --help` lists its CLI commands. Whole-package decompilation is in ignored `artifacts/mytablet/`, with the reviewed [program guide](docs/research/mytablet-program.md) and [feature map](docs/research/mytablet-feature-map.md). The canonical service export is `tablet-service-seeded/`: 4,256 discovered functions; the launcher and DLL add 226. This is static pseudocode, not original or buildable source. Earlier REA evidence and the reusable snapshot remain in `artifacts/rea/`.

The root MIT license covers this project's implementation. It does not grant new rights to the unchanged vendor binaries and documents supplied under `downloads/` and `docs/vendor/`.
