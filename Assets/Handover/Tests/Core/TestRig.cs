using System;
using System.Collections.Generic;
using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>Dock rules that allow everything, for testing transfer mechanics in isolation.</summary>
    public sealed class PermissivePolicy : IDockPolicy
    {
        public RejectReason CanBoard(DollId doll, SlotRef berth) => RejectReason.None;
        public RejectReason CanDeposit(DollId doll, DockId dock) => RejectReason.None;
        public bool IsDryAreaOpen(DockId dock) => true;
        public bool CanRestAt(DollId doll, SlotRef berth) => berth.Dock != DockId.Harbour;
    }

    /// <summary>
    /// Deterministic harness: a simulation driven by synthetic palms at 72 Hz on a virtual
    /// tabletop. Every step asserts the ledger invariants, so any duplicate owner or leaked
    /// reservation fails the test at the step where it happens.
    /// </summary>
    public sealed class TestRig
    {
        public const float Dt = 1f / 72f;
        public static readonly Vector3 StageOrigin = new Vector3(0f, 0.75f, 0.45f);

        public readonly SyntheticHandPoseProvider Hands = new SyntheticHandPoseProvider();
        public readonly HandoverSimulation Sim;
        public readonly List<SimEvent> AllEvents = new List<SimEvent>();
        public bool CheckInvariantsEveryStep = true;

        public TestRig(IDockPolicy policy = null, StageLayoutVariant variant = null, bool confirmStage = true,
            ChapterDefinition chapter = null, InteractionTuning tuning = null)
        {
            Sim = new HandoverSimulation(chapter ?? ChapterDefinition.CreateDefault(), tuning ?? new InteractionTuning(), Hands, policy);
            StageLayoutVariant v = variant ?? Sim.Chapter.LayoutVariants[0];
            Sim.SetStage(StageLayout.Build(v, new StagePlacement(StageOrigin, 0f, 1f, StageSource.VirtualTabletop, Hands.OriginEpoch)));
            ParkHand(HandId.Left);
            ParkHand(HandId.Right);
            if (confirmStage)
            {
                Sim.ConfirmStage();
            }

            Settle();
        }

        /// <summary>A layout with widely spaced docks and large dry areas, so hops can happen away from berths.</summary>
        public static StageLayoutVariant OpenVariant()
        {
            StageLayoutVariant v = StageLayoutVariant.CreateStandard();
            v.Name = "OpenTest";
            v.Width = 0.9f;
            v.Depth = 0.9f;
            v.Dock(DockId.Departure).LocalCenter = new Vector3(-0.34f, 0.09f, 0f);
            v.Dock(DockId.Harbour).LocalCenter = new Vector3(0.34f, 0.09f, 0f);
            v.Dock(DockId.LowRefuge).LocalCenter = new Vector3(0f, 0.03f, -0.32f);
            v.Dock(DockId.LanternRefuge).LocalCenter = new Vector3(0f, 0.09f, 0.32f);
            foreach (DockDefinition d in v.Docks)
            {
                d.DryAreaRadius = 0.25f;
            }

            return v;
        }

        public StageLayout Stage => Sim.Stage;
        public InteractionTuning Tuning => Sim.Tuning;
        public RescueLedger Ledger => Sim.Ledger;

        // ---- stepping --------------------------------------------------------

        public void Step(int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                Hands.Tick(Dt);
                Sim.Step(Dt);
                AllEvents.AddRange(Sim.Events);
                if (CheckInvariantsEveryStep)
                {
                    AssertInvariants();
                }
            }
        }

        public void Run(float seconds)
        {
            Step(Math.Max(1, (int)Math.Ceiling(seconds / Dt)));
        }

        /// <summary>Steps until the condition holds; returns false on timeout.</summary>
        public bool RunUntil(Func<bool> condition, float maxSeconds)
        {
            int frames = (int)Math.Ceiling(maxSeconds / Dt);
            for (int i = 0; i < frames; i++)
            {
                if (condition())
                {
                    return true;
                }

                Step();
            }

            return condition();
        }

        /// <summary>Lets filters, velocity estimates and clinging settle after an abrupt placement.</summary>
        public void Settle(float seconds = 0.4f)
        {
            Run(seconds);
        }

        public void AssertInvariants()
        {
            List<string> problems = Sim.ValidateInvariants();
            if (problems.Count > 0)
            {
                Assert.Fail("Invariant violated at step " + Sim.Clock.StepCount + ":\n  " + string.Join("\n  ", problems));
            }
        }

        public int Count(SimEventKind kind)
        {
            int n = 0;
            foreach (SimEvent e in AllEvents)
            {
                if (e.Kind == kind)
                {
                    n++;
                }
            }

            return n;
        }

        public int Count(SimEventKind kind, Func<SimEvent, bool> predicate)
        {
            int n = 0;
            foreach (SimEvent e in AllEvents)
            {
                if (e.Kind == kind && predicate(e))
                {
                    n++;
                }
            }

            return n;
        }

        public void ClearEvents() => AllEvents.Clear();

        // ---- pose helpers ------------------------------------------------------

        public Vector3 Berth(DockId dock, int berth) => Stage.Dock(dock).Berths[berth];

        public Vector3 DockCenter(DockId dock) => Stage.Dock(dock).Center;

        /// <summary>Places a hand so its support point (where a doll stands) is exactly here.</summary>
        public void SetSupport(HandId hand, Vector3 support, float yawDegrees = 0f, float rollDegrees = 0f)
        {
            Pose3 oriented = PalmBasis.PalmUp(Vector3.Zero, yawDegrees, rollDegrees);
            Vector3 centre = support - oriented.Up * Tuning.SupportHeight;
            Hands.SetPose(hand, new Pose3(centre, oriented.Rotation));
        }

        public void SetPalmDown(HandId hand, Vector3 centre, float yawDegrees = 0f)
        {
            Hands.SetPose(hand, PalmBasis.PalmDown(centre, yawDegrees));
        }

        /// <summary>Moves a hand well away from every dock and the other hand.</summary>
        public void ParkHand(HandId hand)
        {
            float x = hand == HandId.Left ? -0.25f : 0.25f;
            SetSupport(hand, new Vector3(x, 1.15f, 0.1f));
        }

        /// <summary>Support point beside a berth, approached from the stage centre (+X side of Departure).</summary>
        public Vector3 BesideBerth(DockId dock, int berth, float gap = 0.015f, float side = 1f)
        {
            Vector3 b = Berth(dock, berth);
            float offset = Tuning.SupportHalfWidth + Tuning.BerthSupportRadius + gap;
            return b + new Vector3(side * offset, 0f, 0f);
        }

        /// <summary>Puts a palm beside a berth (support at berth height).</summary>
        public void OfferPalmAtBerth(HandId hand, DockId dock, int berth, float gap = 0.015f)
        {
            float side = Berth(dock, berth).X - StageOrigin.X > 0f ? -1f : 1f;
            SetSupport(hand, BesideBerth(dock, berth, gap, side));
        }

        /// <summary>Support point for a receiving palm placed side by side with a carrier at the given gap.</summary>
        public Vector3 BesidePalm(Vector3 carrierSupport, float gap = 0.02f, float side = 1f)
        {
            return carrierSupport + new Vector3(side * (2f * Tuning.SupportHalfWidth + gap), 0f, 0f);
        }

        /// <summary>A hop zone inside a dock's dry area, away from its berths (only valid for <see cref="OpenVariant"/>).</summary>
        public Vector3 HopZone(DockId dock)
        {
            Vector3 c = DockCenter(dock);
            Vector3 towardsCentre = Vector3.Normalize(HMath.Horizontal(StageOrigin - c) + new Vector3(0f, 0f, -0.0001f));
            Vector3 zone = c + towardsCentre * 0.17f;
            return new Vector3(zone.X, c.Y + 0.05f, zone.Z);
        }

        public DollOwner Owner(DollId doll) => Ledger.Doll(doll).Owner;

        public bool IsOnPalm(DollId doll, HandId hand) => Owner(doll) == DollOwner.OnPalm(hand);

        public bool IsAtBerth(DollId doll, DockId dock, int berth) => Owner(doll) == DollOwner.AtDock(SlotRef.DockBerth(dock, berth));

        // ---- scripted actions ----------------------------------------------------

        /// <summary>Boards a doll from a berth onto a palm and waits for the commit.</summary>
        public void Board(HandId hand, DockId dock, int berth, DollId expected)
        {
            OfferPalmAtBerth(hand, dock, berth);
            bool ok = RunUntil(() => IsOnPalm(expected, hand), 3f);
            Assert.IsTrue(ok, $"{expected} did not board {hand} from {dock.Code()}[{berth}]; owner {Owner(expected)}");
            Settle(0.6f);
        }

        /// <summary>Hops the doll on <paramref name="from"/> onto the other palm at a hop zone.</summary>
        public void Hop(HandId from, DollId doll, Vector3 zone)
        {
            HandId to = from.Other();
            float side = to == HandId.Right ? 1f : -1f;
            Vector3 carrier = zone - new Vector3(side * (Tuning.SupportHalfWidth + 0.01f), 0f, 0f);
            SetSupport(from, carrier);
            ParkHand(to);
            Settle(0.3f);
            SetSupport(to, BesidePalm(carrier, 0.02f, side));
            bool ok = RunUntil(() => IsOnPalm(doll, to), 3f);
            Assert.IsTrue(ok, $"{doll} did not hop {from}->{to}; owner {Owner(doll)}");
            ParkHand(from);
            Settle(0.6f);
        }

        /// <summary>Deposits the riding doll at a berth and waits for the commit.</summary>
        public void Deposit(HandId hand, DollId doll, DockId dock, int berth)
        {
            OfferPalmAtBerth(hand, dock, berth);
            bool ok = RunUntil(() => IsAtBerth(doll, dock, berth), 3f);
            Assert.IsTrue(ok, $"{doll} was not deposited at {dock.Code()}[{berth}]; owner {Owner(doll)}");
            ParkHand(hand);
            Settle(0.6f);
        }
    }
}
