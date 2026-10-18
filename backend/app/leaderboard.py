"""Leaderboard in Redis: one sorted set per track, scored by best lap time in ms.

ZADD ... LT keeps only a player's fastest lap; ZRANGE gives the top N, ZRANK the caller's
position. A hash beside the set maps each player to the run that set their time, so the
client can download that ghost. Both writes happen in one Lua script, so two runs from the
same player arriving together can't leave the set and the hash pointing at different runs.
"""

from __future__ import annotations

from dataclasses import dataclass

import redis

SUBMIT_LUA = """
local changed = redis.call('ZADD', KEYS[1], 'LT', 'CH', ARGV[2], ARGV[1])
if changed == 1 then
  redis.call('HSET', KEYS[2], ARGV[1], ARGV[3])
end
return changed
"""


@dataclass(frozen=True)
class Entry:
    rank: int
    player: str
    best_lap_ms: int
    run_id: str


class Leaderboard:
    def __init__(self, client: redis.Redis):
        self.r = client
        self._submit = client.register_script(SUBMIT_LUA)

    @staticmethod
    def _keys(track: str) -> tuple[str, str]:
        return f"lb:{track}", f"lb:{track}:runs"

    def submit(self, track: str, player: str, lap_ms: int, run_id: str) -> bool:
        """Record a validated lap. Returns True if it is the player's new best."""
        scores, runs = self._keys(track)
        return bool(self._submit(keys=[scores, runs], args=[player, lap_ms, run_id]))

    def top(self, track: str, limit: int) -> list[Entry]:
        scores, runs = self._keys(track)
        rows = self.r.zrange(scores, 0, limit - 1, withscores=True)
        if not rows:
            return []
        players = [_s(p) for p, _ in rows]
        run_ids = self.r.hmget(runs, players)
        return [
            Entry(rank=i + 1, player=p, best_lap_ms=int(score), run_id=_s(rid) if rid else "")
            for i, ((_, score), p, rid) in enumerate(zip(rows, players, run_ids))
        ]

    def entry_for(self, track: str, player: str) -> Entry | None:
        scores, runs = self._keys(track)
        pipe = self.r.pipeline()
        pipe.zrank(scores, player)
        pipe.zscore(scores, player)
        pipe.hget(runs, player)
        rank, score, run_id = pipe.execute()
        if rank is None:
            return None
        return Entry(rank=rank + 1, player=player, best_lap_ms=int(score), run_id=_s(run_id) if run_id else "")

    def total(self, track: str) -> int:
        return self.r.zcard(self._keys(track)[0])


def _s(v: bytes | str) -> str:
    return v.decode() if isinstance(v, bytes) else v
