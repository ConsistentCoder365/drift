using UnityEngine;
using UnityEngine.UI;

namespace Drift.Game
{
    /// <summary>
    /// A number shown as one UI Text per character slot, each fed from a static table of
    /// one-character strings. Changing the value never builds a new string, so the HUD
    /// costs 0 B of GC per frame (string.Format or ToString would allocate every frame).
    ///
    /// Pattern characters: '0' = digit, '#' = digit hidden when it is a leading zero,
    /// anything else is a fixed character. Time patterns use "0:00.00".
    /// </summary>
    public sealed class NumberLabel
    {
        private static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

        private readonly Text[] _slots;
        private readonly bool[] _isDigit;
        private readonly bool[] _hideLeadingZero;
        private readonly int[] _digitSlots; // slot indices of digits, least significant first
        private long _last = long.MinValue;

        public RectTransform Root { get; private set; }

        public NumberLabel(Transform parent, string pattern, int fontSize, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Number", typeof(RectTransform));
            Root = (RectTransform)go.transform;
            Root.SetParent(parent, false);

            float slotWidth = fontSize * 0.62f;
            float narrowWidth = fontSize * 0.34f;
            int n = pattern.Length;
            _slots = new Text[n];
            _isDigit = new bool[n];
            _hideLeadingZero = new bool[n];

            float total = 0f;
            for (int i = 0; i < n; i++) total += IsDigitChar(pattern[i]) ? slotWidth : narrowWidth;
            Root.sizeDelta = new Vector2(total, fontSize * 1.25f);

            int digitCount = 0;
            float x = -total * 0.5f;
            for (int i = 0; i < n; i++)
            {
                char c = pattern[i];
                bool digit = IsDigitChar(c);
                float w = digit ? slotWidth : narrowWidth;
                Text t = UiFactory.Label(Root, digit ? "0" : c.ToString(), fontSize, color, TextAnchor.MiddleCenter);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(w + 4f, fontSize * 1.25f);
                rt.anchoredPosition = new Vector2(x + w * 0.5f, 0f);
                x += w;
                _slots[i] = t;
                _isDigit[i] = digit;
                _hideLeadingZero[i] = c == '#';
                if (digit) digitCount++;
            }

            _digitSlots = new int[digitCount];
            int k = 0;
            for (int i = n - 1; i >= 0; i--)
                if (_isDigit[i]) _digitSlots[k++] = i;

            UiFactory.Anchor(Root, anchor);
        }

        private static bool IsDigitChar(char c)
        {
            return c == '0' || c == '#';
        }

        public void SetActive(bool active)
        {
            Root.gameObject.SetActive(active);
        }

        /// <summary>Show a non-negative integer, clamped to what the slots can hold.</summary>
        public void SetInt(long value)
        {
            long max = Pow10(_digitSlots.Length) - 1;
            if (value < 0) value = 0;
            if (value > max) value = max;
            if (value == _last) return;
            _last = value;
            // Most significant digit first, so leading zeros can be hidden.
            bool leading = true;
            for (int k = _digitSlots.Length - 1; k >= 0; k--)
            {
                int d = (int)(value / Pow10(k) % 10);
                int slot = _digitSlots[k];
                bool hide = leading && d == 0 && k > 0 && _hideLeadingZero[slot];
                if (d != 0) leading = false;
                _slots[slot].text = hide ? string.Empty : Digits[d];
            }
        }

        /// <summary>Show centiseconds as M:SS.cc (pattern "0:00.00"). Digit slots: c c S S M.</summary>
        public void SetTime(int centiseconds)
        {
            if (centiseconds < 0) centiseconds = 0;
            if (centiseconds == _last) return;
            _last = centiseconds;
            int cs = centiseconds % 100;
            int s = centiseconds / 100 % 60;
            int m = Mathf.Min(9, centiseconds / 6000);
            SetDigit(0, cs % 10);
            SetDigit(1, cs / 10);
            SetDigit(2, s % 10);
            SetDigit(3, s / 10);
            SetDigit(4, m);
        }

        private void SetDigit(int k, int d)
        {
            if (k < _digitSlots.Length) _slots[_digitSlots[k]].text = Digits[d];
        }

        private static long Pow10(int k)
        {
            long p = 1;
            for (int i = 0; i < k; i++) p *= 10;
            return p;
        }
    }
}
