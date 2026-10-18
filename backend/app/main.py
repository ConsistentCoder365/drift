"""Drift API: runs, leaderboard, ghosts and remote config.

POST /runs                       submit a run (lap times + ghost of the best lap)
GET  /leaderboard?track=&limit=  top N best laps, plus the caller's rank with &player=
GET  /ghosts/{run_id}            download a validated ghost to race against
GET  /config                     tuning values (grip, nitro gain, combo window, ...)
"""

from __future__ import annotations

import base64
import binascii
import uuid
from collections.abc import Iterator
from contextlib import asynccontextmanager

import redis
from fastapi import Depends, FastAPI, HTTPException, Query, Response, status
from sqlalchemy.engine import Engine
from sqlalchemy.orm import Session

from .db import Base, Run, make_engine, make_sessionmaker
from .leaderboard import Leaderboard
from .schemas import EntryOut, LeaderboardOut, RunIn, RunOut
from .settings import Settings, load_settings, load_tuning
from .track import Track, load_track
from .validation import validate_run


def create_app(settings: Settings | None = None, redis_client: redis.Redis | None = None, engine: Engine | None = None) -> FastAPI:
    settings = settings or load_settings()
    engine = engine or make_engine(settings.database_url)
    sessions = make_sessionmaker(engine)
    board = Leaderboard(redis_client or redis.Redis.from_url(settings.redis_url))

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        Base.metadata.create_all(engine)
        yield

    app = FastAPI(title="Drift API", version="0.1.0", lifespan=lifespan)

    def db() -> Iterator[Session]:
        with sessions() as session:
            yield session

    def get_track(track_id: str) -> Track:
        track = load_track(settings.tracks_dir, track_id)
        if track is None:
            raise HTTPException(status.HTTP_404_NOT_FOUND, f"unknown track '{track_id}'")
        return track

    @app.get("/health")
    def health() -> dict:
        return {"ok": True}

    @app.get("/config")
    def config() -> dict:
        return load_tuning(settings.tuning_path)

    @app.post("/runs", response_model=RunOut, status_code=status.HTTP_201_CREATED)
    def submit_run(run: RunIn, session: Session = Depends(db)) -> RunOut:
        track = get_track(run.track)
        try:
            ghost = base64.b64decode(run.ghost, validate=True)
        except (binascii.Error, ValueError):
            raise HTTPException(status.HTTP_422_UNPROCESSABLE_ENTITY, "ghost is not valid base64")

        tuning = load_tuning(settings.tuning_path)
        max_speed = float(tuning["nitro_top_speed"]) * settings.speed_tolerance
        verdict = validate_run(run.lap_times_ms, ghost, track, max_speed)
        best = verdict.best_lap_ms or min(run.lap_times_ms)

        row = Run(
            id=uuid.uuid4(),
            player=run.player.strip(),
            track=track.id,
            lap_times_ms=run.lap_times_ms,
            best_lap_ms=best,
            ghost=ghost,
            validated=verdict.ok,
            rejection_reason=verdict.reason,
            client_version=run.client_version,
        )
        session.add(row)
        session.commit()

        out = RunOut(run_id=str(row.id), validated=verdict.ok, reason=verdict.reason, best_lap_ms=best)
        if verdict.ok:
            out.personal_best = board.submit(track.id, row.player, best, out.run_id)
            entry = board.entry_for(track.id, row.player)
            out.rank = entry.rank if entry else 0
            out.total = board.total(track.id)
        return out

    @app.get("/leaderboard", response_model=LeaderboardOut)
    def leaderboard(
        track: str = Query(..., pattern=r"^[a-z0-9_-]{1,32}$"),
        limit: int = Query(10, ge=1, le=100),
        player: str | None = Query(None, max_length=16),
    ) -> LeaderboardOut:
        t = get_track(track)
        entries = [EntryOut(**e.__dict__) for e in board.top(t.id, limit)]
        you = None
        if player:
            e = board.entry_for(t.id, player.strip())
            you = EntryOut(**e.__dict__) if e else None
        return LeaderboardOut(track=t.id, total=board.total(t.id), entries=entries, you=you)

    @app.get("/ghosts/{run_id}", response_class=Response)
    def ghost(run_id: str, session: Session = Depends(db)) -> Response:
        try:
            key = uuid.UUID(run_id)
        except ValueError:
            raise HTTPException(status.HTTP_404_NOT_FOUND, "no such ghost")
        row = session.get(Run, key)
        # Only validated ghosts are served: a rejected one may be tampered.
        if row is None or not row.validated or not row.ghost:
            raise HTTPException(status.HTTP_404_NOT_FOUND, "no such ghost")
        return Response(content=row.ghost, media_type="application/octet-stream",
                        headers={"Cache-Control": "public, max-age=31536000, immutable"})

    return app


app = create_app()
