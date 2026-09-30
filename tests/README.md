# Tests

Run with `PYTHONPATH=src python -m unittest discover -s tests -v` (set `$env:PYTHONPATH='src'` in PowerShell first).

Tests cover saved vendor settings, lossless edits, invalid bounds, golden feature-report vectors, randomized packed-value roundtrips, explicit CLI write behavior, partial failures, device selection, ctypes layouts, and mocked HID lifecycle/capability checks. They do not send hardware reports.

`scripts/verify_protocol_oracle.py` supplies an additional independent check against bounded emulation of the vendor's actual x86 packing instructions. It needs the optional Unicorn package; the runtime application does not.
