using System.Numerics;

namespace Handover.Core
{
    public enum TransferStage
    {
        /// <summary>Both endpoints reserved; the doll looks and rocks before take-off.</summary>
        Anticipating = 0,
        /// <summary>The doll follows the kinematic arc.</summary>
        Hopping = 1,
        Committed = 2,
        RolledBack = 3,
        /// <summary>Tracking failed mid-hop; the doll waits in the system hold.</summary>
        Recovered = 4,
        /// <summary>Hands separated after the midpoint; the doll was returned to its last safe dock.</summary>
        ReturnedToSafety = 5,
    }

    public enum TransferEndReason
    {
        None = 0,
        Committed = 1,
        AlignmentLost = 2,
        SeparatedBeforeMidpoint = 3,
        ReceiverUnsafeAfterMidpoint = 4,
        TrackingLost = 5,
        Paused = 6,
        OriginChanged = 7,
        Unloaded = 8,
        Restored = 9,
    }

    /// <summary>
    /// One reserved hop. Candidate and Stable live in the resolver; from Reserved onwards the
    /// transaction is the doll's single owner until it commits, rolls back or recovers.
    /// </summary>
    public sealed class TransferTransaction
    {
        public TransferTransaction(TransactionId id, TransferKind kind, DollId doll, SlotRef source, SlotRef target,
            float anticipationSeconds, float hopSeconds, float arcHeight)
        {
            Id = id;
            Kind = kind;
            Doll = doll;
            Source = source;
            Target = target;
            AnticipationSeconds = anticipationSeconds;
            HopSeconds = hopSeconds;
            ArcHeight = arcHeight;
            Stage = TransferStage.Anticipating;
        }

        public TransactionId Id { get; }
        public TransferKind Kind { get; }
        public DollId Doll { get; }
        public SlotRef Source { get; }
        public SlotRef Target { get; }
        public float AnticipationSeconds { get; }
        public float HopSeconds { get; }
        public float ArcHeight { get; }

        public TransferStage Stage { get; internal set; }
        public TransferEndReason EndReason { get; internal set; }
        public float AnticipationElapsed { get; internal set; }
        /// <summary>Normalized arc progress 0..1.</summary>
        public float Progress { get; internal set; }
        /// <summary>True while progress is held because an endpoint's tracking is invalid.</summary>
        public bool ProgressFrozen { get; internal set; }
        /// <summary>After the midpoint the take-off point no longer follows the source.</summary>
        public bool SourceLatched { get; internal set; }
        public Vector3 LatchedSourcePosition { get; internal set; }
        /// <summary>Last evaluated world position of the doll's feet.</summary>
        public Vector3 CurrentPosition { get; internal set; }
        /// <summary>Palm-to-palm hops must stay inside the dry transfer area they started in.</summary>
        public bool HasDryArea { get; internal set; }
        public DockId DryAreaDock { get; internal set; }

        public bool IsActive => Stage == TransferStage.Anticipating || Stage == TransferStage.Hopping;
        public bool HasTakenOff => Stage == TransferStage.Hopping;
        public bool PastMidpoint => Progress >= 0.5f;

        public bool Involves(HandId hand)
        {
            return (Source.IsPalm && Source.Hand == hand) || (Target.IsPalm && Target.Hand == hand);
        }

        public bool Involves(SlotRef slot) => Source == slot || Target == slot;

        public override string ToString() => $"{Id} {Kind} {Doll} {Source}->{Target} {Stage} p={Progress:0.00}";
    }
}
