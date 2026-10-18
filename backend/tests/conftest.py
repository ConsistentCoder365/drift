from __future__ import annotations

from pathlib import Path

import fakeredis
import pytest
from fastapi.testclient import TestClient

from app.db import make_engine
from app.main import create_app
from app.settings import BACKEND_DIR, Settings, load_settings
from app.track import load_track
from tools.synth import synth_lap

FIXTURES = Path(__file__).parent / "fixtures"


@pytest.fixture(scope="session")
def settings() -> Settings:
    base = load_settings()
    return Settings(
        database_url="sqlite:///:memory:",
        redis_url="redis://unused",
        tracks_dir=base.tracks_dir,
        tuning_path=BACKEND_DIR / "config" / "tuning.json",
    )


@pytest.fixture(scope="session")
def track(settings):
    t = load_track(settings.tracks_dir, "harbor")
    assert t is not None, f"harbor.json not found in {settings.tracks_dir}"
    return t


@pytest.fixture(scope="session")
def max_speed() -> float:
    return 50.0 * 1.15


@pytest.fixture(scope="session")
def honest_lap(track) -> tuple[bytes, int]:
    return synth_lap(track, speed=32.0)


@pytest.fixture
def client(settings):
    app = create_app(settings, redis_client=fakeredis.FakeRedis(), engine=make_engine(settings.database_url))
    with TestClient(app) as c:
        yield c


def laps_with_best(best_ms: int) -> list[int]:
    return [best_ms + 1200, best_ms, best_ms + 600]
