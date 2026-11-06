using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>T04: tracking recovery, the system hold and a usable pause.</summary>
    public class RecoveryAndPauseTests
    {
        private static TestRig Rig() => new TestRig(new PermissivePolicy(), TestRig.OpenVariant());

        /// <summary>Keeper rides the right palm; the left palm waits beside it in a hop zone.</summary>
        private static Vector3 KeeperOnRightWithLeftBeside(TestRig rig, out Vector3 receiver)
        {
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ClearEvents();
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            receiver = rig.BesidePalm(zone, 0.02f, -1f);
            rig.SetSupport(HandId.Left, receiver);
            return zone;
        }

        private static void RunUntilProgress(TestRig rig, float minProgress, float maxProgress)
        {
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Transfers.Active.Count == 1
                && rig.Sim.Transfers.Active[0].HasTakenOff
                && rig.Sim.Transfers.Active[0].Progress >= minProgress, 3f), "hop did not reach the requested progress");
            Assert.That(rig.Sim.Transfers.Active[0].Progress, Is.LessThan(maxProgress));
        }

        [Test]
        public void ReceiverLostDuringDwellLosesNothing()
        {
            var rig = Rig();
            KeeperOnRightWithLeftBeside(rig, out _);
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Resolver.Candidates.Count == 1, 1f));
            rig.Hands.Hide(HandId.Left);
            rig.Run(1f);
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing), "an unused empty hand may be absent");
        }

        [Test]
        public void ReceiverLostDuringAnticipationRollsBack()
        {
            var rig = Rig();
            KeeperOnRightWithLeftBeside(rig, out _);
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Transfers.Active.Count == 1, 2f));
            Assert.IsFalse(rig.Sim.Transfers.Active[0].HasTakenOff);
            rig.Hands.Hide(HandId.Left);
            rig.Run(0.5f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
        }

        [Test]
        public void ReceiverLostBeforeMidpointFreezesThenReturnsToSource()
        {
            var rig = Rig();
            KeeperOnRightWithLeftBeside(rig, out _);
            RunUntilProgress(rig, 0.1f, 0.5f);
            rig.Hands.Hide(HandId.Left);
            rig.Step(3);
            TransferTransaction tx = rig.Sim.Transfers.Active[0];
            Assert.IsTrue(tx.ProgressFrozen, "progress stops while the receiver is untracked");
            float frozenAt = tx.Progress;
            rig.Step(2);
            Assert.That(tx.Progress, Is.EqualTo(frozenAt));

            rig.Run(0.3f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.That(rig.Count(SimEventKind.TransferRolledBack, e => e.Code == (int)TransferEndReason.TrackingLost), Is.EqualTo(1));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
        }

        [Test]
        public void ReceiverLostAfterMidpointHoldsThenBlendsBackWhenItReturns()
        {
            var rig = Rig();
            KeeperOnRightWithLeftBeside(rig, out Vector3 receiver);
            RunUntilProgress(rig, 0.6f, 1f);
            rig.Hands.Hide(HandId.Left);
            rig.Run(0.3f);
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Recovering));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));

            double gameplayTime = rig.Sim.Clock.GameplayTime;
            rig.Run(1f);
            Assert.That(rig.Sim.Clock.GameplayTime, Is.EqualTo(gameplayTime), "gameplay time is frozen while recovering");

            // The receiver comes back where the hold is (within 5 cm) and stays for 0.25 s.
            rig.SetSupport(HandId.Left, receiver + new Vector3(0.01f, 0f, 0.01f));
            rig.Step(5);
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold, "needs 0.25 s of stable tracking first");
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Left), 1f));
            Assert.That(rig.Count(SimEventKind.SafetyHoldBlendedBack), Is.EqualTo(1));
            rig.Step();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
            Assert.That(rig.Sim.Director.State.ExposureResets, Is.EqualTo(0));
        }

        [Test]
        public void CarrierLostFarAwayNeedsConfirmationAndReturnsToSafety()
        {
            var rig = Rig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Vector3 carry = TestRig.StageOrigin + new Vector3(0f, 0.2f, 0f);
            rig.SetSupport(HandId.Right, carry);
            rig.Settle();
            rig.Hands.Hide(HandId.Right);
            rig.Run(0.1f);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing), "the last good support is kept for 0.15 s");
            rig.Run(0.1f);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Recovering));
            Vector3 held = rig.Ledger.Doll(DollId.Keeper).HoldPosition;
            Assert.That(Vector3.Distance(held, carry), Is.LessThan(0.02f), "the system rescue marker stays where the doll was");

            rig.SetSupport(HandId.Right, carry + new Vector3(0.2f, 0f, 0f));
            rig.Run(1f);
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold, "never snap a doll across the room to a reappearing palm");

            Assert.IsTrue(rig.Sim.ConfirmRecovery());
            rig.Step();
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
            Assert.That(rig.Sim.Director.State.ExposureResets, Is.EqualTo(0), "tracking recovery is never a failed rescue");
        }

        [Test]
        public void CarrierLostBeforeMidpointIsHeldAndBlendsBackToTheCarrier()
        {
            var rig = Rig();
            Vector3 zone = KeeperOnRightWithLeftBeside(rig, out _);
            RunUntilProgress(rig, 0.1f, 0.5f);
            rig.Hands.Hide(HandId.Right);
            rig.Run(0.3f);
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Recovering));
            // The receiver must not pick the held doll up: the hold is not a player-accessible refuge.
            rig.Run(1f);
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold);

            rig.SetSupport(HandId.Right, zone);
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Right), 1.5f));
            rig.Step();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
        }

        [Test]
        public void BoardingPalmLostAfterMidpointReturnsKeeperToTheDock()
        {
            var rig = new TestRig();
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, 0);
            RunUntilProgress(rig, 0.6f, 1f);
            rig.Hands.Hide(HandId.Right);
            rig.Run(0.4f);
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready), "a failed boarding does not start play");
        }

        [Test]
        public void RecenterWhileCarryingRequiresConfirmation()
        {
            var rig = Rig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Vector3 carry = TestRig.StageOrigin + new Vector3(0f, 0.2f, 0f);
            rig.SetSupport(HandId.Right, carry);
            rig.Settle();
            rig.Hands.Recenter();
            rig.Step();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Recovering));
            Assert.That(rig.Sim.Session.RecoveryReason, Is.EqualTo(RecoveryReason.OriginChanged));
            Assert.IsTrue(rig.Sim.RecoveryNeedsConfirmation);
            rig.Run(1f);
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold, "no blend back across an origin reset");
            rig.Sim.ConfirmRecovery();
            rig.Step();
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
        }

        [Test]
        public void RecenterDuringHopInvalidatesTheConnection()
        {
            var rig = Rig();
            KeeperOnRightWithLeftBeside(rig, out _);
            RunUntilProgress(rig, 0.1f, 0.5f);
            rig.Hands.Recenter();
            rig.Step();
            Assert.That(rig.Sim.Transfers.Active.Count, Is.EqualTo(0));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Recovering));
        }

        [Test]
        public void PauseWhileHoldingSecuresTheDollFreezesTheClockAndKeepsHandsTracked()
        {
            var rig = Rig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Vector3 carry = TestRig.StageOrigin + new Vector3(0f, 0.2f, 0f);
            rig.SetSupport(HandId.Right, carry);
            rig.Settle();

            Assert.IsTrue(rig.Sim.Pause(PauseReason.Menu));
            rig.Step();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Paused));
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold);

            double gameplay = rig.Sim.Clock.GameplayTime;
            double real = rig.Sim.Clock.RealTime;
            // The other hand operates the menu: hands keep tracking and filtering while paused.
            Vector3 menuPoke = TestRig.StageOrigin + new Vector3(-0.2f, 0.35f, -0.2f);
            rig.SetSupport(HandId.Left, menuPoke);
            rig.Run(2f);
            Assert.That(rig.Sim.Clock.GameplayTime, Is.EqualTo(gameplay));
            Assert.That(rig.Sim.Clock.RealTime, Is.GreaterThan(real + 1.9));
            Assert.IsTrue(rig.Sim.Palm(HandId.Left).IsTracked);
            Assert.That(Vector3.Distance(rig.Sim.Palm(HandId.Left).SupportCenter, menuPoke), Is.LessThan(0.005f));

            Assert.IsTrue(rig.Sim.Resume());
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Right), 1f), "the carrying hand never moved, so the doll blends back");
            rig.Step();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
            Assert.That(rig.Sim.Session.ChapterStartCount, Is.EqualTo(1), "Resume never restarts the chapter");
        }

        [Test]
        public void PauseBeforeMidpointRollsBackAndAfterMidpointLands()
        {
            var rig = Rig();
            KeeperOnRightWithLeftBeside(rig, out _);
            RunUntilProgress(rig, 0.1f, 0.5f);
            rig.Sim.Pause(PauseReason.Menu);
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold, "back on the carrier, then secured");
            rig.Sim.Resume();
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Right), 1f));

            rig.ParkHand(HandId.Left);
            rig.Settle();
            rig.ClearEvents();
            KeeperOnRightLeftBesideAgain(rig);
            RunUntilProgress(rig, 0.6f, 1f);
            rig.Sim.Pause(PauseReason.Menu);
            Assert.That(rig.Count(SimEventKind.TransferCommitted, e => e.TransferKind == TransferKind.Handover), Is.EqualTo(0));
            rig.Step();
            Assert.That(rig.Count(SimEventKind.TransferCommitted, e => e.TransferKind == TransferKind.Handover), Is.EqualTo(1),
                "a hop past its midpoint onto a safe receiver lands");
            Assert.IsTrue(rig.Ledger.Doll(DollId.Keeper).IsInSafetyHold);
            Assert.That(rig.Ledger.Doll(DollId.Keeper).HoldReturnHand, Is.EqualTo(HandId.Left));
        }

        private static void KeeperOnRightLeftBesideAgain(TestRig rig)
        {
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
        }

        [Test]
        public void FocusLossPausesAndResumeWaitsForFocusAndConfirmation()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.Sim.NotifyFocus(false);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Paused));
            Assert.That(rig.Sim.Session.PauseReason, Is.EqualTo(PauseReason.FocusLost));
            Assert.IsFalse(rig.Sim.Resume(), "cannot resume without focus");
            rig.Sim.NotifyFocus(true);
            rig.Run(0.5f);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Paused), "regaining focus does not auto-resume");
            Assert.IsTrue(rig.Sim.Resume());
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Session.State == SessionState.Playing, 1f));
            Assert.That(rig.Sim.Session.ChapterStartCount, Is.EqualTo(1));
        }

        [Test]
        public void HeadsetRemovalPausesUntilMountedAndConfirmed()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.Sim.NotifyHeadsetMounted(false);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Paused));
            Assert.IsFalse(rig.Sim.Resume());
            rig.Sim.NotifyHeadsetMounted(true);
            Assert.IsTrue(rig.Sim.Resume());
        }

        [Test]
        public void ShieldingHandLossEntersRecoveryInsteadOfFailure()
        {
            var rig = Rig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            bool engaged = true;
            rig.Sim.ShieldEngaged = hand => hand == HandId.Left && engaged;
            rig.SetPalmDown(HandId.Left, TestRig.StageOrigin + new Vector3(0f, 0.25f, 0.05f));
            rig.Settle();
            rig.Hands.Hide(HandId.Left);
            rig.Run(0.3f);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Recovering));
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right), "the carried doll stays with its tracked hand");
            rig.SetPalmDown(HandId.Left, TestRig.StageOrigin + new Vector3(0f, 0.25f, 0.05f));
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Session.State == SessionState.Playing, 1f));
            Assert.That(rig.Sim.Director.State.ExposureResets, Is.EqualTo(0));
        }
    }
}
