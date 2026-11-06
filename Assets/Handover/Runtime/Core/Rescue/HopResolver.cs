using System;
using System.Collections.Generic;
using System.Numerics;

namespace Handover.Core
{
    /// <summary>A possible transfer between two endpoints, with its readable intention (dwell).</summary>
    public sealed class ConnectionCandidate
    {
        public TransferKind Kind;
        public DollId Doll;
        public SlotRef Source;
        public SlotRef Target;
        public bool HasDryArea;
        public DockId DryAreaDock;
        public float EdgeGap;
        public float VerticalOffset;
        public float NormalDifference;
        public float RelativeSpeed;
        /// <summary>Inside the connect thresholds and settled (dwell accumulates only then).</summary>
        public bool Settled;
        public float Dwell;
        public bool Existing;

        public bool SameEndpoints(ConnectionCandidate other) => Source == other.Source && Target == other.Target && Doll == other.Doll;

        internal void CopyFrom(ConnectionCandidate o)
        {
            Kind = o.Kind;
            Doll = o.Doll;
            Source = o.Source;
            Target = o.Target;
            HasDryArea = o.HasDryArea;
            DryAreaDock = o.DryAreaDock;
            EdgeGap = o.EdgeGap;
            VerticalOffset = o.VerticalOffset;
            NormalDifference = o.NormalDifference;
            RelativeSpeed = o.RelativeSpeed;
            Settled = o.Settled;
            Dwell = o.Dwell;
            Existing = o.Existing;
        }

        public override string ToString() => $"{Kind} {Doll} {Source}->{Target} gap={EdgeGap:0.000} dwell={Dwell:0.00}";
    }

    /// <summary>
    /// Resolves intent from context as well as pose: an occupied dock offers boarding, an
    /// empty palm beside a carrying palm offers receiving, an occupied palm near an open
    /// dock offers depositing. Uses connect/hold hysteresis and a readable dwell, then hands
    /// stable candidates to the <see cref="TransferCoordinator"/>. A seam is feedback, not a collider.
    /// </summary>
    public sealed class HopResolver
    {
        private const float RejectCueInterval = 1.5f;

        private readonly SimulationContext _ctx;
        private readonly TransferCoordinator _transfers;
        private readonly List<ConnectionCandidate> _current = new List<ConnectionCandidate>();
        private readonly List<ConnectionCandidate> _raw = new List<ConnectionCandidate>();
        private readonly List<ConnectionCandidate> _next = new List<ConnectionCandidate>();
        private readonly Stack<ConnectionCandidate> _pool = new Stack<ConnectionCandidate>();
        private readonly HashSet<SlotRef> _usedSlots = new HashSet<SlotRef>();
        private readonly HashSet<DollId> _usedDolls = new HashSet<DollId>();
        private readonly Dictionary<int, double> _lastRejectCue = new Dictionary<int, double>();
        private readonly List<PendingReject> _pendingRejects = new List<PendingReject>();
        // Re-arm hysteresis: after a transfer lands, the same palm/dock (or palm pair) must
        // separate beyond the disconnect gap before another transfer between them is offered,
        // so a hand held still never ping-pongs a doll back.
        private readonly bool[,] _dockNeedsSeparation = new bool[2, 4];
        private bool _pairNeedsSeparation;

        private readonly struct PendingReject
        {
            public readonly HandId Hand;
            public readonly DockId Dock;
            public readonly DollId Doll;
            public readonly RejectReason Reason;

            public PendingReject(HandId hand, DockId dock, DollId doll, RejectReason reason)
            {
                Hand = hand;
                Dock = dock;
                Doll = doll;
                Reason = reason;
            }
        }

        public HopResolver(SimulationContext context, TransferCoordinator transfers)
        {
            _ctx = context;
            _transfers = transfers;
        }

        /// <summary>Candidates currently shown (seams and leaning dolls).</summary>
        public IReadOnlyList<ConnectionCandidate> Candidates => _current;

        /// <summary>Called for every committed transfer so the involved endpoints must separate before re-engaging.</summary>
        public void NoteCommitted(TransferTransaction tx)
        {
            if (tx.Source.IsPalm && tx.Target.IsPalm)
            {
                _pairNeedsSeparation = true;
            }
            else if (tx.Source.IsPalm)
            {
                _dockNeedsSeparation[(int)tx.Source.Hand, (int)tx.Target.Dock] = true;
            }
            else if (tx.Target.IsPalm)
            {
                _dockNeedsSeparation[(int)tx.Target.Hand, (int)tx.Source.Dock] = true;
            }
        }

        /// <summary>Forget re-arm state (replay, restore, reposition).</summary>
        public void ResetHysteresis()
        {
            _pairNeedsSeparation = false;
            System.Array.Clear(_dockNeedsSeparation, 0, _dockNeedsSeparation.Length);
        }

        public bool PairNeedsSeparation => _pairNeedsSeparation;

        public bool DockNeedsSeparation(HandId hand, DockId dock) => _dockNeedsSeparation[(int)hand, (int)dock];

        private void UpdateRearm()
        {
            InteractionTuning t = _ctx.Tuning;
            PalmPlatform left = _ctx.Palm(HandId.Left);
            PalmPlatform right = _ctx.Palm(HandId.Right);
            if (_pairNeedsSeparation)
            {
                if (!left.IsTracked || !right.IsTracked
                    || SupportGeometry.PalmToPalm(left, right, t).EdgeGap > t.DisconnectGap)
                {
                    _pairNeedsSeparation = false;
                }
            }

            foreach (HandId hand in Ids.Hands)
            {
                PalmPlatform palm = _ctx.Palm(hand);
                foreach (DockId dock in Ids.Docks)
                {
                    if (!_dockNeedsSeparation[(int)hand, (int)dock])
                    {
                        continue;
                    }

                    SupportGeometry.Measurement nearest = NearestBerth(_ctx.Stage.Dock(dock), palm, t);
                    if (!palm.IsTracked || nearest.EdgeGap > t.DisconnectGap || nearest.VerticalOffset > t.HoldVerticalOffset * 2f)
                    {
                        _dockNeedsSeparation[(int)hand, (int)dock] = false;
                    }
                }
            }
        }

        public void Clear()
        {
            foreach (ConnectionCandidate c in _current)
            {
                _ctx.Emit(new SimEvent(SimEventKind.CandidateClosed, c.Doll, default, c.Source, c.Target, code: (int)c.Kind));
                _pool.Push(c);
            }

            _current.Clear();
        }

        public void Step(float simDt)
        {
            if (_ctx.Stage == null || !_ctx.Session.IsGameplayRunning)
            {
                Clear();
                return;
            }

            _pendingRejects.Clear();
            UpdateRearm();
            Generate();
            Select();
            EmitRejectCues();
            AdvanceDwellAndReserve(simDt);
        }

        // ---------------------------------------------------------------------

        private bool PalmAvailable(HandId hand)
        {
            PalmPlatform p = _ctx.Palm(hand);
            return p.IsTracked && p.CarryEligible && !p.Clinging && !_transfers.IsHandBusy(hand) && !_transfers.IsInCooldown(hand);
        }

        private void Generate()
        {
            _raw.Clear();
            InteractionTuning t = _ctx.Tuning;
            RescueLedger ledger = _ctx.Ledger;

            foreach (HandId hand in Ids.Hands)
            {
                if (!PalmAvailable(hand))
                {
                    continue;
                }

                PalmPlatform palm = _ctx.Palm(hand);
                SlotRef palmSlot = SlotRef.Palm(hand);
                SlotState palmState = ledger.Get(palmSlot);

                if (palmState.IsEmpty)
                {
                    GenerateBoarding(hand, palm, palmSlot, t);
                    GenerateReceiving(hand, palm, palmSlot, t);
                }
                else if (palmState.IsOccupied)
                {
                    GenerateDeposits(hand, palm, palmSlot, palmState.Doll, t);
                }
            }
        }

        private void GenerateBoarding(HandId hand, PalmPlatform palm, SlotRef palmSlot, InteractionTuning t)
        {
            RescueLedger ledger = _ctx.Ledger;
            foreach (DockId dock in Ids.Docks)
            {
                if (_dockNeedsSeparation[(int)hand, (int)dock])
                {
                    continue;
                }

                DockLayout layout = _ctx.Stage.Dock(dock);
                for (int b = 0; b < layout.Capacity; b++)
                {
                    SlotRef berth = SlotRef.DockBerth(dock, b);
                    SlotState s = ledger.Get(berth);
                    if (!s.IsOccupied || _transfers.IsSlotBusy(berth))
                    {
                        continue;
                    }

                    SupportGeometry.Measurement m = SupportGeometry.PalmToBerth(palm, layout.Berths[b], t);
                    bool existing = HasCurrent(berth, palmSlot, s.Doll);
                    if (!DockInRange(m, existing, t))
                    {
                        continue;
                    }

                    RejectReason reason = _ctx.Policy.CanBoard(s.Doll, berth);
                    if (reason != RejectReason.None)
                    {
                        MaybeRejectCue(hand, dock, s.Doll, reason, m, t);
                        continue;
                    }

                    float speed = palm.SupportVelocity.Length();
                    AddRaw(TransferKind.Boarding, s.Doll, berth, palmSlot, false, default, m, 0f, speed,
                        DockSettled(m, speed, t), existing);
                }
            }
        }

        private void GenerateReceiving(HandId hand, PalmPlatform receiver, SlotRef palmSlot, InteractionTuning t)
        {
            HandId other = hand.Other();
            SlotRef otherSlot = SlotRef.Palm(other);
            SlotState carrierState = _ctx.Ledger.Get(otherSlot);
            if (_pairNeedsSeparation || !carrierState.IsOccupied || !PalmAvailable(other))
            {
                return;
            }

            PalmPlatform carrier = _ctx.Palm(other);
            if (!TryFindSharedDryArea(carrier.SupportCenter, receiver.SupportCenter, out DockId dryDock))
            {
                // Outside a valid dry transfer area: no seam.
                return;
            }

            SupportGeometry.Measurement m = SupportGeometry.PalmToPalm(carrier, receiver, t);
            float normalDifference = HMath.AngleDegrees(carrier.Normal, receiver.Normal);
            float relativeSpeed = (carrier.SupportVelocity - receiver.SupportVelocity).Length();
            bool existing = HasCurrent(otherSlot, palmSlot, carrierState.Doll);

            bool inRange = existing
                ? m.EdgeGap >= t.HoldGapMin && m.EdgeGap <= t.DisconnectGap
                    && m.VerticalOffset <= t.HoldVerticalOffset && normalDifference <= t.HoldNormalDifferenceDegrees
                : m.EdgeGap >= t.ConnectGapMin && m.EdgeGap <= t.ConnectGapMax
                    && m.VerticalOffset <= t.MaxVerticalOffset && normalDifference <= t.MaxNormalDifferenceDegrees;
            if (!inRange)
            {
                return;
            }

            bool settled = m.EdgeGap >= t.ConnectGapMin && m.EdgeGap <= t.ConnectGapMax
                && m.VerticalOffset <= t.MaxVerticalOffset
                && normalDifference <= t.MaxNormalDifferenceDegrees
                && relativeSpeed <= t.MaxRelativeEdgeSpeed;
            AddRaw(TransferKind.Handover, carrierState.Doll, otherSlot, palmSlot, true, dryDock, m, normalDifference,
                relativeSpeed, settled, existing);
        }

        private void GenerateDeposits(HandId hand, PalmPlatform palm, SlotRef palmSlot, DollId doll, InteractionTuning t)
        {
            RescueLedger ledger = _ctx.Ledger;
            foreach (DockId dock in Ids.Docks)
            {
                if (_dockNeedsSeparation[(int)hand, (int)dock])
                {
                    continue;
                }

                DockLayout layout = _ctx.Stage.Dock(dock);
                int berthIndex = ChooseDepositBerth(layout, dock, doll, palm, t, out SupportGeometry.Measurement m);
                RejectReason reason = _ctx.Policy.CanDeposit(doll, dock);
                if (reason == RejectReason.None && berthIndex < 0)
                {
                    reason = RejectReason.DockFull;
                }

                if (reason != RejectReason.None)
                {
                    SupportGeometry.Measurement nearest = NearestBerth(layout, palm, t);
                    MaybeRejectCue(hand, dock, doll, reason, nearest, t);
                    continue;
                }

                SlotRef berth = SlotRef.DockBerth(dock, berthIndex);
                if (_transfers.IsSlotBusy(berth) || !ledger.IsEmpty(berth))
                {
                    continue;
                }

                bool existing = HasCurrent(palmSlot, berth, doll);
                if (!DockInRange(m, existing, t))
                {
                    continue;
                }

                float speed = palm.SupportVelocity.Length();
                AddRaw(TransferKind.Deposit, doll, palmSlot, berth, false, default, m, 0f, speed,
                    DockSettled(m, speed, t), existing);
            }
        }

        /// <summary>
        /// Departure accepts a doll only on its own named berth; other docks offer their nearest
        /// empty berth. Returns -1 when no berth is free.
        /// </summary>
        private int ChooseDepositBerth(DockLayout layout, DockId dock, DollId doll, PalmPlatform palm,
            InteractionTuning t, out SupportGeometry.Measurement best)
        {
            best = default;
            int named = layout.NamedBerthFor(doll);
            if (layout.NamedBerths != null)
            {
                if (named < 0 || !_ctx.Ledger.IsEmpty(SlotRef.DockBerth(dock, named)))
                {
                    return -1;
                }

                best = SupportGeometry.PalmToBerth(palm, layout.Berths[named], t);
                return named;
            }

            int chosen = -1;
            float bestGap = float.MaxValue;
            for (int b = 0; b < layout.Capacity; b++)
            {
                if (!_ctx.Ledger.IsEmpty(SlotRef.DockBerth(dock, b)))
                {
                    continue;
                }

                SupportGeometry.Measurement m = SupportGeometry.PalmToBerth(palm, layout.Berths[b], t);
                if (m.EdgeGap < bestGap)
                {
                    bestGap = m.EdgeGap;
                    best = m;
                    chosen = b;
                }
            }

            return chosen;
        }

        private static SupportGeometry.Measurement NearestBerth(DockLayout layout, PalmPlatform palm, InteractionTuning t)
        {
            SupportGeometry.Measurement best = default;
            float bestGap = float.MaxValue;
            for (int b = 0; b < layout.Capacity; b++)
            {
                SupportGeometry.Measurement m = SupportGeometry.PalmToBerth(palm, layout.Berths[b], t);
                if (m.EdgeGap < bestGap)
                {
                    bestGap = m.EdgeGap;
                    best = m;
                }
            }

            return best;
        }

        private static bool DockInRange(SupportGeometry.Measurement m, bool existing, InteractionTuning t)
        {
            return existing
                ? m.EdgeGap >= t.DockHoldGapMin && m.EdgeGap <= t.DisconnectGap && m.VerticalOffset <= t.HoldVerticalOffset
                : m.EdgeGap >= t.DockConnectGapMin && m.EdgeGap <= t.ConnectGapMax && m.VerticalOffset <= t.MaxVerticalOffset;
        }

        private static bool DockSettled(SupportGeometry.Measurement m, float speed, InteractionTuning t)
        {
            return m.EdgeGap >= t.DockConnectGapMin && m.EdgeGap <= t.ConnectGapMax
                && m.VerticalOffset <= t.MaxVerticalOffset && speed <= t.MaxRelativeEdgeSpeed;
        }

        private bool TryFindSharedDryArea(Vector3 a, Vector3 b, out DockId dock)
        {
            foreach (DockId id in Ids.Docks)
            {
                if (_ctx.Policy.IsDryAreaOpen(id) && _ctx.Stage.IsInDryArea(id, a) && _ctx.Stage.IsInDryArea(id, b))
                {
                    dock = id;
                    return true;
                }
            }

            dock = default;
            return false;
        }

        private void MaybeRejectCue(HandId hand, DockId dock, DollId doll, RejectReason reason,
            SupportGeometry.Measurement m, InteractionTuning t)
        {
            if (reason == RejectReason.NotRunning)
            {
                return;
            }

            if (m.EdgeGap > t.ConnectGapMax || m.VerticalOffset > t.HoldVerticalOffset)
            {
                return;
            }

            _pendingRejects.Add(new PendingReject(hand, dock, doll, reason));
        }

        /// <summary>
        /// Explains a refusal only when the palm has no valid candidate this step, so a correct
        /// action is never accompanied by a refusal cue for a neighbouring berth. Rate-limited.
        /// </summary>
        private void EmitRejectCues()
        {
            double now = _ctx.Clock.RealTime;
            foreach (PendingReject r in _pendingRejects)
            {
                if (_usedSlots.Contains(SlotRef.Palm(r.Hand)))
                {
                    continue;
                }

                int key = ((int)r.Hand * 8 + (int)r.Dock) * 16 + (int)r.Reason;
                if (_lastRejectCue.TryGetValue(key, out double last) && now - last < RejectCueInterval)
                {
                    continue;
                }

                _lastRejectCue[key] = now;
                _ctx.Emit(new SimEvent(SimEventKind.DockRejected, r.Doll, default, SlotRef.Palm(r.Hand),
                    SlotRef.DockBerth(r.Dock, 0), r.Hand, (int)r.Reason));
            }

            _pendingRejects.Clear();
        }

        private bool HasCurrent(SlotRef source, SlotRef target, DollId doll)
        {
            foreach (ConnectionCandidate c in _current)
            {
                if (c.Source == source && c.Target == target && c.Doll == doll)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddRaw(TransferKind kind, DollId doll, SlotRef source, SlotRef target, bool hasDryArea, DockId dryDock,
            SupportGeometry.Measurement m, float normalDifference, float relativeSpeed, bool settled, bool existing)
        {
            ConnectionCandidate c = _pool.Count > 0 ? _pool.Pop() : new ConnectionCandidate();
            c.Kind = kind;
            c.Doll = doll;
            c.Source = source;
            c.Target = target;
            c.HasDryArea = hasDryArea;
            c.DryAreaDock = dryDock;
            c.EdgeGap = m.EdgeGap;
            c.VerticalOffset = m.VerticalOffset;
            c.NormalDifference = normalDifference;
            c.RelativeSpeed = relativeSpeed;
            c.Settled = settled;
            c.Dwell = 0f;
            c.Existing = existing;
            _raw.Add(c);
        }

        private static int KindPriority(TransferKind kind)
        {
            switch (kind)
            {
                case TransferKind.Handover: return 0;
                case TransferKind.Deposit: return 1;
                default: return 2;
            }
        }

        private static int Compare(ConnectionCandidate a, ConnectionCandidate b)
        {
            // Keep existing intentions stable, then nearest edge, then kind, then stable ids.
            int c = b.Existing.CompareTo(a.Existing);
            if (c != 0) return c;
            c = a.EdgeGap.CompareTo(b.EdgeGap);
            if (c != 0) return c;
            c = KindPriority(a.Kind).CompareTo(KindPriority(b.Kind));
            if (c != 0) return c;
            c = ((int)a.Doll).CompareTo((int)b.Doll);
            if (c != 0) return c;
            c = SlotOrder(a.Source).CompareTo(SlotOrder(b.Source));
            if (c != 0) return c;
            return SlotOrder(a.Target).CompareTo(SlotOrder(b.Target));
        }

        private static int SlotOrder(SlotRef s)
        {
            return s.IsPalm ? (int)s.Hand : 10 + (int)s.Dock * 8 + s.Berth;
        }

        private void Select()
        {
            _raw.Sort(Compare);
            _usedSlots.Clear();
            _usedDolls.Clear();
            _next.Clear();

            foreach (ConnectionCandidate c in _raw)
            {
                if (_usedSlots.Contains(c.Source) || _usedSlots.Contains(c.Target) || _usedDolls.Contains(c.Doll))
                {
                    _pool.Push(c);
                    continue;
                }

                _usedSlots.Add(c.Source);
                _usedSlots.Add(c.Target);
                _usedDolls.Add(c.Doll);

                ConnectionCandidate previous = FindCurrent(c);
                if (previous != null)
                {
                    c.Dwell = previous.Dwell;
                }
                else
                {
                    _ctx.Emit(new SimEvent(SimEventKind.CandidateOpened, c.Doll, default, c.Source, c.Target, code: (int)c.Kind));
                }

                _next.Add(c);
            }

            _raw.Clear();

            foreach (ConnectionCandidate old in _current)
            {
                if (!ContainsEndpoints(_next, old))
                {
                    _ctx.Emit(new SimEvent(SimEventKind.CandidateClosed, old.Doll, default, old.Source, old.Target, code: (int)old.Kind));
                }

                _pool.Push(old);
            }

            _current.Clear();
            _current.AddRange(_next);
            _next.Clear();
        }

        private ConnectionCandidate FindCurrent(ConnectionCandidate c)
        {
            foreach (ConnectionCandidate old in _current)
            {
                if (old.SameEndpoints(c))
                {
                    return old;
                }
            }

            return null;
        }

        private static bool ContainsEndpoints(List<ConnectionCandidate> list, ConnectionCandidate c)
        {
            foreach (ConnectionCandidate x in list)
            {
                if (x.SameEndpoints(c))
                {
                    return true;
                }
            }

            return false;
        }

        private void AdvanceDwellAndReserve(float simDt)
        {
            float required = _ctx.Tuning.CandidateDwellSeconds;
            for (int i = 0; i < _current.Count; i++)
            {
                ConnectionCandidate c = _current[i];
                c.Dwell = c.Settled ? c.Dwell + simDt : 0f;
                if (c.Dwell + 1e-6f < required)
                {
                    continue;
                }

                TransferTransaction tx = _transfers.TryBegin(c);
                if (tx == null)
                {
                    c.Dwell = 0f;
                    continue;
                }

                _ctx.Emit(new SimEvent(SimEventKind.CandidateClosed, c.Doll, tx.Id, c.Source, c.Target, code: (int)c.Kind, value: 1f));
                _current.RemoveAt(i);
                _pool.Push(c);
                i--;
            }
        }

        public static bool IsSeam(ConnectionCandidate c) => c.Kind == TransferKind.Handover;

        /// <summary>Normalized dwell progress for presentation (anticipatory rock while incomplete).</summary>
        public float DwellProgress(ConnectionCandidate c)
        {
            float required = Math.Max(1e-4f, _ctx.Tuning.CandidateDwellSeconds);
            return HMath.Clamp01(c.Dwell / required);
        }
    }
}
