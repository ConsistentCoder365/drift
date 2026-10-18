import pytest

from app.ghost import decode, encode
from app.validation import validate_run
from tools.synth import synth_lap, tamper

from .conftest import laps_with_best


def test_honest_lap_is_accepted(track, honest_lap, max_speed):
    ghost, lap_ms = honest_lap
    verdict = validate_run(laps_with_best(lap_ms), ghost, track, max_speed)
    assert verdict.ok, verdict.reason
    assert verdict.best_lap_ms == lap_ms


@pytest.mark.parametrize("speed", [12.0, 25.0, 37.0, 49.0])
def test_honest_laps_at_any_legal_speed_are_accepted(track, max_speed, speed):
    ghost, lap_ms = synth_lap(track, speed=speed)
    verdict = validate_run(laps_with_best(lap_ms), ghost, track, max_speed)
    assert verdict.ok, verdict.reason


@pytest.mark.parametrize("seq", [0, 65000, 65535])
def test_sequence_numbers_may_wrap(track, max_speed, seq):
    ghost, lap_ms = synth_lap(track, speed=30.0, start_seq=seq)
    assert validate_run(laps_with_best(lap_ms), ghost, track, max_speed).ok


@pytest.mark.parametrize("mode,reasons", [
    ("teleport", {"too_fast"}),
    ("drop", {"sample_count_mismatch", "sequence_gap"}),
    ("shortcut", {"checkpoints_missed"}),
])
def test_tampered_ghosts_are_rejected(track, honest_lap, max_speed, mode, reasons):
    ghost, lap_ms = honest_lap
    verdict = validate_run(laps_with_best(lap_ms), tamper(ghost, mode, track), track, max_speed)
    assert not verdict.ok
    assert verdict.reason in reasons


def test_sped_up_ghost_with_a_matching_lap_time_is_rejected(track, honest_lap, max_speed):
    ghost, _ = honest_lap
    fast = tamper(ghost, "speedup")
    claimed = (len(decode(fast)) - 1) * 20
    verdict = validate_run(laps_with_best(claimed), fast, track, max_speed)
    assert not verdict.ok
    assert verdict.reason in {"too_fast", "lap_too_fast"}


def test_editing_only_the_lap_time_is_rejected(track, honest_lap, max_speed):
    ghost, lap_ms = honest_lap
    verdict = validate_run(laps_with_best(lap_ms - 3000), ghost, track, max_speed)
    assert verdict.reason == "sample_count_mismatch"


def test_reordered_sequence_is_rejected(track, honest_lap, max_speed):
    ghost, lap_ms = honest_lap
    g = decode(ghost)
    g.seq[100], g.seq[101] = g.seq[101], g.seq[100]
    assert validate_run(laps_with_best(lap_ms), encode(g), track, max_speed).reason == "sequence_gap"


def test_lap_that_starts_away_from_the_line_is_rejected(track, honest_lap, max_speed):
    ghost, lap_ms = honest_lap
    g = decode(ghost)
    # Drop the first 10 samples and pad the end, so count and sequence still look right.
    g.positions = g.positions[10:] + [g.positions[-1]] * 10
    g.rotations = g.rotations[10:] + [g.rotations[-1]] * 10
    verdict = validate_run(laps_with_best(lap_ms), encode(g), track, max_speed)
    assert verdict.reason == "bad_start"


def test_lap_that_reaches_the_line_early_is_rejected(track, max_speed):
    # A real lap, padded with 100 parked samples after the finish to claim a slower time.
    # (Not a cheat by itself, but the ghost no longer matches what the client records.)
    ghost, lap_ms = synth_lap(track, speed=30.0)
    g = decode(ghost)
    for _ in range(100):
        g.seq.append((g.seq[-1] + 1) & 0xFFFF)
        g.positions.append(g.positions[-1])
        g.rotations.append(g.rotations[-1])
    verdict = validate_run(laps_with_best(lap_ms + 2000), encode(g), track, max_speed)
    assert verdict.reason == "finished_early"


@pytest.mark.parametrize("laps,reason", [
    ([30000, 30000], "wrong_lap_count"),
    ([30000, 30010, 30000], "bad_lap_time"),
    ([30000, 0, 30000], "bad_lap_time"),
    ([30000, 2000, 30000], "lap_too_fast"),
])
def test_impossible_lap_times_are_rejected(track, honest_lap, max_speed, laps, reason):
    ghost, _ = honest_lap
    assert validate_run(laps, ghost, track, max_speed).reason == reason


@pytest.mark.parametrize("data,reason", [
    (b"short", "ghost_too_short"),
    (b"not a ghost at all, just some bytes " * 3, "ghost_bad_magic"),
])
def test_garbage_ghost_is_rejected(track, max_speed, data, reason):
    assert validate_run([30000, 30000, 30000], data, track, max_speed).reason == reason
