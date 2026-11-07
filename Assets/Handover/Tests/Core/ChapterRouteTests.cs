using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>Scripted chapter actions on the real Standard layout with the real chapter rules.</summary>
    public static class ChapterScript
    {
        /// <summary>A hop spot in a dock's dry area, raised clear of its berths.</summary>
        public static Vector3 SpotAbove(TestRig rig, DockId dock)
        {
            return rig.DockCenter(dock) + new Vector3(0f, 0.10f, -0.04f);
        }

        public static void HopAt(TestRig rig, HandId from, DollId doll, DockId dock)
        {
            rig.Hop(from, doll, SpotAbove(rig, dock));
        }

        public static int NearestFreeHarbourBerth(TestRig rig)
        {
            for (int b = 0; b < rig.Ledger.BerthCount(DockId.Harbour); b++)
            {
                if (rig.Ledger.IsEmpty(SlotRef.DockBerth(DockId.Harbour, b)))
                {
                    return b;
                }
            }

            Assert.Fail("Harbour is full.");
            return -1;
        }

        public static void DepositHome(TestRig rig, HandId hand, DollId doll)
        {
            rig.Deposit(hand, doll, DockId.Harbour, NearestFreeHarbourBerth(rig));
        }

        /// <summary>Opening beats: palm-start, the teaching hop, Keeper lights U.</summary>
        public static void Opening(TestRig rig)
        {
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
            HopAt(rig, HandId.Right, DollId.Keeper, DockId.Departure);
            Assert.IsTrue(rig.Sim.Director.State.TutorialHopDone);
            rig.Deposit(HandId.Left, DollId.Keeper, DockId.LanternRefuge, 0);
            Assert.IsTrue(rig.Sim.Director.LanternLit);
            Assert.That(rig.Sim.Flood.Phase, Is.EqualTo(FloodPhase.LowWater));
        }

        public static int Berth(DollId doll) => doll == DollId.Pip ? 1 : 2;

        public static void RescueDirect(TestRig rig, HandId hand, DollId doll)
        {
            rig.Board(hand, DockId.Departure, Berth(doll), doll);
            DepositHome(rig, hand, doll);
        }

        public static void RescueViaLowRefuge(TestRig rig, HandId hand, DollId doll)
        {
            rig.Board(hand, DockId.Departure, Berth(doll), doll);
            rig.Deposit(hand, doll, DockId.LowRefuge, 0);
            rig.Board(hand, DockId.LowRefuge, 0, doll);
            DepositHome(rig, hand, doll);
        }

        public static void BringKeeperHome(TestRig rig)
        {
            rig.Board(HandId.Right, DockId.LanternRefuge, 0, DollId.Keeper);
            DepositHome(rig, HandId.Right, DollId.Keeper);
        }

        public static void AssertCompleted(TestRig rig, int expectedStops)
        {
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Results));
            Assert.IsTrue(rig.Sim.Director.State.Completed);
            Assert.That(rig.Sim.Director.State.Rescued.Count, Is.EqualTo(3));
            Assert.That(rig.Count(SimEventKind.ChapterCompleted), Is.EqualTo(1));
            Assert.That(rig.Sim.Director.Results.LowRefugeStops, Is.EqualTo(expectedStops));
            Assert.That(rig.Count(SimEventKind.CheckpointRejected), Is.EqualTo(0), "every checkpoint along the route is valid");
            foreach (DollId d in Ids.Dolls)
            {
                Assert.IsTrue(rig.Ledger.DollIsAtDock(d, DockId.Harbour));
            }
        }
    }

    /// <summary>T06: the chapter without weather penalties getting in the way (routes use dry docks).</summary>
    public class ChapterRouteTests
    {
        [Test]
        public void ReferenceRoute_PipViaLowRefuge_LumaDirect_KeeperLast()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            ChapterScript.RescueViaLowRefuge(rig, HandId.Right, DollId.Pip);
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.Closed), "R closes after the first ordinary rescue");
            Assert.That(rig.Sim.Flood.Phase, Is.EqualTo(FloodPhase.RisingWater));
            ChapterScript.RescueDirect(rig, HandId.Left, DollId.Luma);
            Assert.IsTrue(rig.Sim.Director.State.LanternLatched);
            Assert.That(rig.Sim.Flood.Phase, Is.EqualTo(FloodPhase.LastCrossing));
            ChapterScript.BringKeeperHome(rig);
            ChapterScript.AssertCompleted(rig, 1);
            Assert.That(rig.Count(SimEventKind.DollRescued), Is.EqualTo(3), "each passenger counted once");
            Assert.That(rig.Count(SimEventKind.CheckpointCreated), Is.EqualTo(5), "start, lantern, two rescues, completion");
            Assert.That(rig.Count(SimEventKind.FloodPhaseChanged), Is.EqualTo(3), "Calm -> Low -> Rising -> Last crossing");
        }

        [Test]
        public void LumaFirstRouteAlsoFinishes()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            ChapterScript.RescueViaLowRefuge(rig, HandId.Left, DollId.Luma);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            ChapterScript.BringKeeperHome(rig);
            ChapterScript.AssertCompleted(rig, 1);
        }

        [Test]
        public void ReplaySolutionOmitsTheLowRefuge()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Luma);
            ChapterScript.BringKeeperHome(rig);
            ChapterScript.AssertCompleted(rig, 0);
        }

        [Test]
        public void TheLanternUnlocksOnlyAfterTheTeachingHop()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ClearEvents();
            rig.OfferPalmAtBerth(HandId.Right, DockId.LanternRefuge, 0);
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.LanternLocked), Is.GreaterThan(0));

            ChapterScript.HopAt(rig, HandId.Right, DollId.Keeper, DockId.Departure);
            Assert.That(rig.Count(SimEventKind.TutorialHopCompleted), Is.EqualTo(1));
            rig.Deposit(HandId.Left, DollId.Keeper, DockId.LanternRefuge, 0);
        }

        [Test]
        public void OnlyKeeperBoardsUntilTheTeachingHop()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.ParkHand(HandId.Right);
            rig.ClearEvents();
            rig.OfferPalmAtBerth(HandId.Left, DockId.Departure, 1);
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsAtBerth(DollId.Pip, DockId.Departure, 1));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.KeeperFirst), Is.GreaterThan(0));
        }

        [Test]
        public void TheSameHandBoardsAnotherBerthOfTheDockWithoutMovingAway()
        {
            var rig = new TestRig();
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, 0);
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Right), 3f));

            // Teaching hop with the palms raised a little above the dock, still inside its dry area.
            Vector3 raised = rig.CommandedSupport(HandId.Right) + new Vector3(0f, 0.08f, 0f);
            rig.SetSupport(HandId.Right, raised);
            rig.SetSupport(HandId.Left, rig.BesidePalm(raised, 0.02f, -1f));
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Keeper, HandId.Left), 3f));
            Assert.IsTrue(rig.Resolver().DockNeedsSeparation(HandId.Right, DockId.Departure),
                "the right palm has not yet moved away from Keeper's berth");

            // Pip's berth is a different berth: it is offered at once, without leaving the dock.
            rig.OfferPalmAtBerth(HandId.Right, DockId.Departure, 1);
            Assert.IsTrue(rig.RunUntil(() => rig.IsOnPalm(DollId.Pip, HandId.Right), 3f),
                "re-arm applies to the berth just used, not to the whole dock");
            Assert.IsTrue(rig.IsAtBerth(DollId.Luma, DockId.Departure, 2));
        }

        [Test]
        public void TheHarbourStaysDarkUntilKeeperHoldsTheLantern()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            ChapterScript.HopAt(rig, HandId.Right, DollId.Keeper, DockId.Departure);  // Keeper on the left
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.ClearEvents();
            rig.OfferPalmAtBerth(HandId.Right, DockId.Harbour, 0);
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Pip, HandId.Right));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.HarbourDark), Is.GreaterThan(0));

            rig.Deposit(HandId.Left, DollId.Keeper, DockId.LanternRefuge, 0);
            rig.Deposit(HandId.Right, DollId.Pip, DockId.Harbour, 0);
            Assert.IsTrue(rig.Sim.Director.State.IsRescued(DollId.Pip));
        }

        [Test]
        public void LiftingKeeperEarlyDimsTheHarbourWithoutLosingRescuedDolls()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            rig.Board(HandId.Left, DockId.Departure, 2, DollId.Luma);
            rig.ParkHand(HandId.Left);

            rig.ClearEvents();
            rig.Board(HandId.Right, DockId.LanternRefuge, 0, DollId.Keeper);
            Assert.That(rig.Count(SimEventKind.LanternDimmed), Is.EqualTo(1));
            Assert.IsFalse(rig.Sim.Director.LanternLit);

            rig.ClearEvents();
            rig.OfferPalmAtBerth(HandId.Left, DockId.Harbour, 1);
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Luma, HandId.Left));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.HarbourDark), Is.GreaterThan(0));
            Assert.IsTrue(rig.Sim.Director.State.IsRescued(DollId.Pip), "already rescued dolls remain safe");
            Assert.IsTrue(rig.Ledger.DollIsAtDock(DollId.Pip, DockId.Harbour));

            rig.ParkHand(HandId.Left);
            rig.Deposit(HandId.Right, DollId.Keeper, DockId.LanternRefuge, 0);
            Assert.IsTrue(rig.Sim.Director.LanternLit);
            ChapterScript.DepositHome(rig, HandId.Left, DollId.Luma);
            Assert.IsTrue(rig.Sim.Director.State.LanternLatched);
            ChapterScript.BringKeeperHome(rig);
            ChapterScript.AssertCompleted(rig, 0);
        }

        [Test]
        public void KeeperComesHomeLast()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            rig.Board(HandId.Right, DockId.LanternRefuge, 0, DollId.Keeper);
            rig.ClearEvents();
            rig.OfferPalmAtBerth(HandId.Right, DockId.Harbour, 0);
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.KeeperComesHomeLast), Is.GreaterThan(0));
        }

        [Test]
        public void OnlyKeeperMayHoldTheLantern()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            rig.Board(HandId.Left, DockId.LanternRefuge, 0, DollId.Keeper);
            rig.ParkHand(HandId.Left);
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.ClearEvents();
            rig.OfferPalmAtBerth(HandId.Right, DockId.LanternRefuge, 0);
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Pip, HandId.Right));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.LanternKeeperOnly), Is.GreaterThan(0));
        }

        [Test]
        public void RescuedPassengersStayAtTheHarbour()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            int pipBerth = rig.Owner(DollId.Pip).Slot.Berth;
            rig.ParkHand(HandId.Right);
            rig.Settle();
            rig.ClearEvents();
            rig.OfferPalmAtBerth(HandId.Right, DockId.Harbour, pipBerth);
            rig.Run(1.5f);
            Assert.IsTrue(rig.Ledger.DollIsAtDock(DollId.Pip, DockId.Harbour));
            Assert.That(rig.Count(SimEventKind.TransferReserved), Is.EqualTo(0));
        }

        [Test]
        public void DuplicateRescueCallbacksDoNotChangeCountsTwice()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            TransferTransaction rescue = null;
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.OfferPalmAtBerth(HandId.Right, DockId.Harbour, 0);
            Assert.IsTrue(rig.RunUntil(() =>
            {
                foreach (TransferTransaction tx in rig.Sim.Transfers.CommittedThisStep)
                {
                    if (tx.Target.Dock == DockId.Harbour)
                    {
                        rescue = tx;
                    }
                }

                return rescue != null;
            }, 3f));

            Assert.IsFalse(rig.Sim.Director.OnTransferCommitted(rescue));
            Assert.IsFalse(rig.Sim.Director.OnTransferCommitted(rescue));
            Assert.That(rig.Sim.Director.State.Rescued.Count, Is.EqualTo(1));
            Assert.That(rig.Count(SimEventKind.DollRescued), Is.EqualTo(1));
            rig.Step();
            Assert.That(rig.Count(SimEventKind.RefugeClosurePending), Is.EqualTo(1), "flood triggers are not repeated either");
        }

        [Test]
        public void TheFinalKeeperRescueCompletesExactlyOnce()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Luma);
            TransferTransaction home = null;
            rig.Board(HandId.Right, DockId.LanternRefuge, 0, DollId.Keeper);
            rig.OfferPalmAtBerth(HandId.Right, DockId.Harbour, 2);
            Assert.IsTrue(rig.RunUntil(() =>
            {
                foreach (TransferTransaction tx in rig.Sim.Transfers.CommittedThisStep)
                {
                    if (tx.Doll == DollId.Keeper && tx.Target.Dock == DockId.Harbour)
                    {
                        home = tx;
                    }
                }

                return home != null;
            }, 3f));

            Assert.IsFalse(rig.Sim.Director.OnTransferCommitted(home));
            rig.Run(1f);
            Assert.That(rig.Count(SimEventKind.ChapterCompleted), Is.EqualTo(1));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Results));
        }

        [Test]
        public void ReplayResetsObjectivesAndKeepsSettingsAndPlacement()
        {
            var rig = new TestRig();
            var settings = new HandoverSettings { LargeCues = true };
            rig.Sim.ApplySettings(settings);
            StageLayout stage = rig.Sim.Stage;
            ChapterScript.Opening(rig);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Luma);
            ChapterScript.BringKeeperHome(rig);
            Assert.IsTrue(rig.Sim.Director.Results.AssistanceUsed.LargeCues, "assistance is shown as context");

            rig.Sim.Replay();
            rig.ParkHand(HandId.Right);
            rig.Settle();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(rig.Sim.Director.State.Rescued.Count, Is.EqualTo(0));
            Assert.IsFalse(rig.Sim.Director.State.TutorialHopDone);
            Assert.That(rig.Sim.Flood.Phase, Is.EqualTo(FloodPhase.Calm));
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.Open));
            Assert.IsTrue(rig.Sim.Settings.LargeCues);
            Assert.AreSame(stage, rig.Sim.Stage);
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
            rig.Board(HandId.Left, DockId.Departure, 0, DollId.Keeper);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
        }
    }
}
