# Performance log

Fill this in with measured numbers only. It becomes the profiling line on the resume.

## Budgets

| Budget | Target | Where to read it |
| --- | --- | --- |
| Frame time on device (CPU and GPU) | ≤ 14 ms | Overlay "avg ms" and "max ms"; Unity Profiler (CPU and GPU modules) |
| GC allocations per frame while racing | 0 B | Overlay "GC B" and "GC max"; Profiler Memory module |
| Draw calls | ≤ 100 | Overlay "draws" (development build); Frame Debugger |
| Cold start to race start | ≤ 5 s | Stopwatch from tapping the icon to the menu, plus `adb logcat` timestamps |
| APK size | ≤ 60 MB | Build log line `[Drift] Built ... MB` |

These are starting targets. Adjust them after the first device baseline.

## How to measure

1. Make a development build with `Drift > Build Android APK (Development + Profiler)` and install it.
2. Connect the Profiler to the phone over USB (`adb forward` is set up by Unity's "Autoconnect Profiler").
3. Race three laps with the ghost on. Save the capture as `docs/captures/<date>-<change>.data`.
4. Record the median of the overlay's "avg ms" over the second lap, and the worst "max ms".
5. Change **one** thing, rebuild, and measure again.

The `DriftGame` component (on the `Drift` object in `Main.unity`) has a checkbox for each experiment, so the "before" is always one click away:

| Toggle | What it changes |
| --- | --- |
| Static Batch Scenery | Combines about 150 props (about 300 renderers) into static batches at load |
| Gpu Instancing | Enables instancing on the shared materials |
| Realtime Shadows | Soft shadows from the sun (expect a GPU cost on mobile) |
| Spawn Scenery | Removes all props, which gives the floor cost of the track itself |

## Baseline and changes

| Date | Device | Change | Frame time avg / max (ms) | Draw calls | GC alloc per frame (B) | Load to race (s) | APK (MB) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | Laptop (editor) | Baseline, all optimisations off | | | | | |
| | Phone | Baseline, all optimisations off | | | | | |
| | Phone | Static batching on | | | | | |
| | Phone | GPU instancing on | | | | | |
| | Phone | | | | | | |

## Summary for the README

| Metric | Before | After |
| --- | --- | --- |
| Frame time (ms) | | |
| Draw calls | | |
| GC alloc per frame (B) | | |
| Load to race (s) | | |
