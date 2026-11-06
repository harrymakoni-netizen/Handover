using System.Numerics;

namespace Handover.Core
{
    /// <summary>The presenter states of Section 14; derived from simulation state, never written back.</summary>
    public enum DollPose
    {
        Waiting = 0,
        Anticipating = 1,
        Riding = 2,
        Hopping = 3,
        Sheltering = 4,
        Suspended = 5,
        Rescued = 6,
    }

    /// <summary>Everything a doll view needs for one frame.</summary>
    public struct DollPresentation
    {
        public DollId Doll;
        public Personality Personality;
        public DollPose Pose;
        /// <summary>World position of the doll's feet.</summary>
        public Vector3 Position;
        /// <summary>Where the doll looks (the palm it leans to, the receiving palm, or the player).</summary>
        public Vector3 LookTarget;
        public bool HasLookTarget;
        /// <summary>0..1 lean towards the look target (candidate dwell or anticipation).</summary>
        public float Lean;
        /// <summary>Crouch for unsafe tilt or exposure.</summary>
        public bool Crouch;
        public bool Clinging;
        /// <summary>Normalized arc progress while hopping.</summary>
        public float HopProgress;
        /// <summary>The palm frame up axis while riding (to stand perpendicular to the palm).</summary>
        public Vector3 SupportUp;
    }

    /// <summary>Pure derivation of what each doll should look like from the authoritative state.</summary>
    public static class PresentationModel
    {
        public static DollPresentation Compute(HandoverSimulation sim, DollId doll)
        {
            RescueLedger ledger = sim.Ledger;
            DollRecord record = ledger.Doll(doll);
            var p = new DollPresentation
            {
                Doll = doll,
                Personality = record.Personality,
                SupportUp = HMath.Up,
            };

            switch (record.Owner.Kind)
            {
                case OwnerKind.SafetyHold:
                    p.Pose = DollPose.Suspended;
                    p.Position = record.HoldPosition;
                    break;

                case OwnerKind.Dock:
                {
                    SlotRef berth = record.Owner.Slot;
                    p.Position = sim.Stage != null ? sim.Stage.BerthPosition(berth) : Vector3.Zero;
                    p.Pose = berth.Dock == DockId.Harbour ? DollPose.Rescued : DollPose.Waiting;
                    ApplyCandidate(sim, doll, ref p);
                    break;
                }

                case OwnerKind.Palm:
                {
                    PalmPlatform palm = sim.Palm(record.Owner.Slot.Hand);
                    p.Position = palm.SupportCenter;
                    p.SupportUp = palm.Normal;
                    p.Pose = DollPose.Riding;
                    p.Clinging = palm.Clinging;
                    bool exposed = sim.Flood.ExposedSeconds(palm.Hand) >= sim.Chapter.ShelterCueSeconds;
                    if (palm.TiltWarning || exposed)
                    {
                        p.Pose = DollPose.Sheltering;
                        p.Crouch = true;
                    }
                    else
                    {
                        ApplyCandidate(sim, doll, ref p);
                    }

                    break;
                }

                case OwnerKind.Transfer:
                {
                    TransferTransaction tx = sim.Transfers.FindActive(doll);
                    if (tx == null)
                    {
                        break;
                    }

                    p.Position = tx.CurrentPosition;
                    p.LookTarget = sim.Transfers.EndpointPosition(tx.Target);
                    p.HasLookTarget = true;
                    if (tx.Stage == TransferStage.Anticipating)
                    {
                        p.Pose = DollPose.Anticipating;
                        p.Lean = HMath.Clamp01(tx.AnticipationElapsed / System.Math.Max(1e-4f, tx.AnticipationSeconds));
                    }
                    else
                    {
                        p.Pose = DollPose.Hopping;
                        p.HopProgress = tx.Progress;
                        p.Lean = 1f;
                    }

                    break;
                }
            }

            return p;
        }

        private static void ApplyCandidate(HandoverSimulation sim, DollId doll, ref DollPresentation p)
        {
            foreach (ConnectionCandidate c in sim.Resolver.Candidates)
            {
                if (c.Doll != doll)
                {
                    continue;
                }

                p.Pose = DollPose.Anticipating;
                p.LookTarget = sim.Transfers.EndpointPosition(c.Target);
                p.HasLookTarget = true;
                // React at once (a visible lean as soon as a palm is offered), then lean further as dwell builds.
                p.Lean = 0.2f + 0.4f * sim.Resolver.DwellProgress(c);
                return;
            }
        }
    }
}
