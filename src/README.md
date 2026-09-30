# Source code

`vinsa_t906` is a Python 3.11+ reconstruction with no third-party runtime dependencies:

- `settings.py`: lossless vendor settings and opaque state files.
- `protocol.py`: pure, evidence-backed feature-report encoders.
- `windows_hid.py`: Windows HID enumeration and explicit feature access.
- `cli.py`: read, preview, and explicitly apply the reconstructed subset.
- `gui.py`: local settings editor and report preview.

This is maintained reconstruction source. It does not contain original Delphi source, kernel-driver code, or an input pen-report decoder. Vendor files and generated decompiler output remain outside this directory.
