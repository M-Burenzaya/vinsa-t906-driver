# Vinsa T906 driver

An evidence-backed Python reconstruction of the tablet's configuration files and HID feature-report encoders, with a Windows transport, CLI, and local settings editor. REA/Ghidra was used to analyze the supplied native Delphi `TabletService.exe`.

This is new implementation source, not the original vendor source or a complete replacement for the vendor application. Mapping and mode reports are reconstructed; pen input, pressure curves, tilt, hotkey dispatch, Wintab/Windows Ink integration, and full device initialization are not implemented. Windows continues to provide the tablet's existing HID input support.

## Run from source

Requires Python 3.11+; no third-party runtime packages. Windows is required for HID access. The GUI uses Python's Tkinter.

```powershell
python scripts/vinsa.py devices
python scripts/vinsa.py settings downloads/windows/installed/MyTablet/TSetting.ini
python scripts/vinsa.py plan --settings downloads/windows/installed/MyTablet/TSetting.ini
python scripts/vinsa.py gui --settings downloads/windows/installed/MyTablet/TSetting.ini
```

`plan` does not access hardware. `apply` without `--write` reads device metadata and previews the reports using its actual USB version:

```powershell
python scripts/vinsa.py apply --settings downloads/windows/installed/MyTablet/TSetting.ini
```

To explicitly send the reconstructed mapping/mode reports, add `--write` to that command. Hardware writes have not been validated on this tablet. The operation sends three feature reports, does not apply smoothness or hotkeys, and is not the full vendor startup sequence. A partial failure is reported rather than described as success. If more than one eligible tablet is connected, use `--device-path` with a path from `devices`.

The GUI loads and saves local settings copies and previews reports; it does not change hardware. Do not treat a successful feature read as decoded current settings: the report remains opaque.

## Verify

```powershell
$env:PYTHONPATH = 'src'
python -m unittest discover -s tests -v
```

The optional independent oracle compares our encoders against bounded emulation of the vendor's actual x86 packing instructions. It needs Unicorn only for this verification:

```powershell
uv run --no-project --with unicorn python scripts/verify_protocol_oracle.py
```

Build an installable wheel with `uv build --out-dir build`, or install into your own virtual environment with `python -m pip install .`. The `vinsa-t906` command exposes the same CLI after installation.

See [analysis and limitations](docs/research/analysis.md) for target hashes, procedure addresses, REA Evidence IDs, and coverage.

## Layout

```text
src/vinsa_t906/      Maintained reconstruction source
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

REA is installed globally, with user-level Ghidra 12.1.4 and Java 21 configured for native analysis. `rea --help` lists its CLI commands. Complete REA exports and the reusable snapshot are in ignored `artifacts/rea/`; reviewed findings are in `docs/research/`.

The root MIT license covers this project's implementation. It does not grant new rights to the unchanged vendor binaries and documents supplied under `downloads/` and `docs/vendor/`.
