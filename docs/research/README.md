# Research notes

Save reviewed investigation notes here. Each finding should include the target filename and SHA-256, the observation, evidence references or offsets, and remaining uncertainty. Distinguish static observations from runtime observations and inferred behavior.

Original vendor software belongs in `downloads/`; original manuals and instructions belong in `docs/vendor/`. Both are included in Git. Extracted files, generated pseudocode, raw captures, and REA evidence exports belong in the ignored `artifacts/` directory.

`vendor-artifacts.json` records the supplied files and their paths before and after organization. Check its hashes when selecting targets for analysis.

`analysis.md` records the verified mapping/mode protocol. `pressure-and-keys.md` follows the remaining pressure and custom-key paths, including the newly identified command-04 and device-storage programming observations. Those operations remain unimplemented and untested on hardware. The .NET pressure tester uses Windows stylus input instead of reconstructing that decoder.

[mytablet-program.md](mytablet-program.md) is the whole-package export guide and review: 4,482 discovered function entries across the service, launcher and communication DLL. [mytablet-feature-map.md](mytablet-feature-map.md) tracks verified, partial and unresolved features. Bulk output is pseudocode, not original source.
