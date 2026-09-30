# Tablet software analysis and reconstruction

Analysis date: 2026-10-11. REA 6.4.0 MCP, Ghidra 12.1.4, Windows x64, native x86 target. The vendor installers/application were not executed. Ghidra analyzed an immutable copy in an ephemeral project; the session was exported and closed.

## Target identity

Primary target: `downloads/windows/installed/MyTablet/TabletService.exe`.

- SHA-256: `083819bcafd2f95faed64f4ae9ec0cf6d152677a23cc4a6984f47a7c5d06097b`
- Size: 1,118,208 bytes; native x86 PE32, image base `0x400000`.
- Static observations include Delphi/VCL RTTI and `TJvHidDeviceController`/`TUsbComm` references. This is not an ordinary managed .NET assembly.
- All supplied vendor inputs are recorded in `vendor-artifacts.json`. The two installers are different artifacts, not duplicates. Neither installer has been unpacked or reconstructed in this pass.

An independent standard-library PE pass identified `TabletCom_vc.dll` exports `DetectDevice`, `EraseBlock`, `WriteBuffer`, and `ReadBuffer`, with disk/ASPI-related strings/imports. A programming/storage role is an inference; its firmware command semantics and original source remain unknown. It is not used by this reconstruction.

## Evidence ledger

The complete session export is local at `artifacts/rea/tablet-service-evidence.json` (19 Evidence records). The reusable analysis snapshot is `artifacts/rea/tablet-service-snapshot.json`. These generated files are excluded from Git. IDs below refer to records in that export, not invented source symbols.

| Observation | Procedure / evidence ID |
| --- | --- |
| Eight-byte report buffer initialized with ID `08`; default full-area rectangle | `0x495548`, `ev_22e7a132b90591f28db36b416f5fcfb2abe82b2c436064f337d04f4d8e0667e9` |
| Command `07` rectangle encoding and 300 ms write interval | `0x494b84`, `ev_7c7535ab5ad275498bc8171424c7c0da631d47abea71a0a7595c6f43ffb9e3a8` |
| Setter copies the four rectangle fields and calls command `07` | `0x494c24`, `ev_c4f97fd47182aa63fda4df4f1e2a0148d63645b09abba9bac85a4f5be2b380d8` |
| INI key names anchor screen/tablet rectangle field offsets | `0x4bc470`, `ev_a675262e5ead9faa5dc7c1cf1aa164b3aff9b793fc5a2ab501f4899a42b8250c` |
| Caller routes tablet fields to command `07` and screen fields to four-word packing | `0x4bdd1c`, `ev_6362a643d450f66218a79a5e1348a57d064d10909447ba5b83ea66a8e4ce8cf8` |
| Screen words are `(top, left, width, height)`; stored before sender | `0x495480`, `ev_1fae78551c0efd9f5992498d45dc8a4df2b3e3f6e846e7db6b02dfe4a522d819` |
| Screen bytes use a different nibble ordering from tablet bytes | `0x495410`, `ev_02b8ece76ed1eb123ed58e6633712898ea9f6b260529c467654828deea8266f5` |
| Screen sender sets command, packs bytes, and calls feature writer | `0x495be0`, `ev_69296726256bc687f90abebbd75070e1140db25b70d487344de651c80a8eeb91` |
| Mode command `06`: boolean byte, left-hand code, gated crop flag, zero padding | `0x494c98`, `ev_3c2965ef565f49f6fb04218fa71300784651c3193f8dc3a6c890c34ef5261fea` |
| Mode setter maps left-hand value `1` to byte `3`, otherwise `0` | `0x494c64`, `ev_0227e0361469a9a9b542e27dbf60767a2abcd4412acc007afd3d2aa9fab01cb4` |
| Device version above `0x1802` forces mode byte to `1` | `0x494c7c`, `ev_7fb2bfef9b4cad51dcddefd79827b6b3abee0f45afb2e4592bb4019265cd7dd9` |
| High-level settings application chooses screen command `1` or `3`; includes additional unimplemented operations | `0x4bdde0`, `ev_babda6f8c3477a02c5c47cde324c41673091660409f05910f2044c308c3f95da` |
| Feature writer passes buffer, length, and device handle to an indirect HID function | `0x46d414`, `ev_e5e1abd757536b5271c5c47f75b493ecfed4dae29cc434f50e6223a0d7206ed3` |
| HID resolver loads `HidD_SetFeature` and other HID APIs | `0x46b604`, `ev_9b3b984f216fc856dda93266b94948f0a02fa63a750fa10635bc07141246cd90` |

Function names of the form `FUN_...` are Ghidra-generated labels. Decompiled signatures/register variables can be imperfect. The encoders are anchored by assembly and separately checked against the original bounded instruction blocks; decompiled text is not presented as original source.

## Reconstructed feature reports

Let `L,T,R,B` be the validated rectangle coordinates, `W=R-L`, and `H=B-T`. Each coordinate is in `0..4095`; reconstructed code rejects empty/inverted rectangles instead of silently truncating invalid input.

Define `hi(a,b)=((a >> 8) & 15) | ((b >> 4) & 240)`.

| Purpose | Eight bytes |
| --- | --- |
| Tablet area | `08 07 hi(L,T) low(L) low(T) hi(W,H) low(W) low(H)` |
| Screen area, legacy | `08 01 low(L) low(W) hi(L,W) low(T) low(H) hi(T,H)` |
| Screen area, ink | Same screen payload, command byte `03` |
| Mode | `08 06 effective_ink left_hand_code crop_gate 00 00 00` |

The mode crop gate depends on object field `+0x1c`, whose UI semantics are unknown. Its observed constructor default is zero; normal reconstruction plans leave it zero. The public pure encoder can represent the gate explicitly without pretending it is the smoothness setting.

`effective_ink` is the requested setting or a version above `0x1802`. Left hand mode uses code `3`, not `1`. The screen command selection remains tied to the saved ink setting; the vendor's version override occurs in the separate mode setter.

The reconstructed plan sends tablet area, mode, and screen area with at least 300 ms between writes. This is a selected subset of the vendor sequence. Commands `04`, device state loading, smoothness logic, and additional initialization are not replayed. Feature writes were not sent during validation.

## Settings and state files

The supplied `TSetting.ini` has InkMode `0`, LeftHandMode `0`, Smoothness `0`, screen rectangle `(1753,0,4095,4095)`, and tablet rectangle `(650,650,3500,3500)`.

Factory defaults observed in the embedded INI differ: InkMode `1`, both rectangles full `0..4095`, other values zero. `settings.py` separates defaults from saved values and preserves unknown keys, encoding bytes, comments, spelling, and line endings. Smoothness is preserved as a nonnegative integer; no undocumented hardware effect is assigned to it. The actual supplied settings file uses CRLF. Synthetic CRCRLF files are additionally covered by tests; an earlier independent note incorrectly described the supplied file's newline bytes.

`TabletWheel.ini` can be preserved/edited as an opaque-key INI. Hotkey modifier meaning and dispatch are not reconstructed. The 64-byte `T906Def.bin` and `T906_2023.dat` roundtrip losslessly as opaque state. Their names and small size do not establish firmware-image semantics.

## Live observations

Read-only Windows enumeration from our new source found `[T906] Driver Inside Tablet`, manufacturer `SZ PING-IT INC.`, VID `08F2`, PID `6811`, USB version `0x2023`. These are direct Windows HID observations, not REA Evidence records. Original binary selection code also contains `(0B57,902E)` and `(08F2,6811)` family candidates; those IDs do not identify T906 exclusively.

| Collection | Usage | Input/output/feature lengths | Treatment |
| --- | --- | --- | --- |
| `MI_01&COL01` | Vendor page `FFA0`, usage 1 | `64 / 0 / 0` | Metadata only; no feature writes |
| `MI_02&COL01` | Vendor page `FFA0`, usage 1 | `8 / 9 / 8` | Matches reconstructed feature length |
| Other collections | Mouse, keyboard, digitizer, consumer control | Vary | Not selected for feature writes |

One explicit `HidD_GetFeature` for report `08` returned success with eight zero bytes. No mapping, pressure, or current mode was inferred from those bytes. A read API result does not prove the write commands work on this firmware.

Live metadata is saved locally in `artifacts/connected-tablet.json`; the opaque read is `artifacts/feature-08-read.json`. No vendor installer/application was launched, and no `HidD_SetFeature` hardware writes were issued by this investigation.

The transport uses Windows' reported feature length and rechecks identity/capabilities when opening a collection. See [Microsoft's HidD_SetFeature contract](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setfeature). Invalid lengths, unsupported collection paths, report IDs, and commands are rejected before writes. This is an implementation choice around the verified protocol, not an observed vendor validation policy.

## Coverage and remaining work

Validation completed: 34 unit tests pass; the local GUI loads the original INI losslessly and previews the expected report; the wheel builds and the installed command starts. The independent oracle passed 3,021 comparisons over 1,007 rectangles (7 boundary/saved cases and 1,000 seeded random rectangles; seed 2310). It emulates only the verified packing ranges with an instruction budget, stopping before clock/API code and return. It does not execute vendor startup or access the tablet.

Original packing blocks:

- Tablet `[0x494b84,0x494bdf)`, SHA-256 `964b991591f0fd2ae5dc503952df979506398dfbead25f65f44be7e797e6dbba`.
- Screen `[0x495410,0x49547d)`, SHA-256 `8495ec1964ed7f84dff1ec22de17200acd41ec778acf6b2428ac520d1ec98dfd`.

| Question | Coverage |
| --- | --- |
| What executable architecture and HID boundary are used? | Answered for the selected service |
| How are tablet/screen regions packed? | Answered; implemented and checked against bounded original instructions |
| How are the basic mode flags represented? | Answered for the selected feature report; crop gate UI meaning unknown |
| Can user settings and state files roundtrip without losing unknown data? | Implemented and tested |
| Can the new source discover the connected T906? | Verified on the current machine |
| Can reconstructed feature writes change the tablet correctly? | Unresolved; writes not hardware-tested |
| What is the complete pen input/pressure/tilt format? | Unresolved; no fabricated decoder provided |
| What do all hotkey, wheel, pressure-curve and smoothness settings do? | Partial static hints only; not implemented |
| Can this replace the entire original application, Wintab/Ink integration or firmware updater? | No; those features remain outside this reconstruction |

The GUI is a new settings editor built on this source. It is not a reconstruction of the original application layout or a claim of feature parity. Full implementation requires focused follow-up analysis and controlled hardware validation, using the preserved target hashes and snapshot rather than guessing missing behavior.
