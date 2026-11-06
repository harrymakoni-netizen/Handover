using System;
using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    public class MathTests
    {
        private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(tolerance), $"expected {expected} got {actual}");
        }

        [Test]
        public void LookRotationMapsForwardAndUp()
        {
            Vector3 forward = Vector3.Normalize(new Vector3(1f, 0f, 1f));
            Quaternion q = HMath.LookRotation(forward, Vector3.UnitY);
            AssertNear(forward, HMath.Rotate(q, Vector3.UnitZ));
            AssertNear(Vector3.UnitY, HMath.Rotate(q, Vector3.UnitY));
            // Unity convention: right = up x forward.
            AssertNear(Vector3.Cross(Vector3.UnitY, forward), HMath.Rotate(q, Vector3.UnitX));
        }

        [Test]
        public void AngleAxisYawMatchesUnityConvention()
        {
            // Unity: Quaternion.AngleAxis(90, up) * forward == right.
            AssertNear(Vector3.UnitX, HMath.Rotate(HMath.AngleAxis(90f, Vector3.UnitY), Vector3.UnitZ));
            Assert.That(HMath.YawOf(Vector3.UnitX), Is.EqualTo((float)(Math.PI / 2)).Within(1e-5f));
        }

        [Test]
        public void DampingIsFrameRateIndependent()
        {
            float tau = 0.04f;
            float oneStep = HMath.DampingAlpha(0.02f, tau);
            float half = HMath.DampingAlpha(0.01f, tau);
            float twoSteps = 1f - (1f - half) * (1f - half);
            Assert.That(twoSteps, Is.EqualTo(oneStep).Within(1e-5f));
        }

        [Test]
        public void ShortestSlerpNeverTakesTheLongWay()
        {
            Quaternion a = HMath.AngleAxis(10f, Vector3.UnitY);
            Quaternion b = Quaternion.Negate(HMath.AngleAxis(20f, Vector3.UnitY));
            Quaternion mid = HMath.SlerpShortest(a, b, 0.5f);
            Vector3 f = HMath.Rotate(mid, Vector3.UnitZ);
            Assert.That(HMath.YawOf(f) * HMath.Rad2Deg, Is.EqualTo(15f).Within(0.01f));
        }

        [Test]
        public void TuningDefaultsAreConsistent()
        {
            Assert.That(new InteractionTuning().Validate(), Is.Empty);
            Assert.That(new InteractionTuning().WithSteadyMode().Validate(), Is.Empty);
        }
    }

    public class PalmBasisTests
    {
        // Joint layout of a palm-up hand with fingers pointing forward (+Z) in Unity's frame.
        // Right hand palm-up: thumb and index on the right (+X), little finger on the left.
        private static void JointsPalmUp(HandId hand, out Vector3 wrist, out Vector3 middle, out Vector3 index, out Vector3 little)
        {
            float s = hand == HandId.Right ? 1f : -1f;
            wrist = new Vector3(0f, 1f, 0f);
            middle = new Vector3(0f, 1f, 0.09f);
            index = new Vector3(0.02f * s, 1f, 0.085f);
            little = new Vector3(-0.035f * s, 1f, 0.075f);
        }

        [TestCase(HandId.Left)]
        [TestCase(HandId.Right)]
        public void PalmUpJointsGiveUpwardNormal(HandId hand)
        {
            JointsPalmUp(hand, out Vector3 w, out Vector3 m, out Vector3 i, out Vector3 l);
            Assert.IsTrue(PalmBasis.TryFromJointPositions(hand, w, m, i, l, out Pose3 palm));
            Assert.That(HMath.AngleDegrees(palm.Up, Vector3.UnitY), Is.LessThan(1f));
            Assert.That(HMath.AngleDegrees(palm.Forward, Vector3.UnitZ), Is.LessThan(1f));
            Assert.That(palm.Position.Z, Is.EqualTo(0.045f).Within(1e-4f));
        }

        [TestCase(HandId.Left)]
        [TestCase(HandId.Right)]
        public void PalmDownJointsGiveDownwardNormal(HandId hand)
        {
            // Turning the hand palm-down about its forward axis mirrors the knuckles across X.
            JointsPalmUp(hand, out Vector3 w, out Vector3 m, out Vector3 i, out Vector3 l);
            i.X = -i.X;
            l.X = -l.X;
            Assert.IsTrue(PalmBasis.TryFromJointPositions(hand, w, m, i, l, out Pose3 palm));
            Assert.That(HMath.AngleDegrees(palm.Up, -Vector3.UnitY), Is.LessThan(1f));
        }

        [Test]
        public void DegenerateJointsAreRejected()
        {
            Vector3 p = Vector3.One;
            Assert.IsFalse(PalmBasis.TryFromJointPositions(HandId.Right, p, p, p, p, out _));
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(-135f)]
        public void ProviderJointWithPalmarDownMapsToPalmFrame(float yaw)
        {
            // Interaction SDK 207 (OpenXR hand skeleton) documents Palmar = local down and
            // Distal = local forward for both hands. A joint whose palmar side faces the sky
            // therefore has its local +Y pointing at the floor.
            Quaternion joint = HMath.AngleAxis(yaw, Vector3.UnitY) * HMath.AngleAxis(180f, Vector3.UnitZ);
            Pose3 palm = PalmBasis.FromProviderJoint(new Pose3(Vector3.Zero, joint), -Vector3.UnitY, Vector3.UnitZ);
            Assert.That(HMath.AngleDegrees(palm.Up, Vector3.UnitY), Is.LessThan(0.5f));
            Assert.That(HMath.AngleDegrees(palm.Forward, HMath.Rotate(HMath.AngleAxis(yaw, Vector3.UnitY), Vector3.UnitZ)), Is.LessThan(0.5f));
        }

        [Test]
        public void SyntheticPalmUpRollTiltsTheNormal()
        {
            Pose3 p = PalmBasis.PalmUp(Vector3.Zero, 0f, 30f);
            Assert.That(HMath.AngleDegrees(p.Up, Vector3.UnitY), Is.EqualTo(30f).Within(0.01f));
            Assert.That(p.Up.X, Is.GreaterThan(0f));
            Pose3 d = PalmBasis.PalmDown(Vector3.Zero, 40f);
            Assert.That(HMath.AngleDegrees(d.Up, -Vector3.UnitY), Is.LessThan(0.01f));
        }
    }

    public class PalmPlatformTests
    {
        private const float Dt = 1f / 72f;

        private sealed class Driver
        {
            public readonly SyntheticHandPoseProvider Provider = new SyntheticHandPoseProvider();
            public readonly InteractionTuning Tuning = new InteractionTuning();
            public readonly PalmPlatform Palm;
            public PalmChange Changes;
            public double Now;

            public Driver()
            {
                Palm = new PalmPlatform(HandId.Right, Tuning);
            }

            public void Step(bool occupied, float dt = Dt)
            {
                Provider.Tick(dt);
                Now += dt;
                Changes |= Palm.Update(Provider.GetSample(HandId.Right), Provider.Now, Provider.OriginEpoch, Now, dt, dt, occupied);
            }

            public void Run(float seconds, bool occupied, float dt = Dt)
            {
                int n = (int)Math.Ceiling(seconds / dt);
                for (int i = 0; i < n; i++)
                {
                    Step(occupied, dt);
                }
            }
        }

        [Test]
        public void FilterConvergesAtDifferentFrameRates()
        {
            var a = new Driver();
            var b = new Driver();
            a.Provider.SetPalmUp(HandId.Right, Vector3.Zero);
            b.Provider.SetPalmUp(HandId.Right, Vector3.Zero);
            a.Run(0.2f, false, 1f / 72f);
            b.Run(0.2f, false, 1f / 120f);
            // A small move that stays below the lag bound, so pure damping is compared.
            a.Provider.SetPalmUp(HandId.Right, new Vector3(0.012f, 0f, 0f));
            b.Provider.SetPalmUp(HandId.Right, new Vector3(0.012f, 0f, 0f));
            a.Run(0.05f, false, 1f / 72f);
            b.Run(0.05f, false, 1f / 120f);
            float xa = a.Palm.FilteredPose.Position.X;
            float xb = b.Palm.FilteredPose.Position.X;
            Assert.That(Math.Abs(xa - xb), Is.LessThan(0.0008f), $"72 Hz {xa:0.0000} vs 120 Hz {xb:0.0000}");
            Assert.That(xa, Is.GreaterThan(0.005f).And.LessThan(0.012f));
        }

        [Test]
        public void SupportLagIsBoundedAndClingReleases()
        {
            var d = new Driver();
            d.Provider.SetPalmUp(HandId.Right, Vector3.Zero);
            d.Run(0.2f, true);
            d.Changes = PalmChange.None;
            d.Provider.SetPalmUp(HandId.Right, new Vector3(0.2f, 0f, 0f));
            d.Step(true);
            Assert.That(Vector3.Distance(d.Palm.RawPose.Position, d.Palm.FilteredPose.Position), Is.LessThanOrEqualTo(d.Tuning.MaxSupportLag + 1e-5f));
            Assert.IsTrue(d.Palm.Clinging);
            Assert.That(d.Changes & PalmChange.ClingStarted, Is.EqualTo(PalmChange.ClingStarted));
            d.Run(0.3f, true);
            Assert.IsFalse(d.Palm.Clinging);
            Assert.That(d.Changes & PalmChange.ClingEnded, Is.EqualTo(PalmChange.ClingEnded));
        }

        [Test]
        public void CarryEligibilityUsesHysteresis()
        {
            var d = new Driver();
            void Angle(float roll)
            {
                d.Provider.SetPose(HandId.Right, PalmBasis.PalmUp(Vector3.Zero, 0f, roll));
                d.Run(0.5f, false);
            }

            Angle(30f);
            Assert.IsTrue(d.Palm.CarryEligible);
            Angle(42f);
            Assert.IsTrue(d.Palm.CarryEligible, "inside the 35-45 band an eligible palm stays eligible");
            Angle(47f);
            Assert.IsFalse(d.Palm.CarryEligible);
            Angle(40f);
            Assert.IsFalse(d.Palm.CarryEligible, "inside the band an ineligible palm stays ineligible");
            Angle(33f);
            Assert.IsTrue(d.Palm.CarryEligible);
        }

        [Test]
        public void TiltWarningNeedsDwellAndDoesNotChatter()
        {
            var d = new Driver();
            d.Provider.SetPose(HandId.Right, PalmBasis.PalmUp(Vector3.Zero, 0f, 10f));
            d.Run(0.5f, true);
            d.Changes = PalmChange.None;

            d.Provider.SetPose(HandId.Right, PalmBasis.PalmUp(Vector3.Zero, 0f, 28f));
            d.Run(0.15f, true);
            Assert.IsFalse(d.Palm.TiltWarning, "a 0.2 s dwell is required");
            d.Run(0.2f, true);
            Assert.IsTrue(d.Palm.TiltWarning);

            // Noise between 21 and 27 degrees must not toggle the warning.
            int starts = 0;
            int clears = 0;
            for (int i = 0; i < 200; i++)
            {
                float roll = 24f + 3f * (float)Math.Sin(i * 0.7);
                d.Provider.SetPose(HandId.Right, PalmBasis.PalmUp(Vector3.Zero, 0f, roll));
                d.Changes = PalmChange.None;
                d.Step(true);
                if ((d.Changes & PalmChange.TiltWarningStarted) != 0) starts++;
                if ((d.Changes & PalmChange.TiltWarningCleared) != 0) clears++;
            }

            Assert.That(starts + clears, Is.EqualTo(0));
            Assert.IsTrue(d.Palm.TiltWarning);

            d.Provider.SetPose(HandId.Right, PalmBasis.PalmUp(Vector3.Zero, 0f, 15f));
            d.Run(0.3f, true);
            Assert.IsFalse(d.Palm.TiltWarning);
        }

        [Test]
        public void TiltedOccupiedPalmStaysOwnerButIsNotEligible()
        {
            var d = new Driver();
            d.Provider.SetPose(HandId.Right, PalmBasis.PalmUp(Vector3.Zero, 0f, 0f));
            d.Run(0.3f, true);
            d.Provider.SetPose(HandId.Right, PalmBasis.PalmUp(Vector3.Zero, 0f, 120f));
            d.Run(0.5f, true);
            Assert.IsFalse(d.Palm.CarryEligible);
            Assert.IsTrue(d.Palm.TiltWarning);
            Assert.IsFalse(d.Palm.ShieldPose, "an occupied hand turned over never becomes a shield");
        }

        [Test]
        public void ShieldPoseNeedsEmptyPalmDownAndDwell()
        {
            var d = new Driver();
            d.Provider.SetPose(HandId.Right, PalmBasis.PalmDown(Vector3.Zero));
            d.Run(0.1f, false);
            Assert.IsFalse(d.Palm.ShieldPose);
            d.Run(0.2f, false);
            Assert.IsTrue(d.Palm.ShieldPose);
            d.Run(0.1f, true);
            Assert.IsFalse(d.Palm.ShieldPose, "an occupied hand cannot shield");
        }

        [Test]
        public void StaleSamplesBecomeUntracked()
        {
            var d = new Driver();
            d.Provider.SetPalmUp(HandId.Right, Vector3.Zero);
            d.Run(0.2f, false);
            Assert.IsTrue(d.Palm.IsTracked);
            d.Provider.Freeze(HandId.Right);
            d.Run(0.05f, false);
            Assert.IsTrue(d.Palm.IsTracked, "within the stale threshold");
            d.Changes = PalmChange.None;
            d.Run(0.1f, false);
            Assert.IsFalse(d.Palm.IsTracked);
            Assert.IsFalse(d.Palm.CarryEligible);
            Assert.That(d.Changes & PalmChange.TrackingLost, Is.EqualTo(PalmChange.TrackingLost));
        }

        [Test]
        public void RecenterReportsOriginChange()
        {
            var d = new Driver();
            d.Provider.SetPalmUp(HandId.Right, Vector3.Zero);
            d.Run(0.2f, false);
            d.Changes = PalmChange.None;
            d.Provider.Recenter();
            d.Provider.SetPalmUp(HandId.Right, new Vector3(0.5f, 0f, 0f));
            d.Step(false);
            Assert.That(d.Changes & PalmChange.OriginChanged, Is.EqualTo(PalmChange.OriginChanged));
            // Continuity is invalidated: the filter snaps instead of clinging across the reset.
            Assert.That(d.Palm.FilteredPose.Position.X, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.IsFalse(d.Palm.Clinging);
        }
    }
}
