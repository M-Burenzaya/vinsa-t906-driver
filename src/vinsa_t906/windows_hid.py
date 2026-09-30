"""Windows HID access using only ctypes; enumeration never sends reports.

Writes are limited to reconstructed report 08 commands 01/03/06/07, with eight-byte
buffers and matching live collection capabilities. They require an explicit
``set_feature`` call. Opening a device does not initialize or change its mode.

API references:
https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setfeature
https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps
https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetdeviceinterfacedetailw
"""

from __future__ import annotations

import ctypes as C
from dataclasses import dataclass
import re
import sys
import time


@dataclass(frozen=True)
class DeviceInfo:
    path: str
    vendor_id: int
    product_id: int
    version: int
    product: str
    manufacturer: str
    usage_page: int
    usage: int
    input_report_length: int
    output_report_length: int
    feature_report_length: int

    @property
    def is_supported_vendor_interface(self) -> bool:
        if (self.vendor_id, self.product_id) not in {(0x0B57, 0x902E), (0x08F2, 0x6811)}:
            return False
        # Require complete components, rather than allowing e.g. col010.
        pattern = (
            rf"(?:^|[#&])vid_{self.vendor_id:04x}&pid_{self.product_id:04x}"
            r"&mi_0[12]&col01(?:[#&]|$)"
        )
        return re.search(pattern, self.path, re.IGNORECASE) is not None


def validate_feature_report(info: DeviceInfo, report: bytes) -> None:
    """Reject writes outside the evidence-backed interface and report format."""
    if not info.is_supported_vendor_interface:
        raise ValueError("unsupported tablet vendor interface")
    if not isinstance(report, bytes):
        raise TypeError("report must be bytes")
    if len(report) != 8:
        raise ValueError("reconstructed feature reports must contain exactly 8 bytes")
    if info.feature_report_length != 8:
        raise ValueError("device feature capabilities do not match the 8-byte report")
    if report[0] != 0x08:
        raise ValueError("only feature report ID 0x08 is reconstructed")
    if report[1] not in (0x01, 0x03, 0x06, 0x07):
        raise ValueError("only commands 0x01, 0x03, 0x06 and 0x07 are reconstructed")


# Fixed-width fields also make structure layout tests portable.
U16, U32, PVOID = C.c_uint16, C.c_uint32, C.c_void_p


class _GUID(C.Structure):
    _fields_ = [("Data1", U32), ("Data2", U16), ("Data3", U16), ("Data4", C.c_ubyte * 8)]


class _INTERFACE_DATA(C.Structure):
    _fields_ = [("cbSize", U32), ("InterfaceClassGuid", _GUID), ("Flags", U32), ("Reserved", C.c_size_t)]


class _ATTRIBUTES(C.Structure):
    _fields_ = [("Size", U32), ("VendorID", U16), ("ProductID", U16), ("VersionNumber", U16)]


class _CAPS(C.Structure):
    _fields_ = [(name, U16) for name in (
        "Usage", "UsagePage", "InputReportByteLength", "OutputReportByteLength",
        "FeatureReportByteLength",
    )] + [("Reserved", U16 * 17)] + [(name, U16) for name in (
        "NumberLinkCollectionNodes", "NumberInputButtonCaps", "NumberInputValueCaps",
        "NumberInputDataIndices", "NumberOutputButtonCaps", "NumberOutputValueCaps",
        "NumberOutputDataIndices", "NumberFeatureButtonCaps", "NumberFeatureValueCaps",
        "NumberFeatureDataIndices",
    )]


class _WindowsApi:
    def __init__(self):
        if sys.platform != "win32":
            raise OSError("Windows HID transport is available only on Windows")
        self.hid = C.WinDLL("hid", use_last_error=True)
        self.setup = C.WinDLL("setupapi", use_last_error=True)
        self.kernel = C.WinDLL("kernel32", use_last_error=True)
        prototypes = [
            (self.hid, "HidD_GetHidGuid", None, [C.POINTER(_GUID)]),
            (self.hid, "HidD_GetAttributes", C.c_ubyte, [PVOID, C.POINTER(_ATTRIBUTES)]),
            (self.hid, "HidD_GetPreparsedData", C.c_ubyte, [PVOID, C.POINTER(PVOID)]),
            (self.hid, "HidD_FreePreparsedData", C.c_ubyte, [PVOID]),
            (self.hid, "HidP_GetCaps", C.c_int32, [PVOID, C.POINTER(_CAPS)]),
            (self.kernel, "CreateFileW", PVOID, [C.c_wchar_p, U32, U32, PVOID, U32, U32, PVOID]),
            (self.kernel, "CloseHandle", C.c_int32, [PVOID]),
            (self.setup, "SetupDiGetClassDevsW", PVOID, [C.POINTER(_GUID), C.c_wchar_p, PVOID, U32]),
            (self.setup, "SetupDiEnumDeviceInterfaces", C.c_int32, [PVOID, PVOID, C.POINTER(_GUID), U32, C.POINTER(_INTERFACE_DATA)]),
            (self.setup, "SetupDiGetDeviceInterfaceDetailW", C.c_int32, [PVOID, C.POINTER(_INTERFACE_DATA), PVOID, U32, C.POINTER(U32), PVOID]),
            (self.setup, "SetupDiDestroyDeviceInfoList", C.c_int32, [PVOID]),
        ]
        for name in ("HidD_GetProductString", "HidD_GetManufacturerString", "HidD_GetFeature", "HidD_SetFeature"):
            prototypes.append((self.hid, name, C.c_ubyte, [PVOID, PVOID, U32]))
        for library, name, result, args in prototypes:
            function = getattr(library, name)
            function.restype, function.argtypes = result, args

    @staticmethod
    def _error():
        return C.WinError(C.get_last_error())

    def open(self, path):
        # Attribute/feature operations need no streaming access rights. Share
        # read/write with the installed driver; do not acquire it exclusively.
        handle = self.kernel.CreateFileW(path, 0, 3, None, 3, 0, None)
        if handle == C.c_void_p(-1).value:
            raise self._error()
        return handle

    def close(self, handle):
        if not self.kernel.CloseHandle(handle):
            raise self._error()

    def paths(self):
        guid = _GUID()
        self.hid.HidD_GetHidGuid(C.byref(guid))
        device_set = self.setup.SetupDiGetClassDevsW(C.byref(guid), None, None, 0x12)
        if device_set == C.c_void_p(-1).value:
            raise self._error()
        try:
            index = 0
            while True:
                interface = _INTERFACE_DATA()
                interface.cbSize = C.sizeof(interface)
                if not self.setup.SetupDiEnumDeviceInterfaces(device_set, None, C.byref(guid), index, C.byref(interface)):
                    if C.get_last_error() == 259:  # ERROR_NO_MORE_ITEMS
                        break
                    raise self._error()
                index += 1
                required = U32()
                ok = self.setup.SetupDiGetDeviceInterfaceDetailW(device_set, C.byref(interface), None, 0, C.byref(required), None)
                if ok or C.get_last_error() != 122:  # ERROR_INSUFFICIENT_BUFFER
                    raise self._error()
                detail = C.create_string_buffer(required.value)
                # sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W) has ABI padding;
                # DevicePath itself always starts immediately after the DWORD.
                C.cast(detail, C.POINTER(U32))[0] = 8 if C.sizeof(PVOID) == 8 else 6
                if not self.setup.SetupDiGetDeviceInterfaceDetailW(device_set, C.byref(interface), detail, required.value, None, None):
                    raise self._error()
                yield C.wstring_at(C.addressof(detail) + 4)
        finally:
            self.setup.SetupDiDestroyDeviceInfoList(device_set)

    def info(self, handle, path):
        attributes = _ATTRIBUTES()
        attributes.Size = C.sizeof(attributes)
        if not self.hid.HidD_GetAttributes(handle, C.byref(attributes)):
            raise self._error()
        preparsed = PVOID()
        if not self.hid.HidD_GetPreparsedData(handle, C.byref(preparsed)):
            raise self._error()
        try:
            caps = _CAPS()
            status = self.hid.HidP_GetCaps(preparsed, C.byref(caps))
            if status != 0x00110000:  # HIDP_STATUS_SUCCESS
                raise OSError(f"HidP_GetCaps failed with NTSTATUS 0x{status & 0xffffffff:08x}")
        finally:
            self.hid.HidD_FreePreparsedData(preparsed)

        def name(function):
            buffer = C.create_unicode_buffer(126)
            return buffer.value if function(handle, buffer, C.sizeof(buffer)) else ""

        return DeviceInfo(path, attributes.VendorID, attributes.ProductID,
                          attributes.VersionNumber, name(self.hid.HidD_GetProductString),
                          name(self.hid.HidD_GetManufacturerString), caps.UsagePage, caps.Usage,
                          caps.InputReportByteLength, caps.OutputReportByteLength,
                          caps.FeatureReportByteLength)

    def get_feature(self, handle, report_id, length):
        buffer = (C.c_ubyte * length)()
        buffer[0] = report_id
        if not self.hid.HidD_GetFeature(handle, buffer, length):
            raise self._error()
        return bytes(buffer)

    def set_feature(self, handle, report):
        buffer = (C.c_ubyte * len(report)).from_buffer_copy(report)
        if not self.hid.HidD_SetFeature(handle, buffer, len(report)):
            raise self._error()


def enumerate_devices() -> list[DeviceInfo]:
    """Read collection metadata. Inaccessible/disconnected collections are omitted."""
    api = _WindowsApi()
    devices = []
    for path in api.paths():
        try:
            handle = api.open(path)
        except OSError:
            continue
        try:
            devices.append(api.info(handle, path))
        except OSError:
            continue
        finally:
            api.close(handle)
    return devices


class HidDevice:
    """Explicit feature-report transport; use ``with HidDevice(info) as device``."""

    def __init__(self, info: DeviceInfo):
        self.info = info
        self._api = None
        self._handle = None
        self._last_write = None

    def __enter__(self):
        if self._handle is not None:
            raise RuntimeError("device is already open")
        api = _WindowsApi()
        handle = api.open(self.info.path)
        try:
            live = api.info(handle, self.info.path)
            fields = ("vendor_id", "product_id", "usage_page", "usage", "input_report_length",
                      "output_report_length", "feature_report_length")
            if any(getattr(live, field) != getattr(self.info, field) for field in fields):
                raise ValueError("live device identity or capabilities differ from enumeration")
        except BaseException:
            api.close(handle)
            raise
        self._api, self._handle, self.info = api, handle, live
        self._last_write = None
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.close()

    def close(self):
        if self._handle is not None:
            handle, self._handle = self._handle, None
            self._api.close(handle)

    def _require_open(self):
        if self._handle is None:
            raise RuntimeError("device must be opened in a context manager")

    def get_feature(self, report_id: int, length: int | None = None) -> bytes:
        self._require_open()
        if not isinstance(report_id, int) or not 0 <= report_id <= 255:
            raise ValueError("report ID must be a byte")
        declared = self.info.feature_report_length
        if declared < 1 or (length is not None and length != declared):
            raise ValueError("feature buffer length must equal the declared capability")
        return self._api.get_feature(self._handle, report_id, declared)

    def set_feature(self, report: bytes) -> None:
        self._require_open()
        validate_feature_report(self.info, report)
        if self._last_write is not None:
            remaining = 0.3 - (time.monotonic() - self._last_write)
            if remaining > 0:
                time.sleep(remaining)
        self._api.set_feature(self._handle, report)
        self._last_write = time.monotonic()
