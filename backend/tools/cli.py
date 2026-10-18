"""Command line helpers for the Drift API.

  python -m tools.cli synth --out lap.bin [--speed 30]
  python -m tools.cli tamper lap.bin bad.bin --mode teleport|drop|shortcut|speedup
  python -m tools.cli submit --ghost lap.bin --lap-ms 26520 [--player Joy] [--url http://localhost:8000]
  python -m tools.cli demo [--url http://localhost:8000]   # one honest run + every tamper mode
"""

from __future__ import annotations

import argparse
import base64
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

from app.ghost import decode
from app.settings import load_settings
from app.track import load_track

from .synth import synth_lap, tamper

TAMPER_MODES = ("teleport", "drop", "shortcut", "speedup")


def _track(track_id: str):
    track = load_track(load_settings().tracks_dir, track_id)
    if track is None:
        sys.exit(f"track {track_id!r} not found")
    return track


def _post_run(url: str, player: str, track: str, laps: list[int], ghost: bytes) -> dict:
    body = json.dumps({
        "player": player,
        "track": track,
        "lap_times_ms": laps,
        "ghost": base64.b64encode(ghost).decode(),
        "client_version": "cli",
    }).encode()
    req = urllib.request.Request(url.rstrip("/") + "/runs", data=body, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return json.loads(resp.read())
    except urllib.error.HTTPError as e:
        return {"http_error": e.code, "detail": e.read().decode()}


def _laps_for(best_ms: int, laps: int) -> list[int]:
    # The ghost is the best lap; the others are a little slower.
    return [best_ms + 1200, best_ms] + [best_ms + 600] * (laps - 2) if laps >= 2 else [best_ms]


def main(argv: list[str] | None = None) -> None:
    p = argparse.ArgumentParser(prog="tools.cli")
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("synth", help="write a valid synthetic lap ghost")
    s.add_argument("--track", default="harbor")
    s.add_argument("--speed", type=float, default=30.0)
    s.add_argument("--out", type=Path, required=True)

    t = sub.add_parser("tamper", help="edit a ghost like a cheater would")
    t.add_argument("src", type=Path)
    t.add_argument("dst", type=Path)
    t.add_argument("--mode", choices=TAMPER_MODES, required=True)

    u = sub.add_parser("submit", help="POST a run")
    u.add_argument("--url", default="http://localhost:8000")
    u.add_argument("--player", default="CLI")
    u.add_argument("--track", default="harbor")
    u.add_argument("--ghost", type=Path, required=True)
    u.add_argument("--lap-ms", type=int, required=True, help="claimed best lap; the ghost must be this lap")

    d = sub.add_parser("demo", help="submit an honest run and every tampered variant")
    d.add_argument("--url", default="http://localhost:8000")
    d.add_argument("--track", default="harbor")

    a = p.parse_args(argv)
    if a.cmd == "synth":
        ghost, lap_ms = synth_lap(_track(a.track), a.speed)
        a.out.write_bytes(ghost)
        print(f"wrote {a.out}: {len(decode(ghost))} samples, {len(ghost)} bytes, lap {lap_ms} ms")
    elif a.cmd == "tamper":
        a.dst.write_bytes(tamper(a.src.read_bytes(), a.mode, _track("harbor")))
        print(f"wrote {a.dst} ({a.mode})")
    elif a.cmd == "submit":
        track = _track(a.track)
        print(json.dumps(_post_run(a.url, a.player, a.track, _laps_for(a.lap_ms, track.laps), a.ghost.read_bytes()), indent=2))
    elif a.cmd == "demo":
        track = _track(a.track)
        ghost, lap_ms = synth_lap(track, 30.0)
        laps = _laps_for(lap_ms, track.laps)
        print(f"honest       -> {_post_run(a.url, 'Honest', a.track, laps, ghost)}")
        for mode in TAMPER_MODES:
            print(f"{mode:<12} -> {_post_run(a.url, 'Cheater', a.track, laps, tamper(ghost, mode, track))}")
        faster = [ms - 5000 for ms in laps]
        print(f"edited time  -> {_post_run(a.url, 'Cheater', a.track, faster, ghost)}")


if __name__ == "__main__":
    main()
