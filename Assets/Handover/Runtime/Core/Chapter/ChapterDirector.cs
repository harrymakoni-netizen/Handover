using System.Collections.Generic;

namespace Handover.Core
{
    /// <summary>
    /// Chapter objectives and dock rules. Consumes committed transfers (de-duplicated by
    /// transaction id) and owns <see cref="ChapterState"/>. It never animates and never
    /// moves a doll itself.
    /// </summary>
    public sealed class ChapterDirector : IDockPolicy
    {
        private readonly SimulationContext _ctx;
        private readonly HashSet<TransactionId> _processed = new HashSet<TransactionId>();

        public ChapterDirector(SimulationContext context)
        {
            _ctx = context;
            State = new ChapterState();
        }

        public ChapterState State { get; }

        /// <summary>Reset objectives for a new chapter (Replay keeps settings and placement).</summary>
        public void ResetChapter()
        {
            State.Reset();
            _processed.Clear();
        }

        /// <summary>Restore logical state from a checkpoint; processed ids are kept so stale callbacks stay ignored.</summary>
        public void RestoreState(ChapterState snapshot)
        {
            State.CopyFrom(snapshot);
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
                default:
                    return RejectReason.None;
            }
        }

        public bool IsDryAreaOpen(DockId dock)
        {
            return true;
        }

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
                case DockId.Harbour:
                    return false;
                default:
                    return true;
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
                    if (tx.Doll == DollId.Keeper && _ctx.Session.State == SessionState.Ready)
                    {
                        // Keeper's committed Dock-to-Palm landing starts play exactly once.
                        _ctx.Session.TryStartChapter(tx.Id);
                    }

                    break;

                case TransferKind.Handover:
                    if (!State.TutorialHopDone)
                    {
                        // The first Palm-to-Palm commit completes the teaching objective and unlocks U.
                        State.TutorialHopDone = true;
                        _ctx.Emit(new SimEvent(SimEventKind.TutorialHopCompleted, tx.Doll, tx.Id));
                    }

                    break;
            }

            return true;
        }

        public bool HasProcessed(TransactionId id) => _processed.Contains(id);
    }
}
