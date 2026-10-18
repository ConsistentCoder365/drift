from __future__ import annotations

import uuid
from datetime import datetime

from sqlalchemy import JSON, Boolean, DateTime, Integer, LargeBinary, String, Uuid, create_engine, func
from sqlalchemy.engine import Engine
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, sessionmaker
from sqlalchemy.pool import StaticPool


class Base(DeclarativeBase):
    pass


class Run(Base):
    """Every submitted run, accepted or not. Rejected runs are kept for review (validated = false)."""

    __tablename__ = "runs"

    id: Mapped[uuid.UUID] = mapped_column(Uuid, primary_key=True, default=uuid.uuid4)
    player: Mapped[str] = mapped_column(String(16), index=True)
    track: Mapped[str] = mapped_column(String(32), index=True)
    lap_times_ms: Mapped[list[int]] = mapped_column(JSON)
    best_lap_ms: Mapped[int] = mapped_column(Integer)
    # About 18 KB per 30 s lap, so a bytea column is fine at this scale (S3 is a stretch goal).
    ghost: Mapped[bytes | None] = mapped_column(LargeBinary, deferred=True)
    validated: Mapped[bool] = mapped_column(Boolean, index=True)
    rejection_reason: Mapped[str | None] = mapped_column(String(64))
    client_version: Mapped[str | None] = mapped_column(String(16))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())


def make_engine(url: str) -> Engine:
    if url.startswith("sqlite"):
        # In-memory SQLite (tests) must share one connection across threads.
        kwargs = {"connect_args": {"check_same_thread": False}}
        if ":memory:" in url:
            kwargs["poolclass"] = StaticPool
        return create_engine(url, **kwargs)
    return create_engine(url, pool_pre_ping=True)


def make_sessionmaker(engine: Engine) -> sessionmaker:
    return sessionmaker(bind=engine, expire_on_commit=False)
