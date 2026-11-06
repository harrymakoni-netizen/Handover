using System;
using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>T02: palm platforms and palm-to-start.</summary>
    public class BoardingAndStartTests
    {
        private const int KeeperBerth = 0;

        [TestCase(HandId.Left)]
        [TestCase(HandId.Right)]
        public void EitherPalmStartsPlayOnlyAfterCommittedBoarding(HandId hand)
        {
            var rig = new TestRig();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready));

            rig.OfferPalmAtBerth(hand, DockId.Departure, KeeperBerth);
            Assert.IsTrue(rig.RunUntil(() => rig.Count(SimEventKind.TransferTakeoff) > 0, 2f), "boarding should take off");
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready), "take-off is not a commit");

            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, hand), 2f));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
            Assert.That(rig.Count(SimEventKind.ChapterStarted), Is.EqualTo(1));
            Assert.IsTrue(rig.Sim.Session.StartTransaction.IsValid);
            Assert.That(rig.Count(SimEventKind.TransferCommitted, e => e.Transaction == rig.Sim.Session.StartTransaction), Is.EqualTo(1));
        }

        [Test]
        public void AttemptCancelledBeforeTakeoffDoesNotStartPlay()
        {
            var rig = new TestRig();
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, KeeperBerth);
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Transfers.Active.Count > 0, 2f), "boarding reserved");
            Assert.That(rig.Sim.Transfers.Active[0].Stage, Is.EqualTo(TransferStage.Anticipating));

            rig.ParkHand(HandId.Right);
            rig.Run(0.5f);
            Assert.That(rig.Count(SimEventKind.TransferRolledBack), Is.EqualTo(1));
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, KeeperBerth));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(rig.Count(SimEventKind.ChapterStarted), Is.EqualTo(0));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
        }

        [Test]
        public void PalmWithdrawnDuringHopReturnsKeeperAndDoesNotStartPlay()
        {
            var rig = new TestRig();
            rig.OfferPalmAtBerth(HandId.Left, DockId.Departure, KeeperBerth);
            Assert.IsTrue(rig.RunUntil(() => rig.Count(SimEventKind.TransferTakeoff) > 0, 2f));
            rig.Step(3);
            Assert.That(rig.Sim.Transfers.Active[0].Progress, Is.LessThan(0.5f));

            rig.ParkHand(HandId.Left);
            rig.Run(0.3f);
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, KeeperBerth));
            Assert.That(rig.Count(SimEventKind.TransferRolledBack, e => e.Code == (int)TransferEndReason.SeparatedBeforeMidpoint), Is.EqualTo(1));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready));
        }

        [Test]
        public void DuplicateCommitCallbacksDoNotStartPlayTwice()
        {
            var rig = new TestRig();
            TransferTransaction committed = null;
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, KeeperBerth);
            Assert.IsTrue(rig.RunUntil(() =>
            {
                if (rig.Sim.Transfers.CommittedThisStep.Count > 0)
                {
                    committed = rig.Sim.Transfers.CommittedThisStep[0];
                }

                return committed != null;
            }, 3f));

            Assert.That(rig.Sim.Session.ChapterStartCount, Is.EqualTo(1));
            Assert.IsFalse(rig.Sim.Director.OnTransferCommitted(committed), "a repeated callback is ignored");
            Assert.IsFalse(rig.Sim.Session.TryStartChapter(committed.Id));
            Assert.That(rig.Sim.Session.ChapterStartCount, Is.EqualTo(1));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
        }

        [Test]
        public void OnlyKeeperCanBoardInReady()
        {
            var rig = new TestRig();
            // Beside Luma's berth, out of reach of Keeper's: Pip and Luma are refused with a cue.
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, 2);
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsAtBerth(DollId.Pip, DockId.Departure, 1));
            Assert.IsTrue(rig.IsAtBerth(DollId.Luma, DockId.Departure, 2));
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.KeeperFirst), Is.GreaterThan(0));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready));
        }

        [Test]
        public void PalmNearSeveralBerthsBoardsKeeperFirst()
        {
            var rig = new TestRig();
            // Beside Pip's berth but also within reach of Keeper's: Keeper has priority.
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, 1);
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Right), 3f));
            Assert.IsTrue(rig.IsAtBerth(DollId.Pip, DockId.Departure, 1));
            Assert.That(rig.Count(SimEventKind.DockRejected), Is.EqualTo(0), "no refusal cue while a valid boarding is offered");
        }

        [Test]
        public void KeeperDoesNotHopBackWhileThePalmRestsBesideTheDock()
        {
            var rig = new TestRig();
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, KeeperBerth);
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Right), 3f));
            rig.Run(3f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right), "the palm must leave before the dock re-arms");
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(1));
        }

        [Test]
        public void NothingBoardsBeforeTheStageIsConfirmed()
        {
            var rig = new TestRig(confirmStage: false);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Setup));
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, KeeperBerth);
            rig.Run(2f);
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, KeeperBerth));
            Assert.That(rig.Count(SimEventKind.CandidateOpened), Is.EqualTo(0));
        }

        [Test]
        public void ResumeDoesNotRestartAndReplayReturnsToReady()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, KeeperBerth, DollId.Keeper);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));

            Assert.IsTrue(rig.Sim.Session.Pause(PauseReason.Menu));
            rig.Run(0.5f);
            Assert.IsTrue(rig.Sim.Session.Resume());
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
            Assert.That(rig.Sim.Session.ChapterStartCount, Is.EqualTo(1), "Resume must not restart");

            rig.Sim.Replay();
            rig.Step();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready));
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, KeeperBerth));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));

            rig.ParkHand(HandId.Right);
            rig.Settle();
            rig.Board(HandId.Left, DockId.Departure, KeeperBerth, DollId.Keeper);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
            Assert.That(rig.Sim.Session.ChapterStartCount, Is.EqualTo(2), "one start per chapter");
        }

        [Test]
        public void KeeperStaysAttachedThroughAMinuteOfSlowAndAbruptCarrying()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, KeeperBerth, DollId.Keeper);
            rig.ClearEvents();

            // Carry above the stage, clear of every dock, for 60 seconds.
            Vector3 centre = TestRig.StageOrigin + new Vector3(0f, 0.3f, -0.05f);
            int frames = (int)(60f / TestRig.Dt);
            float maxLag = 0f;
            for (int i = 0; i < frames; i++)
            {
                float t = i * TestRig.Dt;
                Vector3 slow = new Vector3(0.12f * (float)Math.Sin(t * 0.8f), 0.04f * (float)Math.Sin(t * 1.3f), 0.06f * (float)Math.Cos(t * 0.6f));
                // An abrupt 8 cm jerk every five seconds.
                Vector3 jerk = (i % 360) < 6 ? new Vector3(0.08f, 0f, 0f) : Vector3.Zero;
                float roll = 15f * (float)Math.Sin(t * 0.9f);
                rig.SetSupport(HandId.Right, centre + slow + jerk, 0f, roll);
                rig.Step();
                PalmPlatform palm = rig.Sim.Palm(HandId.Right);
                maxLag = Math.Max(maxLag, Vector3.Distance(palm.RawPose.Position, palm.FilteredPose.Position));
                Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right), $"Keeper left the palm at t={t:0.00}");
            }

            Assert.That(maxLag, Is.LessThanOrEqualTo(rig.Tuning.MaxSupportLag + 1e-4f));
            Assert.That(rig.Count(SimEventKind.ClingStarted), Is.GreaterThan(0), "abrupt motion produces a cling reaction");
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
        }

        [Test]
        public void ClingingPalmDoesNotStartBoarding()
        {
            var rig = new TestRig();
            // Arrive abruptly and keep jittering beyond the lag bound: no candidate may become stable.
            for (int i = 0; i < 144; i++)
            {
                Vector3 jitter = new Vector3((i % 2 == 0 ? 1f : -1f) * 0.02f, 0f, 0f);
                rig.SetSupport(HandId.Right, rig.BesideBerth(DockId.Departure, KeeperBerth) + jitter);
                rig.Step();
            }

            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, KeeperBerth));
        }
    }
}
