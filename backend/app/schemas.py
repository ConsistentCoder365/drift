from __future__ import annotations

from pydantic import BaseModel, Field

# 120 s lap at 50 Hz = 6001 samples x 12 B + 36 B header, about 96 KB as base64.
MAX_GHOST_BASE64 = 100_000


class RunIn(BaseModel):
    player: str = Field(min_length=1, max_length=16, pattern=r"^[A-Za-z0-9 _-]+$")
    track: str = Field(pattern=r"^[a-z0-9_-]{1,32}$")
    lap_times_ms: list[int] = Field(min_length=1, max_length=10)
    ghost: str = Field(min_length=1, max_length=MAX_GHOST_BASE64, description="base64 GhostCodec bytes of the best lap")
    client_version: str | None = Field(default=None, max_length=16)


class RunOut(BaseModel):
    run_id: str
    validated: bool
    reason: str | None = None
    best_lap_ms: int
    rank: int = 0  # 0 = not on the leaderboard
    total: int = 0
    personal_best: bool = False


class EntryOut(BaseModel):
    rank: int
    player: str
    best_lap_ms: int
    run_id: str


class LeaderboardOut(BaseModel):
    track: str
    total: int
    entries: list[EntryOut]
    you: EntryOut | None = None
