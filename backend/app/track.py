"""Track geometry. Mirrors Track.cs: same JSON, same gates, same crossing rule."""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path

Vec2 = tuple[float, float]


@dataclass(frozen=True)
class Gate:
    center: Vec2
    forward: Vec2  # unit direction of travel
    across: Vec2  # unit direction along the gate line
    half_width: float

    def side(self, p: Vec2) -> float:
        return (p[0] - self.center[0]) * self.forward[0] + (p[1] - self.center[1]) * self.forward[1]

    def crossed(self, prev: Vec2, cur: Vec2) -> bool:
        """True if prev -> cur crosses the gate line forwards, within its width."""
        s0, s1 = self.side(prev), self.side(cur)
        if not (s0 < 0 <= s1):
            return False
        t = s0 / (s0 - s1)
        hit = (prev[0] + (cur[0] - prev[0]) * t, prev[1] + (cur[1] - prev[1]) * t)
        lateral = (hit[0] - self.center[0]) * self.across[0] + (hit[1] - self.center[1]) * self.across[1]
        return abs(lateral) <= self.half_width


@dataclass(frozen=True)
class Track:
    id: str
    name: str
    laps: int
    road_width: float
    gate_margin: float
    points: tuple[Vec2, ...]
    checkpoints: tuple[int, ...]

    def point(self, i: int) -> Vec2:
        return self.points[i % len(self.points)]

    def gates(self) -> list[Gate]:
        gates = []
        for i in self.checkpoints:
            a, b = self.point(i - 1), self.point(i + 1)
            dx, dz = b[0] - a[0], b[1] - a[1]
            n = math.hypot(dx, dz)
            fwd = (dx / n, dz / n)
            gates.append(Gate(self.point(i), fwd, (fwd[1], -fwd[0]), self.road_width / 2 + self.gate_margin))
        return gates

    def centerline(self, per_segment: int = 24) -> list[Vec2]:
        """Dense closed centreline (uniform Catmull-Rom), starting at control point 0."""
        out = []
        for i in range(len(self.points)):
            p0, p1, p2, p3 = self.point(i - 1), self.point(i), self.point(i + 1), self.point(i + 2)
            for k in range(per_segment):
                out.append(catmull_rom(p0, p1, p2, p3, k / per_segment))
        return out

    def length(self) -> float:
        c = self.centerline()
        return sum(math.dist(c[i], c[(i + 1) % len(c)]) for i in range(len(c)))


def catmull_rom(p0: Vec2, p1: Vec2, p2: Vec2, p3: Vec2, t: float) -> Vec2:
    t2, t3 = t * t, t * t * t
    return tuple(  # type: ignore[return-value]
        0.5 * (2 * p1[i] + (-p0[i] + p2[i]) * t + (2 * p0[i] - 5 * p1[i] + 4 * p2[i] - p3[i]) * t2
               + (-p0[i] + 3 * p1[i] - 3 * p2[i] + p3[i]) * t3)
        for i in range(2)
    )


def parse_track(data: dict) -> Track:
    return Track(
        id=data["id"],
        name=data.get("name", data["id"]),
        laps=int(data.get("laps", 3)),
        road_width=float(data.get("road_width", 14.0)),
        gate_margin=float(data.get("gate_margin", 3.0)),
        points=tuple((float(p["x"]), float(p["z"])) for p in data["points"]),
        checkpoints=tuple(int(i) for i in data["checkpoints"]),
    )


@lru_cache(maxsize=16)
def load_track(tracks_dir: Path, track_id: str) -> Track | None:
    path = tracks_dir / f"{track_id}.json"
    if not path.is_file():
        return None
    return parse_track(json.loads(path.read_text()))
