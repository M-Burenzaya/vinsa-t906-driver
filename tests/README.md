# Verification

Run `dotnet run --project tests/VinsaStudio.CoreChecks -- --devices` for the 30 backend checks and read-only device inventory. Checks include lossless settings, original-file protection, golden report bytes, device eligibility, monitor normalization and 9,000 movement/resize cases. They never send tablet commands.

Build with `dotnet build src/VinsaStudio -c Release`. Run the published app with `--smoke-test` to render all four pages and exit. Optional `--preview-dir artifacts/ui/<new-folder>` captures the desktop preview and saves rendered pages locally; choose a fresh folder. A regular smoke test does not capture desktop pixels.

`scripts/verify_protocol_oracle.py` compares exported .NET reports against isolated vendor x86 instructions using optional Unicorn. Run `uv run --with unicorn python scripts/verify_protocol_oracle.py`. It checks 2,000 reports, never starts vendor software and never accesses hardware.

Real pen pressure, mapping application, reconnect behavior and full vendor initialization still require separate hardware verification.
