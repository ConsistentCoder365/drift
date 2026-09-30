using Drift.Core;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class DriftPhysicsTests
    {
        private const float Eps = 1e-4f;
        private static readonly Vector3 F = Vector3.forward;
        private static readonly Vector3 R = Vector3.right;
        private static readonly Vector3 U = Vector3.up;

        private static void AssertVec(Vector3 expected, Vector3 actual, float eps = Eps)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(eps), "x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(eps), "y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(eps), "z");
        }

        [Test]
        public void Decompose_SplitsIntoForwardAndLateralParts_ThatSumBack()
        {
            var v = new Vector3(3f, 0f, 4f);
            DriftPhysics.Decompose(v, F, R, out Vector3 fwd, out Vector3 lat);
            AssertVec(new Vector3(0f, 0f, 4f), fwd);
            AssertVec(new Vector3(3f, 0f, 0f), lat);
            AssertVec(v, fwd + lat);
        }

        [Test]
        public void Decompose_WorksForARotatedCar()
        {
            Quaternion q = Quaternion.Euler(0f, 30f, 0f);
            Vector3 f = q * Vector3.forward, r = q * Vector3.right;
            Vector3 v = f * 10f + r * 2f;
            DriftPhysics.Decompose(v, f, r, out Vector3 fwd, out Vector3 lat);
            AssertVec(f * 10f, fwd);
            AssertVec(r * 2f, lat);
        }

        [Test]
        public void ApplyGrip_KeepsForwardSpeed_AndDecaysLateralExponentially()
        {
            var v = new Vector3(5f, 0f, 20f);
            Vector3 result = DriftPhysics.ApplyGrip(v, F, R, 4f, 0.1f);
            AssertVec(new Vector3(5f * Mathf.Exp(-0.4f), 0f, 20f), result);
        }

        [Test]
        public void ApplyGrip_WithZeroGrip_ChangesNothing()
        {
            var v = new Vector3(5f, 0f, 20f);
            AssertVec(v, DriftPhysics.ApplyGrip(v, F, R, 0f, 0.02f));
        }

        [Test]
        public void ApplyGrip_IsTimestepIndependent()
        {
            var v = new Vector3(8f, 0f, 15f);
            Vector3 oneBigStep = DriftPhysics.ApplyGrip(v, F, R, 6f, 0.04f);
            Vector3 twoSmallSteps = DriftPhysics.ApplyGrip(DriftPhysics.ApplyGrip(v, F, R, 6f, 0.02f), F, R, 6f, 0.02f);
            AssertVec(oneBigStep, twoSmallSteps, 1e-5f);

            // 50 Hz for one second equals 25 Hz for one second.
            Vector3 at50 = v, at25 = v;
            for (int i = 0; i < 50; i++) at50 = DriftPhysics.ApplyGrip(at50, F, R, 6f, 0.02f);
            for (int i = 0; i < 25; i++) at25 = DriftPhysics.ApplyGrip(at25, F, R, 6f, 0.04f);
            AssertVec(at50, at25, 1e-4f);
        }

        [Test]
        public void ApplyGrip_LeavesVerticalVelocityAlone()
        {
            var v = new Vector3(2f, -3f, 10f);
            Vector3 result = DriftPhysics.ApplyGrip(v, F, R, 50f, 1f);
            Assert.That(result.y, Is.EqualTo(-3f).Within(Eps));
        }

        [Test]
        public void KeepMomentum_FullKeepPreservesSpeed_ZeroKeepIsPlainGrip()
        {
            var before = new Vector3(6f, 0f, 20f);
            Vector3 after = DriftPhysics.ApplyGrip(before, F, R, 10f, 0.1f);
            Assert.That(DriftPhysics.KeepMomentum(before, after, F, 1f).magnitude, Is.EqualTo(before.magnitude).Within(Eps));
            AssertVec(after, DriftPhysics.KeepMomentum(before, after, F, 0f));

            // Direction always comes from the gripped velocity.
            Vector3 kept = DriftPhysics.KeepMomentum(before, after, F, 0.5f);
            AssertVec(after.normalized, kept.normalized);
        }

        [Test]
        public void KeepMomentum_DoesNothingWhenMovingBackwards()
        {
            var before = new Vector3(1f, 0f, -5f);
            Vector3 after = new Vector3(0.5f, 0f, -5f);
            AssertVec(after, DriftPhysics.KeepMomentum(before, after, F, 1f));
        }

        [TestCase(0f, 0f, 1f, 0f)]
        [TestCase(1f, 0f, 0f, 90f)]
        [TestCase(-1f, 0f, 0f, -90f)]
        [TestCase(1f, 0f, 1f, 45f)]
        [TestCase(-1f, 0f, 1f, -45f)]
        [TestCase(0f, 0f, -1f, 180f)]
        public void DriftAngle_IsSignedAngleFromNoseToVelocity(float vx, float vy, float vz, float expectedDeg)
        {
            float deg = DriftPhysics.DriftAngle(F, new Vector3(vx, vy, vz), U) * Mathf.Rad2Deg;
            Assert.That(Mathf.Abs(deg), Is.EqualTo(Mathf.Abs(expectedDeg)).Within(1e-3f));
            if (Mathf.Abs(expectedDeg) < 179f) Assert.That(deg, Is.EqualTo(expectedDeg).Within(1e-3f));
        }

        [Test]
        public void DriftAngle_DoesNotDependOnSpeedOrHeading()
        {
            float slow = DriftPhysics.DriftAngle(F, new Vector3(1f, 0f, 2f), U);
            float fast = DriftPhysics.DriftAngle(F, new Vector3(10f, 0f, 20f), U);
            Assert.That(fast, Is.EqualTo(slow).Within(1e-5f));

            Quaternion q = Quaternion.Euler(0f, 123f, 0f);
            float rotated = DriftPhysics.DriftAngle(q * F, q * new Vector3(1f, 0f, 2f), U);
            Assert.That(rotated, Is.EqualTo(slow).Within(1e-5f));
        }

        [Test]
        public void DriftAngle_IsZeroWhenStopped()
        {
            Assert.That(DriftPhysics.DriftAngle(F, Vector3.zero, U), Is.EqualTo(0f));
        }

        [Test]
        public void DriveAcceleration_FadesToZeroAtTopSpeed()
        {
            Assert.That(DriftPhysics.DriveAcceleration(0f, 40f, 10f, 1f), Is.EqualTo(10f).Within(Eps));
            Assert.That(DriftPhysics.DriveAcceleration(20f, 40f, 10f, 1f), Is.EqualTo(5f).Within(Eps));
            Assert.That(DriftPhysics.DriveAcceleration(40f, 40f, 10f, 1f), Is.EqualTo(0f).Within(Eps));
            Assert.That(DriftPhysics.DriveAcceleration(50f, 40f, 10f, 1f), Is.LessThan(0f), "above top speed it slows down");
            Assert.That(DriftPhysics.DriveAcceleration(0f, 40f, 10f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void TargetYawRate_ScalesWithSpeed_SoTheCarCannotSpinInPlace()
        {
            var t = new TuningValues();
            Assert.That(DriftPhysics.TargetYawRate(1f, 0f, false, t), Is.EqualTo(0f));
            float half = DriftPhysics.TargetYawRate(1f, t.steer_full_speed * 0.5f, false, t);
            float full = DriftPhysics.TargetYawRate(1f, t.steer_full_speed * 2f, false, t);
            Assert.That(half, Is.EqualTo(t.max_yaw_rate * 0.5f).Within(Eps));
            Assert.That(full, Is.EqualTo(t.max_yaw_rate).Within(Eps));
            Assert.That(DriftPhysics.TargetYawRate(-1f, 30f, false, t), Is.EqualTo(-t.max_yaw_rate).Within(Eps));
            Assert.That(DriftPhysics.TargetYawRate(1f, 30f, true, t), Is.EqualTo(t.max_yaw_rate * t.drift_yaw_boost).Within(Eps));
        }

        [Test]
        public void StepVelocity_NeverExceedsTheValidatedSpeedCap()
        {
            var t = new TuningValues();
            var input = new CarInput { Throttle = 1f, Nitro = true };
            Vector3 v = F * 200f; // e.g. after a physics impulse
            Vector3 result = DriftPhysics.StepVelocity(v, F, R, t.grip_normal, input, t, 0.02f);
            Assert.That(result.magnitude, Is.LessThanOrEqualTo(t.nitro_top_speed * 1.05f + Eps));
        }

        [Test]
        public void StepVelocity_AcceleratesTowardTopSpeed()
        {
            var t = new TuningValues();
            var input = new CarInput { Throttle = 1f };
            Vector3 v = Vector3.zero;
            for (int i = 0; i < 50 * 20; i++) v = DriftPhysics.StepVelocity(v, F, R, t.grip_normal, input, t, 0.02f);
            Assert.That(v.magnitude, Is.EqualTo(t.top_speed).Within(0.5f));
        }

        // A tiny planar car simulation using the same functions CarController calls.
        private static float SimulateMaxDriftAngle(bool drift, out float speedKeptFraction)
        {
            var t = new TuningValues();
            const float dt = 0.02f;
            float heading = 0f, yawRate = 0f, grip = t.grip_normal;
            Vector3 v = new Vector3(0f, 0f, 30f);
            float startSpeed = v.magnitude, maxAngle = 0f;
            var input = new CarInput { Steer = 1f, Throttle = 1f, Drift = drift };
            for (int i = 0; i < 75; i++) // 1.5 s of full steer
            {
                Quaternion q = Quaternion.Euler(0f, heading * Mathf.Rad2Deg, 0f);
                Vector3 f = q * Vector3.forward, r = q * Vector3.right;
                grip = drift ? t.grip_drift : Damping.Damp(grip, t.grip_normal, t.grip_recovery, dt);
                v = DriftPhysics.StepVelocity(v, f, r, grip, input, t, dt);
                float target = DriftPhysics.TargetYawRate(input.Steer, Vector3.Dot(v, f), drift, t);
                yawRate += DriftPhysics.YawAcceleration(yawRate, target, t.yaw_response) * dt;
                heading += yawRate * dt;
                maxAngle = Mathf.Max(maxAngle, Mathf.Abs(DriftPhysics.DriftAngle(f, v, Vector3.up)) * Mathf.Rad2Deg);
            }
            speedKeptFraction = v.magnitude / startSpeed;
            return maxAngle;
        }

        [Test]
        public void Tuning_NormalCorneringIsNotADrift()
        {
            float angle = SimulateMaxDriftAngle(false, out _);
            Assert.That(angle, Is.LessThan(new TuningValues().drift_min_angle_deg), "grip driving should stay under the drift threshold");
        }

        [Test]
        public void Tuning_HoldingDriftSlidesWithoutStallingTheCar()
        {
            float angle = SimulateMaxDriftAngle(true, out float speedKept);
            Assert.That(angle, Is.GreaterThan(25f), "holding Drift should swing the tail out");
            Assert.That(angle, Is.LessThan(90f), "but not spin the car");
            Assert.That(speedKept, Is.GreaterThan(0.6f), "a drift should cost some speed, not all of it");
        }

        [Test]
        public void Damping_FactorComposesAcrossSteps()
        {
            float twoSteps = 1f - (1f - Damping.Factor(5f, 0.01f)) * (1f - Damping.Factor(5f, 0.01f));
            Assert.That(twoSteps, Is.EqualTo(Damping.Factor(5f, 0.02f)).Within(1e-6f));
            Assert.That(Damping.Damp(0f, 10f, 1000f, 1f), Is.EqualTo(10f).Within(1e-3f));
            Assert.That(Damping.Damp(0f, 10f, 3f, 0f), Is.EqualTo(0f));
        }
    }
}
