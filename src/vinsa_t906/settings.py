"""Evidence-backed vendor settings, with lossless local file round-tripping.

No methods here communicate with the tablet. Factory settings come from the
embedded INI in TabletService.exe; files on disk contain separate user settings.
Unknown INI sections and keys, comments, and newline bytes survive editing.
"""

from __future__ import annotations

import configparser
from dataclasses import dataclass, field
from pathlib import Path
import re


@dataclass(frozen=True)
class MappingRectangle:
    """Nonempty rectangle in the vendor's inclusive 0..4095 coordinate range."""

    left: int = 0
    top: int = 0
    right: int = 4095
    bottom: int = 4095

    def __post_init__(self) -> None:
        for name in ("left", "top", "right", "bottom"):
            value = getattr(self, name)
            if type(value) is not int or not 0 <= value <= 4095:
                raise ValueError(f"{name} must be an integer in 0..4095")
        if self.left >= self.right or self.top >= self.bottom:
            raise ValueError("rectangle must have left < right and top < bottom")


FACTORY_INI = (
    "[MainItem]\r\nInkMode=1\r\nSmoothness=0\r\nLeftHandMode=0\r\n"
    "[Mapping]\r\nScreenMapLeft=0\r\nScreenMapTop=0\r\n"
    "ScreenMapRight=4095\r\nScreenMapBottom=4095\r\n"
    "TabletMapLeft=0\r\nTabletMapTop=0\r\n"
    "TabletMapRight=4095\r\nTabletMapBottom=4095\r\n"
).encode("ascii")


@dataclass(frozen=True)
class IniDocument:
    """Lossless ASCII-compatible vendor INI, also usable for TabletWheel.ini.

    Values remain strings, so unknown fields acquire no guessed semantics.
    ``with_value`` returns a new document; existing files are never auto-written.
    UTF-8 with BOM is accepted, while arbitrary legacy bytes are preserved with
    surrogateescape. Section/key lookup follows ConfigParser's case rules.
    """

    raw: bytes

    def __post_init__(self) -> None:
        if not isinstance(self.raw, bytes):
            raise TypeError("raw must be bytes")
        self._parser()

    @classmethod
    def from_file(cls, path: str | Path) -> IniDocument:
        return cls(Path(path).read_bytes())

    def _parser(self) -> configparser.ConfigParser:
        parser = configparser.ConfigParser(interpolation=None)
        # CRCRLF is a single vendor line ending, not an extra blank line.
        text = self.raw.decode("utf-8-sig", errors="surrogateescape")
        text = re.sub(r"\r+\n|\r", "\n", text)
        try:
            parser.read_string(text)
        except configparser.Error as exc:
            raise ValueError(f"invalid vendor INI: {exc}") from exc
        return parser

    def get(self, section: str, key: str) -> str:
        """Read a value; absent sections or keys raise ValueError."""
        try:
            return self._parser().get(section, key)
        except configparser.Error as exc:
            raise ValueError(f"missing [{section}] {key}") from exc

    def with_value(self, section: str, key: str, value: str) -> IniDocument:
        """Replace an existing scalar value, retaining all unrelated bytes."""
        if not isinstance(value, str) or any(c in value for c in "\r\n"):
            raise ValueError("INI replacement must be a single-line string")
        if self.get(section, key) == value:
            return self
        # Keep terminators in place rather than using ConfigParser.write(),
        # which would change key spelling, spacing, and vendor line endings.
        text = self.raw.decode("utf-8-sig", errors="surrogateescape")
        lines = re.split(r"(\r+\n|\n|\r)", text)
        current_section = None
        found = False
        for index in range(0, len(lines), 2):
            line = lines[index]
            header = re.match(r"\s*\[([^\]]+)\]", line)
            if header:
                current_section = header.group(1)
                continue
            if current_section != section or line.lstrip().startswith(("#", ";")):
                continue
            assignment = re.match(r"(\s*([^=:]+?)\s*[=:]\s*)(.*?)(\s*)$", line)
            if assignment and assignment.group(2).strip().lower() == key.lower():
                lines[index] = assignment.group(1) + value + assignment.group(4)
                found = True
                break
        if not found:
            raise ValueError(f"[{section}] {key} has no editable scalar assignment")
        prefix = b"\xef\xbb\xbf" if self.raw.startswith(b"\xef\xbb\xbf") else b""
        return IniDocument(prefix + "".join(lines).encode("utf-8", errors="surrogateescape"))

    def to_file(self, path: str | Path) -> None:
        """Write to an explicitly selected path (choose a copy to keep originals)."""
        Path(path).write_bytes(self.raw)


@dataclass(frozen=True)
class TabletSettings:
    """Validated TSetting.ini values plus the original lossless document.

    Use ``dataclasses.replace(settings, ...)`` to edit immutable settings.
    InkMode and LeftHandMode accept exactly 0/1. Smoothness remains an unsigned
    INI integer because its units and supported runtime range are unresolved.
    Missing fields are errors rather than silently replacing user values.
    """

    ink_mode: bool = True
    smoothness: int = 0
    left_hand_mode: bool = False
    screen_map: MappingRectangle = field(default_factory=MappingRectangle)
    tablet_map: MappingRectangle = field(default_factory=MappingRectangle)
    _document: IniDocument = field(default_factory=lambda: IniDocument(FACTORY_INI), repr=False, compare=False)

    def __post_init__(self) -> None:
        for name in ("ink_mode", "left_hand_mode"):
            if type(getattr(self, name)) is not bool:
                raise ValueError(f"{name} must be bool")
        if type(self.smoothness) is not int or not 0 <= self.smoothness <= 2147483647:
            raise ValueError("smoothness must be an integer in 0..2147483647")
        for name in ("screen_map", "tablet_map"):
            if not isinstance(getattr(self, name), MappingRectangle):
                raise ValueError(f"{name} must be MappingRectangle")

    @classmethod
    def factory_defaults(cls) -> TabletSettings:
        return cls()

    @classmethod
    def from_bytes(cls, raw: bytes) -> TabletSettings:
        document = IniDocument(raw)

        def boolean(key: str) -> bool:
            value = document.get("MainItem", key)
            if value not in ("0", "1"):
                raise ValueError(f"[MainItem] {key} must be 0 or 1")
            return value == "1"

        def rectangle(prefix: str) -> MappingRectangle:
            values = []
            for suffix in ("Left", "Top", "Right", "Bottom"):
                value = document.get("Mapping", prefix + suffix)
                if not re.fullmatch(r"[0-9]+", value):
                    raise ValueError(f"[Mapping] {prefix + suffix} must be an unsigned integer")
                values.append(int(value))
            return MappingRectangle(*values)

        return cls(
            ink_mode=boolean("InkMode"),
            smoothness=cls._smoothness(document.get("MainItem", "Smoothness")),
            left_hand_mode=boolean("LeftHandMode"),
            screen_map=rectangle("ScreenMap"),
            tablet_map=rectangle("TabletMap"),
            _document=document,
        )

    @classmethod
    def from_file(cls, path: str | Path) -> TabletSettings:
        return cls.from_bytes(Path(path).read_bytes())

    @staticmethod
    def _smoothness(value: str) -> int:
        if not re.fullmatch(r"[0-9]+", value):
            raise ValueError("[MainItem] Smoothness must be an unsigned decimal integer")
        number = int(value)
        if number > 2147483647:
            raise ValueError("[MainItem] Smoothness exceeds the INI integer range")
        return number

    def to_bytes(self) -> bytes:
        document = self._document
        for key, value in (
            ("InkMode", self.ink_mode),
            ("LeftHandMode", self.left_hand_mode),
        ):
            document = document.with_value("MainItem", key, str(int(value)))
        if int(document.get("MainItem", "Smoothness")) != self.smoothness:
            document = document.with_value("MainItem", "Smoothness", str(self.smoothness))
        for prefix, rectangle in (("ScreenMap", self.screen_map), ("TabletMap", self.tablet_map)):
            for suffix in ("Left", "Top", "Right", "Bottom"):
                value = getattr(rectangle, suffix.lower())
                existing = document.get("Mapping", prefix + suffix)
                # Preserve equivalent user spellings such as zero-padded values.
                if int(existing) != value:
                    document = document.with_value("Mapping", prefix + suffix, str(value))
        return document.raw

    def to_file(self, path: str | Path) -> None:
        Path(path).write_bytes(self.to_bytes())


@dataclass(frozen=True)
class DeviceState:
    """Opaque 64-byte vendor .dat/.bin contents; no field semantics implied."""

    raw: bytes

    def __post_init__(self) -> None:
        if not isinstance(self.raw, bytes) or len(self.raw) != 64:
            raise ValueError("device state must be exactly 64 bytes")

    @classmethod
    def from_file(cls, path: str | Path) -> DeviceState:
        return cls(Path(path).read_bytes())

    def to_file(self, path: str | Path) -> None:
        Path(path).write_bytes(self.raw)
