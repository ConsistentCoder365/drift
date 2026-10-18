import json
import math
import random

import pytest

from app.ghost import Ghost, GhostDecodeError, decode, encode, pack_rotation, unpack_rotation

from .conftest import FIXTURES


def angle_deg(a, b) -> float:
    dot = min(1.0, abs(sum(x * y for x, y in zip(a, b))))
    return math.degrees(2 * math.acos(dot))


def test_decodes_the_ghost_written_by_the_csharp_codec():
    """The C# encoder (tools/core-tests) wrote these bytes; Python must read the same poses."""
    data = (FIXTURES / "csharp_ghost.bin").read_bytes()
    expected = json.loads((FIXTURES / "csharp_ghost.json").read_text())
    ghost = decode(data)

    assert ghost.sample_rate == 50
    assert ghost.bounds_min == pytest.approx(tuple(expected["bounds_min"]))
    assert len(ghost) == len(expected["samples"])
    step = (expected["bounds_max"][0] - expected["bounds_min"][0]) / 65535
    for i, s in enumerate(expected["samples"]):
        assert ghost.seq[i] == s["seq"]
        assert math.dist(ghost.positions[i], s["pos"]) < step * 1.5
        assert angle_deg(ghost.rotations[i], s["rot"]) < 0.25


def test_python_reencodes_the_csharp_ghost_to_the_same_bytes():
    data = (FIXTURES / "csharp_ghost.bin").read_bytes()
    assert encode(decode(data)) == data


def test_round_trip():
    g = Ghost(50, (-10.0, -1.0, -10.0), (10.0, 3.0, 10.0))
    for i in range(100):
        g.seq.append(i)
        g.positions.append((math.sin(i * 0.1) * 9, 0.0, math.cos(i * 0.1) * 9))
        g.rotations.append((0.0, math.sin(i * 0.05), 0.0, math.cos(i * 0.05)))
    back = decode(encode(g))
    assert back.seq == g.seq
    for a, b in zip(back.positions, g.positions):
        assert math.dist(a, b) < 0.001
    for a, b in zip(back.rotations, g.rotations):
        assert angle_deg(a, b) < 0.25


def test_rotation_yaw_only_is_exact_in_x_and_z():
    q = (0.0, math.sin(0.4), 0.0, math.cos(0.4))
    x, _, z, _ = unpack_rotation(pack_rotation(q))
    assert x == 0.0 and z == 0.0


def test_rotation_random_worst_case_under_a_quarter_degree():
    rng = random.Random(3)
    worst = 0.0
    for _ in range(3000):
        q = [rng.gauss(0, 1) for _ in range(4)]
        n = math.sqrt(sum(v * v for v in q))
        q = tuple(v / n for v in q)
        worst = max(worst, angle_deg(q, unpack_rotation(pack_rotation(q))))
    assert worst < 0.25


@pytest.mark.parametrize("mutate,reason", [
    (lambda b: b[:10], "too short"),
    (lambda b: b"XXXX" + b[4:], "bad magic"),
    (lambda b: b[:-1], "length mismatch"),
    (lambda b: b[:4] + b"\x09" + b[5:], "unsupported version"),
])
def test_rejects_malformed_ghosts(mutate, reason):
    good = (FIXTURES / "csharp_ghost.bin").read_bytes()
    with pytest.raises(GhostDecodeError, match=reason):
        decode(mutate(good))
