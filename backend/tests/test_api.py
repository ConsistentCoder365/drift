import base64

from tools.synth import synth_lap, tamper

from .conftest import laps_with_best


def submit(client, player, laps, ghost, track="harbor"):
    return client.post("/runs", json={
        "player": player,
        "track": track,
        "lap_times_ms": laps,
        "ghost": base64.b64encode(ghost).decode(),
        "client_version": "test",
    })


def test_valid_run_lands_on_the_leaderboard_and_serves_its_ghost(client, honest_lap):
    ghost, lap_ms = honest_lap
    r = submit(client, "Joy", laps_with_best(lap_ms), ghost)
    assert r.status_code == 201
    body = r.json()
    assert body["validated"] is True
    assert body["reason"] is None
    assert body["best_lap_ms"] == lap_ms
    assert body["rank"] == 1 and body["total"] == 1
    assert body["personal_best"] is True

    board = client.get("/leaderboard", params={"track": "harbor", "limit": 5, "player": "Joy"}).json()
    assert board["total"] == 1
    assert board["entries"][0] == {"rank": 1, "player": "Joy", "best_lap_ms": lap_ms, "run_id": body["run_id"]}
    assert board["you"]["rank"] == 1

    g = client.get(f"/ghosts/{body['run_id']}")
    assert g.status_code == 200
    assert g.headers["content-type"] == "application/octet-stream"
    assert g.content == ghost


def test_tampered_run_is_stored_but_kept_off_the_leaderboard(client, track, honest_lap):
    ghost, lap_ms = honest_lap
    r = submit(client, "Cheater", laps_with_best(lap_ms), tamper(ghost, "teleport", track))
    assert r.status_code == 201
    body = r.json()
    assert body["validated"] is False
    assert body["reason"] == "too_fast"
    assert body["rank"] == 0

    board = client.get("/leaderboard", params={"track": "harbor", "player": "Cheater"}).json()
    assert board["total"] == 0
    assert board["you"] is None
    assert client.get(f"/ghosts/{body['run_id']}").status_code == 404, "rejected ghosts are never served"


def test_only_a_faster_lap_replaces_a_players_entry(client, track):
    slow, slow_ms = synth_lap(track, speed=25.0)
    fast, fast_ms = synth_lap(track, speed=33.0)
    first = submit(client, "Joy", laps_with_best(fast_ms), fast).json()
    second = submit(client, "Joy", laps_with_best(slow_ms), slow).json()
    assert second["validated"] is True
    assert second["personal_best"] is False

    entry = client.get("/leaderboard", params={"track": "harbor"}).json()["entries"][0]
    assert entry["best_lap_ms"] == fast_ms
    assert entry["run_id"] == first["run_id"], "the ghost link still points at the faster run"

    faster, faster_ms = synth_lap(track, speed=36.0)
    third = submit(client, "Joy", laps_with_best(faster_ms), faster).json()
    assert third["personal_best"] is True
    entry = client.get("/leaderboard", params={"track": "harbor"}).json()["entries"][0]
    assert entry["best_lap_ms"] == faster_ms and entry["run_id"] == third["run_id"]


def test_leaderboard_orders_players_and_reports_the_callers_rank(client, track):
    for name, speed in [("Slow", 20.0), ("Fast", 36.0), ("Mid", 28.0)]:
        ghost, ms = synth_lap(track, speed=speed)
        assert submit(client, name, laps_with_best(ms), ghost).json()["validated"]

    board = client.get("/leaderboard", params={"track": "harbor", "limit": 2, "player": "Slow"}).json()
    assert [e["player"] for e in board["entries"]] == ["Fast", "Mid"]
    assert board["total"] == 3
    assert board["you"]["rank"] == 3 and board["you"]["player"] == "Slow"


def test_config_serves_tuning_values(client):
    cfg = client.get("/config").json()
    for key in ("grip_normal", "grip_drift", "nitro_gain_per_point", "combo_window", "nitro_top_speed"):
        assert key in cfg
    assert cfg["combo_window"] == 1.5


def test_bad_requests(client, honest_lap):
    ghost, lap_ms = honest_lap
    assert submit(client, "Joy", laps_with_best(lap_ms), ghost, track="moon").status_code == 404
    assert submit(client, "<script>", laps_with_best(lap_ms), ghost).status_code == 422
    assert submit(client, "", laps_with_best(lap_ms), ghost).status_code == 422
    bad = client.post("/runs", json={"player": "Joy", "track": "harbor", "lap_times_ms": [1, 2, 3], "ghost": "!!!"})
    assert bad.status_code == 422
    assert client.get("/leaderboard", params={"track": "moon"}).status_code == 404
    assert client.get("/ghosts/not-a-uuid").status_code == 404
    assert client.get("/ghosts/00000000-0000-0000-0000-000000000000").status_code == 404
