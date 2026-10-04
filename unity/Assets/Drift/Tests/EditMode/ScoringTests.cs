using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class DriftScorerTests
    {
        private const float Dt = 0.02f;
        private TuningValues _t;
        private DriftScorer _s;

        [SetUp]
        public void SetUp()
        {
            _t = new TuningValues();
            _s = new DriftScorer(_t);
        }

        private void Slide(float seconds, float deg, float speed)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++) _s.Step(Dt, deg * Mathf.Deg2Rad, speed);
        }

        private void Grip(float seconds)
        {
            Slide(seconds, 0f, 30f);
        }

        [Test]
        public void NoPoints_BelowTheAngleOrSpeedThreshold()
        {
            Slide(1f, _t.drift_min_angle_deg - 1f, 30f);
            Slide(1f, 40f, _t.drift_min_speed - 1f);
            Assert.That(_s.LiveComboPoints, Is.EqualTo(0f));
            Assert.That(_s.IsDrifting, Is.False);
        }

        [Test]
        public void Points_BuildAtAngleTimesSpeedPerSecond()
        {
            float gained = _s.Step(1f, 30f * Mathf.Deg2Rad, 20f);
            Assert.That(gained, Is.EqualTo(30f * 20f * _t.points_per_degree_meter).Within(1e-2f));
            Assert.That(_s.IsDrifting, Is.True);
            Assert.That(_s.Multiplier, Is.EqualTo(1));
        }

        [Test]
        public void NegativeAnglesScoreTheSameAsPositive()
        {
            float left = _s.Step(Dt, -30f * Mathf.Deg2Rad, 20f);
            _s.Reset();
            float right = _s.Step(Dt, 30f * Mathf.Deg2Rad, 20f);
            Assert.That(left, Is.EqualTo(right).Within(1e-5f));
        }

        [Test]
        public void DriftsWithinTheComboWindow_ChainAndRaiseTheMultiplier()
        {
            Slide(1f, 30f, 25f);
            Grip(0.6f);
            Slide(1f, 30f, 25f);
            Assert.That(_s.ChainCount, Is.EqualTo(2));
            Assert.That(_s.Multiplier, Is.EqualTo(2));
            Grip(0.6f);
            Slide(0.5f, 30f, 25f);
            Assert.That(_s.Multiplier, Is.EqualTo(3));
        }

        [Test]
        public void SecondDriftOfAChain_ScoresDouble()
        {
            Slide(1f, 30f, 25f);
            Grip(0.5f);
            float before = _s.LiveComboPoints;
            Slide(1f, 30f, 25f);
            float second = _s.LiveComboPoints - before;
            Assert.That(second, Is.EqualTo(before * 2f).Within(before * 0.05f));
        }

        [Test]
        public void ChainIsBanked_WhenTheWindowRunsOut()
        {
            Slide(1f, 30f, 25f);
            float combo = _s.LiveComboPoints;
            Grip(_t.combo_window + 0.5f);
            Assert.That(_s.ChainCount, Is.EqualTo(0));
            Assert.That(_s.TotalPoints, Is.EqualTo(combo).Within(1e-3f));
            Assert.That(_s.BestCombo, Is.EqualTo(combo).Within(1e-3f));

            Slide(1f, 30f, 25f);
            Assert.That(_s.Multiplier, Is.EqualTo(1), "a new chain starts at x1");
        }

        [Test]
        public void WallHit_LosesTheChainButKeepsBankedPoints()
        {
            Slide(1f, 30f, 25f);
            Grip(_t.combo_window + 0.5f);
            float banked = _s.TotalPoints;
            Slide(1f, 30f, 25f);
            Grip(0.4f);
            Slide(0.5f, 30f, 25f);
            _s.WallHit();
            Assert.That(_s.LiveComboPoints, Is.EqualTo(0f));
            Assert.That(_s.ChainCount, Is.EqualTo(0));
            Assert.That(_s.TotalPoints, Is.EqualTo(banked).Within(1e-3f));
        }

        [Test]
        public void AngleFlickeringUnderTheGracePeriod_IsOneDrift()
        {
            for (int i = 0; i < 20; i++)
            {
                Slide(0.1f, 30f, 25f);
                Slide(0.04f, 2f, 25f); // shorter than drift_end_grace
            }
            Assert.That(_s.ChainCount, Is.EqualTo(1), "flicker must not farm the multiplier");
        }

        [Test]
        public void MultiplierIsCapped()
        {
            for (int i = 0; i < 10; i++)
            {
                Slide(0.4f, 30f, 25f);
                Grip(0.4f);
            }
            Assert.That(_s.Multiplier, Is.EqualTo(_t.combo_max_multiplier));
        }

        [Test]
        public void Finish_BanksTheDriftInProgress()
        {
            Slide(1f, 30f, 25f);
            float live = _s.LiveComboPoints;
            _s.Finish();
            Assert.That(_s.TotalPoints, Is.EqualTo(live).Within(1e-3f));
            Assert.That(_s.BestCombo, Is.EqualTo(live).Within(1e-3f));
        }
    }

    public class NitroMeterTests
    {
        [Test]
        public void FillsFromPoints_AndClampsAtFull()
        {
            var t = new TuningValues();
            var n = new NitroMeter(t);
            n.AddFromPoints(100f);
            Assert.That(n.Fill, Is.EqualTo(100f * t.nitro_gain_per_point).Within(1e-5f));
            n.Add(10f);
            Assert.That(n.Fill, Is.EqualTo(1f));
        }

        [Test]
        public void Firing_SpendsTheMeterForAProportionalBoost()
        {
            var t = new TuningValues();
            var n = new NitroMeter(t);
            n.Add(0.5f);
            Assert.That(n.TryFire(), Is.True);
            Assert.That(n.Fill, Is.EqualTo(0f));
            Assert.That(n.IsBoosting, Is.True);
            Assert.That(n.BoostRemaining, Is.EqualTo(0.5f * t.nitro_full_duration).Within(1e-5f));
            Assert.That(n.TryFire(), Is.False, "can't stack boosts");

            for (int i = 0; i < 200 && n.IsBoosting; i++) n.Tick(0.02f);
            Assert.That(n.IsBoosting, Is.False);
        }

        [Test]
        public void CannotFireBelowTheMinimum()
        {
            var t = new TuningValues();
            var n = new NitroMeter(t);
            n.Add(t.nitro_min_to_fire * 0.5f);
            Assert.That(n.TryFire(), Is.False);
            Assert.That(n.IsBoosting, Is.False);
        }
    }
}
