namespace Handover.Core
{
    public enum TransferKind
    {
        /// <summary>Dock berth to palm.</summary>
        Boarding = 0,
        /// <summary>Palm to palm (side-by-side hop).</summary>
        Handover = 1,
        /// <summary>Palm to dock berth.</summary>
        Deposit = 2,
    }

    /// <summary>Why a dock refused an action. Shown as an explanatory cue; never drops a doll.</summary>
    public enum RejectReason
    {
        None = 0,
        /// <summary>Gameplay is not running (setup, paused, recovering, results).</summary>
        NotRunning,
        /// <summary>Only Keeper may board until the teaching hop is complete.</summary>
        KeeperFirst,
        /// <summary>The lantern refuge unlocks after Keeper's teaching hop.</summary>
        LanternLocked,
        /// <summary>The lantern socket only accepts Keeper.</summary>
        LanternKeeperOnly,
        /// <summary>The harbour is dark: Keeper must hold the lantern at U.</summary>
        HarbourDark,
        /// <summary>Keeper comes home last, after the lantern is latched.</summary>
        KeeperComesHomeLast,
        /// <summary>The low refuge is closed or closing.</summary>
        RefugeClosed,
        /// <summary>A doll may only return to its own Departure berth.</summary>
        NotHomeBerth,
        /// <summary>Rescued passengers stay at the harbour.</summary>
        AlreadyHome,
        /// <summary>Every berth is occupied or reserved.</summary>
        DockFull,
        /// <summary>Keeper cannot leave the lantern while a passenger is landing at the harbour.</summary>
        HarbourLanding,
    }

    public enum SimEventKind
    {
        // Session
        SessionStateChanged,
        ChapterStarted,

        // Palms
        TrackingLost,
        TrackingRestored,
        OriginChanged,
        TiltWarningStarted,
        TiltWarningCleared,
        ClingStarted,
        ClingEnded,
        ShieldRaised,
        ShieldLowered,

        // Candidates
        CandidateOpened,
        CandidateClosed,
        DockRejected,

        // Transfers
        TransferReserved,
        TransferTakeoff,
        TransferCommitted,
        TransferRolledBack,
        TransferRecovered,
        TransferReturnedToSafety,

        // Recovery
        SafetyHoldEntered,
        SafetyHoldBlendedBack,
        SafetyHoldRestored,

        // Chapter
        TutorialHopCompleted,
        LanternLit,
        LanternDimmed,
        LanternLatched,
        DollRescued,
        ChapterCompleted,

        // Flood
        FloodPhaseChanged,
        RefugeClosurePending,
        RefugeClosed,
        RefugeReopened,
        ShelterCue,
        ExposureCleared,
        ExposureReset,

        // Persistence
        CheckpointCreated,
        CheckpointRestored,
        CheckpointRejected,
    }

    /// <summary>
    /// One simulation event. A single allocation-free struct; unused fields keep defaults.
    /// <see cref="Code"/> carries an enum payload (state, reason, phase) as an int.
    /// Presenters consume events; they never decide rescue counts or ownership.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly SimEventKind Kind;
        public readonly DollId Doll;
        public readonly TransactionId Transaction;
        public readonly SlotRef From;
        public readonly SlotRef To;
        public readonly HandId Hand;
        public readonly int Code;
        public readonly float Value;

        public SimEvent(SimEventKind kind, DollId doll = DollId.None, TransactionId transaction = default,
            SlotRef from = default, SlotRef to = default, HandId hand = HandId.Left, int code = 0, float value = 0f)
        {
            Kind = kind;
            Doll = doll;
            Transaction = transaction;
            From = from;
            To = to;
            Hand = hand;
            Code = code;
            Value = value;
        }

        public TransferKind TransferKind => (TransferKind)Code;
        public RejectReason Reason => (RejectReason)Code;

        public override string ToString()
        {
            return $"{Kind} doll={Doll} {Transaction} {From}->{To} hand={Hand} code={Code} value={Value:0.###}";
        }
    }
}
