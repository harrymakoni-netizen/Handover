using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>T03: side-by-side hops and docking, tested on the transfer mechanics alone.</summary>
    public class HopTransactionTests
    {
        private static TestRig MechanicsRig()
        {
            return new TestRig(new PermissivePolicy(), TestRig.OpenVariant());
        }

        private static int HomeBerth(DollId doll) => doll == DollId.Keeper ? 0 : doll == DollId.Pip ? 1 : 2;

        [Test]
        public void FortyScriptedHopsAcrossAllDollsAndBothDirections()
        {
            var rig = MechanicsRig();
            int hops = 0;
            int leftToRight = 0;
            int rightToLeft = 0;
            DollId[] order = { DollId.Keeper, DollId.Pip, DollId.Luma };
            int[] perDoll = { 14, 13, 13 };

            for (int d = 0; d < order.Length; d++)
            {
                DollId doll = order[d];
                HandId carrier = d % 2 == 0 ? HandId.Right : HandId.Left;
                rig.Board(carrier, DockId.Departure, HomeBerth(doll), doll);
                DockId[] zones = { DockId.Departure, DockId.LowRefuge, DockId.Harbour, DockId.LanternRefuge };
                for (int i = 0; i < perDoll[d]; i++)
                {
                    long before = rig.Sim.Transfers.IssuedCount;
                    rig.Hop(carrier, doll, rig.HopZone(zones[i % zones.Length]));
                    Assert.That(rig.Sim.Transfers.IssuedCount, Is.EqualTo(before + 1), "exactly one transaction per hop");
                    if (carrier == HandId.Left) leftToRight++; else rightToLeft++;
                    carrier = carrier.Other();
                    hops++;
                }

                rig.Deposit(carrier, doll, DockId.Departure, HomeBerth(doll));
            }

            Assert.That(hops, Is.EqualTo(40));
            Assert.That(leftToRight, Is.GreaterThanOrEqualTo(19));
            Assert.That(rightToLeft, Is.GreaterThanOrEqualTo(19));
            Assert.That(rig.Count(SimEventKind.TransferCommitted, e => e.TransferKind == TransferKind.Handover), Is.EqualTo(40));
            Assert.That(rig.Count(SimEventKind.TransferRolledBack), Is.EqualTo(0));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
            foreach (DollId doll in order)
            {
                Assert.IsTrue(rig.IsAtBerth(doll, DockId.Departure, HomeBerth(doll)));
            }
        }

        [Test]
        public void FullReceiverRejectsHop()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.Board(HandId.Left, DockId.Departure, 1, DollId.Pip);
            rig.ClearEvents();
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            rig.Run(2f);
            Assert.That(rig.Count(SimEventKind.CandidateOpened, e => e.TransferKind == TransferKind.Handover), Is.EqualTo(0));
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.IsTrue(rig.IsOnPalm(DollId.Pip, HandId.Left));
        }

        [Test]
        public void StaleReceiverPoseRejectsHop()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            rig.Hands.Freeze(HandId.Left);
            rig.Run(0.15f);
            rig.ClearEvents();
            rig.Run(2f);
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
        }

        [Test]
        public void OriginMismatchRejectsHop()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            // The left hand keeps reporting the old origin epoch after a recenter.
            rig.Hands.Freeze(HandId.Left);
            rig.Hands.Recenter();
            rig.ClearEvents();
            rig.Run(2f);
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
            Assert.IsFalse(rig.Sim.Palm(HandId.Left).IsTracked);
        }

        [Test]
        public void NoSeamOutsideADryTransferArea()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            // Midway between docks: exposed, not dry.
            Vector3 exposed = TestRig.StageOrigin + new Vector3(0f, 0.14f, 0f);
            rig.SetSupport(HandId.Right, exposed);
            rig.SetSupport(HandId.Left, rig.BesidePalm(exposed, 0.02f, -1f));
            rig.ClearEvents();
            rig.Run(2f);
            Assert.That(rig.Count(SimEventKind.CandidateOpened), Is.EqualTo(0));
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
        }

        [Test]
        public void TiltedReceiverBeyondNormalLimitGetsNoSeam()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone, 0f, -12f);
            // 34 degrees is carry-eligible but 46 degrees from the carrier's normal.
            rig.ParkHand(HandId.Left);
            rig.Settle();
            // Pre-tilt the receiver away from the stage, then bring it alongside already tilted.
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.2f, -1f), 0f, 34f);
            rig.Settle();
            rig.ClearEvents();
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f), 0f, 34f);
            rig.Run(2f);
            Assert.That(rig.Count(SimEventKind.CandidateOpened, e => e.TransferKind == TransferKind.Handover), Is.EqualTo(0));
        }

        [Test]
        public void MovingPairDoesNotAccumulateDwell()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.ClearEvents();
            // Hands side by side but moving apart and together at about 0.3 m/s relative speed.
            for (int i = 0; i < 216; i++)
            {
                float gap = 0.025f + 0.012f * (float)System.Math.Sin(i * 0.5f);
                rig.SetSupport(HandId.Right, zone);
                rig.SetSupport(HandId.Left, rig.BesidePalm(zone, gap, -1f));
                rig.Step();
            }

            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
        }

        [Test]
        public void CancelBeforeTakeoffReleasesBothReservations()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ClearEvents();
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Transfers.Active.Count == 1, 2f));
            Assert.That(rig.Ledger.Get(SlotRef.Palm(HandId.Left)).IsReserved);
            Assert.That(rig.Ledger.Get(SlotRef.Palm(HandId.Right)).IsReserved);

            rig.ParkHand(HandId.Left);
            rig.Step(2);
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.That(rig.Count(SimEventKind.TransferRolledBack, e => e.Code == (int)TransferEndReason.AlignmentLost), Is.EqualTo(1));
        }

        [Test]
        public void SeparationBeforeMidpointReturnsToSource()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ClearEvents();
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            Assert.IsTrue(rig.RunUntil(() => rig.Count(SimEventKind.TransferTakeoff) > 0, 2f));
            rig.Step(4);
            Assert.That(rig.Sim.Transfers.Active[0].Progress, Is.InRange(0.01f, 0.49f));

            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.15f, -1f));
            rig.Run(0.3f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.That(rig.Count(SimEventKind.TransferRolledBack, e => e.Code == (int)TransferEndReason.SeparatedBeforeMidpoint), Is.EqualTo(1));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
        }

        [Test]
        public void SeparationAfterMidpointReturnsDollToLastSafeDock()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ClearEvents();
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Transfers.Active.Count == 1 && rig.Sim.Transfers.Active[0].Progress > 0.6f, 3f));

            // The receiver is pulled far away after the midpoint.
            rig.SetSupport(HandId.Left, zone + new Vector3(-0.4f, 0f, 0f));
            rig.Run(0.3f);
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
            Assert.That(rig.Count(SimEventKind.TransferReturnedToSafety), Is.EqualTo(1));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
            Assert.That(rig.Ledger.PalmIsEmpty(HandId.Left) && rig.Ledger.PalmIsEmpty(HandId.Right));
        }

        [Test]
        public void ReceiverTurnedOverAfterMidpointReturnsToSafety()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ClearEvents();
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            Vector3 receiver = rig.BesidePalm(zone, 0.02f, -1f);
            rig.SetSupport(HandId.Left, receiver);
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Transfers.Active.Count == 1 && rig.Sim.Transfers.Active[0].Progress > 0.55f, 3f));
            rig.SetSupport(HandId.Left, receiver, 0f, 80f);
            rig.Run(0.3f);
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
        }

        [Test]
        public void UnloadDuringHopReleasesReservationsExactlyOnce()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ClearEvents();
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            Assert.IsTrue(rig.RunUntil(() => rig.Count(SimEventKind.TransferTakeoff) > 0, 2f));
            rig.Step(5);

            rig.Sim.Unload();
            rig.AssertInvariants();
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
            Assert.That(rig.Sim.Transfers.Active.Count, Is.EqualTo(0));
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.DoesNotThrow(() => rig.Sim.Unload(), "a second unload has nothing left to release");
        }

        [Test]
        public void DepositAndReboardUseTheSameTransactionSystem()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Left, DockId.Departure, 1, DollId.Pip);
            rig.Deposit(HandId.Left, DollId.Pip, DockId.LowRefuge, 0);
            Assert.That(rig.Ledger.Doll(DollId.Pip).LastSafeDock, Is.EqualTo(SlotRef.DockBerth(DockId.LowRefuge, 0)));
            rig.Board(HandId.Right, DockId.LowRefuge, 0, DollId.Pip);
            rig.Deposit(HandId.Right, DollId.Pip, DockId.Harbour, 1);
            Assert.That(rig.Count(SimEventKind.TransferCommitted, e => e.TransferKind == TransferKind.Boarding), Is.EqualTo(2));
            Assert.That(rig.Count(SimEventKind.TransferCommitted, e => e.TransferKind == TransferKind.Deposit), Is.EqualTo(2));
        }

        [Test]
        public void DepartureAcceptsOnlyTheDollsOwnBerth()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.ParkHand(HandId.Right);
            rig.Settle();
            // Offer Pip at Luma's berth (occupied) and at Keeper's berth (empty but named for Keeper).
            rig.Board(HandId.Left, DockId.Departure, 0, DollId.Keeper);
            rig.ParkHand(HandId.Left);
            rig.ClearEvents();
            // In front of Keeper's empty berth, out of reach of Pip's own berth.
            Vector3 keeperBerth = rig.Berth(DockId.Departure, 0);
            rig.SetSupport(HandId.Right, keeperBerth - new Vector3(0f, 0f, rig.Tuning.SupportHalfLength + rig.Tuning.BerthSupportRadius + 0.015f));
            rig.Run(2f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Pip, HandId.Right), "Pip is not deposited on Keeper's named berth");
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
            rig.Deposit(HandId.Right, DollId.Pip, DockId.Departure, 1);
        }

        [Test]
        public void FullDockRejectsWithACue()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.Deposit(HandId.Right, DollId.Pip, DockId.LowRefuge, 0);
            rig.Board(HandId.Right, DockId.Departure, 2, DollId.Luma);
            rig.ClearEvents();
            rig.OfferPalmAtBerth(HandId.Right, DockId.LowRefuge, 0);
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Luma, HandId.Right));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.DockFull), Is.GreaterThan(0));
            Assert.That(rig.Count(SimEventKind.DockRejected), Is.LessThanOrEqualTo(2), "refusal cues are rate-limited");
        }

        [Test]
        public void ReceiverCooldownPreventsImmediateHopBack()
        {
            var rig = MechanicsRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ClearEvents();
            Vector3 zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Left), 3f));
            Assert.IsTrue(rig.Sim.Transfers.IsInCooldown(HandId.Left));
            // Both hands stay side by side; the doll must not ping-pong straight back,
            // neither during the cooldown nor after it (the pair must separate to re-arm).
            rig.Run(0.45f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Left));
            rig.Run(3f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Left));
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(1));

            // Separate beyond the disconnect gap and rejoin: the hop back is offered again.
            Vector3 left = rig.BesidePalm(zone, 0.02f, -1f);
            rig.SetSupport(HandId.Right, zone + new Vector3(0.12f, 0f, 0f));
            rig.Settle();
            rig.SetSupport(HandId.Right, rig.BesidePalm(left, 0.02f, 1f));
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Right), 3f));
        }

        [Test]
        public void ArcFollowsWorldUpAndEndpoints()
        {
            Vector3 a = new Vector3(0f, 1f, 0f);
            Vector3 b = new Vector3(0.1f, 1f, 0f);
            Assert.That(Vector3.Distance(SupportGeometry.Arc(a, b, 0f, 0.015f), a), Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(SupportGeometry.Arc(a, b, 1f, 0.015f), b), Is.LessThan(1e-6f));
            Assert.That(SupportGeometry.Arc(a, b, 0.5f, 0.015f).Y, Is.EqualTo(1.015f).Within(1e-6f));
        }
    }
}
