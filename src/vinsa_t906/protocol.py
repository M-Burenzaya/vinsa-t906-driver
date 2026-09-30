"""Pure encoders reconstructed from TabletService.exe using REA/Ghidra.

Target SHA256: 083819bcafd2f95faed64f4ae9ec0cf6d152677a23cc4a6984f47a7c5d06097b.
Function addresses and Evidence IDs are recorded in docs/research/analysis.md.
These are feature output reports, not a pen/pressure input-report parser.
"""

from __future__ import annotations

from .settings import MappingRectangle, TabletSettings

REPORT_ID = 0x08
REPORT_LENGTH = 8
MIN_FEATURE_INTERVAL_SECONDS = 0.3


def _pair_high(first: int, second: int) -> int:
    return ((first >> 8) & 0x0F) | ((second >> 4) & 0xF0)


def encode_tablet_mapping(rectangle: MappingRectangle) -> bytes:
    """Command 7: high nibbles, low X/Y bytes, then width/height.

    Reconstructed from FUN_00494b84. The named tablet rectangle is anchored by
    the settings writer and its callers, not just a filename or string guess.
    """
    left, top = rectangle.left, rectangle.top
    width, height = rectangle.right - left, rectangle.bottom - top
    return bytes((REPORT_ID, 7, _pair_high(left, top), left & 255, top & 255,
                  _pair_high(width, height), width & 255, height & 255))


def encode_screen_mapping(rectangle: MappingRectangle, *, ink_mode: bool) -> bytes:
    """Command 1 (legacy) / 3 (ink): two different nibble-packed pairs.

    FUN_004bdd1c passes (top, left, width, height) as four 16-bit words into
    FUN_00495480. FUN_00495410 packs them with a different byte ordering from
    command 7. FUN_00495be0 sends the command selected by FUN_004bdde0.
    """
    if type(ink_mode) is not bool:
        raise ValueError("ink_mode must be bool")
    left, top = rectangle.left, rectangle.top
    width, height = rectangle.right - left, rectangle.bottom - top
    return bytes((REPORT_ID, 3 if ink_mode else 1, left & 255, width & 255,
                  _pair_high(left, width), top & 255, height & 255,
                  _pair_high(top, height)))


def encode_mode(*, ink_mode: bool, left_hand_mode: bool,
                tablet_map: MappingRectangle, device_version: int = 0,
                clipping_enabled: bool = False) -> bytes:
    """Command 6; version >0x1802 forces the ink byte to 1 in vendor code.

    The clipping gate corresponds to object+0x1c. Its UI meaning is unknown;
    the default matches the observed constructor's zero initialization.
    Smoothness is not encoded here: its HID semantics are not established.
    """
    for value in (ink_mode, left_hand_mode, clipping_enabled):
        if type(value) is not bool:
            raise ValueError("mode flags must be bool")
    if type(device_version) is not int or not 0 <= device_version <= 65535:
        raise ValueError("device_version must be a 16-bit integer")
    effective_ink = ink_mode or device_version > 0x1802
    cropped = tablet_map != MappingRectangle()
    return bytes((REPORT_ID, 6, int(effective_ink), 3 if left_hand_mode else 0,
                  int(clipping_enabled and cropped), 0, 0, 0))


def plan_feature_reports(settings: TabletSettings, *, device_version: int = 0) -> tuple[bytes, ...]:
    """Reconstructed subset: tablet region, mode, then screen region.

    The full vendor initialization includes additional commands that this
    implementation does not emit. This plan is not a complete driver startup.
    """
    return (
        encode_tablet_mapping(settings.tablet_map),
        encode_mode(ink_mode=settings.ink_mode, left_hand_mode=settings.left_hand_mode,
                    tablet_map=settings.tablet_map, device_version=device_version),
        encode_screen_mapping(settings.screen_map, ink_mode=settings.ink_mode),
    )
