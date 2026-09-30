"""Test reconstructed settings against shipped files, without modifying them."""

from dataclasses import FrozenInstanceError, replace
from pathlib import Path
import tempfile
import unittest

from vinsa_t906.settings import DeviceState, IniDocument, MappingRectangle, TabletSettings


VENDOR = Path(__file__).resolve().parents[1] / "downloads/windows/installed/MyTablet"


class SettingsTests(unittest.TestCase):
    def test_shipped_values_and_byte_exact_roundtrip(self):
        path = VENDOR / "TSetting.ini"
        settings = TabletSettings.from_file(path)
        self.assertFalse(settings.ink_mode)
        self.assertEqual(settings.smoothness, 0)
        self.assertIs(type(settings.smoothness), int)
        self.assertFalse(settings.left_hand_mode)
        self.assertEqual(settings.screen_map, MappingRectangle(1753, 0, 4095, 4095))
        self.assertEqual(settings.tablet_map, MappingRectangle(650, 650, 3500, 3500))
        self.assertIn(b"\r\n", path.read_bytes())
        self.assertEqual(settings.to_bytes(), path.read_bytes())
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / "settings.ini"
            settings.to_file(destination)
            self.assertEqual(destination.read_bytes(), path.read_bytes())

    def test_embedded_factory_defaults_are_distinct_from_saved_user_values(self):
        settings = TabletSettings.factory_defaults()
        self.assertTrue(settings.ink_mode)
        self.assertEqual(settings.screen_map, MappingRectangle())
        self.assertEqual(settings.tablet_map, MappingRectangle())
        self.assertEqual(TabletSettings.from_bytes(settings.to_bytes()), settings)

    def test_edit_preserves_unknown_data_spelling_and_vendor_line_endings(self):
        original = (VENDOR / "TSetting.ini").read_bytes().replace(b"\r\n", b"\r\r\n")
        original = b"; vendor comment\r\r\n" + original + b"[Future]\r\r\nOddKey=opaque\xff\r\r\n"
        original = original.replace(b"InkMode=0", b"  inKMode : 0  ")
        settings = TabletSettings.from_bytes(original)
        edited = replace(settings, ink_mode=True, tablet_map=MappingRectangle(1, 2, 3000, 4000))
        expected = original.replace(b"  inKMode : 0  ", b"  inKMode : 1  ")
        for before, after in ((b"Left=650", b"Left=1"), (b"Top=650", b"Top=2"), (b"Right=3500", b"Right=3000"), (b"Bottom=3500", b"Bottom=4000")):
            expected = expected.replace(before, after)
        self.assertEqual(edited.to_bytes(), expected)
        self.assertEqual(TabletSettings.from_bytes(expected), edited)
        self.assertEqual(settings.to_bytes(), original)

    def test_equivalent_numeric_spelling_and_bom_are_preserved(self):
        original = b"\xef\xbb\xbf" + TabletSettings().to_bytes().replace(b"ScreenMapLeft=0", b"ScreenMapLeft=0000")
        self.assertEqual(TabletSettings.from_bytes(original).to_bytes(), original)

    def test_rectangle_rejects_invalid_bounds_and_types(self):
        for coordinates in ((-1, 0, 4095, 4095), (0, 0, 4096, 4095), (10, 0, 10, 100), (0, 10, 100, 1), (False, 0, 4095, 4095), (0.0, 0, 4095, 4095)):
            with self.subTest(coordinates=coordinates), self.assertRaises(ValueError):
                MappingRectangle(*coordinates)
        with self.assertRaises(FrozenInstanceError):
            MappingRectangle().left = 1

    def test_invalid_boolean_missing_key_and_malformed_ini(self):
        original = TabletSettings().to_bytes()
        for value in (b"2", b"true", b"-1", b""):
            with self.subTest(value=value), self.assertRaises(ValueError):
                TabletSettings.from_bytes(original.replace(b"InkMode=1", b"InkMode=" + value))
        for field in ("ink_mode", "left_hand_mode"):
            with self.subTest(field=field), self.assertRaises(ValueError):
                replace(TabletSettings(), **{field: 1})
        with self.assertRaises(ValueError):
            TabletSettings.from_bytes(original.replace(b"InkMode=1\r\n", b""))
        with self.assertRaises(ValueError):
            TabletSettings.from_bytes(b"not an ini")

    def test_smoothness_is_preserved_as_an_integer_without_guessed_semantics(self):
        original = TabletSettings().to_bytes().replace(b"Smoothness=0", b"Smoothness=7")
        settings = TabletSettings.from_bytes(original)
        self.assertEqual(settings.smoothness, 7)
        self.assertEqual(settings.to_bytes(), original)
        self.assertEqual(replace(settings, smoothness=23).to_bytes(), original.replace(b"Smoothness=7", b"Smoothness=23"))
        padded = original.replace(b"Smoothness=7", b"Smoothness=0007")
        self.assertEqual(TabletSettings.from_bytes(padded).to_bytes(), padded)
        for value in (True, False, -1, 2147483648, 7.0):
            with self.subTest(value=value), self.assertRaises(ValueError):
                replace(settings, smoothness=value)
        for value in (b"-1", b"true", b"2147483648", b""):
            with self.subTest(value=value), self.assertRaises(ValueError):
                TabletSettings.from_bytes(original.replace(b"Smoothness=7", b"Smoothness=" + value))
        maximum = replace(settings, smoothness=2147483647)
        self.assertEqual(TabletSettings.from_bytes(maximum.to_bytes()), maximum)

    def test_wheel_document_preserves_key_pairs_without_modifier_interpretation(self):
        path = VENDOR / "TabletWheel.ini"
        document = IniDocument.from_file(path)
        self.assertEqual(document.get("EnableItem", "RotateCanvasEnabled"), "1")
        self.assertEqual(document.get("KeyDefine", "ZoomRLeft.extraKey"), "1")
        self.assertEqual(document.get("KeyDefine", "ZoomRLeft.MainKey"), "189")
        self.assertEqual(document.raw, path.read_bytes())
        edited = document.with_value("EnableItem", "HideWheel", "1")
        self.assertEqual(edited.raw, document.raw.replace(b"HideWheel=0", b"HideWheel=1"))
        with self.assertRaises(ValueError):
            document.with_value("EnableItem", "HideWheel", "1\nInjected=1")

    def test_shipped_state_is_opaque_and_exactly_roundtrips(self):
        for filename in ("T906Def.bin", "T906_2023.dat"):
            with self.subTest(filename=filename):
                original = (VENDOR / filename).read_bytes()
                state = DeviceState.from_file(VENDOR / filename)
                self.assertEqual(state.raw, original)
                with tempfile.TemporaryDirectory() as directory:
                    destination = Path(directory) / filename
                    state.to_file(destination)
                    self.assertEqual(destination.read_bytes(), original)
        for size in (0, 63, 65):
            with self.subTest(size=size), self.assertRaises(ValueError):
                DeviceState(bytes(size))


if __name__ == "__main__":
    unittest.main()
