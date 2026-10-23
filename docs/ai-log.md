# AI tool log

What was built with an agentic coding harness (Claude Code), what the tests caught, and what was rewritten by hand.

## Built with Claude Code

Claude Code generated the first pass of each part from the PRD:

- **Unity skeleton:** assembly definitions, the package manifest, and an editor script that sets up URP, materials, the scene and Android settings.
- **Pure C# core:** `DriftPhysics`, `Damping`, `DriftScorer`, `NitroMeter`, `Track`/`LapTracker`, `GhostBuffer`, `GhostRecorder`, `GhostCodec`.
- **Engine layer:** car controller, chase camera, procedural track builder, ghost player, HUD, touch input, API client, remote config, perf overlay.
- **Tests and backend:** 57 EditMode tests, a .NET harness that runs them without Unity, and the FastAPI backend with 37 pytest tests.

## What the tests caught

- **Rotation quantisation bias.** The first `GhostCodec` mapped components onto 1023 steps, so 0.0 couldn't be stored exactly. Every yaw-only rotation, which is every rotation this car makes, came back about 0.14° off. `Rotation_IdentityAndYawOnlyRoundTrip` failed on it. The fix was 1022 steps, which puts 0.0 exactly on code 511, applied in both C# and Python.
- **A wrong test.** The garbage-ghost test fed in fewer bytes than the header, so the codec correctly answered "too short" rather than "bad magic". The test was fixed, not the code.

## How it was checked

- The core tests pass on .NET 8.
- The backend tests pass.
- The full Docker Compose stack ran the tamper demo against real Postgres and Redis.

## Still to check

The Unity engine-layer scripts were written without a Unity install to compile them against. The first job is to open the project in Unity 6, fix any compile errors, and play-test.

## Rewritten by hand

Fill this in as you go. Interviewers ask about it.

| Area | What the AI wrote | What I changed and why |
| --- | --- | --- |
| | | |
