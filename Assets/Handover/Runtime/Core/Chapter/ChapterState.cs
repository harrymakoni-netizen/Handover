using System.Collections.Generic;

namespace Handover.Core
{
    public enum RefugeStatus
    {
        Open = 0,
        /// <summary>Closure is queued: departures allowed, arrivals rejected, water held at the old height.</summary>
        ClosurePending = 1,
        Closed = 2,
    }

    /// <summary>
    /// Logical chapter progress. Checkpoints snapshot this together with every doll's
    /// (normalized) dock placement. Rescued passengers are a set: each unique id counts once.
    /// </summary>
    public sealed class ChapterState
    {
        private readonly HashSet<DollId> _rescued = new HashSet<DollId>();

        public bool TutorialHopDone;
        public bool LanternEverLit;
        public bool LanternLatched;
        public FloodPhase Phase = FloodPhase.Calm;
        public RefugeStatus LowRefuge = RefugeStatus.Open;
        /// <summary>Low-refuge rests this run (Keeper's lantern stop is never counted).</summary>
        public int LowRefugeStops;
        /// <summary>Exposure resets this run. Tracking and rest recovery never increment it.</summary>
        public int ExposureResets;
        public bool Completed;

        public IReadOnlyCollection<DollId> Rescued => _rescued;

        public bool IsRescued(DollId doll) => _rescued.Contains(doll);

        /// <summary>Adds a rescued passenger; false when it was already counted.</summary>
        public bool MarkRescued(DollId doll) => _rescued.Add(doll);

        public int OrdinaryRescuedCount
        {
            get
            {
                int n = 0;
                if (_rescued.Contains(DollId.Pip))
                {
                    n++;
                }

                if (_rescued.Contains(DollId.Luma))
                {
                    n++;
                }

                return n;
            }
        }

        public void Reset()
        {
            _rescued.Clear();
            TutorialHopDone = false;
            LanternEverLit = false;
            LanternLatched = false;
            Phase = FloodPhase.Calm;
            LowRefuge = RefugeStatus.Open;
            LowRefugeStops = 0;
            ExposureResets = 0;
            Completed = false;
        }

        public void CopyFrom(ChapterState other)
        {
            _rescued.Clear();
            foreach (DollId d in other._rescued)
            {
                _rescued.Add(d);
            }

            TutorialHopDone = other.TutorialHopDone;
            LanternEverLit = other.LanternEverLit;
            LanternLatched = other.LanternLatched;
            Phase = other.Phase;
            LowRefuge = other.LowRefuge;
            LowRefugeStops = other.LowRefugeStops;
            ExposureResets = other.ExposureResets;
            Completed = other.Completed;
        }

        public ChapterState Clone()
        {
            var copy = new ChapterState();
            copy.CopyFrom(this);
            return copy;
        }
    }

    /// <summary>Dock rules the transfer system consults. Chapter rules implement it; tests may substitute.</summary>
    public interface IDockPolicy
    {
        /// <summary>May this doll board a palm from this berth now?</summary>
        RejectReason CanBoard(DollId doll, SlotRef berth);

        /// <summary>May this doll be deposited at this dock now (berth capacity is checked separately)?</summary>
        RejectReason CanDeposit(DollId doll, DockId dock);

        /// <summary>Whether the dock's dry transfer area currently exists.</summary>
        bool IsDryAreaOpen(DockId dock);

        /// <summary>
        /// Whether a system return (local reset or recovery) may place the doll on this berth.
        /// Independent of session state; never true for the harbour (rescue is always deliberate).
        /// </summary>
        bool CanRestAt(DollId doll, SlotRef berth);
    }
}
