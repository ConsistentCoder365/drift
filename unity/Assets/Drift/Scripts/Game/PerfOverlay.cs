using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Game
{
    /// <summary>
    /// On-device numbers for the performance log: FPS, average and worst frame time over the
    /// last half second, GC bytes allocated in the last frame (and the worst in the window),
    /// draw calls and batches. Uses ProfilerRecorder, so it reads the same counters as the
    /// Unity Profiler; draw-call counters only exist in development builds and the editor.
    /// Like the HUD it doesn't allocate, so it doesn't spoil the GC number it reports.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        private const float Window = 0.5f;

        private ProfilerRecorder _gc, _drawCalls, _batches;
        private NumberLabel _fps, _avgMs, _maxMs, _gcBytes, _gcMax, _draws, _batch;
        private GameObject _drawRow, _batchRow;
        private float _windowTime, _windowMax, _windowGcMax;
        private int _windowFrames;

        public void Build(Transform canvasRoot)
        {
            Image panel = UiFactory.CreateImage(canvasRoot, "Perf", new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Place(panel.rectTransform, TextAnchor.LowerCenter, new Vector2(0f, 8f), new Vector2(760f, 120f));
            Transform p = panel.rectTransform;

            _fps = Row(p, "FPS", "##0", 0, 0);
            _avgMs = Row(p, "avg ms", "#0.0", 1, 0);
            _maxMs = Row(p, "max ms", "#0.0", 2, 0);
            _gcBytes = Row(p, "GC B", "#####0", 0, 1);
            _gcMax = Row(p, "GC max", "#####0", 1, 1);
            _draws = Row(p, "draws", "###0", 2, 1);
            _batch = Row(p, "batches", "###0", 3, 0);
            _drawRow = _draws.Root.parent.gameObject;
            _batchRow = _batch.Root.parent.gameObject;
        }

        private static NumberLabel Row(Transform parent, string caption, string pattern, int col, int row)
        {
            RectTransform cell = UiFactory.Rect(parent, caption);
            UiFactory.Place(cell, TextAnchor.UpperLeft, new Vector2(14f + col * 186f, -8f - row * 56f), new Vector2(180f, 50f));
            UiFactory.Label(cell, caption, 20, UiFactory.Muted, TextAnchor.UpperLeft, new Vector2(0f, 0f), new Vector2(90f, 24f), TextAnchor.MiddleLeft);
            var label = new NumberLabel(cell, pattern, 28, Color.white, TextAnchor.LowerLeft);
            label.Root.anchoredPosition = new Vector2(0f, 2f);
            return label;
        }

        private void OnEnable()
        {
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        }

        private void OnDisable()
        {
            _gc.Dispose();
            _drawCalls.Dispose();
            _batches.Dispose();
        }

        private void Update()
        {
            if (_fps == null) return;
            float dt = Time.unscaledDeltaTime;
            _windowTime += dt;
            _windowFrames++;
            if (dt > _windowMax) _windowMax = dt;

            long gc = _gc.Valid ? _gc.LastValue : 0;
            if (gc > _windowGcMax) _windowGcMax = gc;
            _gcBytes.SetInt(gc);

            if (_windowTime >= Window)
            {
                float avg = _windowTime / _windowFrames;
                _fps.SetInt(Mathf.RoundToInt(1f / avg));
                _avgMs.SetInt(Mathf.RoundToInt(avg * 10000f));
                _maxMs.SetInt(Mathf.RoundToInt(_windowMax * 10000f));
                _gcMax.SetInt((long)_windowGcMax);
                _windowTime = 0f;
                _windowFrames = 0;
                _windowMax = 0f;
                _windowGcMax = 0f;
            }

            _drawRow.SetActive(_drawCalls.Valid);
            _batchRow.SetActive(_batches.Valid);
            if (_drawCalls.Valid) _draws.SetInt(_drawCalls.LastValue);
            if (_batches.Valid) _batch.SetInt(_batches.LastValue);
        }
    }
}
