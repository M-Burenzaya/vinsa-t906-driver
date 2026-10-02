# Verification

Run `dotnet run --project tests/VinsaStudio.CoreChecks -- --devices` for the 47 backend checks and read-only device inventory. Checks include lossless settings, original-file protection, golden report bytes, device eligibility, monitor normalization and 9,000 movement/resize cases, and deterministic pressure-history timing. Graph checks cover idle trace expiry, retained event history, stationary contact, sampling boundaries, bounded long timer gaps and clear. Profile checks cover shortcut labels, invalid bindings/schema versions, JSON round-trips and replacing saved local profiles. They never send tablet commands.

Build with `dotnet build src/VinsaStudio -c Release`. Run the published app with `--smoke-test` to render the original-style main window and two dialogs, exercise graph mode switching, slider endpoints/reset, preset/key selection and dialog reopening, and exit. Optional `--preview-dir artifacts/ui/<new-folder>` captures the desktop preview and saves rendered windows locally; choose a fresh folder. A regular smoke test does not capture desktop pixels.

`scripts/verify_protocol_oracle.py` compares exported .NET reports against isolated vendor x86 instructions using optional Unicorn. Run `uv run --with unicorn python scripts/verify_protocol_oracle.py`. It checks 2,000 reports, never starts vendor software and never accesses hardware.

Real pen pressure, mapping application, reconnect behavior and full vendor initialization still require separate hardware verification.
