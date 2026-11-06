using System;
using System.Collections.Generic;
using System.IO;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>T04: atomic checkpoints, the full checkpoint schema and save failure handling.</summary>
    public class CheckpointTests
    {
        private static readonly int[] BerthCounts = { 3, 1, 1, 3 };
        private static readonly int[] HomeBerths = { -1, 0, 1, 2 };

        private static Checkpoint Fixture(params DollPlacement[] placements)
        {
            return new Checkpoint { Label = "fixture", Placements = placements, State = new ChapterState() };
        }

        private static DollPlacement P(DollId d, DockId dock, int berth) => new DollPlacement(d, dock, berth);

        private static Checkpoint Home() => Fixture(P(DollId.Keeper, DockId.Departure, 0), P(DollId.Pip, DockId.Departure, 1), P(DollId.Luma, DockId.Departure, 2));

        /// <summary>Keeper lit U, Pip is home, Luma rests at R while the closure is queued.</summary>
        private static Checkpoint QueuedClosure()
        {
            Checkpoint c = Fixture(P(DollId.Keeper, DockId.LanternRefuge, 0), P(DollId.Pip, DockId.Harbour, 0), P(DollId.Luma, DockId.LowRefuge, 0));
            c.State.TutorialHopDone = true;
            c.State.LanternEverLit = true;
            c.State.Phase = FloodPhase.LowWater;
            c.State.LowRefuge = RefugeStatus.ClosurePending;
            c.State.MarkRescued(DollId.Pip);
            c.State.LowRefugeStops = 1;
            return c;
        }

        private static List<string> Validate(Checkpoint c) => CheckpointRules.Validate(c, BerthCounts, HomeBerths);

        [Test]
        public void HomeAndQueuedClosureFixturesAreValid()
        {
            Assert.That(Validate(Home()), Is.Empty);
            Assert.That(Validate(QueuedClosure()), Is.Empty, "a queued phase-1 checkpoint may keep an occupant at R");
        }

        private static IEnumerable<TestCaseData> InvalidCheckpoints()
        {
            Checkpoint c;

            c = Home(); c.Placements[1] = P(DollId.Pip, DockId.Departure, 0);
            yield return new TestCaseData(c).SetName("Invalid_TwoDollsOneBerth");
            c = Home(); c.Placements[2] = P(DollId.Pip, DockId.Departure, 1);
            yield return new TestCaseData(c).SetName("Invalid_DollPlacedTwice");
            c = Home(); c.Placements[2] = P((DollId)9, DockId.Departure, 2);
            yield return new TestCaseData(c).SetName("Invalid_UnknownDoll");
            c = Home(); c.Placements[1] = P(DollId.Pip, DockId.LowRefuge, 3);
            yield return new TestCaseData(c).SetName("Invalid_BeyondCapacity");
            c = Home(); c.Placements = new[] { c.Placements[0], c.Placements[1] };
            yield return new TestCaseData(c).SetName("Invalid_MissingPassenger");
            c = Home(); c.Placements[1] = P(DollId.Pip, DockId.Departure, 2); c.Placements[2] = P(DollId.Luma, DockId.Departure, 1);
            yield return new TestCaseData(c).SetName("Invalid_NotOwnDepartureBerth");
            c = Home(); c.State.TutorialHopDone = true; c.Placements[1] = P(DollId.Pip, DockId.LanternRefuge, 0);
            yield return new TestCaseData(c).SetName("Invalid_PipAtLantern");
            c = Home(); c.Placements[0] = P(DollId.Keeper, DockId.LanternRefuge, 0);
            yield return new TestCaseData(c).SetName("Invalid_KeeperAtLanternBeforeTeachingHop");
            c = QueuedClosure(); c.State.LowRefuge = RefugeStatus.Closed; c.State.Phase = FloodPhase.RisingWater;
            yield return new TestCaseData(c).SetName("Invalid_ClosedRefugeOccupied");
            c = QueuedClosure(); c.State.Phase = FloodPhase.RisingWater;
            yield return new TestCaseData(c).SetName("Invalid_WaterRoseWhileClosurePending");
            c = QueuedClosure(); c.Placements[1] = P(DollId.Pip, DockId.Departure, 1);
            yield return new TestCaseData(c).SetName("Invalid_RescuedButNotAtHarbour");
            c = QueuedClosure(); c.Placements[2] = P(DollId.Luma, DockId.Harbour, 1);
            yield return new TestCaseData(c).SetName("Invalid_AtHarbourButNotRescued");
            c = QueuedClosure(); c.Placements[0] = P(DollId.Keeper, DockId.Harbour, 2); c.State.MarkRescued(DollId.Keeper);
            yield return new TestCaseData(c).SetName("Invalid_KeeperHomeBeforeLatch");
            c = Home(); c.State.Phase = FloodPhase.RisingWater; c.State.TutorialHopDone = true; c.State.LanternEverLit = true;
            yield return new TestCaseData(c).SetName("Invalid_WaterRoseBeforeAnyRescue");
            c = Home(); c.State.LanternEverLit = true;
            yield return new TestCaseData(c).SetName("Invalid_LanternBeforeTeachingHop");
            c = Home(); c.SchemaVersion = 99;
            yield return new TestCaseData(c).SetName("Invalid_UnknownSchema");
            c = Home(); c.State.LowRefugeStops = -1;
            yield return new TestCaseData(c).SetName("Invalid_NegativeCounter");
        }

        [TestCaseSource(nameof(InvalidCheckpoints))]
        public void IllegalCheckpointsAreRejected(Checkpoint c)
        {
            Assert.That(Validate(c), Is.Not.Empty);
        }

        [Test]
        public void ConfirmingTheStageCreatesTheInitialCheckpoint()
        {
            var rig = new TestRig();
            Checkpoint start = rig.Sim.Checkpoints.Latest;
            Assert.IsNotNull(start);
            Assert.That(start.Label, Is.EqualTo("start"));
            Assert.That(start.PlacementOf(DollId.Keeper).Slot, Is.EqualTo(SlotRef.DockBerth(DockId.Departure, 0)));
        }

        [Test]
        public void CaptureNormalizesPalmTransferAndHoldOwners()
        {
            var rig = new TestRig(new PermissivePolicy(), TestRig.OpenVariant());
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.Deposit(HandId.Right, DollId.Keeper, DockId.LowRefuge, 0);
            rig.Board(HandId.Right, DockId.LowRefuge, 0, DollId.Keeper);   // Keeper on a palm, last safe R
            rig.OfferPalmAtBerth(HandId.Left, DockId.Departure, 1);        // Pip boarding in progress
            Assert.IsTrue(rig.RunUntil(() => rig.Owner(DollId.Pip).Kind == OwnerKind.Transfer, 2f));

            Checkpoint c = rig.Sim.Checkpoints.Capture("test", rig.Sim.Director.State, new PermissivePolicy());
            Assert.That(c.PlacementOf(DollId.Keeper).Slot, Is.EqualTo(SlotRef.DockBerth(DockId.LowRefuge, 0)), "palm owner -> last safe dock");
            Assert.That(c.PlacementOf(DollId.Pip).Slot, Is.EqualTo(SlotRef.DockBerth(DockId.Departure, 1)), "transfer owner -> reserved rollback dock");
            Assert.That(c.PlacementOf(DollId.Luma).Slot, Is.EqualTo(SlotRef.DockBerth(DockId.Departure, 2)));
        }

        [Test]
        public void CaptureFallsBackHomeWhenTheLastSafeBerthIsTaken()
        {
            var rig = new TestRig(new PermissivePolicy(), TestRig.OpenVariant());
            rig.Board(HandId.Right, DockId.Departure, 1, DollId.Pip);
            rig.Deposit(HandId.Right, DollId.Pip, DockId.LowRefuge, 0);
            rig.Board(HandId.Right, DockId.LowRefuge, 0, DollId.Pip);      // Pip rides; last safe = R
            rig.ParkHand(HandId.Right);
            rig.Board(HandId.Left, DockId.Departure, 2, DollId.Luma);
            rig.Deposit(HandId.Left, DollId.Luma, DockId.LowRefuge, 0);     // Luma now occupies R
            Checkpoint c = rig.Sim.Checkpoints.Capture("test", rig.Sim.Director.State, new PermissivePolicy());
            Assert.That(c.PlacementOf(DollId.Luma).Slot, Is.EqualTo(SlotRef.DockBerth(DockId.LowRefuge, 0)));
            Assert.That(c.PlacementOf(DollId.Pip).Slot, Is.EqualTo(SlotRef.DockBerth(DockId.Departure, 1)));
            Assert.That(rig.Sim.Checkpoints.Validate(c), Is.Empty);
        }

        [Test]
        public void RestoreReproducesTheCompleteSnapshot()
        {
            var rig = new TestRig(new PermissivePolicy(), TestRig.OpenVariant());
            Checkpoint fixture = QueuedClosure();
            Assert.IsTrue(rig.Sim.RestoreCheckpoint(fixture));
            rig.Step();
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.LanternRefuge, 0));
            Assert.IsTrue(rig.IsAtBerth(DollId.Pip, DockId.Harbour, 0));
            Assert.IsTrue(rig.IsAtBerth(DollId.Luma, DockId.LowRefuge, 0));
            ChapterState s = rig.Sim.Director.State;
            Assert.IsTrue(s.TutorialHopDone && s.LanternEverLit && !s.LanternLatched);
            Assert.That(s.LowRefuge, Is.EqualTo(RefugeStatus.ClosurePending));
            Assert.That(s.Phase, Is.EqualTo(FloodPhase.LowWater));
            Assert.IsTrue(s.IsRescued(DollId.Pip));
            Assert.That(s.LowRefugeStops, Is.EqualTo(1));
        }

        [Test]
        public void RestoreDuringAHopReleasesEverything()
        {
            var rig = new TestRig(new PermissivePolicy(), TestRig.OpenVariant());
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            var zone = rig.HopZone(DockId.Departure);
            rig.SetSupport(HandId.Right, zone);
            rig.SetSupport(HandId.Left, rig.BesidePalm(zone, 0.02f, -1f));
            Assert.IsTrue(rig.RunUntil(() => rig.Sim.Transfers.Active.Count == 1, 2f));
            rig.Sim.RestoreCheckpoint(Home());
            rig.AssertInvariants();
            Assert.That(rig.Ledger.ActiveReservationCount, Is.EqualTo(0));
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
        }

        [Test]
        public void InvalidSnapshotFallsBackToThePreviousValidCheckpoint()
        {
            var rig = new TestRig(new PermissivePolicy(), TestRig.OpenVariant());
            Assert.That(rig.Sim.Checkpoints.Accept(QueuedClosure()), Is.Empty);
            Checkpoint broken = QueuedClosure();
            broken.Placements[0] = P(DollId.Keeper, DockId.Harbour, 2);
            Assert.IsFalse(rig.Sim.RestoreCheckpoint(broken));
            rig.Step();
            Assert.That(rig.Count(SimEventKind.CheckpointRejected), Is.EqualTo(1));
            Assert.IsTrue(rig.IsAtBerth(DollId.Luma, DockId.LowRefuge, 0), "the previous valid checkpoint was used");
        }

        [Test]
        public void RestartFromCheckpointResumesPlay()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.Sim.Pause(PauseReason.Menu);
            rig.Sim.RestartFromCheckpoint();
            rig.Step();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing));
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
            Assert.IsFalse(rig.Sim.HasHeldDolls());
        }

        [Test]
        public void RepositionRestoresAConsistentCheckpointThenContinuesAfterConfirm()
        {
            var rig = new TestRig();
            rig.Board(HandId.Right, DockId.Departure, 0, DollId.Keeper);
            rig.Sim.Pause(PauseReason.Menu);
            rig.Sim.BeginReposition();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Setup));
            Assert.IsNull(rig.Sim.Stage);
            Assert.IsTrue(rig.IsAtBerth(DollId.Keeper, DockId.Departure, 0));
            rig.Sim.SetStage(StageLayout.Build(StageLayoutVariant.CreateStandard(),
                new StagePlacement(TestRig.StageOrigin + new System.Numerics.Vector3(0.05f, 0f, 0f), 0.2f, 1f, StageSource.VirtualTabletop, 0)));
            rig.Sim.ConfirmStage();
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Playing), "a chapter in progress continues without a new palm-start");
            Assert.That(rig.Sim.Session.ChapterStartCount, Is.EqualTo(1));
        }
    }

    public class SaveTests
    {
        private static readonly int[] BerthCounts = { 3, 1, 1, 3 };
        private static readonly int[] HomeBerths = { -1, 0, 1, 2 };

        private static SaveStore Store(IFileSystem fs) =>
            new SaveStore(fs, "/saves", c => CheckpointRules.Validate(c, BerthCounts, HomeBerths));

        private static SaveData Sample()
        {
            var data = new SaveData();
            data.Settings.SteadyMode = true;
            data.Settings.EffectsVolume = 0.25f;
            data.Progress.RecordCompletion(1);
            var c = new Checkpoint
            {
                Label = "lantern",
                Placements = new[]
                {
                    new DollPlacement(DollId.Keeper, DockId.LanternRefuge, 0),
                    new DollPlacement(DollId.Pip, DockId.Departure, 1),
                    new DollPlacement(DollId.Luma, DockId.Departure, 2),
                },
            };
            c.State.TutorialHopDone = true;
            c.State.LanternEverLit = true;
            c.State.Phase = FloodPhase.LowWater;
            data.Checkpoint = c;
            return data;
        }

        [Test]
        public void RoundTripPreservesSettingsProgressAndCheckpoint()
        {
            SaveData original = Sample();
            string json = SaveSerializer.ToJson(original);
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out SaveData loaded, out string error), error);
            Assert.IsTrue(loaded.Settings.SteadyMode);
            Assert.That(loaded.Settings.EffectsVolume, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(loaded.Progress.BestLowRefugeStops, Is.EqualTo(1));
            Assert.That(loaded.Progress.HarbourVignette, Is.EquivalentTo(new[] { DollId.Keeper, DollId.Pip, DollId.Luma }));
            Assert.That(loaded.Checkpoint.PlacementOf(DollId.Keeper).Slot, Is.EqualTo(SlotRef.DockBerth(DockId.LanternRefuge, 0)));
            Assert.That(loaded.Checkpoint.State.Phase, Is.EqualTo(FloodPhase.LowWater));
            Assert.That(SaveSerializer.ToJson(loaded), Is.EqualTo(json), "serialization is stable");
            Assert.That(json, Does.Not.Contain("pose").And.Not.Contain("progress\": 0."), "no hand poses or hop progress are saved");
        }

        [Test]
        public void SaveReplacesAtomicallyAndKeepsABackup()
        {
            var fs = new InMemoryFileSystem();
            SaveStore store = Store(fs);
            Assert.IsTrue(store.Save(new SaveData(), out _));
            Assert.IsTrue(fs.Exists(store.SavePath));
            Assert.IsFalse(fs.Exists(store.TempPath));
            string first = fs.Files[store.SavePath];

            Assert.IsTrue(store.Save(Sample(), out _));
            Assert.That(fs.Files[store.BackupPath], Is.EqualTo(first));
            Assert.IsFalse(fs.Exists(store.TempPath));
            Assert.IsTrue(store.Load().Data.Settings.SteadyMode);
        }

        [Test]
        public void InterruptedWriteLeavesThePreviousSaveIntact()
        {
            var fs = new InMemoryFileSystem();
            SaveStore store = Store(fs);
            store.Save(Sample(), out _);
            string before = fs.Files[store.SavePath];
            fs.FailNextWriteAfterChars = 12;
            Assert.IsFalse(store.Save(new SaveData(), out string error));
            Assert.That(error, Does.Contain("Could not write"));
            Assert.That(fs.Files[store.SavePath], Is.EqualTo(before));
            Assert.IsFalse(fs.Exists(store.TempPath));
        }

        [Test]
        public void CorruptSaveFallsBackToTheBackup()
        {
            var fs = new InMemoryFileSystem();
            SaveStore store = Store(fs);
            store.Save(Sample(), out _);
            store.Save(Sample(), out _);
            fs.Files[store.SavePath] = "{ \"schema\": 1, \"settings\": ";
            SaveLoadResult r = store.Load();
            Assert.IsTrue(r.FromBackup);
            Assert.IsNotNull(r.Message);
            Assert.IsTrue(r.Data.Settings.SteadyMode);
        }

        [Test]
        public void BothCorruptFallsBackToDefaultsWithARecoverableMessage()
        {
            var fs = new InMemoryFileSystem();
            SaveStore store = Store(fs);
            fs.Files[store.SavePath] = "garbage";
            fs.Files[store.BackupPath] = "{}";
            SaveLoadResult r = store.Load();
            Assert.IsTrue(r.UsedDefaults);
            Assert.IsNotNull(r.Message);
            Assert.IsFalse(r.Data.Settings.SteadyMode);
        }

        [Test]
        public void NoSaveGivesDefaultsWithoutAMessage()
        {
            SaveLoadResult r = Store(new InMemoryFileSystem()).Load();
            Assert.IsTrue(r.UsedDefaults);
            Assert.IsNull(r.Message);
        }

        [TestCase("\"doll\": \"keeper\"", "\"doll\": \"ghost\"")]
        [TestCase("\"dock\": \"U\"", "\"dock\": \"X\"")]
        [TestCase("\"effectsVolume\": 0.25", "\"effectsVolume\": 7")]
        [TestCase("\"phase\": \"LowWater\"", "\"phase\": \"Tsunami\"")]
        [TestCase("\"phase\": \"LowWater\"", "\"phase\": \"2\"")]
        [TestCase("\"schema\": 1,\n  \"settings\"", "\"schema\": 2,\n  \"settings\"")]
        public void UnknownIdsAndIllegalValuesAreRejected(string find, string replace)
        {
            string json = SaveSerializer.ToJson(Sample());
            Assert.That(json, Does.Contain(find));
            Assert.IsFalse(SaveSerializer.TryFromJson(json.Replace(find, replace), out _, out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IllegalCheckpointIsDroppedButSettingsSurvive()
        {
            var fs = new InMemoryFileSystem();
            SaveStore store = Store(fs);
            SaveData data = Sample();
            data.Checkpoint.Placements[1] = new DollPlacement(DollId.Pip, DockId.LanternRefuge, 0);
            data.Checkpoint.Placements[0] = new DollPlacement(DollId.Keeper, DockId.Departure, 0);
            store.Save(data, out _);
            SaveLoadResult r = store.Load();
            Assert.IsNull(r.Data.Checkpoint);
            Assert.IsTrue(r.Data.Settings.SteadyMode);
            Assert.IsNotNull(r.Message);
        }

        [Test]
        public void PhysicalFileSystemRoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "handover-save-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveStore(new PhysicalFileSystem(), dir, c => CheckpointRules.Validate(c, BerthCounts, HomeBerths));
                Assert.IsTrue(store.Save(new SaveData(), out string e1), e1);
                Assert.IsTrue(store.Save(Sample(), out string e2), e2);
                Assert.IsTrue(File.Exists(store.BackupPath));
                Assert.IsFalse(File.Exists(store.TempPath));
                Assert.IsTrue(store.Load().Data.Settings.SteadyMode);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void MiniJsonIsStrict()
        {
            Assert.IsFalse(MiniJson.TryParse("{\"a\":1} x", out _, out _));
            Assert.IsFalse(MiniJson.TryParse("{\"a\":1,\"a\":2}", out _, out _));
            Assert.IsFalse(MiniJson.TryParse("[1,]", out _, out _));
            Assert.IsTrue(MiniJson.TryParse("{\"s\":\"a\\\"b\\u0041\",\"n\":-1.5e2,\"t\":[true,false,null]}", out object v, out _));
            var o = (Dictionary<string, object>)v;
            Assert.That(o["s"], Is.EqualTo("a\"bA"));
            Assert.That(o["n"], Is.EqualTo(-150.0));
        }
    }
}
