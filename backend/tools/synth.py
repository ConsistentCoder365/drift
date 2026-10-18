"""Synthetic laps and tampering, for tests and for demoing the anti-cheat by hand.

A synthetic lap drives the track centreline at a constant speed and records it exactly the
way GhostRecorder does: first sample on the step just past the start line, one sample per
20 ms step, last sample on the step that crosses the line again.
"""

from __future__ import annotations

import bisect
import math

from app.ghost import Ghost, decode, encode
from app.track import Track

STEP = 0.02


def _arc_table(points: list[tuple[float, float]]) -> list[float]:
    cum = [0.0]
    for i in range(len(points)):
        cum.append(cum[-1] + math.dist(points[i], points[(i + 1) % len(points)]))
    return cum


def _point_at(points, cum, s):
    total = cum[-1]
    s %= total
    i = max(0, bisect.bisect_right(cum, s) - 1)
    seg = cum[i + 1] - cum[i]
    t = (s - cum[i]) / seg if seg > 0 else 0.0
    a, b = points[i], points[(i + 1) % len(points)]
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t), (b[0] - a[0], b[1] - a[1])


def _yaw_quat(dx: float, dz: float) -> tuple[float, float, float, float]:
    # Unity: yaw 0 faces +z, positive yaw turns toward +x.
    yaw = math.atan2(dx, dz)
    return (0.0, math.sin(yaw / 2), 0.0, math.cos(yaw / 2))


def synth_lap(track: Track, speed: float = 30.0, start_seq: int = 1234,
              bounds_min=(-200.0, -1.0, -80.0), bounds_max=(240.0, 3.0, 200.0)) -> tuple[bytes, int]:
    """Returns (ghost bytes, lap time in ms)."""
    pts = track.centerline()
    cum = _arc_table(pts)
    length = cum[-1]
    s = speed * STEP * 0.5  # the first sample is half a step past the line
    ghost = Ghost(50, bounds_min, bounds_max)
    steps = 0
    while True:
        (x, z), (dx, dz) = _point_at(pts, cum, s)
        ghost.seq.append((start_seq + steps) & 0xFFFF)
        ghost.positions.append((x, 0.0, z))
        ghost.rotations.append(_yaw_quat(dx, dz))
        if s >= length:
            break
        s += speed * STEP
        steps += 1
    return encode(ghost), steps * 20


def tamper(ghost_bytes: bytes, mode: str, track: Track | None = None) -> bytes:
    """Edit a ghost the way a cheater might, then re-encode it. "shortcut" needs the track."""
    g = decode(ghost_bytes)
    n = len(g)
    if mode == "teleport":
        # Jump 40 m sideways for one sample: impossible speed.
        x, y, z = g.positions[n // 2]
        g.positions[n // 2] = (x + 40.0, y, z)
    elif mode == "drop":
        # Delete a stretch of samples: sequence gap, and too few samples for the lap time.
        del g.seq[n // 3:n // 3 + 50], g.positions[n // 3:n // 3 + 50], g.rotations[n // 3:n // 3 + 50]
    elif mode == "shortcut":
        # Swing smoothly out to 25 m beside the line around the second checkpoint, slowly
        # enough to pass the speed check, so the path goes round the gate instead of through it.
        if track is None:
            raise ValueError("shortcut needs the track")
        gate = track.gates()[1]
        mid = min(range(n), key=lambda i: math.dist((g.positions[i][0], g.positions[i][2]), gate.center))
        for k in range(400):
            i = mid - 200 + k
            if 0 < i < n - 1:
                w = math.sin(math.pi * k / 400) ** 2 * 25.0
                x, y, z = g.positions[i]
                g.positions[i] = (x + gate.across[0] * w, y, z + gate.across[1] * w)
    elif mode == "speedup":
        # Resample to every other step: half the time, double the speed.
        g.seq = [(g.seq[0] + k) & 0xFFFF for k in range(0, (n + 1) // 2)]
        g.positions = g.positions[::2]
        g.rotations = g.rotations[::2]
    else:
        raise ValueError(f"unknown tamper mode {mode!r}")
    return encode(g)
