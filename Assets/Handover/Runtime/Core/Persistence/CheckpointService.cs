using System.Collections.Generic;

namespace Handover.Core
{
    /// <summary>Receives every accepted checkpoint (for example to write the save file).</summary>
    public interface ICheckpointSink
    {
        void OnCheckpoint(Checkpoint checkpoint);
    }

    /// <summary>
    /// Captures atomic checkpoints: all owners, occupancy, rescued ids, tutorial flag, lantern
    /// latch, phase and pending closure together. Dynamic owners are normalized to their
    /// reserved rollback dock or last safe dock; capacity is validated before acceptance.
    /// Keeps a short history so an invalid snapshot can fall back to the previous valid one.
    /// </summary>
    public sealed class CheckpointService
    {
        private const int HistoryLength = 4;

        private readonly RescueLedger _ledger;
        private readonly int[] _berthCounts = new int[4];
        private readonly int[] _homeBerths = new int[4];
        private readonly List<Checkpoint> _history = new List<Checkpoint>();

        public CheckpointService(RescueLedger ledger)
        {
            _ledger = ledger;
            foreach (DockId dock in Ids.Docks)
            {
                _berthCounts[(int)dock] = ledger.BerthCount(dock);
            }

            foreach (DollId doll in Ids.Dolls)
            {
                _homeBerths[(int)doll] = ledger.Doll(doll).HomeBerth.Berth;
            }
        }

        public IReadOnlyList<int> BerthCounts => _berthCounts;
        public IReadOnlyList<int> HomeBerths => _homeBerths;
        public Checkpoint Latest => _history.Count > 0 ? _history[_history.Count - 1] : null;
        public IReadOnlyList<Checkpoint> History => _history;

        public List<string> Validate(Checkpoint checkpoint)
        {
            return CheckpointRules.Validate(checkpoint, _berthCounts, _homeBerths);
        }

        /// <summary>Builds a normalized snapshot of the current state (not yet accepted).</summary>
        public Checkpoint Capture(string label, ChapterState state, IDockPolicy policy)
        {
            var placements = new List<DollPlacement>();
            var used = new HashSet<SlotRef>();

            // Dolls really resting on a dock keep their berth (including a queued-closure refuge).
            foreach (DollId doll in Ids.Dolls)
            {
                DollOwner owner = _ledger.Doll(doll).Owner;
                if (owner.Kind == OwnerKind.Dock)
                {
                    placements.Add(new DollPlacement(doll, owner.Slot.Dock, owner.Slot.Berth));
                    used.Add(owner.Slot);
                }
            }

            // Dynamic owners return to their reserved rollback dock or last safe dock; when that
            // berth is taken or no longer a valid resting place, to their own Departure berth.
            foreach (DollId doll in Ids.Dolls)
            {
                DollRecord record = _ledger.Doll(doll);
                if (record.Owner.Kind == OwnerKind.Dock)
                {
                    continue;
                }

                SlotRef target = record.LastSafeDock;
                if (record.Owner.Kind == OwnerKind.Transfer
                    && _ledger.TryGetReservation(record.Owner.Transaction, out _, out SlotRef source, out _)
                    && source.IsDock)
                {
                    target = source;
                }

                if (!target.IsDock || used.Contains(target) || !policy.CanRestAt(doll, target))
                {
                    target = record.HomeBerth;
                }

                used.Add(target);
                placements.Add(new DollPlacement(doll, target.Dock, target.Berth));
            }

            placements.Sort((a, b) => ((int)a.Doll).CompareTo((int)b.Doll));
            return new Checkpoint
            {
                Label = label,
                Placements = placements.ToArray(),
                State = state.Clone(),
            };
        }

        /// <summary>Validates and records a checkpoint. Returns the problems when rejected.</summary>
        public List<string> Accept(Checkpoint checkpoint)
        {
            List<string> problems = Validate(checkpoint);
            if (problems.Count == 0)
            {
                _history.Add(checkpoint.Clone());
                if (_history.Count > HistoryLength)
                {
                    _history.RemoveAt(0);
                }
            }

            return problems;
        }

        /// <summary>The newest checkpoint that still validates (for "use the previous valid checkpoint").</summary>
        public Checkpoint LatestValid()
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (Validate(_history[i]).Count == 0)
                {
                    return _history[i];
                }
            }

            return null;
        }

        public void Clear()
        {
            _history.Clear();
        }

        /// <summary>Seeds the history from a saved checkpoint after it has been validated.</summary>
        public bool Seed(Checkpoint checkpoint)
        {
            return Accept(checkpoint).Count == 0;
        }

        /// <summary>The start-of-chapter snapshot: everyone on their Departure berth.</summary>
        public Checkpoint Initial()
        {
            var placements = new DollPlacement[Ids.Dolls.Length];
            for (int i = 0; i < Ids.Dolls.Length; i++)
            {
                DollId doll = Ids.Dolls[i];
                placements[i] = new DollPlacement(doll, DockId.Departure, _homeBerths[(int)doll]);
            }

            return new Checkpoint { Label = "start", Placements = placements, State = new ChapterState() };
        }
    }
}
