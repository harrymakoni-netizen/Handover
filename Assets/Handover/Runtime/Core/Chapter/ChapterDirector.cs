using System.Collections.Generic;

namespace Handover.Core
{
    /// <summary>Summary shown at the harbour. Assistance settings are context, never a lower tier.</summary>
    public sealed class ChapterResults
    {
        public bool Completed;
        /// <summary>Low-refuge rests this run; Keeper's lantern stop is not counted.</summary>
        public int LowRefugeStops;
        public int ExposureResets;
        public HandoverSettings AssistanceUsed;
    }

    /// <summary>
    /// Chapter objectives and dock rules. Consumes committed transfers (de-duplicated by
    /// transaction id) and owns <see cref="ChapterState"/>. It never animates and never
    /// moves a doll itself.
    /// Ordering rule: U unlocks with Keeper's teaching hop; H accepts Pip and Luma only while
    /// Keeper holds the lantern at U; after both arrive the light latches and Keeper is
    /// released to come home last.
    /// </summary>
    public sealed class ChapterDirector : IDockPolicy
    {
        private readonly SimulationContext _ctx;
        private readonly HashSet<TransactionId> _processed = new HashSet<TransactionId>();
        private readonly Queue<string> _checkpointRequests = new Queue<string>();

        public ChapterDirector(SimulationContext context)
        {
            _ctx = context;
            State = new ChapterState();
        }

        public ChapterState State { get; }
        public FloodHazardService Flood { get; internal set; }
        public ChapterResults Results { get; private set; }

        private bool Relaxed => _ctx.Settings != null && _ctx.Settings.RelaxedMode;

        /// <summary>Reset objectives for a new chapter (Replay keeps settings and placement).</summary>
        public void ResetChapter()
        {
            State.Reset();
            _processed.Clear();
            _checkpointRequests.Clear();
            Results = null;
        }

        /// <summary>Restore logical state from a checkpoint; processed ids are kept so stale callbacks stay ignored.</summary>
        public void RestoreState(ChapterState snapshot)
        {
            State.CopyFrom(snapshot);
            _checkpointRequests.Clear();
            Results = State.Completed ? BuildResults() : null;
        }

        public bool TakeCheckpointRequest(out string label)
        {
            if (_checkpointRequests.Count > 0)
            {
                label = _checkpointRequests.Dequeue();
                return true;
            }

            label = null;
            return false;
        }

        /// <summary>The lantern is lit while Keeper holds it at U (or is just lifting it), or once latched.</summary>
        public bool LanternLit => State.LanternLatched || KeeperHoldsLantern();

        public bool KeeperHoldsLantern()
        {
            DollOwner owner = _ctx.Ledger.Doll(DollId.Keeper).Owner;
            if (owner.Kind == OwnerKind.Dock)
            {
                return owner.Slot.Dock == DockId.LanternRefuge;
            }

            return owner.Kind == OwnerKind.Transfer
                && _ctx.Ledger.TryGetReservation(owner.Transaction, out _, out SlotRef source, out _)
                && source.IsDock && source.Dock == DockId.LanternRefuge;
        }

        // ---------------------------------------------------------------------
        // IDockPolicy
        // ---------------------------------------------------------------------

        public RejectReason CanBoard(DollId doll, SlotRef berth)
        {
            if (!_ctx.Session.IsGameplayRunning)
            {
                return RejectReason.NotRunning;
            }

            switch (berth.Dock)
            {
                case DockId.Harbour:
                    return RejectReason.AlreadyHome;
                case DockId.Departure:
                    // Keeper has priority until the teaching hop is complete; in Ready only Keeper boards.
                    if ((_ctx.Session.State == SessionState.Ready || !State.TutorialHopDone) && doll != DollId.Keeper)
                    {
                        return RejectReason.KeeperFirst;
                    }

                    return RejectReason.None;
                case DockId.LanternRefuge:
                    // Lifting Keeper early is allowed (it dims the harbour), but not while a
                    // passenger is landing at the harbour on the strength of the light.
                    if (!State.LanternLatched && _ctx.Ledger.DockHasReservation(DockId.Harbour))
                    {
                        return RejectReason.HarbourLanding;
                    }

                    return RejectReason.None;
                default:
                    return RejectReason.None;
            }
        }

        public RejectReason CanDeposit(DollId doll, DockId dock)
        {
            if (!_ctx.Session.IsGameplayRunning || _ctx.Session.State == SessionState.Ready)
            {
                return RejectReason.NotRunning;
            }

            switch (dock)
            {
                case DockId.LanternRefuge:
                    if (doll != DollId.Keeper)
                    {
                        return RejectReason.LanternKeeperOnly;
                    }

                    return State.TutorialHopDone ? RejectReason.None : RejectReason.LanternLocked;
                case DockId.Harbour:
                    if (doll == DollId.Keeper)
                    {
                        return State.LanternLatched ? RejectReason.None : RejectReason.KeeperComesHomeLast;
                    }

                    return LanternLit ? RejectReason.None : RejectReason.HarbourDark;
                case DockId.LowRefuge:
                    // While a closure is queued, departures are allowed but arrivals are rejected.
                    return IsLowRefugeOpen() ? RejectReason.None : RejectReason.RefugeClosed;
                default:
                    return RejectReason.None;
            }
        }

        public bool IsDryAreaOpen(DockId dock)
        {
            return dock != DockId.LowRefuge || IsLowRefugeOpen() || _ctx.Ledger.CountAtDock(DockId.LowRefuge, true) > 0;
        }

        private bool IsLowRefugeOpen() => Relaxed || State.LowRefuge == RefugeStatus.Open;

        public bool CanRestAt(DollId doll, SlotRef berth)
        {
            if (!berth.IsDock)
            {
                return false;
            }

            switch (berth.Dock)
            {
                case DockId.Departure:
                    return _ctx.Ledger.Doll(doll).HomeBerth == berth;
                case DockId.LanternRefuge:
                    return doll == DollId.Keeper && State.TutorialHopDone;
                case DockId.LowRefuge:
                    return IsLowRefugeOpen();
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------------
        // Committed transfers
        // ---------------------------------------------------------------------

        /// <summary>Applies one committed transfer. Returns false for a duplicate callback.</summary>
        public bool OnTransferCommitted(TransferTransaction tx)
        {
            if (!_processed.Add(tx.Id))
            {
                return false;
            }

            switch (tx.Kind)
            {
                case TransferKind.Boarding:
                    OnBoarded(tx);
                    break;
                case TransferKind.Handover:
                    if (!State.TutorialHopDone)
                    {
                        // The first Palm-to-Palm commit completes the teaching objective and unlocks U.
                        State.TutorialHopDone = true;
                        _ctx.Emit(new SimEvent(SimEventKind.TutorialHopCompleted, tx.Doll, tx.Id));
                    }

                    break;
                case TransferKind.Deposit:
                    OnDeposited(tx);
                    break;
            }

            return true;
        }

        private void OnBoarded(TransferTransaction tx)
        {
            if (tx.Doll == DollId.Keeper && tx.Source.Dock == DockId.Departure && _ctx.Session.State == SessionState.Ready)
            {
                // Keeper's committed Dock-to-Palm landing starts play exactly once.
                _ctx.Session.TryStartChapter(tx.Id);
            }

            if (tx.Doll == DollId.Keeper && tx.Source.Dock == DockId.LanternRefuge && !State.LanternLatched)
            {
                // Lifting Keeper early dims the harbour until they return; rescued dolls stay safe.
                _ctx.Emit(new SimEvent(SimEventKind.LanternDimmed, DollId.Keeper, tx.Id));
            }
        }

        private void OnDeposited(TransferTransaction tx)
        {
            switch (tx.Target.Dock)
            {
                case DockId.LanternRefuge:
                {
                    bool first = !State.LanternEverLit;
                    State.LanternEverLit = true;
                    _ctx.Emit(new SimEvent(SimEventKind.LanternLit, DollId.Keeper, tx.Id, code: first ? 1 : 0));
                    if (first)
                    {
                        Flood?.OnLanternFirstLit();
                    }

                    _checkpointRequests.Enqueue("lantern");
                    break;
                }

                case DockId.LowRefuge:
                    State.LowRefugeStops++;
                    break;

                case DockId.Harbour:
                    OnArrivedHome(tx);
                    break;
            }
        }

        private void OnArrivedHome(TransferTransaction tx)
        {
            if (!State.MarkRescued(tx.Doll))
            {
                return;
            }

            _ctx.Emit(new SimEvent(SimEventKind.DollRescued, tx.Doll, tx.Id, value: State.Rescued.Count));

            if (tx.Doll.IsOrdinaryPassenger())
            {
                int ordinary = State.OrdinaryRescuedCount;
                if (ordinary == 2)
                {
                    // After both arrive, latch the light on and release Keeper for rescue.
                    State.LanternLatched = true;
                    _ctx.Emit(new SimEvent(SimEventKind.LanternLatched, DollId.Keeper, tx.Id));
                }

                Flood?.OnOrdinaryRescued(ordinary);
                _checkpointRequests.Enqueue("rescue");
                return;
            }

            // Keeper is home last: the group reunites.
            State.Completed = true;
            Results = BuildResults();
            _ctx.Emit(new SimEvent(SimEventKind.ChapterCompleted, DollId.Keeper, tx.Id, value: State.LowRefugeStops));
            _ctx.Session.CompleteChapter();
            _checkpointRequests.Enqueue("complete");
        }

        private ChapterResults BuildResults()
        {
            return new ChapterResults
            {
                Completed = State.Completed,
                LowRefugeStops = State.LowRefugeStops,
                ExposureResets = State.ExposureResets,
                AssistanceUsed = _ctx.Settings?.Clone() ?? new HandoverSettings(),
            };
        }

        public bool HasProcessed(TransactionId id) => _processed.Contains(id);
    }
}
