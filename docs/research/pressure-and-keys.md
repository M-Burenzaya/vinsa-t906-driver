# Pressure and key reconstruction status

Target: `TabletService.exe` SHA-256 `083819bcafd2f95faed64f4ae9ec0cf6d152677a23cc4a6984f47a7c5d06097b`. This note records bounded offline observations on 2026-10-11. Resource names and recovered Delphi methods are useful navigation anchors; they do not establish original source recovery or hardware behavior.

The follow-up REA session was exported to local, ignored `artifacts/rea/pressure-keys-evidence.json` (25 records including the reused mapping evidence), then closed. The .NET tester uses [WPF stylus pressure](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.styluspoint.pressurefactor?view=windowsdesktop-10.0) and checks [Windows-reported pressure support](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.tablethardwarecapabilities?view=windowsdesktop-10.0) before updating the graph.

## Existing coverage

Screen/tablet mapping encoders and selected mode reports have been reconstructed and checked against bounded original instruction blocks. The earlier work did **not** reconstruct all pressure, pen-key, tablet-key, initialization, and input processing behavior. Refer to `analysis.md` for that evidence ledger and validation boundaries. No hardware writes were performed for this investigation.

## Packaged interface evidence

REA resource inventory Evidence `ev_87b30fa920161eaa6855cb31dd6aca8ae9af918bc449423f5083082c1deda757` identifies these original resource payloads:

| Resource | Original file offset / length | SHA-256 |
| --- | --- | --- |
| `TFORMA` | `1022512 / 7517` | `09fb7c9a66e5748f1117c5fa215e71aca0ac1a114fc24bc73d858885516a5bb8` |
| `TSETKEYST906` | `1090448 / 5655` | `67a0b00411716e06cbd6a665fa6801d1a7144861869cf27f73fc2bd7c7a0ee6a` |

Both start with Delphi's `TPF0` binary form signature. Bounded reads of `TFORMA` identify a pressure graph (`THeartMachine`, `PressureLine`), vertical progress bar (`pbPressure`), maximum-pressure label with caption `8192`, drawing area/image, clear/reset controls, modifier checkboxes, and custom-key text field. Its `TjanTracker` pressure slider stores signed 16-bit `Minimum=-255`, `Maximum=255`, and an 8-bit initial `Value=0`. The pressure timer has a stored interval of 100 ms; the apply timer has 400 ms. These are stored form properties, which runtime code may change. The graph component's name does not prove that it is an editable pressure transfer curve; it is also consistent with a time history of pressure readings.

`TSETKEYST906` contains ten buttons (`FlatBtn1` through `FlatBtn10`), key combo box, Shift/Ctrl/Alt checkboxes, update and reset controls. All ten buttons refer to the same click/mouse-down handlers. This corresponds to the ten editable tablet keys shown in the supplied screenshot.

Delphi method-table descriptors were read offline from the executable. Each listed descriptor has a 16-bit record length equal to `7 + name_length`, a 32-bit code address, and the corresponding length-prefixed method name. Pairing duplicate method names with their adjacent form/class table is necessary.

| Form / method | Address |
| --- | --- |
| `TFormA.tmrPressureLineTimer` | `0x4b3d70` |
| `TFormA.FormCreate` | `0x4b40ac` |
| `TFormA.tmrApplyTimer` | `0x4b459c` |
| `TFormA.jntrckr1ChangedValue` | `0x4b465c` |
| `TFormA.FormPaint` | `0x4b4cb4` |
| `TFormA.btnClearImageClick` | `0x4b4d40` |
| `TFormA.btnResetPressureClick` | `0x4b4d4c` |
| `TFormA.tmrCursorTimer` | `0x4b51a0` |
| `TSetKeysT906.FormCreate` | `0x4ad898` |
| `TSetKeysT906.FlatBtn1Click` | `0x4ada64` |
| `TSetKeysT906.FlatBtn1MouseDown` | `0x4ada70` |
| `TSetKeysT906.BtnUpdateClick` | `0x4adb74` |
| `TSetKeysT906.cbb1Change` | `0x4adf64` |
| `TSetKeysT906.chkCtrlMouseUp` | `0x4adf6c` |
| `TSetKeysT906.BtnResetClick` | `0x4ae000` |

These are navigation observations; the method bodies need focused decompilation before reproducing their effects.

The expanded package review validated each count-prefixed contiguous method table against its adjacent class name. This corrected the T906 addresses above: the earlier `0x4b1e7c..0x4b25e4` group belongs to `TSetKeysM1161`, not T906. Duplicate handler names across model-specific forms must be paired by their table/class rather than selected by name alone. Source-bound details are retained in `artifacts/mytablet/architecture/package-facts.json`.

## State and apply-sequence hints

The first 20 bytes of the supplied 64-byte `T906Def.bin` are:

```text
00 45  00 42  01 BD  01 BB  00 DB
00 DD  00 09  00 20  01 00  02 00
```

The bytes themselves are observed file contents. Interpreting successive pairs as modifier/key produces the screenshot's E, B, Ctrl+minus, Ctrl+equals, left bracket, right bracket, Tab, Space, Ctrl-only, Alt-only assignments using Windows virtual-key values. This is an inference: a strong candidate layout with modifier `01` for Ctrl and `02` for Alt. The remaining bytes, Shift combinations, firmware-specific storage/transmission, and whether defaults equal active assignments must be confirmed in code. The entire file remains preserved as opaque state in the current implementation.

The separate supplied `T906_2023.dat` does **not** have these same pairs: its first six bytes are `05 F3 1D 01 00 00`, followed by 56 bytes of `FF`, ending with `38 DE`. Its relationship to active customized assignments is unknown. A default-file name and screenshot correlation do not show that it stores the current settings.

Existing apply-function Evidence `ev_babda6f8c3477a02c5c47cde324c41673091660409f05910f2044c308c3f95da`, procedure `0x4bdde0`, gives further navigation:

- `FUN_004b39fc` returns a short value that is split across device-object bytes `+0xaa` and `+0xab` and passed to `FUN_0049890c` with offset `0x5aa`. Its pressure-slider meaning is a candidate until the callee is inspected.
- UI-state bytes `+0x3e4` and `+0x3e5` are copied to device-object bytes `+0xa8` and `+0xa9`.
- `FUN_0049537c` and, when these bytes or the short value change, `FUN_00494db8` participate in applying that state. Their report format is not established by the caller alone.

The exported snapshot also contains a wheel-mode selector (`0x4bb7d0`, request anchored at `0x4bb820`). Its modifier conversion calls `0x4bb778`. Wheel-specific modifier representations should not automatically be applied to tablet or pen key storage.

## Focused REA follow-up

The follow-up evidence export is `artifacts/rea/pressure-keys-evidence.json`, excluded from Git. These code observations refine the earlier caller-only hints; decompiled signatures and object field types remain recovered analysis, not original source.

| Procedure | Evidence ID | Observation |
| --- | --- | --- |
| `0x4b39fc` | `ev_bfe1751106796f119c8f6abea86f902606c3f42aa71a8c1fbb4b28b1b8e7b621` | Reads the tracker's integer field at `+0x24c`; positive values are shifted right by two, while nonpositive values are unchanged. This identifies an asymmetric slider conversion. |
| `0x49537c` | `ev_7cdd364b5227107edeb78552712bc66e8b6b5179479f4ad34e2a755fdae49970` | Stores command `04` at report-buffer field `+0x55` and copies six bytes from object fields `+0xa8..+0xad` into `+0x56..+0x5b`; observes a 300 ms interval before calling feature writer `0x46d414`. Report ID `08` at `+0x54` is anchored by the earlier report initialization evidence. |
| `0x494db8` | `ev_cd9f0a907cbda560a4bc658b472b836b278cee0276f85e7375015ed7e0091f41` | Loads 64-byte state through `0x494f78`, compares pen/pressure fields, updates bytes `+0x5e..+0x62`, sends the command-04 path, calls reset helper `0x494d84`, then sums 62 bytes `+0x5c..+0x99` and places checksum high/low bytes at `+0x9a/+0x9b`. The subsequent path calls `EraseBlock`, sleeps 100 ms, and calls `WriteBuffer` three times. |
| `0x4b3d70` | `ev_e794ea5b163baa0f9c075512e9cfb00996f2e43aeed76dab53b659d4d07245ab` | Forwards a live field at `+0x3d8` to the pressure graph and progress display. Combined with the timer and graph properties, this supports interpreting the displayed graph as pressure history rather than an editable transfer curve. |

Together with the apply caller, the command-04 payload contains pen-key bytes and a split pressure adjustment value. The complete meaning of all six payload bytes, modifier bit assignments, conversion limits at runtime, raw pen-pressure input decoding, and drawing-test behavior still require additional evidence. The asymmetric slider conversion does not by itself identify a mathematical pressure transfer curve.

The `EraseBlock`/`WriteBuffer` path is stronger evidence for persistent device-storage programming than the earlier DLL export-name hint. It remains a static observation: storage destination, failure behavior, endurance, firmware compatibility, and actual execution have not been verified. **This sequence is not implemented or replayed by the reconstruction.** A UI edit must not silently invoke storage erasure or programming based on incomplete decompilation.

## Reuse in a .NET application

A .NET Windows interface can reuse the documented mapping protocol and preserve existing vendor settings. Windows stylus APIs can supply pressure for a new graph and drawing tester when the tablet exposes supported pen input. That would reuse the Windows input stack; it would not establish equivalence to the vendor's raw input decoder or its pressure fine-tune command. Changing the tablet's actual pressure response and custom key assignments still requires the missing command/state analysis and validation. No feature should be presented as fully reconstructed solely because its old interface or stored defaults were identified.
