"""Server-side lap validation from the submitted ghost.

The client can't be trusted, so before a lap reaches the leaderboard its replay must be
physically possible on this track. It's a first layer, not real anti-cheat: a determined
cheater can still synthesise a plausible path, but they can't just edit a lap time.

The client contract (GhostRecorder.cs): a lap of N physics steps at 50 Hz is reported as
N x 20 ms and has N + 1 samples, from the step that crossed the start line to the step that
crossed it again, with consecutive sequence numbers.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

from .ghost import Ghost, GhostDecodeError, decode
from .track import Track

SAMPLE_RATE = 50
STEP_MS = 1000 // SAMPLE_RATE
# Positions are quantised to ~6 mm on this track; allow a little slack for that.
POSITION_SLACK_M = 0.05


@dataclass(frozen=True)
class Verdict:
    ok: bool
    reason: str | None = None
    best_lap_ms: int = 0


def reject(reason: str, best: int = 0) -> Verdict:
    return Verdict(False, reason, best)


def validate_run(lap_times_ms: list[int], ghost_bytes: bytes, track: Track, max_speed: float) -> Verdict:
    """`max_speed` is the top speed with nitro (m/s) times a tolerance."""
    if len(lap_times_ms) != track.laps:
        return reject("wrong_lap_count")
    if any(ms <= 0 or ms % STEP_MS for ms in lap_times_ms):
        return reject("bad_lap_time")
    best = min(lap_times_ms)

    # Even a perfect line can't be shorter than ~90% of the centreline at top speed.
    fastest_possible_ms = track.length() * 0.9 / max_speed * 1000
    if best < fastest_possible_ms:
        return reject("lap_too_fast", best)

    try:
        ghost = decode(ghost_bytes)
    except GhostDecodeError as e:
        return reject(f"ghost_{str(e).replace(' ', '_')}", best)
    if ghost.sample_rate != SAMPLE_RATE:
        return reject("bad_sample_rate", best)

    reason = (
        check_sample_count(ghost, best)
        or check_sequence(ghost)
        or check_speed(ghost, max_speed)
        or check_checkpoints(ghost, track, max_speed)
    )
    return reject(reason, best) if reason else Verdict(True, None, best)


def check_sample_count(ghost: Ghost, lap_ms: int) -> str | None:
    """The ghost must be the best lap: one sample per step, plus the closing sample."""
    if len(ghost) != lap_ms // STEP_MS + 1:
        return "sample_count_mismatch"
    return None


def check_sequence(ghost: Ghost) -> str | None:
    for a, b in zip(ghost.seq, ghost.seq[1:]):
        if (b - a) & 0xFFFF != 1:
            return "sequence_gap"
    return None


def check_speed(ghost: Ghost, max_speed: float) -> str | None:
    """No step may move further than the car can travel in 20 ms at full nitro."""
    limit = max_speed / SAMPLE_RATE + POSITION_SLACK_M
    p = ghost.positions
    for i in range(1, len(p)):
        if math.hypot(p[i][0] - p[i - 1][0], p[i][2] - p[i - 1][2]) > limit:
            return "too_fast"
    return None


def check_checkpoints(ghost: Ghost, track: Track, max_speed: float) -> str | None:
    """Start just past the line, cross every checkpoint in order, finish back over the line."""
    gates = track.gates()
    flat = [(x, z) for x, _, z in ghost.positions]

    start = gates[0]
    side = start.side(flat[0])
    if not (-POSITION_SLACK_M <= side <= max_speed / SAMPLE_RATE + POSITION_SLACK_M) or abs(_lateral(start, flat[0])) > start.half_width:
        return "bad_start"

    # The lap ends on the step that crosses the line, so the final crossing belongs in the
    # last segment. Quantisation can move a sample that sat within millimetres of the line to
    # the other side, so that crossing may show up one segment early, or the last sample may
    # land a hair short of the line; both are accepted.
    next_gate = 1 % len(gates)
    for i in range(1, len(flat)):
        if not gates[next_gate].crossed(flat[i - 1], flat[i]):
            continue
        if next_gate == 0:
            return None if i >= len(flat) - 2 else "finished_early"
        next_gate = (next_gate + 1) % len(gates)

    last = flat[-1]
    if next_gate == 0 and -POSITION_SLACK_M <= start.side(last) < 0 and abs(_lateral(start, last)) <= start.half_width:
        return None
    return "checkpoints_missed"


def _lateral(gate, p) -> float:
    return (p[0] - gate.center[0]) * gate.across[0] + (p[1] - gate.center[1]) * gate.across[1]
