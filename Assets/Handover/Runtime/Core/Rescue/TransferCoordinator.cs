using System;
using System.Collections.Generic;
using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// Reserves, animates and commits transfers. Candidate -> Stable happens in the
    /// <see cref="HopResolver"/>; here: Reserved -> Anticipating -> Hopping -> Committed, with
    /// every pre-commit failure ending in Rollback or SafeRecovery. All paths release both
    /// reservations exactly once through the ledger.
    /// </summary>
    public sealed class TransferCoordinator
    {
        private readonly SimulationContext _ctx;
        private readonly List<TransferTransaction> _active = new List<TransferTransaction>();
        private readonly List<TransferTransaction> _committedThisStep = new List<TransferTransaction>();
        private readonly float[] _cooldown = new float[2];
        private long _nextId = 1;

        public TransferCoordinator(SimulationContext context)
        {
            _ctx = context;
        }

        public IReadOnlyList<TransferTransaction> Active => _active;
        /// <summary>Transactions that committed during the current step, in commit order.</summary>
        public IReadOnlyList<TransferTransaction> CommittedThisStep => _committedThisStep;
        public long IssuedCount => _nextId - 1;

        public void BeginStep()
        {
            _committedThisStep.Clear();
        }

        public bool IsHandBusy(HandId hand)
        {
            foreach (TransferTransaction tx in _active)
            {
                if (tx.Involves(hand))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsSlotBusy(SlotRef slot)
        {
            foreach (TransferTransaction tx in _active)
            {
                if (tx.Involves(slot))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsInCooldown(HandId hand) => _cooldown[(int)hand] > 0f;

        public float CooldownRemaining(HandId hand) => _cooldown[(int)hand];

        public TransferTransaction FindActive(DollId doll)
        {
            foreach (TransferTransaction tx in _active)
            {
                if (tx.Doll == doll)
                {
                    return tx;
                }
            }

            return null;
        }

        /// <summary>Reserves both endpoints for a stable candidate. Returns null when the ledger refuses.</summary>
        public TransferTransaction TryBegin(ConnectionCandidate candidate)
        {
            InteractionTuning t = _ctx.Tuning;
            var id = new TransactionId(_nextId);
            if (!_ctx.Ledger.TryReserve(id, candidate.Doll, candidate.Source, candidate.Target, out _))
            {
                return null;
            }

            _nextId++;
            DollDefinition def = _ctx.Chapter.Doll(candidate.Doll);
            float anticipation = t.AnticipationSeconds * (def != null ? def.AnticipationScale : 1f);
            var tx = new TransferTransaction(id, candidate.Kind, candidate.Doll, candidate.Source, candidate.Target,
                anticipation, t.HopSeconds, t.ArcHeight)
            {
                CurrentPosition = EndpointPosition(candidate.Source),
            };
            tx.DryAreaDock = candidate.DryAreaDock;
            tx.HasDryArea = candidate.HasDryArea;
            _active.Add(tx);
            _ctx.Emit(new SimEvent(SimEventKind.TransferReserved, tx.Doll, tx.Id, tx.Source, tx.Target, code: (int)tx.Kind));
            return tx;
        }

        public void Advance(float simDt)
        {
            for (int h = 0; h < 2; h++)
            {
                _cooldown[h] = Math.Max(0f, _cooldown[h] - simDt);
            }

            if (simDt <= 0f)
            {
                return;
            }

            for (int i = 0; i < _active.Count; i++)
            {
                TransferTransaction tx = _active[i];
                Step(tx, simDt);
                if (!tx.IsActive)
                {
                    _active.RemoveAt(i);
                    i--;
                }
            }
        }

        private void Step(TransferTransaction tx, float dt)
        {
            InteractionTuning t = _ctx.Tuning;
            bool sourceTracked = EndpointTracked(tx.Source);
            bool targetTracked = EndpointTracked(tx.Target);

            if (tx.Stage == TransferStage.Anticipating)
            {
                // Before take-off, cancel when alignment fails (an untracked endpoint cannot hold alignment).
                if (!sourceTracked || !targetTracked || !AlignmentHolds(tx))
                {
                    Rollback(tx, TransferEndReason.AlignmentLost);
                    return;
                }

                tx.CurrentPosition = EndpointPosition(tx.Source);
                tx.AnticipationElapsed += dt;
                if (tx.AnticipationElapsed >= tx.AnticipationSeconds)
                {
                    tx.Stage = TransferStage.Hopping;
                    tx.Progress = 0f;
                    _ctx.Emit(new SimEvent(SimEventKind.TransferTakeoff, tx.Doll, tx.Id, tx.Source, tx.Target, code: (int)tx.Kind));
                }

                return;
            }

            // Hopping. Stop progress while an endpoint the doll still depends on is untracked;
            // after the midpoint the take-off point is latched, so only the receiver matters.
            bool dependsOnSource = !tx.SourceLatched;
            bool frozen = !targetTracked || (dependsOnSource && !sourceTracked);
            tx.ProgressFrozen = frozen;
            if (frozen)
            {
                return;
            }

            Vector3 from = tx.SourceLatched ? tx.LatchedSourcePosition : EndpointPosition(tx.Source);
            Vector3 to = EndpointPosition(tx.Target);

            if (!tx.PastMidpoint)
            {
                if (!sourceTracked || Separated(tx))
                {
                    // Hands separated but remain tracked: return to the source before the midpoint.
                    Rollback(tx, TransferEndReason.SeparatedBeforeMidpoint);
                    return;
                }
            }
            else if (!ReceiverSafeAndClose(tx, from, to))
            {
                // After the midpoint, complete only if the receiver remains safe and close.
                ReturnToSafety(tx, TransferEndReason.ReceiverUnsafeAfterMidpoint);
                return;
            }

            float before = tx.Progress;
            tx.Progress = Math.Min(1f, tx.Progress + dt / Math.Max(1e-4f, tx.HopSeconds));
            if (before < 0.5f && tx.Progress >= 0.5f)
            {
                tx.SourceLatched = true;
                tx.LatchedSourcePosition = from;
            }

            tx.CurrentPosition = SupportGeometry.Arc(from, to, tx.Progress, tx.ArcHeight);
            if (tx.Progress >= 1f)
            {
                Commit(tx);
            }
        }

        private bool AlignmentHolds(TransferTransaction tx)
        {
            InteractionTuning t = _ctx.Tuning;
            if (tx.Source.IsPalm && tx.Target.IsPalm)
            {
                PalmPlatform a = _ctx.Palm(tx.Source.Hand);
                PalmPlatform b = _ctx.Palm(tx.Target.Hand);
                SupportGeometry.Measurement m = SupportGeometry.PalmToPalm(a, b, t);
                float normalDifference = HMath.AngleDegrees(a.Normal, b.Normal);
                float relativeSpeed = (a.SupportVelocity - b.SupportVelocity).Length();
                bool inDryArea = !tx.HasDryArea
                    || (_ctx.Stage != null
                        && _ctx.Stage.IsInDryArea(tx.DryAreaDock, a.SupportCenter)
                        && _ctx.Stage.IsInDryArea(tx.DryAreaDock, b.SupportCenter));
                return m.EdgeGap >= t.HoldGapMin
                    && m.EdgeGap <= t.DisconnectGap
                    && m.VerticalOffset <= t.HoldVerticalOffset
                    && normalDifference <= t.HoldNormalDifferenceDegrees
                    && relativeSpeed <= t.CancelRelativeEdgeSpeed
                    && b.CarryEligible && !a.Clinging && !b.Clinging
                    && inDryArea;
            }

            PalmPlatform palm = _ctx.Palm(tx.Source.IsPalm ? tx.Source.Hand : tx.Target.Hand);
            Vector3 berth = _ctx.Stage.BerthPosition(tx.Source.IsDock ? tx.Source : tx.Target);
            SupportGeometry.Measurement dm = SupportGeometry.PalmToBerth(palm, berth, t);
            return dm.EdgeGap >= t.DockHoldGapMin
                && dm.EdgeGap <= t.DisconnectGap
                && dm.VerticalOffset <= t.HoldVerticalOffset
                && palm.CarryEligible && !palm.Clinging
                && palm.SupportVelocity.Length() <= t.CancelRelativeEdgeSpeed;
        }

        private bool Separated(TransferTransaction tx)
        {
            InteractionTuning t = _ctx.Tuning;
            if (tx.Target.IsPalm && !_ctx.Palm(tx.Target.Hand).CarryEligible)
            {
                return true;
            }

            if (tx.Source.IsPalm && tx.Target.IsPalm)
            {
                SupportGeometry.Measurement m = SupportGeometry.PalmToPalm(_ctx.Palm(tx.Source.Hand), _ctx.Palm(tx.Target.Hand), t);
                return m.EdgeGap > t.DisconnectGap || m.VerticalOffset > t.HoldVerticalOffset;
            }

            PalmPlatform palm = _ctx.Palm(tx.Source.IsPalm ? tx.Source.Hand : tx.Target.Hand);
            Vector3 berth = _ctx.Stage.BerthPosition(tx.Source.IsDock ? tx.Source : tx.Target);
            SupportGeometry.Measurement dm = SupportGeometry.PalmToBerth(palm, berth, t);
            return dm.EdgeGap > t.DisconnectGap || dm.VerticalOffset > t.HoldVerticalOffset;
        }

        private bool ReceiverSafeAndClose(TransferTransaction tx, Vector3 from, Vector3 to)
        {
            InteractionTuning t = _ctx.Tuning;
            if (tx.Target.IsPalm)
            {
                PalmPlatform receiver = _ctx.Palm(tx.Target.Hand);
                if (!receiver.IsTracked || !receiver.CarryEligible)
                {
                    return false;
                }
            }

            return HMath.HorizontalDistance(from, to) <= t.MaxPostMidpointSpan
                && Math.Abs(to.Y - from.Y) <= t.HoldVerticalOffset + 0.02f;
        }

        private void Commit(TransferTransaction tx)
        {
            _ctx.Ledger.Commit(tx.Id);
            tx.Stage = TransferStage.Committed;
            tx.EndReason = TransferEndReason.Committed;
            tx.CurrentPosition = EndpointPosition(tx.Target);
            StartCooldown(tx.Source);
            StartCooldown(tx.Target);
            _committedThisStep.Add(tx);
            _ctx.Emit(new SimEvent(SimEventKind.TransferCommitted, tx.Doll, tx.Id, tx.Source, tx.Target, code: (int)tx.Kind));
        }

        private void Rollback(TransferTransaction tx, TransferEndReason reason)
        {
            _ctx.Ledger.Rollback(tx.Id);
            tx.Stage = TransferStage.RolledBack;
            tx.EndReason = reason;
            tx.CurrentPosition = EndpointPosition(tx.Source);
            _ctx.Emit(new SimEvent(SimEventKind.TransferRolledBack, tx.Doll, tx.Id, tx.Source, tx.Target, code: (int)reason, value: tx.Progress));
        }

        private void RecoverToHold(TransferTransaction tx, TransferEndReason reason, bool hasReturnHand, HandId returnHand,
            int holdEpoch)
        {
            _ctx.Ledger.RecoverToHold(tx.Id, tx.CurrentPosition, hasReturnHand, returnHand, holdEpoch);
            tx.Stage = TransferStage.Recovered;
            tx.EndReason = reason;
            _ctx.Emit(new SimEvent(SimEventKind.TransferRecovered, tx.Doll, tx.Id, tx.Source, tx.Target, code: (int)reason, value: tx.Progress));
            _ctx.Emit(new SimEvent(SimEventKind.SafetyHoldEntered, tx.Doll, tx.Id, hand: returnHand, code: hasReturnHand ? 1 : 0));
        }

        /// <summary>Local reset: release both reservations and place the doll on its last valid resting berth.</summary>
        private void ReturnToSafety(TransferTransaction tx, TransferEndReason reason)
        {
            _ctx.Ledger.RecoverToHold(tx.Id, tx.CurrentPosition, false, HandId.Left, _ctx.ProviderEpoch);
            SlotRef berth = SafeBerthFor(tx.Doll);
            _ctx.Ledger.ReleaseHoldToDock(tx.Doll, berth);
            tx.Stage = TransferStage.ReturnedToSafety;
            tx.EndReason = reason;
            _ctx.Emit(new SimEvent(SimEventKind.TransferReturnedToSafety, tx.Doll, tx.Id, tx.Source, berth, code: (int)reason, value: tx.Progress));
        }

        /// <summary>
        /// The berth a doll returns to after a local reset: its last safe dock when that berth is
        /// free and still a valid resting place, otherwise its own Departure berth (always free).
        /// </summary>
        public SlotRef SafeBerthFor(DollId doll)
        {
            DollRecord record = _ctx.Ledger.Doll(doll);
            SlotRef last = record.LastSafeDock;
            if (last.IsDock && _ctx.Ledger.IsEmpty(last) && _ctx.Policy.CanRestAt(doll, last))
            {
                return last;
            }

            return record.HomeBerth;
        }

        // ---------------------------------------------------------------------
        // Interruptions requested by the simulation (recovery, pause, origin change, unload)
        // ---------------------------------------------------------------------

        /// <summary>
        /// A palm endpoint has been untracked beyond the grace period. Before the midpoint the doll
        /// returns to its source; after it the doll waits in the system hold for the receiver.
        /// </summary>
        public void ResolveLostHand(HandId hand)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                TransferTransaction tx = _active[i];
                if (!tx.Involves(hand))
                {
                    continue;
                }

                if (!tx.HasTakenOff || !tx.PastMidpoint || tx.Source.IsDock)
                {
                    // Before the midpoint, or when boarding from a dock (always a valid place to
                    // return to), the doll goes back to its source.
                    Rollback(tx, TransferEndReason.TrackingLost);
                }
                else if (tx.Target.IsPalm && tx.Target.Hand == hand)
                {
                    RecoverToHold(tx, TransferEndReason.TrackingLost, true, hand, _ctx.Palm(hand).OriginEpoch);
                }
                else
                {
                    // The lost hand was the latched source; the receiver no longer depends on it.
                    continue;
                }

                _active.RemoveAt(i);
                i--;
            }
        }

        /// <summary>
        /// Pause: settle every transaction immediately. Pre-take-off and pre-midpoint hops return
        /// to the source; later hops land if the receiver is safe, otherwise the doll is held.
        /// </summary>
        public void SettleAll(TransferEndReason reason)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                TransferTransaction tx = _active[i];
                if (!tx.HasTakenOff || !tx.PastMidpoint)
                {
                    Rollback(tx, reason);
                }
                else
                {
                    Vector3 from = tx.LatchedSourcePosition;
                    Vector3 to = EndpointPosition(tx.Target);
                    if (EndpointTracked(tx.Target) && ReceiverSafeAndClose(tx, from, to))
                    {
                        Commit(tx);
                    }
                    else
                    {
                        bool palmTarget = tx.Target.IsPalm;
                        RecoverToHold(tx, reason, palmTarget, palmTarget ? tx.Target.Hand : HandId.Left, _ctx.ProviderEpoch);
                    }
                }
            }

            _active.Clear();
        }

        /// <summary>
        /// Origin reset: invalidate every active connection before movement is processed.
        /// Hops that have passed the midpoint cannot blend back across epochs, so they are held.
        /// </summary>
        public void InvalidateForOriginChange(int holdEpoch)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                TransferTransaction tx = _active[i];
                if (!tx.HasTakenOff || !tx.PastMidpoint)
                {
                    Rollback(tx, TransferEndReason.OriginChanged);
                }
                else
                {
                    bool palmTarget = tx.Target.IsPalm;
                    RecoverToHold(tx, TransferEndReason.OriginChanged, palmTarget, palmTarget ? tx.Target.Hand : HandId.Left, holdEpoch);
                }
            }

            _active.Clear();
        }

        /// <summary>Unload or checkpoint restore: every transaction rolls back; reservations are released once.</summary>
        public void CancelAll(TransferEndReason reason)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                Rollback(_active[i], reason);
            }

            _active.Clear();
        }

        public void ClearCooldowns()
        {
            _cooldown[0] = 0f;
            _cooldown[1] = 0f;
        }

        private void StartCooldown(SlotRef slot)
        {
            if (slot.IsPalm)
            {
                _cooldown[(int)slot.Hand] = _ctx.Tuning.ReceiveCooldownSeconds;
            }
        }

        public Vector3 EndpointPosition(SlotRef slot)
        {
            return slot.IsPalm ? _ctx.Palm(slot.Hand).SupportCenter : _ctx.Stage.BerthPosition(slot);
        }

        private bool EndpointTracked(SlotRef slot)
        {
            return !slot.IsPalm || _ctx.Palm(slot.Hand).IsTracked;
        }
    }
}
