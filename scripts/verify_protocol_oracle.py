"""Compare reconstructed encoders to isolated original x86 packing blocks.

Optional dependency: ``uv run --with unicorn python scripts/verify_protocol_oracle.py``.
This emulates only verified arithmetic/memory instructions, stopping before
clock/API code or RET. It never starts the vendor application, calls an API,
or accesses a tablet. The original file hash and machine blocks are the oracle.
"""

from __future__ import annotations

import hashlib
from pathlib import Path
import json
import subprocess
import tempfile
import struct
import sys


ROOT = Path(__file__).resolve().parents[1]



SOURCE = ROOT / "downloads/windows/installed/MyTablet/TabletService.exe"
SOURCE_SHA256 = "083819bcafd2f95faed64f4ae9ec0cf6d152677a23cc4a6984f47a7c5d06097b"
TABLET_START, TABLET_STOP = 0x494B84, 0x494BDF
SCREEN_START, SCREEN_STOP = 0x495410, 0x49547D
OBJECT_ADDRESS, OBJECT_SIZE = 0x600000, 0x1000
STACK_ADDRESS, STACK_SIZE = 0x700000, 0x10000
STACK_TOP = STACK_ADDRESS + STACK_SIZE - 16
MAX_INSTRUCTIONS = 256


def code_section(raw: bytes) -> tuple[int, bytes]:
    """Read the original native PE32 CODE section using only struct."""
    if len(raw) < 64 or raw[:2] != b"MZ":
        raise ValueError("expected DOS/PE executable")
    pe = struct.unpack_from("<I", raw, 0x3C)[0]
    if raw[pe:pe + 4] != b"PE\0\0":
        raise ValueError("missing PE signature")
    machine, count = struct.unpack_from("<HH", raw, pe + 4)
    optional_size = struct.unpack_from("<H", raw, pe + 20)[0]
    optional = pe + 24
    if machine != 0x14C or struct.unpack_from("<H", raw, optional)[0] != 0x10B:
        raise ValueError("oracle must be a native x86 PE32")
    image_base = struct.unpack_from("<I", raw, optional + 28)[0]
    for index in range(count):
        offset = optional + optional_size + index * 40
        name = raw[offset:offset + 8].rstrip(b"\0")
        _, rva, raw_size, raw_offset = struct.unpack_from("<4I", raw, offset + 8)
        if name != b"CODE":
            continue
        if raw_offset + raw_size > len(raw):
            raise ValueError("CODE section exceeds file bounds")
        address = image_base + rva
        if address != 0x401000:
            raise ValueError("unexpected CODE virtual address")
        return address, raw[raw_offset:raw_offset + raw_size]
    raise ValueError("CODE section not found")


def main() -> int:
    try:
        from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE, UC_PROT_READ, UC_PROT_WRITE, UC_PROT_EXEC
        from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBP, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EFLAGS
    except ImportError:
        print("Optional dependency missing. Run: uv run --with unicorn python scripts/verify_protocol_oracle.py", file=sys.stderr)
        return 2

    raw = SOURCE.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    if digest != SOURCE_SHA256:
        raise ValueError(f"oracle source SHA256 mismatch: {digest}")
    address, code = code_section(raw)
    if code[SCREEN_STOP - address] != 0xC3:
        raise ValueError("screen stop is not the verified RET boundary")
    if code[TABLET_STOP - address:TABLET_STOP - address + 2] != b"\xeb\x07":
        raise ValueError("tablet stop is not the verified pre-clock boundary")
    emulator = Uc(UC_ARCH_X86, UC_MODE_32)
    emulator.mem_map(address, (len(code) + 0xFFF) & ~0xFFF, UC_PROT_READ | UC_PROT_EXEC)
    emulator.mem_write(address, code)
    emulator.mem_map(OBJECT_ADDRESS, OBJECT_SIZE, UC_PROT_READ | UC_PROT_WRITE)
    emulator.mem_map(STACK_ADDRESS, STACK_SIZE, UC_PROT_READ | UC_PROT_WRITE)
    active = [0, 0]

    def guard(uc, instruction_address, instruction_size, _user_data):
        start, stop = active
        if not start <= instruction_address < stop or instruction_address + instruction_size > stop:
            raise RuntimeError(f"instruction escaped verified packing range at 0x{instruction_address:x}")

    emulator.hook_add(UC_HOOK_CODE, guard)
    registers = (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBP)

    def run(rectangle: dict, *, screen: bool, ink_mode: bool = False) -> bytes:
        # Fresh object, stack and registers per comparison; no prior-case state.
        emulator.mem_write(OBJECT_ADDRESS, bytes(OBJECT_SIZE))
        emulator.mem_write(STACK_ADDRESS, bytes(STACK_SIZE))
        for register in registers:
            emulator.reg_write(register, 0)
        emulator.reg_write(UC_X86_REG_EFLAGS, 2)
        emulator.reg_write(UC_X86_REG_ESP, STACK_TOP)
        emulator.reg_write(UC_X86_REG_EAX, OBJECT_ADDRESS)
        emulator.mem_write(OBJECT_ADDRESS + 0x54, b"\x08")
        if screen:
            fields = (rectangle["Top"], rectangle["Left"], rectangle["Right"] - rectangle["Left"], rectangle["Bottom"] - rectangle["Top"])
            emulator.mem_write(OBJECT_ADDRESS + 0x24, struct.pack("<4H", *fields))
            emulator.mem_write(OBJECT_ADDRESS + 0x55, bytes((3 if ink_mode else 1,)))
            start, stop = SCREEN_START, SCREEN_STOP
        else:
            fields = (rectangle["Left"], rectangle["Top"], rectangle["Right"], rectangle["Bottom"])
            emulator.mem_write(OBJECT_ADDRESS + 4, struct.pack("<4I", *fields))
            start, stop = TABLET_START, TABLET_STOP
        active[:] = start, stop
        # A strict instruction budget and range hook bound these fixed,
        # straight-line blocks without a watchdog thread per comparison.
        emulator.emu_start(start, stop, count=MAX_INSTRUCTIONS)
        pc = emulator.reg_read(UC_X86_REG_EIP)
        if pc != stop:
            raise RuntimeError(f"instruction limit reached before packing finished: 0x{pc:x}")
        return bytes(emulator.mem_read(OBJECT_ADDRESS + 0x54, 8))

    # The .NET backend supplies reports; the oracle is the vendor machine code.
    with tempfile.TemporaryDirectory(prefix="vinsa-oracle-") as temporary:
        output = Path(temporary) / "vectors.json"
        subprocess.run(["dotnet", "run", "--project", str(ROOT / "tests/VinsaStudio.CoreChecks"),
                        "--", "--export", str(output)], check=True, cwd=ROOT)
        rectangles = json.loads(output.read_text())
    comparisons = 0
    for rectangle in rectangles:
        for screen, expected_hex in ((False, rectangle["Reports"][0]), (True, rectangle["Reports"][2])):
            expected = bytes.fromhex(expected_hex)
            observed = run(rectangle, screen=screen, ink_mode=expected[1] == 3)
            if observed != expected:
                raise AssertionError(f"packing mismatch {rectangle}: {observed.hex()} != {expected.hex()}")
            comparisons += 1

    print(f"Source SHA256: {digest}")
    for name, start, stop in (("tablet", TABLET_START, TABLET_STOP), ("screen", SCREEN_START, SCREEN_STOP)):
        block = code[start - address:stop - address]
        print(f"{name}: 0x{start:x}..0x{stop:x} (exclusive), block SHA256 {hashlib.sha256(block).hexdigest()}")
    print(f"PASS: {comparisons} encoder comparisons across {len(rectangles)} rectangles (deterministic .NET vectors, both screen command modes).")
    print("Only isolated original packing instructions were emulated; no vendor startup, API calls, or device access.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
