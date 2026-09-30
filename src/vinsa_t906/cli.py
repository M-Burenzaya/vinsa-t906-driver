"""Command line access to reconstructed settings and Windows HID features."""

from __future__ import annotations

import argparse
from dataclasses import asdict
import json
from pathlib import Path
import sys

from .protocol import plan_feature_reports
from .settings import DeviceState, TabletSettings
from .windows_hid import HidDevice, enumerate_devices, validate_feature_report

FAMILY_IDS = {(0x0B57, 0x902E), (0x08F2, 0x6811)}


def _json(value) -> None:
    print(json.dumps(value, indent=2, ensure_ascii=False))


def select_device(path: str | None = None):
    candidates = [d for d in enumerate_devices() if d.is_supported_vendor_interface
                  and d.feature_report_length == 8 and (path is None or d.path == path)]
    if len(candidates) != 1:
        raise ValueError(f"expected exactly one matching 8-byte vendor interface; found {len(candidates)}. "
                         "Use devices to inspect paths, then --device-path to select one.")
    return candidates[0]


def settings_summary(settings: TabletSettings) -> dict:
    return {"ink_mode": settings.ink_mode, "smoothness": settings.smoothness,
            "left_hand_mode": settings.left_hand_mode,
            "screen_map": asdict(settings.screen_map), "tablet_map": asdict(settings.tablet_map)}


def plan_summary(settings: TabletSettings, version: int) -> dict:
    return {"settings": settings_summary(settings), "device_version": version,
            "reports": [{"purpose": name, "hex": report.hex(" ")} for name, report in zip(
                ("tablet mapping", "mode", "screen mapping"),
                plan_feature_reports(settings, device_version=version))],
            "limitations": ["Smoothness, pressure curves, hotkeys and full vendor initialization are not applied.",
                            "Encoders are statically verified; device write behavior has not been hardware-validated."]}


def _settings(path: str | None) -> TabletSettings:
    return TabletSettings.from_file(path) if path else TabletSettings.factory_defaults()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Reconstructed Vinsa tablet configuration tool")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("devices", help="read connected tablet-family HID collection metadata")
    settings_parser = sub.add_parser("settings", help="read a vendor TSetting.ini without changing it")
    settings_parser.add_argument("path")
    plan = sub.add_parser("plan", help="preview feature reports without opening hardware")
    plan.add_argument("--settings")
    plan.add_argument("--device-version", type=lambda v: int(v, 0), default=0,
                      help="USB bcdDevice, decimal or 0x-prefixed; defaults to 0 when unknown")
    apply = sub.add_parser("apply", help="preview mapping/mode for hardware; --write sends the reports")
    apply.add_argument("--settings", required=True)
    apply.add_argument("--device-path")
    apply.add_argument("--write", action="store_true", help="explicitly send three reconstructed feature reports")
    read = sub.add_parser("read-feature", help="read an opaque feature report without writing settings")
    read.add_argument("--device-path")
    read.add_argument("--report-id", type=lambda v: int(v, 0), default=8)
    state = sub.add_parser("state", help="inspect an opaque 64-byte vendor state file")
    state.add_argument("path")
    gui = sub.add_parser("gui", help="open a local settings editor and report preview")
    gui.add_argument("--settings")
    args = parser.parse_args(argv)
    try:
        if args.command == "devices":
            _json([{**asdict(d), "vendor_id_hex": f"{d.vendor_id:04x}",
                    "product_id_hex": f"{d.product_id:04x}",
                    "supported_vendor_interface": d.is_supported_vendor_interface,
                    "supports_reconstructed_writes": d.is_supported_vendor_interface and d.feature_report_length == 8}
                   for d in enumerate_devices() if (d.vendor_id, d.product_id) in FAMILY_IDS])
        elif args.command == "settings":
            _json(settings_summary(_settings(args.path)))
        elif args.command == "plan":
            _json(plan_summary(_settings(args.settings), args.device_version))
        elif args.command == "apply":
            settings = _settings(args.settings)
            info = select_device(args.device_path)
            # Preview uses the real bcdDevice; never silently assume old firmware.
            summary = {"device": asdict(info), **plan_summary(settings, info.version), "written": False}
            if args.write:
                with HidDevice(info) as device:
                    reports = plan_feature_reports(settings, device_version=device.info.version)
                    for report in reports:
                        validate_feature_report(device.info, report)
                    completed = []
                    for report in reports:
                        try:
                            device.set_feature(report)
                        except OSError as exc:
                            raise OSError(f"feature write failed after {len(completed)} reports; "
                                          f"device may be partially updated: {exc}") from exc
                        completed.append(report.hex(" "))
                summary["written"] = True
            _json(summary)
        elif args.command == "read-feature":
            info = select_device(args.device_path)
            with HidDevice(info) as device:
                report = device.get_feature(args.report_id)
            _json({"device_path": info.path, "hex": report.hex(" "), "decoded": False})
        elif args.command == "state":
            state = DeviceState.from_file(args.path)
            _json({"path": str(Path(args.path)), "bytes": len(state.raw), "hex": state.raw.hex(" "),
                   "decoded": False})
        elif args.command == "gui":
            from .gui import run
            run(args.settings)
        return 0
    except (OSError, ValueError, RuntimeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
