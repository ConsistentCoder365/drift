# AI tool log

What was built with an agentic coding harness (Claude Code), what the tests caught, and what was rewritten by hand.

## Built with Claude Code (Oct 24, 2026)

The first pass was generated from the PRD in one session:

- The Unity project skeleton: assembly definitions, package manifest, and an editor script that sets up URP, materials, the scene and Android settings.
- The pure C# core: `DriftPhysics`, `Damping`, `DriftScorer`, `NitroMeter`, `Track`/`LapTracker`, `GhostBuffer`, `GhostRecorder`, `GhostCodec`.
- The engine layer: car controller, chase camera, procedural track builder, ghost player, HUD, touch input, API client, remote config, perf overlay.
- 57 EditMode tests, a .NET harness that runs them without Unity, and the FastAPI backend with 37 pytest tests.

**What the tests caught in that session:**

- **Rotation quantisation bias.** The first `GhostCodec` mapped components onto 1023 steps, so 0.0 couldn't be stored exactly. Every yaw-only rotation, which is every rotation this car makes, came back about 0.14° off. `Rotation_IdentityAndYawOnlyRoundTrip` failed on it. The fix was 1022 steps, which puts 0.0 exactly on code 511, applied in both C# and Python.
- **A wrong test.** The garbage-ghost test fed in fewer bytes than the header, so the codec correctly answered "too short" rather than "bad magic". The test was fixed, not the code.

**Verified in that session:** the core tests on .NET 8, the backend tests, and the full Docker Compose stack, with the tamper demo against real Postgres and Redis.

**Not verified in that session:** the Unity engine-layer scripts were not compiled, because there was no Unity install on that machine. The first job is to open the project and fix any compile errors.

## Rewritten by hand

Fill this in as you go. Interviewers ask about it.

| Area | What the AI wrote | What I changed and why |
| --- | --- | --- |
| | | |
