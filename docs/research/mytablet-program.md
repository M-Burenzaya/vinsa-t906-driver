# MyTablet whole-program static export

Reviewed on 2026-10-11. The three application code images have been exported into local `artifacts/mytablet/` and inspected alongside the existing REA evidence. This makes the discovered code, assembly, resources and navigation indices available inside this project. It does **not** recover the original Delphi project or establish that every feature has been reconstructed.

## Export coverage

| Image | Preferred code folder | Discovered function entries | Pseudocode generated | Failed decompilations | Defined strings |
| --- | --- | ---: | ---: | ---: | ---: |
| `TabletService.exe` | `artifacts/mytablet/tablet-service-seeded/` | 4,256 | 4,256 | 0 | 2,687 |
| `TStart.exe` | `artifacts/mytablet/tstart/` | 108 | 108 | 0 | 42 |
| `TabletCom_vc.dll` | `artifacts/mytablet/tablet-com/` | 118 | 118 | 0 | 126 |
| Total | | **4,482** | **4,482** | **0** | **2,855** |

These counts come from each folder's `summary.json`. Entries include import thunks and Delphi runtime/library procedures; they are not counts of application features. Successful decompilation means Ghidra produced text, not that its types, calling conventions, indirect calls or function boundaries are correct. Some output explicitly contains unrecovered jump-table warnings.

The first service autoanalysis found 3,990 function entries. Comparing it with bounded Delphi published-method descriptors exposed 164 missing entries among 197 candidates (`unseeded-missing-methods.json`). Seeding all 197 before autoanalysis produced 4,256 entries, an increase of 266 after further discovery. `seeded-script.log` records 197 seeds created, zero failures, and the final export count. The seeded export is the preferred service code view; `tablet-service/` retains the first pass and the shared resource extraction. The separate REA inventory of 4,519 entries uses another analysis and includes external API entries, so it must not be substituted for the Ghidra totals.

All 72 service resource payloads were extracted, including 18 binary Delphi forms. The launcher has two extracted resources and the DLL has none. Each `resources/manifest.json` binds payload offsets, lengths and hashes to a REA resource inventory. This includes the main form, screen mapper, wheel configuration and several tablet-family key forms; their presence does not mean every feature applies to T906.

The code export covers these three application images. It does not decompile the installer, uninstaller, Windows HID stack, device firmware or every possible runtime-generated/indirect code path. Configuration and localization artifacts remain preserved separately in the vendor package.

## Finding your way through the export

For a function at `0x4bdde0`, open `tablet-service-seeded/pseudocode/004bdde0.c` and the corresponding `assembly/004bdde0.asm`. `functions.json` records each entry, recovered name/signature, body ranges, instruction count, static call targets and decompiler result. `symbols.json` and `strings.json` provide address-based search indices. Static call targets omit indirect Delphi virtual/event calls unless independently resolved.

`tablet-service/pe-and-methods.json` and `architecture/package-facts.json` provide source-bound PE and published-method candidates. `delphi-method-seeds.tsv` records the seed list; `scripts/research/SeedMyTabletMethods.java` and `ExportMyTablet.java` retain the repeatable Ghidra steps. Existing focused REA evidence remains under `artifacts/rea/` and is linked in [analysis.md](analysis.md), [pressure-and-keys.md](pressure-and-keys.md) and [mytablet-feature-map.md](mytablet-feature-map.md).

Generated code, resources and Ghidra project databases remain ignored by Git. Reviewed explanations and research scripts are the maintained project material. Vendor artifacts were read as data; this export did not execute the application or send hardware commands.

## Reviewed architecture and behavior

| Area | Entry points in the export | What the inspection establishes |
| --- | --- | --- |
| Launcher | `tstart/pseudocode/00403a58.c` | Entry constructs a `tabletservice.exe` path and calls `ShellExecuteA` with verb `runas`, null parameters/directory, and show mode 1. Elevation is explicitly requested by the launcher as well as by the main executable's manifest. |
| Main initialization | `tablet-service-seeded/pseudocode/004bebac.c` | Creates several controller/settings objects, registers callbacks, selects Windows Ink state from loaded settings, configures language and tray/window behavior. The object/class names and full ordering still need resolved Delphi metadata and runtime verification. |
| Settings displayed by the UI | `004beeb8.c` | Reads stored/controller fields into the main form, including pen options, pressure tuning and screen/tablet rectangles. It contains the explicit string `SVer: 4.8.1.905`. Reading the UI state is distinct from applying hardware settings. |
| Apply settings | `004bdde0.c` | Copies form settings, calls settings update paths, sends mode/pressure and mapping commands, and conditionally invokes persistent state programming when pen/pressure state changes. The new mapping-only application does not reproduce this entire sequence. |
| Device change | `0046d830.c` | Reconciles an existing device collection with fresh discovery, removes/updates entries, updates counts and invokes the change path `0x46d710`. This supports an application-managed device lifecycle; it does not prove the user's reset cause or complete reconnect recovery. |
| Original mapping form | `004a80ac.c`, `004a814c.c`, resource `tablet-service/resources/resource-049.bin` | Creates a selection component and selects model-specific tablet illustrations. `FormShow` handles multiple tablet families and may override the stored form geometry. The physical T906 active-area dimensions have not been established. |
| Tablet selection presets | `004a85c0.c` and `.asm`, rectangle comparator `004a660c.c` | Toggle cycles full area `(0,0,4095,4095)`, inset `(650,650,3500,3500)`, inset `(1024,1024,3071,3071)`, and a model-specific preset from `0x4a62d4`, labelled `16:9`. Duplicate current rectangles are skipped. Assembly confirms the numeric constants; the helper selects different constants by model rather than accepting arbitrary physical dimensions. The source string at `0x4a6580` is `T906`; assembly at `0x4a645e..0x4a6468` branches to `0x4a64a0`, which sets `(0,148,4095,3948)` through the rectangle helper `0x418c4c` / `0x406bbc`. These exact preset coordinates are now used by the .NET mapping UI, without assuming physical tablet dimensions. |
| Screen toggle | `004a7fe8.c`, `004a5840.c` | Delegates through a component callback. The exported wrapper alone does not establish the exact monitor cycling policy; the indirect target and its state must be resolved. |
| Pressure and pen options | `004b39fc.c`, `0049537c.c`, `00494db8.c` | Slider conversion, command-04 six-byte copying, checksum and conditional storage calls are independently covered by focused REA evidence. Full payload semantics and raw pen input remain partial. |
| Storage DLL | `tablet-com/pseudocode/10001000.c`, `10001010.c`, `10001030.c`, `10001090.c` | Four exports wrap detection, erasure, writing and reading. Read/write wrappers use blocks up to `0x100` bytes and call internal transport helpers. Decompiled loop lengths/return conventions should be checked against assembly before reuse. |
| Storage transport | `tablet-com/pseudocode/100014d0.c`, `10001630.c`, `10001730.c` | Distinct erasure/write command structures are passed to a lower transport dispatcher. Its backend branch depends on global state. This is a separate boundary from ordinary HID feature-report configuration; block destinations and failure recovery are not established. |

The launcher `runas` call is a stronger explanation of the elevation behavior than its filename or a generic unsigned-publisher assumption. It does not establish what specific Windows warning appeared, whether Windows blocked the application, or its current signing/policy state.

The application is a native Delphi/VCL control program with tablet-family support and device communication. `TabletService.exe` is not proven to be a registered Windows service merely by its name. Some recovered procedures belong to the bundled runtime and UI framework. A clean replacement should maintain its own .NET source, reuse established protocol boundaries, and add behavior only after the relevant caller/callee and device lifecycle are understood.

## Limits before claiming feature parity

The main mapping commands, a pressure update path and selected UI/state relationships have focused evidence. The complete raw input decoder, tilt/button dispatch, key modifier encoding, Wintab/Ink interaction, startup/reconnect scheduling, display-change handling and persistent programming recovery are not fully traced or device-tested. Bulk export supplies the material for that work; it does not close those gaps.

Use the [feature map](mytablet-feature-map.md) as the reconstruction checklist. Treat decompiler stack variables such as `extraout_ECX`, `in_ZF`, huge synthetic arrays and unresolved indirect calls as evidence of imperfect recovery. Validate critical control flow and packed fields against the accompanying assembly and retained binary before implementing them.

## Source identity

| Image | SHA-256 |
| --- | --- |
| `TabletService.exe` | `083819bcafd2f95faed64f4ae9ec0cf6d152677a23cc4a6984f47a7c5d06097b` |
| `TStart.exe` | `a3699861d7a96e505c85e0e5c617a15a7b4b9d4d28e3d513374c8cc9c8d1511e` |
| `TabletCom_vc.dll` | `56fc575d86550cb2a7262adcfe5926ea3851db3c405f9517c18b4a6f4005607c` |

The export summaries and resource manifests retain these identities. They distinguish the reviewed artifacts from a similarly named executable or another driver release.
