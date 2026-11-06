using System.Collections.Generic;
using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>T08 (logic): cue ownership, priority and rate limits; presentation states.</summary>
    public class CueArbiterTests
    {
        private static readonly SimEvent Tilt = new SimEvent(SimEventKind.TiltWarningStarted, DollId.Pip);
        private static readonly SimEvent Hold = new SimEvent(SimEventKind.SafetyHoldEntered, DollId.Pip);

        private static List<CueRequest> Run(CueArbiter arbiter, SessionState state, double now, params SimEvent[] events)
        {
            var output = new List<CueRequest>();
            arbiter.Process(events, state, now, false, false, FloodPhase.LowWater, output);
            return output;
        }

        private static int CountOf(List<CueRequest> cues, CueId id) => cues.FindAll(c => c.Id == id).Count;

        [Test]
        public void ANoisyThresholdDoesNotChatter()
        {
            var arbiter = new CueArbiter();
            var all = new List<CueRequest>();
            for (int i = 0; i < 144; i++)
            {
                all.AddRange(Run(arbiter, SessionState.Playing, i / 72.0, Tilt));
            }

            Assert.That(CountOf(all, CueId.TiltWarning), Is.EqualTo(2), "one warning per second at most over two seconds");
        }

        [Test]
        public void TrackingRecoveryOutranksTiltWarnings()
        {
            var arbiter = new CueArbiter();
            List<CueRequest> frame = Run(arbiter, SessionState.Playing, 10.0, Tilt, Hold);
            Assert.That(CountOf(frame, CueId.TrackingRecovery), Is.EqualTo(1));
            Assert.That(CountOf(frame, CueId.TiltWarning), Is.EqualTo(0));
            Assert.That(CountOf(Run(arbiter, SessionState.Playing, 10.5, Tilt), CueId.TiltWarning), Is.EqualTo(0), "warnings stay ducked after recovery");
            Assert.That(CountOf(Run(arbiter, SessionState.Playing, 11.2, Tilt), CueId.TiltWarning), Is.EqualTo(1));
        }

        [TestCase(SessionState.Paused)]
        [TestCase(SessionState.Recovering)]
        public void GameplayWarningsStopWhilePausedOrRecovering(SessionState state)
        {
            var arbiter = new CueArbiter();
            var output = new List<CueRequest>();
            arbiter.Process(new[] { Tilt, new SimEvent(SimEventKind.ShelterCue) }, state, 3.0, true, true, FloodPhase.LowWater, output);
            Assert.That(output, Is.Empty);
            Assert.IsFalse(arbiter.TiltLoopActive);
            Assert.IsFalse(arbiter.RainLoopActive);

            arbiter.Process(new SimEvent[0], SessionState.Playing, 4.0, true, true, FloodPhase.LowWater, output);
            Assert.IsTrue(arbiter.TiltLoopActive);
            Assert.IsTrue(arbiter.RainLoopActive);
        }

        [Test]
        public void MenuFeedbackOutranksGameplayCues()
        {
            var arbiter = new CueArbiter();
            var output = new List<CueRequest>();
            Assert.IsTrue(arbiter.RequestMenu(5.0, output));
            var land = new SimEvent(SimEventKind.TransferCommitted, DollId.Pip, code: (int)TransferKind.Deposit);
            Assert.That(CountOf(Run(arbiter, SessionState.Playing, 5.1, land), CueId.LandTap), Is.EqualTo(0));
            Assert.That(CountOf(Run(arbiter, SessionState.Playing, 5.4, land), CueId.LandTap), Is.EqualTo(1));
        }

        [Test]
        public void TheHarbourMelodyBuildsWithEachRescueAndCompletesOnce()
        {
            var rig = new TestRig();
            var arbiter = new CueArbiter();
            var cues = new List<CueRequest>();
            void Collect()
            {
                arbiter.Process(rig.Sim.Events, rig.Sim.Session.State, rig.Sim.Clock.RealTime, false, false, rig.Sim.Flood.Phase, cues);
            }

            // Drive the reference route, feeding the arbiter after every step.
            rig.Sim.ApplySettings(new HandoverSettings());
            ChapterScript.Opening(rig);
            ChapterScript.RescueViaLowRefuge(rig, HandId.Right, DollId.Pip);
            ChapterScript.RescueDirect(rig, HandId.Left, DollId.Luma);
            ChapterScript.BringKeeperHome(rig);

            // Replay the recorded events through the arbiter in order, one step's worth at a time.
            arbiter.Reset();
            double t = 0;
            foreach (SimEvent e in rig.AllEvents)
            {
                t += 0.5;
                arbiter.Process(new[] { e }, SessionState.Playing, t, false, false, FloodPhase.LowWater, cues);
            }

            List<CueRequest> phrases = cues.FindAll(c => c.Id == CueId.RescuePhrase);
            Assert.That(phrases.ConvertAll(c => c.Variant), Is.EqualTo(new List<int> { 1, 2, 3 }));
            Assert.That(CountOf(cues, CueId.CompletionMelody), Is.EqualTo(1));
            Assert.That(CountOf(cues, CueId.SeamConnect), Is.GreaterThanOrEqualTo(1));
            Assert.That(CountOf(cues, CueId.BoardTap), Is.GreaterThanOrEqualTo(4));
            Assert.That(CountOf(cues, CueId.LanternLatched), Is.EqualTo(1));
            Collect();
        }
    }

    public class PresentationTests
    {
        [Test]
        public void ADollLeansAnticipatesHopsAndRides()
        {
            var rig = new TestRig(new PermissivePolicy(), TestRig.OpenVariant());
            Assert.That(PresentationModel.Compute(rig.Sim, DollId.Keeper).Pose, Is.EqualTo(DollPose.Waiting));

            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, 0);
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Resolver.Candidates.Count > 0, 1f));
            rig.Step(5);
            DollPresentation leaning = PresentationModel.Compute(rig.Sim, DollId.Keeper);
            Assert.That(leaning.Pose, Is.EqualTo(DollPose.Anticipating));
            Assert.IsTrue(leaning.HasLookTarget);
            Assert.That(leaning.Lean, Is.GreaterThan(0f));

            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Transfers.Active.Count == 1 && rig.Sim.Transfers.Active[0].HasTakenOff, 2f));
            rig.Step(2);
            DollPresentation hopping = PresentationModel.Compute(rig.Sim, DollId.Keeper);
            Assert.That(hopping.Pose, Is.EqualTo(DollPose.Hopping));
            Assert.That(hopping.HopProgress, Is.GreaterThan(0f));

            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Right), 2f));
            DollPresentation riding = PresentationModel.Compute(rig.Sim, DollId.Keeper);
            Assert.That(riding.Pose, Is.EqualTo(DollPose.Riding));
            Assert.That(Vector3.Distance(riding.Position, rig.Sim.Palm(HandId.Right).SupportCenter), Is.LessThan(1e-5f));
        }

        [Test]
        public void TiltCrouchesAndTheHoldSuspends()
        {
            var rig = new TestRig(new PermissivePolicy(), TestRig.OpenVariant());
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Vector3 carry = TestRig.StageOrigin + new Vector3(0f, 0.25f, 0f);
            rig.SetSupport(HandId.Right, carry, 0f, 30f);
            rig.Run(0.6f);
            DollPresentation tilted = PresentationModel.Compute(rig.Sim, DollId.Keeper);
            Assert.That(tilted.Pose, Is.EqualTo(DollPose.Sheltering));
            Assert.IsTrue(tilted.Crouch);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right), "warnings do not make the passenger slide");

            rig.Hands.Hide(HandId.Right);
            rig.Run(0.3f);
            DollPresentation held = PresentationModel.Compute(rig.Sim, DollId.Keeper);
            Assert.That(held.Pose, Is.EqualTo(DollPose.Suspended));
        }

        [Test]
        public void RescuedPassengersGatherAtTheHarbour()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            Assert.That(PresentationModel.Compute(rig.Sim, DollId.Pip).Pose, Is.EqualTo(DollPose.Rescued));
            Assert.That(PresentationModel.Compute(rig.Sim, DollId.Luma).Pose, Is.EqualTo(DollPose.Waiting));
        }
    }
}
