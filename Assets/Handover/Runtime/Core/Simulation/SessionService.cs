using System.Collections.Generic;

namespace Handover.Core
{
    public enum SessionState
    {
        Setup = 0,
        Ready = 1,
        Playing = 2,
        Paused = 3,
        Recovering = 4,
        Results = 5,
        Error = 6,
    }

    public enum PauseReason
    {
        None = 0,
        Menu = 1,
        FocusLost = 2,
        HeadsetRemoved = 3,
    }

    public enum RecoveryReason
    {
        None = 0,
        TrackingLost = 1,
        OriginChanged = 2,
        ResumeFromPause = 3,
    }

    /// <summary>
    /// The one authority for session state: Setup, Ready, Playing, Paused, Recovering,
    /// Results and Error. Ready permits Keeper's initial boarding; its committed landing
    /// enters Playing exactly once (de-duplicated by transaction id). Resume restores the
    /// prior state; Replay resets the chapter and returns to Ready.
    /// </summary>
    public sealed class SessionService
    {
        private readonly List<SimEvent> _events;
        private TransactionId _startTransaction;

        public SessionService(List<SimEvent> events)
        {
            _events = events;
            State = SessionState.Setup;
        }

        public SessionState State { get; private set; }
        /// <summary>State that Resume returns to.</summary>
        public SessionState ResumeState { get; private set; }
        public PauseReason PauseReason { get; private set; }
        public RecoveryReason RecoveryReason { get; private set; }
        public string ErrorMessage { get; private set; }
        public TransactionId StartTransaction => _startTransaction;
        public bool ChapterStarted => _startTransaction.IsValid || ChapterResumedFromSave;
        /// <summary>True when play continues from a saved checkpoint rather than a palm-start.</summary>
        public bool ChapterResumedFromSave { get; private set; }
        public int ChapterStartCount { get; private set; }

        /// <summary>Dwell, hops, weather and exposure advance only in Ready and Playing.</summary>
        public bool IsGameplayRunning => State == SessionState.Ready || State == SessionState.Playing;

        public void ConfirmStage(bool continueChapter)
        {
            if (State != SessionState.Setup)
            {
                return;
            }

            SetState(continueChapter ? SessionState.Playing : SessionState.Ready);
        }

        /// <summary>
        /// Called for Keeper's committed Dock-to-Palm boarding. Returns true exactly once per
        /// chapter; duplicates and replays of the same transaction are ignored.
        /// </summary>
        public bool TryStartChapter(TransactionId boardingCommit)
        {
            if (State != SessionState.Ready || !boardingCommit.IsValid || _startTransaction.IsValid)
            {
                return false;
            }

            _startTransaction = boardingCommit;
            ChapterStartCount++;
            SetState(SessionState.Playing);
            _events.Add(new SimEvent(SimEventKind.ChapterStarted, DollId.Keeper, boardingCommit));
            return true;
        }

        /// <summary>Continue a saved chapter without a new palm-start.</summary>
        public void MarkResumedFromSave()
        {
            ChapterResumedFromSave = true;
        }

        public bool Pause(PauseReason reason)
        {
            if (State == SessionState.Paused)
            {
                // A stronger reason (headset removed) replaces a weaker one; the resume target stays.
                if (reason > PauseReason)
                {
                    PauseReason = reason;
                }

                return false;
            }

            if (State != SessionState.Ready && State != SessionState.Playing && State != SessionState.Recovering)
            {
                return false;
            }

            ResumeState = State;
            PauseReason = reason;
            SetState(SessionState.Paused);
            return true;
        }

        /// <summary>Returns to the state before the pause. Never restarts the chapter.</summary>
        public bool Resume()
        {
            if (State != SessionState.Paused)
            {
                return false;
            }

            PauseReason = PauseReason.None;
            SetState(ResumeState);
            return true;
        }

        public bool EnterRecovering(RecoveryReason reason)
        {
            if (State != SessionState.Playing && State != SessionState.Ready)
            {
                if (State == SessionState.Paused && ResumeState != SessionState.Recovering)
                {
                    // Resume will land in recovery so held dolls are revalidated first.
                    ResumeState = SessionState.Recovering;
                    RecoveryReason = reason;
                    return true;
                }

                return false;
            }

            RecoveryReason = reason;
            ResumeState = State;
            SetState(SessionState.Recovering);
            return true;
        }

        public bool ExitRecovering()
        {
            if (State != SessionState.Recovering)
            {
                return false;
            }

            RecoveryReason = RecoveryReason.None;
            SetState(ChapterStarted ? SessionState.Playing : SessionState.Ready);
            return true;
        }

        public bool CompleteChapter()
        {
            if (State != SessionState.Playing)
            {
                return false;
            }

            SetState(SessionState.Results);
            return true;
        }

        /// <summary>Replay: reset chapter bookkeeping and return to Ready on the confirmed stage.</summary>
        public void Replay()
        {
            _startTransaction = TransactionId.None;
            ChapterResumedFromSave = false;
            PauseReason = PauseReason.None;
            RecoveryReason = RecoveryReason.None;
            SetState(SessionState.Ready);
        }

        /// <summary>Back to stage placement (Reposition from the pause menu, or anchor shift).</summary>
        public void BeginSetup()
        {
            PauseReason = PauseReason.None;
            RecoveryReason = RecoveryReason.None;
            SetState(SessionState.Setup);
        }

        public void Fail(string message)
        {
            ErrorMessage = message;
            SetState(SessionState.Error);
        }

        private void SetState(SessionState next)
        {
            if (next == State)
            {
                return;
            }

            SessionState previous = State;
            State = next;
            _events.Add(new SimEvent(SimEventKind.SessionStateChanged, code: (int)next, value: (int)previous));
        }
    }
}
