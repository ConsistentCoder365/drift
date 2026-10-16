"""Ghost binary format. Mirrors unity/Assets/Drift/Scripts/Core/GhostCodec.cs; change both together.

Header (36 bytes, little-endian): magic "DRGH", u8 version, u8 sample rate, u16 reserved,
u32 sample count, f32[3] bounds min, f32[3] bounds max.
Sample (12 bytes): u16 sequence, u16[3] position quantised inside the bounds,
u32 smallest-three rotation (2-bit dropped index, then 3 x 10-bit components on codes 0..1022).
"""

from __future__ import annotations

import math
import struct
from dataclasses import dataclass, field

MAGIC = b"DRGH"
VERSION = 1
HEADER = struct.Struct("<4sBBHI3f3f")
SAMPLE = struct.Struct("<HHHHI")
ROT_RANGE = 0.70710678  # 1/sqrt(2)
ROT_MASK = (1 << 10) - 1
ROT_STEPS = (1 << 10) - 2  # even step count, so 0.0 is exactly code 511

Vec3 = tuple[float, float, float]
Quat = tuple[float, float, float, float]


class GhostDecodeError(ValueError):
    pass


@dataclass
class Ghost:
    sample_rate: int
    bounds_min: Vec3
    bounds_max: Vec3
    seq: list[int] = field(default_factory=list)
    positions: list[Vec3] = field(default_factory=list)
    rotations: list[Quat] = field(default_factory=list)

    def __len__(self) -> int:
        return len(self.seq)


def quantize_position(value: float, lo: float, hi: float) -> int:
    span = hi - lo
    if span <= 0:
        return 0
    t = min(1.0, max(0.0, (value - lo) / span))
    return round(t * 65535)


def dequantize_position(q: int, lo: float, hi: float) -> float:
    return lo + (q / 65535) * (hi - lo)


def _quantize_component(v: float) -> int:
    t = min(1.0, max(0.0, (v + ROT_RANGE) / (2 * ROT_RANGE)))
    return round(t * ROT_STEPS)


def _dequantize_component(q: int) -> float:
    return min(q, ROT_STEPS) / ROT_STEPS * (2 * ROT_RANGE) - ROT_RANGE


def pack_rotation(q: Quat) -> int:
    x, y, z, w = q
    n = math.sqrt(x * x + y * y + z * z + w * w)
    comps = [0.0, 0.0, 0.0, 1.0] if n < 1e-6 else [x / n, y / n, z / n, w / n]
    largest = max(range(4), key=lambda i: abs(comps[i]))
    if comps[largest] < 0:
        comps = [-c for c in comps]
    a, b, c = (comps[i] for i in range(4) if i != largest)
    return (largest << 30) | (_quantize_component(a) << 20) | (_quantize_component(b) << 10) | _quantize_component(c)


def unpack_rotation(packed: int) -> Quat:
    largest = packed >> 30
    small = [
        _dequantize_component((packed >> 20) & ROT_MASK),
        _dequantize_component((packed >> 10) & ROT_MASK),
        _dequantize_component(packed & ROT_MASK),
    ]
    d = math.sqrt(max(0.0, 1.0 - sum(v * v for v in small)))
    comps = small[:largest] + [d] + small[largest:]
    n = math.sqrt(sum(v * v for v in comps))
    return tuple(v / n for v in comps)  # type: ignore[return-value]


def decode(data: bytes) -> Ghost:
    if len(data) < HEADER.size:
        raise GhostDecodeError("too short")
    magic, version, rate, _reserved, count, *bounds = HEADER.unpack_from(data, 0)
    if magic != MAGIC:
        raise GhostDecodeError("bad magic")
    if version != VERSION:
        raise GhostDecodeError("unsupported version")
    if len(data) != HEADER.size + count * SAMPLE.size:
        raise GhostDecodeError("length mismatch")
    lo, hi = tuple(bounds[:3]), tuple(bounds[3:])
    if not all(math.isfinite(v) for v in bounds) or any(h < l for l, h in zip(lo, hi)):
        raise GhostDecodeError("bad bounds")

    ghost = Ghost(rate, lo, hi)  # type: ignore[arg-type]
    for seq, qx, qy, qz, rot in SAMPLE.iter_unpack(data[HEADER.size:]):
        ghost.seq.append(seq)
        ghost.positions.append((
            dequantize_position(qx, lo[0], hi[0]),
            dequantize_position(qy, lo[1], hi[1]),
            dequantize_position(qz, lo[2], hi[2]),
        ))
        ghost.rotations.append(unpack_rotation(rot))
    return ghost


def encode(ghost: Ghost) -> bytes:
    lo, hi = ghost.bounds_min, ghost.bounds_max
    out = bytearray(HEADER.pack(MAGIC, VERSION, ghost.sample_rate, 0, len(ghost), *lo, *hi))
    for seq, p, r in zip(ghost.seq, ghost.positions, ghost.rotations):
        out += SAMPLE.pack(
            seq & 0xFFFF,
            quantize_position(p[0], lo[0], hi[0]),
            quantize_position(p[1], lo[1], hi[1]),
            quantize_position(p[2], lo[2], hi[2]),
            pack_rotation(r),
        )
    return bytes(out)
