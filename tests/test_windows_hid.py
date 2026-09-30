"""Transport guards exercised without Windows DLLs or hardware writes."""

from dataclasses import replace
import ctypes
import unittest
from unittest.mock import Mock, patch

from vinsa_t906 import windows_hid as hid


def device_info(**changes):
    info = hid.DeviceInfo(
        path=r"\\?\hid#vid_08f2&pid_6811&mi_01&col01#fixture#{guid}",
        vendor_id=0x08F2, product_id=0x6811, version=0x1803,
        product="Tablet", manufacturer="Vendor", usage_page=0xFF00, usage=1,
        input_report_length=8, output_report_length=0, feature_report_length=8,
    )
    return replace(info, **changes)


class SelectorTests(unittest.TestCase):
    def test_supported_interfaces(self):
        for vendor, product in ((0x08F2, 0x6811), (0x0B57, 0x902E)):
            for interface in (1, 2):
                path = rf"\\?\hid#vid_{vendor:04x}&pid_{product:04x}&mi_{interface:02x}&col01#instance"
                self.assertTrue(device_info(path=path, vendor_id=vendor,
                                            product_id=product).is_supported_vendor_interface)

    def test_reject_other_collection_interface_and_identity(self):
        for component in ("mi_00&col01", "mi_03&col01", "mi_01&col02", "mi_01&col010", "mi_010&col01"):
            self.assertFalse(device_info(path=f"hid#vid_08f2&pid_6811&{component}#fixture").is_supported_vendor_interface)
        self.assertFalse(device_info(vendor_id=0x1234).is_supported_vendor_interface)
        self.assertFalse(device_info(path="hid#vid_0b57&pid_902e&mi_01&col01#fixture").is_supported_vendor_interface)

    def test_windows_struct_layout(self):
        self.assertEqual(ctypes.sizeof(hid._CAPS), 64)
        self.assertEqual(ctypes.sizeof(hid._ATTRIBUTES), 12)
        self.assertEqual(ctypes.sizeof(hid._INTERFACE_DATA), 32 if ctypes.sizeof(ctypes.c_void_p) == 8 else 28)


class GuardTests(unittest.TestCase):
    def test_known_commands(self):
        for command in (1, 3, 6, 7):
            hid.validate_feature_report(device_info(), bytes([8, command, 0, 0, 0, 0, 0, 0]))

    def test_unsafe_reports_rejected(self):
        for report in (b"", bytes(7), bytes(9), bytes([9, 7, 0, 0, 0, 0, 0, 0]), bytes([8, 255, 0, 0, 0, 0, 0, 0])):
            with self.assertRaises(ValueError):
                hid.validate_feature_report(device_info(), report)
        for info in (device_info(feature_report_length=9), device_info(vendor_id=0x1234)):
            with self.assertRaises(ValueError):
                hid.validate_feature_report(info, b"\x08\x07" + bytes(6))
        with self.assertRaises(TypeError):
            hid.validate_feature_report(device_info(), bytearray([8, 7, 0, 0, 0, 0, 0, 0]))


class MockTransportTests(unittest.TestCase):
    def setUp(self):
        self.api = Mock()
        self.api.open.return_value = 123
        self.api.info.return_value = device_info()
        self.patch = patch.object(hid, "_WindowsApi", return_value=self.api)
        self.patch.start()
        self.addCleanup(self.patch.stop)

    def test_enumeration_only_reads_metadata(self):
        self.api.paths.return_value = [device_info().path]
        self.assertEqual(hid.enumerate_devices(), [device_info()])
        self.api.close.assert_called_once_with(123)
        self.api.get_feature.assert_not_called()
        self.api.set_feature.assert_not_called()

    def test_open_only_reads_metadata_and_close_on_exception(self):
        with self.assertRaises(RuntimeError):
            with hid.HidDevice(device_info()):
                raise RuntimeError("fixture")
        self.api.set_feature.assert_not_called()
        self.api.get_feature.assert_not_called()
        self.api.close.assert_called_once_with(123)

    def test_live_capability_mismatch_closes_before_writes(self):
        self.api.info.return_value = device_info(feature_report_length=64)
        with self.assertRaises(ValueError):
            with hid.HidDevice(device_info()):
                pass
        self.api.close.assert_called_once_with(123)
        self.api.set_feature.assert_not_called()

    def test_get_uses_declared_length(self):
        self.api.get_feature.return_value = b"\x08" + bytes(7)
        with hid.HidDevice(device_info()) as device:
            self.assertEqual(device.get_feature(8), b"\x08" + bytes(7))
            self.api.get_feature.assert_called_once_with(123, 8, 8)
            for report_id, length in ((256, None), (-1, None), (8, 7)):
                with self.assertRaises(ValueError):
                    device.get_feature(report_id, length)

    def test_rejected_write_never_reaches_backend(self):
        with hid.HidDevice(device_info()) as device:
            with self.assertRaises(ValueError):
                device.set_feature(b"\x08\xff" + bytes(6))
        self.api.set_feature.assert_not_called()

    def test_explicit_write_and_spacing(self):
        report = b"\x08\x07" + bytes(6)
        with patch.object(hid.time, "monotonic", side_effect=[1.0, 1.1, 1.3]), patch.object(hid.time, "sleep") as sleep:
            with hid.HidDevice(device_info()) as device:
                device.set_feature(report)
                device.set_feature(report)
        sleep.assert_called_once()
        self.assertAlmostEqual(sleep.call_args.args[0], 0.2)
        self.assertEqual(self.api.set_feature.call_count, 2)

    def test_requires_context(self):
        device = hid.HidDevice(device_info())
        with self.assertRaises(RuntimeError):
            device.get_feature(8)
        with self.assertRaises(RuntimeError):
            device.set_feature(b"\x08\x07" + bytes(6))


class PlatformTests(unittest.TestCase):
    def test_non_windows_guard(self):
        with patch.object(hid.sys, "platform", "linux"):
            with self.assertRaisesRegex(OSError, "only on Windows"):
                hid._WindowsApi()


if __name__ == "__main__":
    unittest.main()
