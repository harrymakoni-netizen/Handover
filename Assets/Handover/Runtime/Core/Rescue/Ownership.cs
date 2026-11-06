using System;
using System.Numerics;

namespace Handover.Core
{
    public enum SlotStateKind
    {
        Empty = 0,
        Occupied = 1,
        Reserved = 2,
    }

    /// <summary>A slot is Empty, Occupied(doll) or Reserved(transaction, doll) - never two at once.</summary>
    public readonly struct SlotState : IEquatable<SlotState>
    {
        public readonly SlotStateKind Kind;
        public readonly DollId Doll;
        public readonly TransactionId Transaction;

        private SlotState(SlotStateKind kind, DollId doll, TransactionId transaction)
        {
            Kind = kind;
            Doll = doll;
            Transaction = transaction;
        }

        public static SlotState Empty => default;
        public static SlotState Occupied(DollId doll) => new SlotState(SlotStateKind.Occupied, doll, TransactionId.None);
        public static SlotState Reserved(TransactionId tx, DollId doll) => new SlotState(SlotStateKind.Reserved, doll, tx);

        public bool IsEmpty => Kind == SlotStateKind.Empty;
        public bool IsOccupied => Kind == SlotStateKind.Occupied;
        public bool IsReserved => Kind == SlotStateKind.Reserved;

        public bool Equals(SlotState other) => Kind == other.Kind && Doll == other.Doll && Transaction == other.Transaction;
        public override bool Equals(object obj) => obj is SlotState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, Doll, Transaction);

        public override string ToString()
        {
            switch (Kind)
            {
                case SlotStateKind.Occupied: return $"Occupied({Doll})";
                case SlotStateKind.Reserved: return $"Reserved({Transaction}, {Doll})";
                default: return "Empty";
            }
        }
    }

    public enum OwnerKind
    {
        None = 0,
        Dock = 1,
        Palm = 2,
        Transfer = 3,
        SafetyHold = 4,
    }

    /// <summary>Exactly one owner per doll: a dock berth, a palm, a transfer transaction or the system hold.</summary>
    public readonly struct DollOwner : IEquatable<DollOwner>
    {
        public readonly OwnerKind Kind;
        public readonly SlotRef Slot;
        public readonly TransactionId Transaction;

        private DollOwner(OwnerKind kind, SlotRef slot, TransactionId transaction)
        {
            Kind = kind;
            Slot = slot;
            Transaction = transaction;
        }

        public static DollOwner None => default;
        public static DollOwner AtDock(SlotRef berth) => new DollOwner(OwnerKind.Dock, berth, TransactionId.None);
        public static DollOwner OnPalm(HandId hand) => new DollOwner(OwnerKind.Palm, SlotRef.Palm(hand), TransactionId.None);
        public static DollOwner InTransfer(TransactionId tx) => new DollOwner(OwnerKind.Transfer, SlotRef.None, tx);
        public static DollOwner InSafetyHold() => new DollOwner(OwnerKind.SafetyHold, SlotRef.None, TransactionId.None);

        public static DollOwner ForSlot(SlotRef slot)
        {
            return slot.IsPalm ? OnPalm(slot.Hand) : AtDock(slot);
        }

        public bool Equals(DollOwner other) => Kind == other.Kind && Slot == other.Slot && Transaction == other.Transaction;
        public override bool Equals(object obj) => obj is DollOwner other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, Slot, Transaction);
        public static bool operator ==(DollOwner a, DollOwner b) => a.Equals(b);
        public static bool operator !=(DollOwner a, DollOwner b) => !a.Equals(b);

        public override string ToString()
        {
            switch (Kind)
            {
                case OwnerKind.Dock: return $"Dock {Slot}";
                case OwnerKind.Palm: return $"Palm {Slot.Hand}";
                case OwnerKind.Transfer: return $"Transfer {Transaction}";
                case OwnerKind.SafetyHold: return "SafetyHold";
                default: return "None";
            }
        }
    }

    /// <summary>Runtime record of one passenger.</summary>
    public sealed class DollRecord
    {
        public DollRecord(DollId id, Personality personality, SlotRef homeBerth)
        {
            Id = id;
            Personality = personality;
            HomeBerth = homeBerth;
            LastSafeDock = homeBerth;
        }

        public DollId Id { get; }
        public Personality Personality { get; }
        /// <summary>The doll's named berth at Departure; always free for this doll alone.</summary>
        public SlotRef HomeBerth { get; }
        public DollOwner Owner { get; internal set; }
        /// <summary>The last dock berth the doll rested on; the rollback target for recovery.</summary>
        public SlotRef LastSafeDock { get; internal set; }

        // System hold details (valid while Owner is SafetyHold).
        public Vector3 HoldPosition { get; internal set; }
        public bool HoldHasReturnHand { get; internal set; }
        public HandId HoldReturnHand { get; internal set; }
        public int HoldOriginEpoch { get; internal set; }

        public bool IsInSafetyHold => Owner.Kind == OwnerKind.SafetyHold;

        public override string ToString() => $"{Id}: {Owner}";
    }
}
