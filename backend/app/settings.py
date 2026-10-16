from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path

BACKEND_DIR = Path(__file__).resolve().parent.parent


@dataclass(frozen=True)
class Settings:
    database_url: str
    redis_url: str
    tracks_dir: Path
    tuning_path: Path
    # Laps are checked against nitro top speed x this factor.
    speed_tolerance: float = 1.15


def load_settings() -> Settings:
    return Settings(
        database_url=os.getenv("DATABASE_URL", "sqlite:///./drift.db"),
        redis_url=os.getenv("REDIS_URL", "redis://localhost:6379/0"),
        tracks_dir=Path(os.getenv("TRACKS_DIR", BACKEND_DIR.parent / "unity" / "Assets" / "Resources" / "Tracks")),
        tuning_path=Path(os.getenv("TUNING_PATH", BACKEND_DIR / "config" / "tuning.json")),
        speed_tolerance=float(os.getenv("SPEED_TOLERANCE", "1.15")),
    )


# Fallbacks if the tuning file is missing a key; the same values as TuningValues.cs.
DEFAULT_TUNING = {
    "version": 1,
    "grip_normal": 20.0,
    "grip_drift": 3.5,
    "grip_recovery": 6.0,
    "slide_momentum_keep": 0.85,
    "top_speed": 38.0,
    "nitro_top_speed": 50.0,
    "acceleration": 14.0,
    "nitro_acceleration": 26.0,
    "drift_drag": 0.12,
    "coast_drag": 0.6,
    "max_yaw_rate": 2.1,
    "drift_yaw_boost": 1.45,
    "steer_full_speed": 14.0,
    "yaw_response": 12.0,
    "drift_min_angle_deg": 8.0,
    "drift_min_speed": 8.0,
    "drift_end_grace": 0.15,
    "points_per_degree_meter": 0.1,
    "combo_window": 1.5,
    "combo_max_multiplier": 5,
    "nitro_gain_per_point": 0.0025,
    "nitro_full_duration": 3.0,
    "nitro_min_to_fire": 0.15,
    "wall_speed_keep": 0.7,
}


def load_tuning(path: Path) -> dict:
    """Read on every request, so editing the file retunes the game without a restart or a new APK."""
    tuning = dict(DEFAULT_TUNING)
    try:
        tuning.update(json.loads(path.read_text()))
    except (OSError, ValueError):
        pass
    return tuning
