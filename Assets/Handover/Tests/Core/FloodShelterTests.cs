using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>T07: progress-triggered flood, queued refuge closure and virtual shelter.</summary>
    public class FloodShelterTests
    {
        /// <summary>The open middle of the Standard stage: in the rain, outside every dry area.</summary>
        private static Vector3 Crossing(TestRig rig) => TestRig.StageOrigin + new Vector3(0f, 0.12f, 0f);

        private static void CarryPipIntoTheRain(TestRig rig)
        {
            ChapterScript.Opening(rig);
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.ClearEvents();
            rig.SetSupport(HandId.Right, Crossing(rig));
        }

        private static void RaiseShield(TestRig rig, HandId shieldHand, HandId carrier, float distance = 0.15f)
        {
            Vector3 head = rig.CommandedSupport(carrier) + Vector3.UnitY * rig.Sim.Chapter.DollHeadHeight;
            rig.SetPalmDown(shieldHand, rig.Sim.Flood.IdealCanopyCentre(head, distance));
        }

        [Test]
        public void PhaseTableStartsCalmAndCalmsAgainForTheLastCrossing()
        {
            ChapterDefinition def = ChapterDefinition.CreateDefault();
            Assert.IsFalse(def.RainActive[(int)FloodPhase.Calm]);
            Assert.IsTrue(def.RainActive[(int)FloodPhase.LowWater]);
            Assert.IsTrue(def.RainActive[(int)FloodPhase.RisingWater]);
            Assert.IsFalse(def.RainActive[(int)FloodPhase.LastCrossing]);
            Assert.That(def.WaterHeights[(int)FloodPhase.LowWater], Is.EqualTo(0.01f));
            Assert.That(def.WaterHeights[(int)FloodPhase.RisingWater], Is.EqualTo(0.04f));
            Assert.That(def.WaterHeights[(int)FloodPhase.LastCrossing], Is.EqualTo(0.06f));
            StageLayoutVariant v = StageLayoutVariant.CreateStandard();
            Assert.That(v.Dock(DockId.LowRefuge).LocalCenter.Y, Is.LessThan(def.WaterHeights[(int)FloodPhase.RisingWater]), "R floods in phase 1");
            Assert.That(v.Dock(DockId.Departure).LocalCenter.Y, Is.GreaterThan(def.WaterHeights[(int)FloodPhase.LastCrossing]), "D, U and H stay dry");
        }

        [Test]
        public void UnshelteredCrossingCuesShelterThenRestoresTheCheckpoint()
        {
            var rig = new TestRig();
            CarryPipIntoTheRain(rig);
            rig.Run(0.45f);
            Assert.That(rig.Count(SimEventKind.ShelterCue), Is.EqualTo(0));
            rig.Run(0.15f);
            Assert.That(rig.Count(SimEventKind.ShelterCue), Is.EqualTo(1));
            Assert.IsTrue(rig.RunUntil(() => rig.Count(SimEventKind.ExposureReset) == 1, 1.6f));
            rig.Step();
            Assert.IsTrue(rig.IsAtBerth(DollId.Pip, DockId.Departure, 1), "the complete snapshot was restored");
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.LanternRefuge, 0));
            Assert.That(rig.Sim.Director.State.ExposureResets, Is.EqualTo(1));
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
        }

        [Test]
        public void TheCanopyOfAnEmptyPalmDownHandShelters()
        {
            var rig = new TestRig();
            CarryPipIntoTheRain(rig);
            RaiseShield(rig, HandId.Left, HandId.Right);
            rig.Run(3f);
            Assert.That(rig.Count(SimEventKind.ShelterCue), Is.EqualTo(0));
            Assert.That(rig.Count(SimEventKind.ExposureReset), Is.EqualTo(0));
            Assert.IsTrue(rig.IsOnPalm(DollId.Pip, HandId.Right));
            Assert.IsTrue(rig.Sim.Flood.IsShieldEngaged(HandId.Left));
        }

        [Test]
        public void AShieldOutOfTheRainLineDoesNotShelter()
        {
            var rig = new TestRig();
            CarryPipIntoTheRain(rig);
            // Directly above the doll: the angled rain passes beside it.
            rig.SetPalmDown(HandId.Left, rig.CommandedSupport(HandId.Right) + new Vector3(-0.12f, 0.15f, -0.1f));
            Assert.IsTrue(rig.RunUntil(() => rig.Count(SimEventKind.ExposureReset) == 1, 2.5f));
        }

        [Test]
        public void ContinuousExposureOnlyResetsAfterThreeTenthsOfASecondOfSafety()
        {
            var rig = new TestRig();
            CarryPipIntoTheRain(rig);
            rig.Run(1.2f);
            // Brief shelter (0.2 s) pauses exposure without resetting it...
            RaiseShield(rig, HandId.Left, HandId.Right);
            rig.Run(0.45f); // shield dwell 0.2 s, then 0.2 s sheltered
            rig.ParkHand(HandId.Left);
            Assert.IsTrue(rig.RunUntil(() => rig.Count(SimEventKind.ExposureReset) == 1, 1.0f), "1.2 s + 0.8 s is continuous exposure");

            // ...while 0.3 s of safety clears it.
            rig.Step();
            rig.ClearEvents();
            rig.ParkHand(HandId.Left);
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.ClearEvents();
            rig.SetSupport(HandId.Right, Crossing(rig));
            rig.Run(1.4f);
            RaiseShield(rig, HandId.Left, HandId.Right);
            rig.Run(0.6f); // dwell 0.2 s then 0.4 s sheltered
            Assert.That(rig.Count(SimEventKind.ExposureCleared), Is.EqualTo(1));
            rig.ParkHand(HandId.Left);
            rig.Run(1.4f);
            Assert.That(rig.Count(SimEventKind.ExposureReset), Is.EqualTo(0));
        }

        [Test]
        public void DryAreaHandoversNeedNoShield()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.ClearEvents();
            ChapterScript.HopAt(rig, HandId.Right, DollId.Pip, DockId.Departure);
            ChapterScript.HopAt(rig, HandId.Left, DollId.Pip, DockId.Harbour);
            rig.Run(2f);
            Assert.That(rig.Count(SimEventKind.ShelterCue), Is.EqualTo(0));
            Assert.That(rig.Count(SimEventKind.TransferCommitted, e => e.TransferKind == TransferKind.Handover), Is.EqualTo(2));
        }

        [Test]
        public void PausedChapterHasFrozenHazardClocks()
        {
            var rig = new TestRig();
            CarryPipIntoTheRain(rig);
            rig.Run(0.4f);
            float exposed = rig.Sim.Flood.ExposedSeconds(HandId.Right);
            Assert.That(exposed, Is.GreaterThan(0.3f));
            rig.Sim.Pause(PauseReason.Menu);
            rig.Run(3f);
            Assert.That(rig.Count(SimEventKind.ExposureReset), Is.EqualTo(0));
            rig.Sim.Resume();
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Session.State == SessionState.Playing, 1f));
            Assert.That(rig.Sim.Flood.ExposedSeconds(HandId.Right), Is.EqualTo(exposed).Within(0.03f), "exposure neither advanced nor reset while paused");
        }

        [Test]
        public void ActiveShieldingHandDropoutCausesRecoveryNotFailure()
        {
            var rig = new TestRig();
            CarryPipIntoTheRain(rig);
            RaiseShield(rig, HandId.Left, HandId.Right);
            rig.Run(1f);
            Assert.IsTrue(rig.Sim.Flood.IsShieldEngaged(HandId.Left));
            Vector3 shield = rig.Hands.GetPose(HandId.Left).Position;
            rig.Hands.Hide(HandId.Left);
            rig.Run(0.3f);
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Recovering));
            rig.Run(3f);
            Assert.That(rig.Count(SimEventKind.ExposureReset), Is.EqualTo(0), "weather cannot punish a missing shielding hand");
            rig.Hands.SetPose(HandId.Left, PalmBasis.PalmDown(shield));
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Session.State == SessionState.Playing, 1f));
            Assert.IsTrue(rig.IsOnPalm(DollId.Pip, HandId.Right));
        }

        [Test]
        public void TheLowRefugeNeverClosesWhileOccupiedAndRejectsNewArrivals()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            rig.Board(HandId.Left, DockId.Departure, 2, DollId.Luma);
            rig.Deposit(HandId.Left, DollId.Luma, DockId.LowRefuge, 0);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);

            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.ClosurePending));
            Assert.That(rig.Sim.Flood.Phase, Is.EqualTo(FloodPhase.LowWater), "the water keeps its old height while R is occupied");
            Checkpoint rescue = rig.Sim.Checkpoints.Latest;
            Assert.That(rescue.Label, Is.EqualTo("rescue"));
            Assert.That(rescue.PlacementOf(DollId.Luma).Dock, Is.EqualTo(DockId.LowRefuge));
            Assert.That(rescue.State.LowRefuge, Is.EqualTo(RefugeStatus.ClosurePending));

            // Keeper may be lifted early but cannot rest at a closing refuge.
            rig.Board(HandId.Right, DockId.LanternRefuge, 0, DollId.Keeper);
            rig.ClearEvents();
            rig.SetSupport(HandId.Right, rig.Berth(DockId.LowRefuge, 0) + new Vector3(0f, 0f, rig.Tuning.SupportHalfLength + rig.Tuning.BerthSupportRadius + 0.015f));
            rig.Run(1.5f);
            Assert.IsTrue(rig.IsOnPalm(DollId.Keeper, HandId.Right));
            Assert.That(rig.Count(SimEventKind.DockRejected, e => e.Reason == RejectReason.RefugeClosed), Is.GreaterThan(0));
            rig.Deposit(HandId.Right, DollId.Keeper, DockId.LanternRefuge, 0);

            // Departure is still permitted; the closure commits once R is empty and unreserved.
            rig.ClearEvents();
            rig.Board(HandId.Left, DockId.LowRefuge, 0, DollId.Luma);
            Assert.That(rig.Count(SimEventKind.RefugeClosed), Is.EqualTo(1));
            Assert.That(rig.Sim.Flood.Phase, Is.EqualTo(FloodPhase.RisingWater));

            // The restored snapshot resumes the queued condition exactly.
            rig.Sim.RestoreCheckpoint(rescue);
            rig.Step();
            Assert.IsTrue(rig.IsAtBerth(DollId.Luma, DockId.LowRefuge, 0));
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.ClosurePending));
        }

        [Test]
        public void TheLowRefugeNeverClosesWhileReserved()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.ParkHand(HandId.Right);
            rig.Board(HandId.Left, DockId.Departure, 2, DollId.Luma);
            rig.ParkHand(HandId.Left);
            rig.Settle();

            // Pip lands at the harbour while Luma's hop into R is already reserved.
            rig.OfferPalmAtBerth(HandId.Right, DockId.Harbour, 0);
            rig.Run(0.4f);
            rig.OfferPalmAtBerth(HandId.Left, DockId.LowRefuge, 0);
            bool sawPendingWhileReserved = false;
            for (int i = 0; i < 300; i++)
            {
                rig.Step();
                bool rBusy = rig.Ledger.CountAtDock(DockId.LowRefuge, includeReserved: true) > 0;
                Assert.IsFalse(rBusy && rig.Sim.Flood.LowRefuge == RefugeStatus.Closed, "R closed while occupied or reserved");
                if (rig.Ledger.DockHasReservation(DockId.LowRefuge) && rig.Sim.Flood.LowRefuge == RefugeStatus.ClosurePending)
                {
                    sawPendingWhileReserved = true;
                }
            }

            Assert.IsTrue(sawPendingWhileReserved, "the scenario must overlap the reservation and the queued closure");
            Assert.IsTrue(rig.IsAtBerth(DollId.Luma, DockId.LowRefuge, 0), "a reserved arrival completes");
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.ClosurePending));
        }

        [Test]
        public void RelaxedModeKeepsTheLowRefugeOpenAndRemovesPenalties()
        {
            var rig = new TestRig();
            rig.Sim.ApplySettings(new HandoverSettings { RelaxedMode = true });
            CarryPipIntoTheRain(rig);
            rig.Run(3f);
            Assert.That(rig.Count(SimEventKind.ShelterCue) + rig.Count(SimEventKind.ExposureReset), Is.EqualTo(0));
            ChapterScript.DepositHome(rig, HandId.Right, DollId.Pip);
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.Open));
            rig.Board(HandId.Right, DockId.Departure, 2, DollId.Luma);
            rig.Deposit(HandId.Right, DollId.Luma, DockId.LowRefuge, 0);
            Assert.That(rig.Count(SimEventKind.CheckpointRejected), Is.EqualTo(0));
        }

        [Test]
        public void LeavingRelaxedModeAfterTheFirstRescueQueuesTheClosure()
        {
            var rig = new TestRig();
            rig.Sim.ApplySettings(new HandoverSettings { RelaxedMode = true });
            ChapterScript.Opening(rig);
            ChapterScript.RescueDirect(rig, HandId.Right, DollId.Pip);
            rig.Board(HandId.Right, DockId.Departure, 2, DollId.Luma);
            rig.Deposit(HandId.Right, DollId.Luma, DockId.LowRefuge, 0);
            rig.Sim.ApplySettings(new HandoverSettings { RelaxedMode = false });
            rig.Step();
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.ClosurePending), "occupied, so only queued");
            Assert.That(rig.Sim.Checkpoints.Validate(rig.Sim.CreateCheckpoint("toggle")), Is.Empty);
            rig.Board(HandId.Right, DockId.LowRefuge, 0, DollId.Luma);
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.Closed));
        }

        [Test]
        public void ShelterAssistRemovesPenaltiesButKeepsTheRoutePuzzle()
        {
            var rig = new TestRig();
            rig.Sim.ApplySettings(new HandoverSettings { ShelterAssist = true });
            CarryPipIntoTheRain(rig);
            rig.Run(3f);
            Assert.That(rig.Count(SimEventKind.ExposureReset), Is.EqualTo(0));
            ChapterScript.DepositHome(rig, HandId.Right, DollId.Pip);
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.Closed), "R still closes after the first rescue");
        }

        [Test]
        public void TheWholeChapterIsSolvableWithWeatherAndShieldedCrossings()
        {
            var rig = new TestRig();
            ChapterScript.Opening(rig);

            // Luma rests at R first; R then closes, so Pip must take the direct crossing.
            ChapterScript.RescueViaLowRefuge(rig, HandId.Left, DollId.Luma);
            Assert.That(rig.Sim.Flood.LowRefuge, Is.EqualTo(RefugeStatus.Closed));
            rig.ParkHand(HandId.Left);

            // Pip: a direct crossing through the rain, sheltered by the other hand all the way.
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            Vector3 from = rig.BesideBerth(DockId.Departure, 1);
            Vector3 to = rig.BesideBerth(DockId.Harbour, 0, 0.015f, -1f);
            for (int i = 0; i <= 144; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / 144f) + new Vector3(0f, 0.03f * (float)System.Math.Sin(i / 144f * System.Math.PI), 0f);
                rig.SetSupport(HandId.Right, p);
                RaiseShield(rig, HandId.Left, HandId.Right);
                rig.Step();
            }

            Assert.IsTrue(rig.RunUntil(() => rig.Ledger.DollIsAtDock(DollId.Pip, DockId.Harbour), 3f));
            Assert.That(rig.Count(SimEventKind.ShelterCue), Is.EqualTo(0), "the shield covered the whole crossing");
            rig.ParkHand(HandId.Left);
            rig.ParkHand(HandId.Right);
            rig.Settle();

            ChapterScript.BringKeeperHome(rig);
            Assert.That(rig.Count(SimEventKind.ExposureReset), Is.EqualTo(0));
            ChapterScript.AssertCompleted(rig, 1);
        }

        [Test]
        public void CanopyGeometry()
        {
            var rig = new TestRig();
            Vector3 head = TestRig.StageOrigin + new Vector3(0f, 0.15f, 0f);
            Vector3 rain = rig.Sim.Flood.RainFromWorld;
            var shield = new PalmPlatform(HandId.Left, rig.Tuning);
            var provider = new SyntheticHandPoseProvider();
            void Place(Vector3 centre)
            {
                provider.SetPose(HandId.Left, PalmBasis.PalmDown(centre));
                provider.Tick(0.02);
                shield.ResetContinuity();
                shield.Update(provider.GetSample(HandId.Left), provider.Now, provider.OriginEpoch, provider.Now, 0.02f, 0.02f, false);
            }

            Place(head + rain * 0.15f);
            Assert.IsTrue(rig.Sim.Flood.CanopyCovers(shield, head, rain));
            Place(head + rain * 0.15f + new Vector3(0.12f, 0f, 0f));
            Assert.IsFalse(rig.Sim.Flood.CanopyCovers(shield, head, rain), "beyond the canopy radius");
            Place(head - rain * 0.15f);
            Assert.IsFalse(rig.Sim.Flood.CanopyCovers(shield, head, rain), "behind the doll");
            Place(head + rain * 0.5f);
            Assert.IsFalse(rig.Sim.Flood.CanopyCovers(shield, head, rain), "too far along the rain ray");
        }
    }
}
