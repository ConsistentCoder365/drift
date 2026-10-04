using UnityEngine;

namespace Drift.Core
{
    /// <summary>
    /// Drift points and the combo multiplier.
    /// While sliding, points build at |angle in degrees| x speed x points_per_degree_meter per second,
    /// times the multiplier. A drift that starts within combo_window seconds of the previous one
    /// extends the chain (multiplier = chain length, capped). The chain is banked when the window
    /// runs out and lost on a wall hit.
    /// </summary>
    public sealed class DriftScorer
    {
        private TuningValues _t;
        private float _notSlidingTime;
        private float _sinceLastDrift = float.PositiveInfinity;

        public bool IsDrifting { get; private set; }
        public int ChainCount { get; private set; }
        /// <summary>Points of the drift in progress, already multiplied.</summary>
        public float CurrentDriftPoints { get; private set; }
        /// <summary>Points of finished drifts in the current chain.</summary>
        public float ChainPoints { get; private set; }
        public float TotalPoints { get; private set; }
        public float BestCombo { get; private set; }
        public float DriftAngleDeg { get; private set; }

        public int Multiplier
        {
            get { return Mathf.Clamp(ChainCount, 1, Mathf.Max(1, _t.combo_max_multiplier)); }
        }

        /// <summary>Everything the current chain is worth right now (shown on the HUD).</summary>
        public float LiveComboPoints
        {
            get { return ChainPoints + CurrentDriftPoints; }
        }

        public DriftScorer(TuningValues tuning)
        {
            _t = tuning;
        }

        public void SetTuning(TuningValues tuning)
        {
            _t = tuning;
        }

        public void Reset()
        {
            IsDrifting = false;
            ChainCount = 0;
            CurrentDriftPoints = 0f;
            ChainPoints = 0f;
            TotalPoints = 0f;
            BestCombo = 0f;
            DriftAngleDeg = 0f;
            _notSlidingTime = 0f;
            _sinceLastDrift = float.PositiveInfinity;
        }

        /// <summary>Advance one step. Returns the points earned this step (used to fill nitro).</summary>
        public float Step(float dt, float driftAngleRad, float speed)
        {
            DriftAngleDeg = driftAngleRad * Mathf.Rad2Deg;
            float absDeg = Mathf.Abs(DriftAngleDeg);
            bool sliding = speed >= _t.drift_min_speed && absDeg >= _t.drift_min_angle_deg;

            if (sliding)
            {
                _notSlidingTime = 0f;
                if (!IsDrifting) StartDrift();
                float gained = absDeg * speed * _t.points_per_degree_meter * dt * Multiplier;
                CurrentDriftPoints += gained;
                return gained;
            }

            if (IsDrifting)
            {
                // A short grace period so an angle flickering around the threshold
                // doesn't count as several separate drifts and farm the multiplier.
                _notSlidingTime += dt;
                if (_notSlidingTime >= _t.drift_end_grace) EndDrift();
                return 0f;
            }

            _sinceLastDrift += dt;
            if (ChainCount > 0 && _sinceLastDrift > _t.combo_window) BankChain();
            return 0f;
        }

        public void WallHit()
        {
            IsDrifting = false;
            CurrentDriftPoints = 0f;
            ChainPoints = 0f;
            ChainCount = 0;
            _notSlidingTime = 0f;
            _sinceLastDrift = float.PositiveInfinity;
        }

        /// <summary>Bank whatever is in progress (end of race).</summary>
        public void Finish()
        {
            if (IsDrifting) EndDrift();
            if (ChainCount > 0) BankChain();
        }

        private void StartDrift()
        {
            if (ChainCount > 0 && _sinceLastDrift <= _t.combo_window)
            {
                ChainCount++;
            }
            else
            {
                if (ChainCount > 0) BankChain();
                ChainCount = 1;
            }
            IsDrifting = true;
            CurrentDriftPoints = 0f;
        }

        private void EndDrift()
        {
            IsDrifting = false;
            ChainPoints += CurrentDriftPoints;
            CurrentDriftPoints = 0f;
            _notSlidingTime = 0f;
            // The grace period already counts as time since the drift ended.
            _sinceLastDrift = _t.drift_end_grace;
        }

        private void BankChain()
        {
            TotalPoints += ChainPoints;
            if (ChainPoints > BestCombo) BestCombo = ChainPoints;
            ChainPoints = 0f;
            ChainCount = 0;
        }
    }
}
