using System;
using System.Collections.Generic;
using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// The single authority for slot occupancy and doll ownership. Every mutation is an
    /// atomic transition that keeps these invariants:
    /// one owner per doll; one occupied-or-reserved state per slot; a transfer reserves
    /// exactly its source and target slots and is the doll's only owner meanwhile;
    /// reservations are released exactly once (commit, rollback, recovery or unload).
    /// Presentation code may read the ledger but never writes it.
    /// </summary>
    public sealed class RescueLedger
    {
        /// <summary>Hold epoch that no palm can match (holds created by an origin reset).</summary>
        public const int NoBlendEpoch = int.MinValue;

        private readonly Dictionary<SlotRef, SlotState> _slots = new Dictionary<SlotRef, SlotState>();
        private readonly List<SlotRef> _slotOrder = new List<SlotRef>();
        private readonly int[] _berthCounts = new int[4];
        private readonly DollRecord[] _dolls = new DollRecord[4];
        private readonly Dictionary<TransactionId, Reservation> _reservations = new Dictionary<TransactionId, Reservation>();

        private readonly struct Reservation
        {
            public readonly DollId Doll;
            public readonly SlotRef Source;
            public readonly SlotRef Target;

            public Reservation(DollId doll, SlotRef source, SlotRef target)
            {
                Doll = doll;
                Source = source;
                Target = target;
            }
        }

        /// <param name="berthCounts">Berths per dock, indexed by <see cref="DockId"/>.</param>
        /// <param name="dolls">Passengers with their personalities; each starts at its Departure home berth.</param>
        public RescueLedger(IReadOnlyList<int> berthCounts, IReadOnlyList<(DollId id, Personality personality, int homeBerth)> dolls)
        {
            foreach (HandId hand in Ids.Hands)
            {
                AddSlot(SlotRef.Palm(hand));
            }

            foreach (DockId dock in Ids.Docks)
            {
                int count = berthCounts[(int)dock];
                if (count < 1)
                {
                    throw new ArgumentException($"Dock {dock.Code()} needs at least one berth.");
                }

                _berthCounts[(int)dock] = count;
                for (int b = 0; b < count; b++)
                {
                    AddSlot(SlotRef.DockBerth(dock, b));
                }
            }

            foreach (var (id, personality, homeBerth) in dolls)
            {
                if (homeBerth < 0 || homeBerth >= _berthCounts[(int)DockId.Departure])
                {
                    throw new ArgumentException($"{id} has no Departure berth {homeBerth}.");
                }

                _dolls[(int)id] = new DollRecord(id, personality, SlotRef.DockBerth(DockId.Departure, homeBerth));
            }

            foreach (DollId id in Ids.Dolls)
            {
                if (_dolls[(int)id] == null)
                {
                    throw new ArgumentException($"Missing passenger {id}.");
                }
            }

            ResetToDeparture();
        }

        private void AddSlot(SlotRef slot)
        {
            _slots.Add(slot, SlotState.Empty);
            _slotOrder.Add(slot);
        }

        public IReadOnlyList<SlotRef> AllSlots => _slotOrder;
        public int ActiveReservationCount => _reservations.Count;

        public int BerthCount(DockId dock) => _berthCounts[(int)dock];

        public DollRecord Doll(DollId id) => _dolls[(int)id];

        public SlotState Get(SlotRef slot)
        {
            return _slots.TryGetValue(slot, out SlotState state) ? state : SlotState.Empty;
        }

        public bool Exists(SlotRef slot) => _slots.ContainsKey(slot);

        public bool IsEmpty(SlotRef slot) => Exists(slot) && Get(slot).IsEmpty;

        /// <summary>The doll riding a palm, if any (reserved palms report their doll too).</summary>
        public DollId PalmDoll(HandId hand)
        {
            SlotState s = Get(SlotRef.Palm(hand));
            return s.Kind == SlotStateKind.Empty ? DollId.None : s.Doll;
        }

        public bool PalmIsOccupied(HandId hand) => Get(SlotRef.Palm(hand)).IsOccupied;

        public bool PalmIsEmpty(HandId hand) => Get(SlotRef.Palm(hand)).IsEmpty;

        public int CountAtDock(DockId dock, bool includeReserved)
        {
            int n = 0;
            for (int b = 0; b < _berthCounts[(int)dock]; b++)
            {
                SlotState s = Get(SlotRef.DockBerth(dock, b));
                if (s.IsOccupied || (includeReserved && s.IsReserved))
                {
                    n++;
                }
            }

            return n;
        }

        public bool DockHasReservation(DockId dock)
        {
            for (int b = 0; b < _berthCounts[(int)dock]; b++)
            {
                if (Get(SlotRef.DockBerth(dock, b)).IsReserved)
                {
                    return true;
                }
            }

            return false;
        }

        public bool DollIsAtDock(DollId doll, DockId dock)
        {
            DollOwner owner = Doll(doll).Owner;
            return owner.Kind == OwnerKind.Dock && owner.Slot.Dock == dock;
        }

        public bool TryGetReservation(TransactionId tx, out DollId doll, out SlotRef source, out SlotRef target)
        {
            if (_reservations.TryGetValue(tx, out Reservation r))
            {
                doll = r.Doll;
                source = r.Source;
                target = r.Target;
                return true;
            }

            doll = DollId.None;
            source = SlotRef.None;
            target = SlotRef.None;
            return false;
        }

        // ---------------------------------------------------------------------
        // Transitions
        // ---------------------------------------------------------------------

        /// <summary>Clears every slot and places all passengers at their Departure home berths.</summary>
        public void ResetToDeparture()
        {
            var placements = new List<(DollId, SlotRef)>();
            foreach (DollId id in Ids.Dolls)
            {
                placements.Add((id, Doll(id).HomeBerth));
            }

            Restore(placements);
        }

        /// <summary>
        /// Replaces the whole occupancy with docked placements (checkpoint restore).
        /// Every reservation is discarded; callers must have ended their transactions.
        /// </summary>
        public void Restore(IReadOnlyList<(DollId doll, SlotRef berth)> placements)
        {
            var seen = new HashSet<DollId>();
            var used = new HashSet<SlotRef>();
            foreach (var (doll, berth) in placements)
            {
                if (!berth.IsDock || !Exists(berth))
                {
                    throw new ArgumentException($"{doll} must restore to an existing dock berth, not {berth}.");
                }

                if (!seen.Add(doll) || !used.Add(berth))
                {
                    throw new ArgumentException("Restore placements must be unique per doll and per berth.");
                }
            }

            foreach (DollId id in Ids.Dolls)
            {
                if (!seen.Contains(id))
                {
                    throw new ArgumentException($"Restore is missing {id}.");
                }
            }

            _reservations.Clear();
            foreach (SlotRef slot in _slotOrder)
            {
                _slots[slot] = SlotState.Empty;
            }

            foreach (var (doll, berth) in placements)
            {
                _slots[berth] = SlotState.Occupied(doll);
                DollRecord record = Doll(doll);
                record.Owner = DollOwner.AtDock(berth);
                record.LastSafeDock = berth;
                record.HoldHasReturnHand = false;
            }
        }

        /// <summary>
        /// Atomically reserves both endpoints and makes the transaction the doll's only owner.
        /// Source must be occupied by the doll; target must be empty.
        /// </summary>
        public bool TryReserve(TransactionId tx, DollId doll, SlotRef source, SlotRef target, out string error)
        {
            error = null;
            if (!tx.IsValid || _reservations.ContainsKey(tx))
            {
                error = "Transaction id is invalid or already in use.";
                return false;
            }

            if (source == target || !Exists(source) || !Exists(target))
            {
                error = $"Invalid endpoints {source} -> {target}.";
                return false;
            }

            SlotState s = Get(source);
            if (!s.IsOccupied || s.Doll != doll)
            {
                error = $"Source {source} is {s}, not Occupied({doll}).";
                return false;
            }

            DollRecord record = Doll(doll);
            if (record.Owner != DollOwner.ForSlot(source))
            {
                error = $"{doll} is owned by {record.Owner}, not {source}.";
                return false;
            }

            SlotState t = Get(target);
            if (!t.IsEmpty)
            {
                error = $"Target {target} is {t}.";
                return false;
            }

            _slots[source] = SlotState.Reserved(tx, doll);
            _slots[target] = SlotState.Reserved(tx, doll);
            _reservations.Add(tx, new Reservation(doll, source, target));
            record.Owner = DollOwner.InTransfer(tx);
            return true;
        }

        /// <summary>Receiver reservation becomes occupancy; the source is released. Exactly once.</summary>
        public void Commit(TransactionId tx)
        {
            Reservation r = Take(tx);
            _slots[r.Source] = SlotState.Empty;
            _slots[r.Target] = SlotState.Occupied(r.Doll);
            DollRecord record = Doll(r.Doll);
            record.Owner = DollOwner.ForSlot(r.Target);
            if (r.Target.IsDock)
            {
                record.LastSafeDock = r.Target;
            }
        }

        /// <summary>The doll returns to the source; the target is released. Exactly once.</summary>
        public void Rollback(TransactionId tx)
        {
            Reservation r = Take(tx);
            _slots[r.Target] = SlotState.Empty;
            _slots[r.Source] = SlotState.Occupied(r.Doll);
            Doll(r.Doll).Owner = DollOwner.ForSlot(r.Source);
        }

        /// <summary>Both endpoints are released and the doll waits in the stationary system hold.</summary>
        public void RecoverToHold(TransactionId tx, Vector3 holdPosition, bool hasReturnHand, HandId returnHand, int originEpoch)
        {
            Reservation r = Take(tx);
            _slots[r.Source] = SlotState.Empty;
            _slots[r.Target] = SlotState.Empty;
            EnterHold(Doll(r.Doll), holdPosition, hasReturnHand, returnHand, originEpoch);
        }

        /// <summary>A riding doll is secured in the system hold at its current position.</summary>
        public void MovePalmDollToHold(HandId hand, Vector3 holdPosition, int originEpoch)
        {
            SlotRef palm = SlotRef.Palm(hand);
            SlotState s = Get(palm);
            if (!s.IsOccupied)
            {
                throw new InvalidOperationException($"{palm} is {s}; only an occupied palm can be secured.");
            }

            _slots[palm] = SlotState.Empty;
            EnterHold(Doll(s.Doll), holdPosition, true, hand, originEpoch);
        }

        private static void EnterHold(DollRecord record, Vector3 position, bool hasReturnHand, HandId returnHand, int originEpoch)
        {
            record.Owner = DollOwner.InSafetyHold();
            record.HoldPosition = position;
            record.HoldHasReturnHand = hasReturnHand;
            record.HoldReturnHand = returnHand;
            record.HoldOriginEpoch = originEpoch;
        }

        /// <summary>Blend back onto the returning palm (which must be empty).</summary>
        public void ReleaseHoldToPalm(DollId doll, HandId hand)
        {
            DollRecord record = RequireHeld(doll);
            SlotRef palm = SlotRef.Palm(hand);
            if (!Get(palm).IsEmpty)
            {
                throw new InvalidOperationException($"{palm} is not empty.");
            }

            _slots[palm] = SlotState.Occupied(doll);
            record.Owner = DollOwner.OnPalm(hand);
            record.HoldHasReturnHand = false;
        }

        /// <summary>Restore a held doll to a dock berth (which must be empty).</summary>
        public void ReleaseHoldToDock(DollId doll, SlotRef berth)
        {
            DollRecord record = RequireHeld(doll);
            if (!berth.IsDock || !Get(berth).IsEmpty)
            {
                throw new InvalidOperationException($"{berth} is not an empty dock berth.");
            }

            _slots[berth] = SlotState.Occupied(doll);
            record.Owner = DollOwner.AtDock(berth);
            record.LastSafeDock = berth;
            record.HoldHasReturnHand = false;
        }

        /// <summary>Moves a riding doll straight to a dock berth (system local reset).</summary>
        public void ReturnPalmDollToDock(HandId hand, SlotRef berth)
        {
            SlotRef palm = SlotRef.Palm(hand);
            SlotState s = Get(palm);
            if (!s.IsOccupied)
            {
                throw new InvalidOperationException($"{palm} is {s}.");
            }

            if (!berth.IsDock || !Get(berth).IsEmpty)
            {
                throw new InvalidOperationException($"{berth} is not an empty dock berth.");
            }

            _slots[palm] = SlotState.Empty;
            _slots[berth] = SlotState.Occupied(s.Doll);
            DollRecord record = Doll(s.Doll);
            record.Owner = DollOwner.AtDock(berth);
            record.LastSafeDock = berth;
        }

        private DollRecord RequireHeld(DollId doll)
        {
            DollRecord record = Doll(doll);
            if (!record.IsInSafetyHold)
            {
                throw new InvalidOperationException($"{doll} is not in the system hold.");
            }

            return record;
        }

        private Reservation Take(TransactionId tx)
        {
            if (!_reservations.TryGetValue(tx, out Reservation r))
            {
                throw new InvalidOperationException($"{tx} holds no reservation (already released?).");
            }

            _reservations.Remove(tx);
            return r;
        }

        // ---------------------------------------------------------------------
        // Invariants
        // ---------------------------------------------------------------------

        /// <summary>Returns every violated invariant; an empty list means the ledger is consistent.</summary>
        public List<string> ValidateInvariants()
        {
            var problems = new List<string>();
            var reservedSlotsByTx = new Dictionary<TransactionId, int>();

            foreach (SlotRef slot in _slotOrder)
            {
                SlotState s = _slots[slot];
                switch (s.Kind)
                {
                    case SlotStateKind.Occupied:
                    {
                        DollOwner owner = Doll(s.Doll).Owner;
                        if (owner != DollOwner.ForSlot(slot))
                        {
                            problems.Add($"{slot} is occupied by {s.Doll} but the doll's owner is {owner}.");
                        }

                        break;
                    }

                    case SlotStateKind.Reserved:
                    {
                        if (!_reservations.TryGetValue(s.Transaction, out Reservation r))
                        {
                            problems.Add($"{slot} is reserved by unknown {s.Transaction} (leaked slot).");
                        }
                        else if ((r.Source != slot && r.Target != slot) || r.Doll != s.Doll)
                        {
                            problems.Add($"{slot} reservation does not match {s.Transaction}.");
                        }

                        reservedSlotsByTx.TryGetValue(s.Transaction, out int n);
                        reservedSlotsByTx[s.Transaction] = n + 1;
                        break;
                    }
                }
            }

            foreach (var pair in _reservations)
            {
                reservedSlotsByTx.TryGetValue(pair.Key, out int n);
                if (n != 2)
                {
                    problems.Add($"{pair.Key} should reserve exactly two slots but reserves {n}.");
                }

                DollOwner owner = Doll(pair.Value.Doll).Owner;
                if (owner != DollOwner.InTransfer(pair.Key))
                {
                    problems.Add($"{pair.Value.Doll} should be owned by {pair.Key} but is owned by {owner}.");
                }
            }

            foreach (DollId id in Ids.Dolls)
            {
                DollRecord record = Doll(id);
                int references = 0;
                foreach (SlotRef slot in _slotOrder)
                {
                    SlotState s = _slots[slot];
                    if (s.Kind != SlotStateKind.Empty && s.Doll == id)
                    {
                        references++;
                    }
                }

                switch (record.Owner.Kind)
                {
                    case OwnerKind.Dock:
                    case OwnerKind.Palm:
                        if (references != 1)
                        {
                            problems.Add($"{id} should appear in exactly one slot but appears in {references}.");
                        }

                        break;
                    case OwnerKind.Transfer:
                        if (references != 2)
                        {
                            problems.Add($"{id} in transfer should appear in exactly two reserved slots but appears in {references}.");
                        }

                        break;
                    case OwnerKind.SafetyHold:
                        if (references != 0)
                        {
                            problems.Add($"{id} is in the system hold but still appears in {references} slot(s).");
                        }

                        break;
                    default:
                        problems.Add($"{id} has no owner.");
                        break;
                }

                if (!record.LastSafeDock.IsDock)
                {
                    problems.Add($"{id} has no last safe dock.");
                }
            }

            return problems;
        }
    }
}
