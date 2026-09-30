from contextlib import redirect_stdout, redirect_stderr
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from vinsa_t906.cli import main
from vinsa_t906.windows_hid import DeviceInfo

INI = str(Path(__file__).resolve().parents[1] / "downloads/windows/installed/MyTablet/TSetting.ini")
INFO = DeviceInfo(r"\\?\hid#vid_08f2&pid_6811&mi_02&col01#test", 0x08f2, 0x6811, 0x2023,
                  "[T906] Driver Inside Tablet", "SZ PING-IT INC.", 0xffa0, 1, 8, 9, 8)


class CliTests(unittest.TestCase):
    def invoke(self, args):
        output = io.StringIO()
        errors = io.StringIO()
        with redirect_stdout(output), redirect_stderr(errors):
            result = main(args)
        return result, output.getvalue(), errors.getvalue()

    def test_plan_does_not_enumerate_or_open_hardware(self):
        with patch("vinsa_t906.cli.enumerate_devices") as enum, patch("vinsa_t906.cli.HidDevice") as device:
            result, output, _ = self.invoke(["plan", "--settings", INI])
        self.assertEqual(result, 0)
        self.assertEqual(len(json.loads(output)["reports"]), 3)
        enum.assert_not_called()
        device.assert_not_called()

    def test_apply_without_write_only_previews_and_uses_live_version(self):
        with patch("vinsa_t906.cli.enumerate_devices", return_value=[INFO]), patch("vinsa_t906.cli.HidDevice") as device:
            result, output, _ = self.invoke(["apply", "--settings", INI])
        self.assertEqual(result, 0)
        self.assertFalse(json.loads(output)["written"])
        self.assertEqual(json.loads(output)["reports"][1]["hex"], "08 06 01 00 00 00 00 00")
        device.assert_not_called()

    def test_ambiguous_hardware_never_writes(self):
        with patch("vinsa_t906.cli.enumerate_devices", return_value=[INFO, INFO]), patch("vinsa_t906.cli.HidDevice") as device:
            result, _, errors = self.invoke(["apply", "--settings", INI, "--write"])
        self.assertEqual(result, 1)
        self.assertIn("found 2", errors)
        device.assert_not_called()

    def test_explicit_apply_sends_three_verified_reports(self):
        with patch("vinsa_t906.cli.enumerate_devices", return_value=[INFO]), patch("vinsa_t906.cli.HidDevice") as transport:
            device = transport.return_value.__enter__.return_value
            device.info = INFO
            result, output, _ = self.invoke(["apply", "--settings", INI, "--write"])
        self.assertEqual(result, 0)
        self.assertTrue(json.loads(output)["written"])
        self.assertEqual(device.set_feature.call_count, 3)

    def test_partial_write_failure_reports_uncertainty(self):
        with patch("vinsa_t906.cli.enumerate_devices", return_value=[INFO]), patch("vinsa_t906.cli.HidDevice") as transport:
            device = transport.return_value.__enter__.return_value
            device.info = INFO
            device.set_feature.side_effect = [None, OSError("disconnected")]
            result, _, errors = self.invoke(["apply", "--settings", INI, "--write"])
        self.assertEqual(result, 1)
        self.assertIn("partially updated", errors)

    def test_invalid_settings_never_opens_hardware(self):
        with patch("vinsa_t906.cli.HidDevice") as transport:
            result, _, _ = self.invoke(["apply", "--settings", "missing-settings.ini", "--write"])
        self.assertEqual(result, 1)
        transport.assert_not_called()


if __name__ == "__main__":
    unittest.main()
