# MyTablet package and feature map

Reviewed on 2026-10-11 against the preserved Windows application, not by executing the vendor program. This is a static investigation and reconstruction guide. Decompiled pseudocode, recovered Delphi form properties, and import tables are not the original project source and do not establish runtime equivalence.

## Package boundary

The three application code images are all native x86 PE32. The supplied package also contains the uninstaller, configuration, localization, default/device-state files, and original installers. An installer or uninstaller is a separate target; this review does not silently execute or unpack either.

| Component | SHA-256 | Established role / boundary |
| --- | --- | --- |
| `TabletService.exe` (1,118,208 bytes) | `083819bcafd2f95faed64f4ae9ec0cf6d152677a23cc4a6984f47a7c5d06097b` | Main Delphi/VCL application. Owns configuration forms, device communication, tray UI, and the application-side settings sequence. A name ending in Service does not prove Windows Service Control Manager registration. |
| `TStart.exe` (15,872 bytes) | `a3699861d7a96e505c85e0e5c617a15a7b4b9d4d28e3d513374c8cc9c8d1511e` | Small native launcher candidate: imports `ShellExecuteA`, contains `tabletservice.exe` at file offset `0x2f08`. Actual launch flags/arguments need its call-site analysis. |
| `TabletCom_vc.dll` (45,056 bytes) | `56fc575d86550cb2a7262adcfe5926ea3851db3c405f9517c18b4a6f4005607c` | Exports `DetectDevice` at `0x10001000`, `EraseBlock` at `0x10001010`, `WriteBuffer` at `0x10001030`, and `ReadBuffer` at `0x10001090`. Storage/programming use is supported by an application call chain; it is not an established pen input decoder. |

Independent bounded PE observations, including sections, ordinary imports/exports, string locations, and 197 plausible service method descriptors, are retained in ignored `artifacts/mytablet/architecture/package-facts.json`. The producer is alongside it as `inspect_package.py`. Those observations have no REA Evidence IDs. SHA-256 binds every observation to the supplied image. Method descriptor candidates were accepted only when their record length equals `7 + name_length`, their name is an identifier, and the address lies in executable code; duplicate names require their class context.

The service imports the four DLL exports directly. The DLL imports `CreateFileA`, `DeviceIoControl`, logical-drive queries, and dynamic loader functions; `WNASPI32.dll` is a string candidate at file offset `0x70f8`. The service's HID API resolver and writer are established separately by REA evidence below. Ordinary imports also include GDI/GDI+, shell tray APIs, legacy keyboard/mouse injection, and mixer APIs. Imports establish a dependency boundary, not which UI action invokes a function.

## Coverage checklist

"Implemented" below describes the new project, not recovered vendor source. Runtime validation means actual device behavior; offline encoder comparisons do not substitute for it.

| Feature / question | Static evidence | Reconstruction and remaining work |
| --- | --- | --- |
| Screen and tablet area encoding | Command `07` at `0x494b84`; screen packing `0x495410`, sender `0x495be0`; INI/application caller links in [analysis.md](analysis.md). | Encoders implemented and compared to original bounded instructions. Hardware effects, complete initialization, DPI/topology changes, and parity with every original interaction remain unverified. |
| Original mapping window | `TSCREENMAPPINGFORM` resource: desktop component, tablet image slot, screen/tablet toggle controls. Its adjacent published-method table identifies `FormCreate` `0x4a80ac`, `FormShow` `0x4a814c`, screen toggle `0x4a7fe8`, and tablet toggle `0x4a85c0`. | Reproduce the desktop preview, selection handles, and device illustration in .NET using the verified mapping model. Form layout alone does not establish physical tablet dimensions or mouse gesture semantics. |
| Windows Ink and handedness | Mode setter `0x494c64`, version override `0x494c7c`, report `0x494c98`, application path `0x4bdde0`. | Selected mode report implemented. Device version above `0x1802` forces the separate mode byte to 1; screen-command choice still follows saved InkMode. Full Windows Ink/Wintab integration is not reconstructed. |
| Ten T906 tablet key assignments | `TSETKEYST906` form has ten buttons and modifier controls. Class-paired update handler `0x4adb74`; reset `0x4ae000`. First twenty default-state bytes correlate with the screenshot's shortcuts. | Strong default-layout inference; full modifier encoding, physical-to-logical key mapping, live state, and storage/write behavior need method-body confirmation. No complete key mapper is claimed. |
| Pen custom function / pressure fine tune | Slider reader `0x4b39fc`, application `0x4bdde0`, command-04 sender `0x49537c`, persistence path `0x494db8`. | Slider conversion and six-byte report copy identified; all payload fields and precise behavioral effect remain incomplete. Persistent programming is not replayed. |
| Pressure graph / numerical display | `TFORMA` includes `THeartMachine` graph, progress bar and pressure labels. `0x4b3d70` forwards live pressure field `+0x3d8` to displays. | This supports a pressure history graph, not a demonstrated editable transfer curve. New WPF graph uses Windows stylus input; original raw pressure path remains to be traced. |
| Drawing tester / clear | Main form includes image/test area; clear handler candidate `0x4b4d40`, paint `0x4b4cb4`, cursor timer `0x4b51a0`. | New WPF tester exists. Original stroke rendering, pressure-to-width conversion, sampling, and input gating remain unverified. |
| Raw pen, tilt and button input | HID controller and device-change infrastructure present; multiple HID collections observed read-only. | No complete input report decoder, tilt parser, or event suppression implementation. Do not derive packet layouts from feature-report output. |
| Wheel shortcuts, rotate, undo/redo, volume | `TabletWheel.ini`, forms `TFRMWHEEL` / `TFRMDEFINEKEY`, mode selector `0x4bb7d0` (REA request `0x4bb820`), conversion `0x4bb778`; mixer/injection imports corroborate possible boundaries. | Configuration preserved. Dispatch, modifier conventions, application targeting, and mode transitions require focused analysis. Not every family feature necessarily applies to T906. |
| Local settings and localization | `TSetting.ini`, `LanSetup.ini`, `TabletLan.txt`, `LanName.txt`; load-settings method candidate `0x4beeb8`, load-language `0x4be070`. | Lossless settings handling implemented. Localization files are UTF-16LE; do not rewrite them as UTF-8. Exact runtime precedence and UI language application remain partial. |
| Device state / persistent programming | Two distinct 64-byte files; `0x494db8` updates a loaded state block, computes checksum, conditionally invokes `EraseBlock` and three `WriteBuffer` calls. | State preserved opaque. Flash/storage destinations, transport requests, compatibility, and failure recovery are not yet established. These files are not assumed to be full firmware images. |
| Startup, tray, reconnection and display change | Main form candidates: `FormCreate` `0x4bebac`, tray timer `0x4bee14`, `LoadSetting` `0x4beeb8`, display timer `0x4bf270`, close `0x4bf220`; launcher candidate above. | Full lifecycle call chain and reconnect/application sequence remain partial. No static-only claim that missing startup is the sole cause of the user's mapping reset. |
| Administrator prompt and signing | Service manifest contains `<requestedExecutionLevel level="requireAdministrator"/>` at file `0x110e98`. All three images have zero PE certificate-directory size. | Service explicitly requests elevation; embedded signing is absent. Catalog signing and current Windows policy were not checked here. TStart has no identified execution-level string in this bounded ASCII scan; do not attribute the same manifest to it. |
| Reset defaults | Embedded full-area INI and separate T906 default block; UI reset handlers recoverable. | Basic factory settings distinguished from saved values. Whole-program reset side effects, including persistent storage, remain to be traced. |

## Evidence references

The earlier mapping bundle is `artifacts/rea/tablet-service-evidence.json`; follow-up pressure/key bundle is `artifacts/rea/pressure-keys-evidence.json`. Both are local ignored outputs. Detailed address/evidence links are in [analysis.md](analysis.md) and [pressure-and-keys.md](pressure-and-keys.md). Key reusable records:

| Observation | REA Evidence ID |
| --- | --- |
| Tablet packing `0x494b84` | `ev_7c7535ab5ad275498bc8171424c7c0da631d47abea71a0a7595c6f43ffb9e3a8` |
| Screen packing `0x495410` | `ev_02b8ece76ed1eb123ed58e6633712898ea9f6b260529c467654828deea8266f5` |
| High-level apply `0x4bdde0` | `ev_babda6f8c3477a02c5c47cde324c41673091660409f05910f2044c308c3f95da` |
| Runtime UI/event dispatch `0x4be394` | `ev_3cc4b953d79b2fa8ba75b9d02a64f33307c392cd44c97789863b359a315b384c` |
| HID writer `0x46d414` | `ev_e5e1abd757536b5271c5c47f75b493ecfed4dae29cc434f50e6223a0d7206ed3` |
| HID resolver (request `0x46b6c0`) | `ev_9b3b984f216fc856dda93266b94948f0a02fa63a750fa10635bc07141246cd90` |
| Slider conversion `0x4b39fc` | `ev_bfe1751106796f119c8f6abea86f902606c3f42aa71a8c1fbb4b28b1b8e7b621` |
| Command-04 copy `0x49537c` | `ev_7cdd364b5227107edeb78552712bc66e8b6b5179479f4ad34e2a755fdae49970` |
| Conditional persistent programming `0x494db8` | `ev_cd9f0a907cbda560a4bc658b472b836b278cee0276f85e7375015ed7e0091f41` |
| Pressure history update `0x4b3d70` | `ev_e794ea5b163baa0f9c075512e9cfb00996f2e43aeed76dab53b659d4d07245ab` |
| Preserved-service resource inventory | `ev_87b30fa920161eaa6855cb31dd6aca8ae9af918bc449423f5083082c1deda757` in `artifacts/rea/tablet-service-resources.json` |

Some earlier notes used a containing function address while a REA request used an interior address; the records retain both request and returned analysis. They must not be treated as independent procedures without checking the actual function entry.

The supplied `T906_2023.dat` additionally matches the checksum algorithm observed in `0x494db8`: the sum of its first 62 unsigned bytes is `0x38de`, equal to its final two bytes in high/low order. This is a source-bound file observation that corroborates the block format; it does not validate every field or authorize programming that block. `T906Def.bin` is a distinct layout/default artifact and must not be substituted for this live-state candidate merely because both files are 64 bytes.

The `0x4be394` assembly record adds an important lifecycle boundary. Its event subcase `+0x8 == 0x0b` compares a window getter at `0x44a928` against handles stored at object `+0x33c` and `+0x340`, examines UI state `+0x3e6`, and conditionally sends screen command `1` or `3` via `0x495be0`. Another subcase (`+0x8 == 0x0a`) also selects these commands. Identifying the getter as the foreground window and the event codes' meaning requires their definitions/callers; the comparisons and sends themselves are observed assembly. Thus the vendor's runtime mode selection contains more state than the reconstructed one-shot plan. Reproducing its lifecycle needs this dispatcher and its producers, not only the INI encoder.

## Original mapping form geometry

Resource file range `[1044840,1046355)` has SHA-256 `2f32b24e8523bc220564344ec8bcc1e406a0965360a0caaf7af3ab37b66c9df0` in the preserved-service resource inventory above. The stored binary Delphi form properties are:

- Form client area: `992 x 756`.
- `TDisplayMonitor DisplayMonitor`: left 16, top 8, width 841, height 393.
- `TImage imgTabletFace`: left 16, top 400, width 841, height 346, initially hidden.
- Screen toggle: left 896, top 144, width 73, height 97, initially visible.
- Tablet toggle: left 896, top 520, width 73, height 97, initially hidden.

The image slot dimensions describe an illustration container, not the tablet's physical active area. The screenshot's active-area proportions may be mimicked visually, but exact millimetres and model-image bounds require additional artifact/code evidence. Runtime code can override the stored form properties.

## Practical reconstruction boundary

Reuse the existing device protocol and Windows input stack where established. Reproduce the original interactions in maintained .NET source, rather than treating a binary form resource or decompiler dump as an embeddable WPF component. Bulk export makes the whole analyzed image inspectable; it does not make every feature understood. Before adding hardware behavior, trace its complete caller/callee sequence and distinguish temporary feature settings from persistent erase/programming operations.
