from pathlib import Path
import random
import unittest

from vinsa_t906.protocol import encode_mode, encode_screen_mapping, encode_tablet_mapping, plan_feature_reports
from vinsa_t906.settings import MappingRectangle, TabletSettings

ROOT = Path(__file__).resolve().parents[1]


class FeatureEncodingTests(unittest.TestCase):
    def test_full_area_golden_vectors(self):
        rectangle = MappingRectangle()
        self.assertEqual(encode_tablet_mapping(rectangle), bytes.fromhex("08 07 00 00 00 ff ff ff"))
        self.assertEqual(encode_screen_mapping(rectangle, ink_mode=True), bytes.fromhex("08 03 00 ff f0 00 ff f0"))

    def test_shipped_user_settings_golden_vectors(self):
        settings = TabletSettings.from_file(ROOT / "downloads/windows/installed/MyTablet/TSetting.ini")
        self.assertEqual(plan_feature_reports(settings), (
            bytes.fromhex("08 07 22 8a 8a bb 22 22"),
            bytes.fromhex("08 06 00 00 00 00 00 00"),
            bytes.fromhex("08 01 d9 26 96 00 ff f0"),
        ))

    def test_packed_values_roundtrip_distinct_byte_orders(self):
        rng = random.Random(906)
        for _ in range(1000):
            left, right = sorted(rng.sample(range(4096), 2))
            top, bottom = sorted(rng.sample(range(4096), 2))
            rectangle = MappingRectangle(left, top, right, bottom)
            t = encode_tablet_mapping(rectangle)
            decoded = (t[3] | (t[2] & 15) << 8, t[4] | (t[2] >> 4) << 8,
                       t[6] | (t[5] & 15) << 8, t[7] | (t[5] >> 4) << 8)
            self.assertEqual(decoded, (left, top, right - left, bottom - top))
            s = encode_screen_mapping(rectangle, ink_mode=False)
            decoded_screen = (s[2] | (s[4] & 15) << 8, s[5] | (s[7] & 15) << 8,
                              s[3] | (s[4] >> 4) << 8, s[6] | (s[7] >> 4) << 8)
            self.assertEqual(decoded_screen, decoded)

    def test_firmware_threshold_and_left_hand_code(self):
        for version, ink_byte in ((0x1802, 0), (0x1803, 1)):
            report = encode_mode(ink_mode=False, left_hand_mode=True,
                                 tablet_map=MappingRectangle(), device_version=version)
            self.assertEqual(report, bytes((8, 6, ink_byte, 3, 0, 0, 0, 0)))

    def test_clipping_gate_requires_cropped_rectangle(self):
        full = encode_mode(ink_mode=True, left_hand_mode=False,
                           tablet_map=MappingRectangle(), clipping_enabled=True)
        crop = encode_mode(ink_mode=True, left_hand_mode=False,
                           tablet_map=MappingRectangle(1, 2, 3000, 4000), clipping_enabled=True)
        self.assertEqual(full[4], 0)
        self.assertEqual(crop[4], 1)

    def test_invalid_flags_and_versions_rejected(self):
        with self.assertRaises(ValueError):
            encode_screen_mapping(MappingRectangle(), ink_mode=1)
        for version in (-1, 65536, True):
            with self.assertRaises(ValueError):
                encode_mode(ink_mode=False, left_hand_mode=False,
                            tablet_map=MappingRectangle(), device_version=version)


if __name__ == "__main__":
    unittest.main()
