using System;

namespace Handover.Core
{
    public enum HandId
    {
        Left = 0,
        Right = 1,
    }

    /// <summary>Stable passenger identities. Numeric values are the deterministic tie-break order.</summary>
    public enum DollId
    {
        None = 0,
        Keeper = 1,
        Pip = 2,
        Luma = 3,
    }

    /// <summary>The four authored landing nodes of the chapter (D, R, U, H).</summary>
    public enum DockId
    {
        Departure = 0,
        LowRefuge = 1,
        LanternRefuge = 2,
        Harbour = 3,
    }

    public enum Personality
    {
        Deliberate = 0,
        Eager = 1,
        Hesitant = 2,
    }

    public static class Ids
    {
        public static readonly HandId[] Hands = { HandId.Left, HandId.Right };
        public static readonly DollId[] Dolls = { DollId.Keeper, DollId.Pip, DollId.Luma };
        public static readonly DockId[] Docks = { DockId.Departure, DockId.LowRefuge, DockId.LanternRefuge, DockId.Harbour };

        public static HandId Other(this HandId hand)
        {
            return hand == HandId.Left ? HandId.Right : HandId.Left;
        }

        public static bool IsOrdinaryPassenger(this DollId doll)
        {
            return doll == DollId.Pip || doll == DollId.Luma;
        }

        public static string Code(this DockId dock)
        {
            switch (dock)
            {
                case DockId.Departure: return "D";
                case DockId.LowRefuge: return "R";
                case DockId.LanternRefuge: return "U";
                case DockId.Harbour: return "H";
                default: return "?";
            }
        }

        /// <summary>Stable lower-case key used in save files.</summary>
        public static string Key(this DollId doll)
        {
            switch (doll)
            {
                case DollId.Keeper: return "keeper";
                case DollId.Pip: return "pip";
                case DollId.Luma: return "luma";
                default: return "none";
            }
        }

        public static bool TryParseDoll(string key, out DollId doll)
        {
            switch (key)
            {
                case "keeper": doll = DollId.Keeper; return true;
                case "pip": doll = DollId.Pip; return true;
                case "luma": doll = DollId.Luma; return true;
                default: doll = DollId.None; return false;
            }
        }

        public static bool TryParseDock(string code, out DockId dock)
        {
            switch (code)
            {
                case "D": dock = DockId.Departure; return true;
                case "R": dock = DockId.LowRefuge; return true;
                case "U": dock = DockId.LanternRefuge; return true;
                case "H": dock = DockId.Harbour; return true;
                default: dock = DockId.Departure; return false;
            }
        }
    }

    /// <summary>Monotonic identifier of one transfer transaction. Zero means "none".</summary>
    public readonly struct TransactionId : IEquatable<TransactionId>, IComparable<TransactionId>
    {
        public readonly long Value;

        public TransactionId(long value)
        {
            Value = value;
        }

        public static TransactionId None => default;
        public bool IsValid => Value > 0;

        public bool Equals(TransactionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TransactionId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(TransactionId other) => Value.CompareTo(other.Value);
        public static bool operator ==(TransactionId a, TransactionId b) => a.Value == b.Value;
        public static bool operator !=(TransactionId a, TransactionId b) => a.Value != b.Value;
        public override string ToString() => IsValid ? $"tx#{Value}" : "tx#none";
    }

    public enum SlotKind
    {
        None = 0,
        Palm = 1,
        Dock = 2,
    }

    /// <summary>Addresses one slot: a palm's single slot or one berth of a dock.</summary>
    public readonly struct SlotRef : IEquatable<SlotRef>
    {
        public readonly SlotKind Kind;
        public readonly HandId Hand;
        public readonly DockId Dock;
        public readonly int Berth;

        private SlotRef(SlotKind kind, HandId hand, DockId dock, int berth)
        {
            Kind = kind;
            Hand = hand;
            Dock = dock;
            Berth = berth;
        }

        public static SlotRef None => default;
        public static SlotRef Palm(HandId hand) => new SlotRef(SlotKind.Palm, hand, default, 0);
        public static SlotRef DockBerth(DockId dock, int berth) => new SlotRef(SlotKind.Dock, default, dock, berth);

        public bool IsNone => Kind == SlotKind.None;
        public bool IsPalm => Kind == SlotKind.Palm;
        public bool IsDock => Kind == SlotKind.Dock;

        public bool Equals(SlotRef other)
        {
            if (Kind != other.Kind)
            {
                return false;
            }

            switch (Kind)
            {
                case SlotKind.Palm: return Hand == other.Hand;
                case SlotKind.Dock: return Dock == other.Dock && Berth == other.Berth;
                default: return true;
            }
        }

        public override bool Equals(object obj) => obj is SlotRef other && Equals(other);

        public override int GetHashCode()
        {
            switch (Kind)
            {
                case SlotKind.Palm: return HashCode.Combine(Kind, Hand);
                case SlotKind.Dock: return HashCode.Combine(Kind, Dock, Berth);
                default: return 0;
            }
        }

        public static bool operator ==(SlotRef a, SlotRef b) => a.Equals(b);
        public static bool operator !=(SlotRef a, SlotRef b) => !a.Equals(b);

        public override string ToString()
        {
            switch (Kind)
            {
                case SlotKind.Palm: return $"Palm({Hand})";
                case SlotKind.Dock: return $"{Dock.Code()}[{Berth}]";
                default: return "None";
            }
        }
    }
}
