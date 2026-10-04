using UnityEngine;

namespace Drift.Core
{
    /// <summary>
    /// The nitro meter (0..1). Drifting fills it; tapping Nitro spends all of it at once
    /// for a boost lasting fill x nitro_full_duration seconds.
    /// </summary>
    public sealed class NitroMeter
    {
        private TuningValues _t;

        public float Fill { get; private set; }
        public bool IsBoosting { get; private set; }
        public float BoostRemaining { get; private set; }

        public NitroMeter(TuningValues tuning)
        {
            _t = tuning;
        }

        public void SetTuning(TuningValues tuning)
        {
            _t = tuning;
        }

        public void Reset()
        {
            Fill = 0f;
            IsBoosting = false;
            BoostRemaining = 0f;
        }

        public void AddFromPoints(float points)
        {
            Add(points * _t.nitro_gain_per_point);
        }

        public void Add(float amount)
        {
            if (amount <= 0f) return;
            Fill = Mathf.Min(1f, Fill + amount);
        }

        public bool TryFire()
        {
            if (IsBoosting || Fill < _t.nitro_min_to_fire) return false;
            BoostRemaining = Fill * _t.nitro_full_duration;
            Fill = 0f;
            IsBoosting = true;
            return true;
        }

        public void Tick(float dt)
        {
            if (!IsBoosting) return;
            BoostRemaining -= dt;
            if (BoostRemaining <= 0f)
            {
                BoostRemaining = 0f;
                IsBoosting = false;
            }
        }
    }
}
