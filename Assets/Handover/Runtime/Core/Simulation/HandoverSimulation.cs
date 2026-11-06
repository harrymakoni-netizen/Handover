using System;
using System.Collections.Generic;

namespace Handover.Core
{
    /// <summary>
    /// Composition root and the single ordered simulation update:
    /// sample and filter palms, resolve candidates, advance transactions, apply committed
    /// objectives, resolve pending flood events and publish cues. Uses one authoritative
    /// clock; pause freezes gameplay time but not UI hand tracking. Rendering may
    /// interpolate from this state but can never commit events.
    /// </summary>
    public sealed class HandoverSimulation
    {
        private readonly IHandPoseProvider _provider;
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly InteractionTuning _baseTuning;
        private readonly IDockPolicy _policyOverride;
        private readonly bool[] _recoveryHands = new bool[2];
        private int _publishedEventCount;
        private int _lastEpoch;
        private bool _hasFocus = true;
        private bool _headsetMounted = true;
        private ICheckpointSink _checkpointSink;

        public HandoverSimulation(ChapterDefinition chapter, InteractionTuning tuning, IHandPoseProvider provider,
            IDockPolicy policyOverride = null)
        {
            Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
            _baseTuning = (tuning ?? new InteractionTuning()).Clone();
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _policyOverride = policyOverride;
            _lastEpoch = provider.OriginEpoch;

            var dolls = new List<(DollId, Personality, int)>();
            foreach (DollDefinition d in chapter.Dolls)
            {
                dolls.Add((d.Id, d.Personality, d.HomeBerth));
            }

            Ledger = new RescueLedger(chapter.LayoutVariants[0].BerthCounts(), dolls);
            Clock = new SimulationClock();
            Session = new SessionService(_events);
            Palms = new[] { new PalmPlatform(HandId.Left, _baseTuning), new PalmPlatform(HandId.Right, _baseTuning) };
            Context = new SimulationContext(chapter, Ledger, Palms, Clock, Session, _events)
            {
                Tuning = _baseTuning,
                ProviderEpoch = provider.OriginEpoch,
            };
            Transfers = new TransferCoordinator(Context);
            Resolver = new HopResolver(Context, Transfers);
            Director = new ChapterDirector(Context);
            Checkpoints = new CheckpointService(Ledger);
            Context.Policy = _policyOverride ?? Director;
        }

        public ChapterDefinition Chapter { get; }
        public SimulationContext Context { get; }
        public RescueLedger Ledger { get; }
        public SimulationClock Clock { get; }
        public SessionService Session { get; }
        public PalmPlatform[] Palms { get; }
        public TransferCoordinator Transfers { get; }
        public HopResolver Resolver { get; }
        public ChapterDirector Director { get; }
        public CheckpointService Checkpoints { get; }
        public StageLayout Stage => Context.Stage;
        public InteractionTuning Tuning => Context.Tuning;
        public HandoverSettings Settings { get; private set; } = new HandoverSettings();

        /// <summary>
        /// Events since the previous <see cref="Step"/> began: those raised by commands issued
        /// between steps, followed by those of the latest step.
        /// </summary>
        public IReadOnlyList<SimEvent> Events => _events;

        public PalmPlatform Palm(HandId hand) => Palms[(int)hand];

        public bool HasFocus => _hasFocus;
        public bool HeadsetMounted => _headsetMounted;

        public void SetCheckpointSink(ICheckpointSink sink)
        {
            _checkpointSink = sink;
        }

        // ---------------------------------------------------------------------
        // Setup and settings
        // ---------------------------------------------------------------------

        /// <summary>Installs a fitted stage. Placement changes stay outside active gameplay.</summary>
        public bool SetStage(StageLayout layout)
        {
            if (Session.State != SessionState.Setup)
            {
                return false;
            }

            if (layout.Variant.Dock(DockId.Departure).Capacity != Ledger.BerthCount(DockId.Departure)
                || layout.Variant.Dock(DockId.Harbour).Capacity != Ledger.BerthCount(DockId.Harbour))
            {
                throw new ArgumentException("Layout berth counts must match the chapter's ledger.");
            }

            Context.Stage = layout;
            return true;
        }

        /// <summary>
        /// Stage confirmed. A new chapter enters Ready with Keeper on the nearest boarding berth;
        /// a chapter in progress (reposition, or a saved checkpoint) continues in Playing.
        /// </summary>
        public bool ConfirmStage()
        {
            if (Session.State != SessionState.Setup || Stage == null)
            {
                return false;
            }

            bool continueChapter = Session.ChapterStarted;
            Session.ConfirmStage(continueChapter);
            if (!continueChapter && Checkpoints.Latest == null)
            {
                AcceptCheckpoint(Checkpoints.Initial());
            }

            return true;
        }

        public void ApplySettings(HandoverSettings settings)
        {
            Settings = (settings ?? new HandoverSettings()).Clone();
            InteractionTuning effective = Settings.SteadyMode ? _baseTuning.WithSteadyMode() : _baseTuning;
            Context.Tuning = effective;
            foreach (PalmPlatform p in Palms)
            {
                p.SetTuning(effective);
            }
        }

        // ---------------------------------------------------------------------
        // The ordered update
        // ---------------------------------------------------------------------

        public void Step(float realDeltaSeconds)
        {
            // Drop what was already published; keep events raised by commands since then.
            _events.RemoveRange(0, Math.Min(_publishedEventCount, _events.Count));
            ProcessCommitted();
            Transfers.BeginStep();
            _processedCommits = 0;

            bool running = Session.IsGameplayRunning;
            Clock.Advance(realDeltaSeconds, running);
            float realDt = Clock.RealDelta;
            float simDt = Clock.GameplayDelta;

            // 1. Sample and filter palms (UI hands keep tracking while paused).
            int epoch = _provider.OriginEpoch;
            Context.ProviderEpoch = epoch;
            double providerNow = _provider.Now;
            foreach (PalmPlatform palm in Palms)
            {
                PalmSample sample = _provider.GetSample(palm.Hand);
                bool occupied = !Ledger.PalmIsEmpty(palm.Hand);
                PalmChange changes = palm.Update(sample, providerNow, epoch, Clock.RealTime, realDt, simDt, occupied);
                PublishPalmChanges(palm.Hand, changes);
            }

            // 2. Origin changes invalidate active connections before any movement is processed.
            if (epoch != _lastEpoch)
            {
                _lastEpoch = epoch;
                HandleOriginChange();
            }

            // 3. Tracking loss of an active hand, and recovery of held passengers.
            if (Session.IsGameplayRunning)
            {
                DetectLostActiveHands();
            }

            if (Session.State == SessionState.Recovering)
            {
                ProcessRecovery();
            }

            // 4. Resolve candidates (dwell uses gameplay time) and 5. advance transactions.
            Resolver.Step(simDt);
            Transfers.Advance(simDt);

            // 6. Apply committed objectives exactly once per transaction id.
            ProcessCommitted();

            _publishedEventCount = _events.Count;
        }

        private int _processedCommits;

        /// <summary>Applies commits not yet processed (from the step or from a command such as Pause).</summary>
        private void ProcessCommitted()
        {
            IReadOnlyList<TransferTransaction> committed = Transfers.CommittedThisStep;
            for (; _processedCommits < committed.Count; _processedCommits++)
            {
                TransferTransaction tx = committed[_processedCommits];
                Resolver.NoteCommitted(tx);
                Director.OnTransferCommitted(tx);
            }
        }

        private void PublishPalmChanges(HandId hand, PalmChange changes)
        {
            if (changes == PalmChange.None)
            {
                return;
            }

            void Emit(PalmChange flag, SimEventKind kind)
            {
                if ((changes & flag) != 0)
                {
                    _events.Add(new SimEvent(kind, Ledger.PalmDoll(hand), hand: hand));
                }
            }

            Emit(PalmChange.TrackingLost, SimEventKind.TrackingLost);
            Emit(PalmChange.TrackingRestored, SimEventKind.TrackingRestored);
            Emit(PalmChange.OriginChanged, SimEventKind.OriginChanged);
            Emit(PalmChange.TiltWarningStarted, SimEventKind.TiltWarningStarted);
            Emit(PalmChange.TiltWarningCleared, SimEventKind.TiltWarningCleared);
            Emit(PalmChange.ClingStarted, SimEventKind.ClingStarted);
            Emit(PalmChange.ClingEnded, SimEventKind.ClingEnded);
            Emit(PalmChange.ShieldRaised, SimEventKind.ShieldRaised);
            Emit(PalmChange.ShieldLowered, SimEventKind.ShieldLowered);
        }

        // ---------------------------------------------------------------------
        // Tracking recovery
        // ---------------------------------------------------------------------

        /// <summary>
        /// A hand is active when it carries a doll, is an endpoint of a transfer or is the shield
        /// over an exposed passenger. An unused empty hand may be absent without consequence.
        /// </summary>
        public bool IsActiveHand(HandId hand)
        {
            return !Ledger.PalmIsEmpty(hand) || Transfers.IsHandBusy(hand) || IsShieldEngaged(hand);
        }

        /// <summary>Hook for the flood hazard: true while this hand's canopy shelters an exposed doll.</summary>
        public Func<HandId, bool> ShieldEngaged { get; set; }

        private bool IsShieldEngaged(HandId hand) => ShieldEngaged != null && ShieldEngaged(hand);

        private void DetectLostActiveHands()
        {
            bool anyLost = false;
            foreach (HandId hand in Ids.Hands)
            {
                PalmPlatform palm = Palm(hand);
                if (palm.IsTracked || !IsActiveHand(hand))
                {
                    continue;
                }

                // Keep the last good support for the grace period, then secure the passenger.
                if (palm.UntrackedDuration(Clock.RealTime) + 1e-6 < Tuning.TrackingGraceSeconds)
                {
                    continue;
                }

                bool wasShielding = IsShieldEngaged(hand);
                Transfers.ResolveLostHand(hand);
                ProcessCommitted();
                bool secured = SecurePalmDoll(hand, palm.SupportCenter, palm.OriginEpoch);
                // A hop that passed the midpoint no longer depends on its (lost) source palm, so
                // that case continues without recovery; only carried, held or sheltering roles do.
                if (secured || HoldsDollFor(hand) || wasShielding)
                {
                    _recoveryHands[(int)hand] = true;
                    anyLost = true;
                }
            }

            if (anyLost)
            {
                Resolver.Clear();
                Session.EnterRecovering(RecoveryReason.TrackingLost);
            }
        }

        private bool SecurePalmDoll(HandId hand, System.Numerics.Vector3 position, int epoch)
        {
            DollId doll = Ledger.PalmDoll(hand);
            if (doll == DollId.None || !Ledger.PalmIsOccupied(hand))
            {
                return false;
            }

            Ledger.MovePalmDollToHold(hand, position, epoch);
            _events.Add(new SimEvent(SimEventKind.SafetyHoldEntered, doll, hand: hand, code: 1));
            return true;
        }

        private bool HoldsDollFor(HandId hand)
        {
            foreach (DollId doll in Ids.Dolls)
            {
                DollRecord r = Ledger.Doll(doll);
                if (r.IsInSafetyHold && r.HoldHasReturnHand && r.HoldReturnHand == hand)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasHeldDolls()
        {
            foreach (DollId doll in Ids.Dolls)
            {
                if (Ledger.Doll(doll).IsInSafetyHold)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Blends a held passenger back onto its returning palm once that palm has tracked stably
        /// for the required time, in the same origin epoch and within the displacement limit.
        /// Never snaps a doll across the room. Leaves Recovering when nothing is left to recover.
        /// </summary>
        private void ProcessRecovery()
        {
            InteractionTuning t = Tuning;
            foreach (DollId doll in Ids.Dolls)
            {
                DollRecord record = Ledger.Doll(doll);
                if (!record.IsInSafetyHold || !record.HoldHasReturnHand)
                {
                    continue;
                }

                HandId hand = record.HoldReturnHand;
                PalmPlatform palm = Palm(hand);
                bool stable = palm.IsTracked && palm.TrackedDuration(Clock.RealTime) + 1e-6 >= t.StableTrackingSeconds;
                bool sameOrigin = palm.OriginEpoch == record.HoldOriginEpoch;
                float displacement = System.Numerics.Vector3.Distance(palm.SupportCenter, record.HoldPosition);
                if (stable && sameOrigin && palm.CarryEligible && Ledger.PalmIsEmpty(hand)
                    && displacement <= t.BlendBackMaxDisplacement)
                {
                    Ledger.ReleaseHoldToPalm(doll, hand);
                    _events.Add(new SimEvent(SimEventKind.SafetyHoldBlendedBack, doll, hand: hand, value: displacement));
                }
            }

            if (HasHeldDolls())
            {
                return;
            }

            foreach (HandId hand in Ids.Hands)
            {
                if (_recoveryHands[(int)hand])
                {
                    PalmPlatform palm = Palm(hand);
                    if (!palm.IsTracked || palm.TrackedDuration(Clock.RealTime) + 1e-6 < t.StableTrackingSeconds)
                    {
                        return;
                    }
                }
            }

            FinishRecovery();
        }

        private void FinishRecovery()
        {
            _recoveryHands[0] = false;
            _recoveryHands[1] = false;
            Session.ExitRecovering();
        }

        /// <summary>True when a held passenger cannot blend back by itself and needs the player's confirmation.</summary>
        public bool RecoveryNeedsConfirmation
        {
            get
            {
                if (Session.State != SessionState.Recovering)
                {
                    return false;
                }

                foreach (DollId doll in Ids.Dolls)
                {
                    DollRecord r = Ledger.Doll(doll);
                    if (r.IsInSafetyHold && (!r.HoldHasReturnHand || r.HoldOriginEpoch == RescueLedger.NoBlendEpoch
                        || Palm(r.HoldReturnHand).OriginEpoch != r.HoldOriginEpoch))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Player-confirmed recovery: every held passenger returns to its logical safe berth
        /// (last safe dock, else its own Departure berth). Never counts as a failed rescue.
        /// </summary>
        public bool ConfirmRecovery()
        {
            if (Session.State != SessionState.Recovering)
            {
                return false;
            }

            ReturnHeldDollsToSafety();
            FinishRecovery();
            return true;
        }

        private void ReturnHeldDollsToSafety()
        {
            foreach (DollId doll in Ids.Dolls)
            {
                if (!Ledger.Doll(doll).IsInSafetyHold)
                {
                    continue;
                }

                SlotRef berth = Transfers.SafeBerthFor(doll);
                Ledger.ReleaseHoldToDock(doll, berth);
                _events.Add(new SimEvent(SimEventKind.SafetyHoldRestored, doll, to: berth));
            }
        }

        private void HandleOriginChange()
        {
            Resolver.Clear();
            Resolver.ResetHysteresis();
            if (Session.State == SessionState.Setup || Session.State == SessionState.Results || Session.State == SessionState.Error)
            {
                return;
            }

            // Blending back across an origin reset is never allowed, so these holds record an
            // epoch no palm can report; the player confirms a return to safety instead.
            Transfers.InvalidateForOriginChange(RescueLedger.NoBlendEpoch);
            foreach (HandId hand in Ids.Hands)
            {
                SecurePalmDoll(hand, Palm(hand).SupportCenter, RescueLedger.NoBlendEpoch);
            }

            if (HasHeldDolls())
            {
                Session.EnterRecovering(RecoveryReason.OriginChanged);
            }
        }

        // ---------------------------------------------------------------------
        // Pause and lifecycle
        // ---------------------------------------------------------------------

        /// <summary>
        /// Suspends gameplay and secures any carried passenger in the system hold while hand UI
        /// stays active. Hops that passed the midpoint land if the receiver is safe.
        /// </summary>
        public bool Pause(PauseReason reason)
        {
            SessionState state = Session.State;
            if (state == SessionState.Ready || state == SessionState.Playing)
            {
                Resolver.Clear();
                Transfers.SettleAll(TransferEndReason.Paused);
                ProcessCommitted();
                foreach (HandId hand in Ids.Hands)
                {
                    SecurePalmDoll(hand, Palm(hand).SupportCenter, Palm(hand).OriginEpoch);
                }
            }

            return Session.Pause(reason);
        }

        /// <summary>
        /// Resume after confirmation. Does not require a new palm-start. Held passengers are
        /// revalidated in Recovering (blend back to the returning palm, or confirm to restore).
        /// </summary>
        public bool Resume()
        {
            if (Session.State != SessionState.Paused || !_hasFocus || !_headsetMounted)
            {
                return false;
            }

            Session.Resume();
            if (HasHeldDolls() && Session.State != SessionState.Recovering)
            {
                Session.EnterRecovering(RecoveryReason.ResumeFromPause);
            }

            return true;
        }

        public void NotifyFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            if (!hasFocus)
            {
                Pause(PauseReason.FocusLost);
            }
        }

        public void NotifyHeadsetMounted(bool mounted)
        {
            _headsetMounted = mounted;
            if (!mounted)
            {
                Pause(PauseReason.HeadsetRemoved);
            }
        }

        // ---------------------------------------------------------------------
        // Checkpoints
        // ---------------------------------------------------------------------

        /// <summary>Captures, validates and records a checkpoint; forwards it to the sink when accepted.</summary>
        public Checkpoint CreateCheckpoint(string label)
        {
            Checkpoint c = Checkpoints.Capture(label, Director.State, Context.Policy);
            return AcceptCheckpoint(c) ? c : null;
        }

        private bool AcceptCheckpoint(Checkpoint c)
        {
            List<string> problems = Checkpoints.Accept(c);
            if (problems.Count > 0)
            {
                _events.Add(new SimEvent(SimEventKind.CheckpointRejected));
                return false;
            }

            _events.Add(new SimEvent(SimEventKind.CheckpointCreated));
            _checkpointSink?.OnCheckpoint(c);
            return true;
        }

        /// <summary>
        /// Restores a complete snapshot (all owners and the chapter state together). Falls back to
        /// the previous valid checkpoint, then to a fresh chapter, when a snapshot is invalid.
        /// </summary>
        public bool RestoreCheckpoint(Checkpoint checkpoint)
        {
            Checkpoint target = checkpoint;
            if (target == null || Checkpoints.Validate(target).Count > 0)
            {
                _events.Add(new SimEvent(SimEventKind.CheckpointRejected));
                target = Checkpoints.LatestValid() ?? Checkpoints.Initial();
            }

            Resolver.Clear();
            Resolver.ResetHysteresis();
            Transfers.CancelAll(TransferEndReason.Restored);
            Transfers.ClearCooldowns();
            ProcessCommitted();

            var placements = new List<(DollId, SlotRef)>();
            foreach (DollPlacement p in target.Placements)
            {
                placements.Add((p.Doll, p.Slot));
            }

            Ledger.Restore(placements);
            Director.RestoreState(target.State);
            foreach (PalmPlatform palm in Palms)
            {
                palm.ResetContinuity();
            }

            _recoveryHands[0] = false;
            _recoveryHands[1] = false;
            _events.Add(new SimEvent(SimEventKind.CheckpointRestored));
            return ReferenceEquals(target, checkpoint);
        }

        /// <summary>"Restart checkpoint" from the pause menu: restore the latest snapshot and resume play.</summary>
        public void RestartFromCheckpoint()
        {
            RestoreCheckpoint(Checkpoints.LatestValid());
            if (Session.State == SessionState.Paused)
            {
                Session.Resume();
            }

            if (Session.State == SessionState.Recovering)
            {
                FinishRecovery();
            }
        }

        /// <summary>Continue a saved chapter: restore its checkpoint and play on once the stage is confirmed.</summary>
        public bool ContinueFromSave(Checkpoint saved)
        {
            if (Session.State != SessionState.Setup || saved == null || Checkpoints.Validate(saved).Count > 0)
            {
                return false;
            }

            Checkpoints.Clear();
            Checkpoints.Seed(saved);
            RestoreCheckpoint(saved);
            Session.MarkResumedFromSave();
            return true;
        }

        /// <summary>Reposition: restore a consistent checkpoint first, then return to stage placement.</summary>
        public void BeginReposition()
        {
            if (Session.State == SessionState.Setup)
            {
                return;
            }

            if (Session.ChapterStarted)
            {
                RestoreCheckpoint(Checkpoints.LatestValid());
            }
            else
            {
                Transfers.CancelAll(TransferEndReason.Restored);
                Resolver.Clear();
                Ledger.ResetToDeparture();
            }

            _recoveryHands[0] = false;
            _recoveryHands[1] = false;
            Context.Stage = null;
            Session.BeginSetup();
        }

        // ---------------------------------------------------------------------
        // Chapter commands
        // ---------------------------------------------------------------------

        /// <summary>Replay: reset objectives and occupancy, keep settings and the confirmed stage, return to Ready.</summary>
        public void Replay()
        {
            Transfers.CancelAll(TransferEndReason.Unloaded);
            Transfers.ClearCooldowns();
            ProcessCommitted();
            Resolver.Clear();
            Resolver.ResetHysteresis();
            Ledger.ResetToDeparture();
            Director.ResetChapter();
            Checkpoints.Clear();
            _recoveryHands[0] = false;
            _recoveryHands[1] = false;
            Session.Replay();
            AcceptCheckpoint(Checkpoints.Initial());
        }

        /// <summary>Unload: end every transaction and release every reservation exactly once.</summary>
        public void Unload()
        {
            Transfers.CancelAll(TransferEndReason.Unloaded);
            ProcessCommitted();
            Resolver.Clear();
        }

        public List<string> ValidateInvariants()
        {
            List<string> problems = Ledger.ValidateInvariants();
            foreach (TransferTransaction tx in Transfers.Active)
            {
                if (Ledger.Doll(tx.Doll).Owner != DollOwner.InTransfer(tx.Id))
                {
                    problems.Add($"Active {tx} does not own {tx.Doll}.");
                }
            }

            if (Ledger.ActiveReservationCount != Transfers.Active.Count)
            {
                problems.Add($"{Ledger.ActiveReservationCount} reservation(s) for {Transfers.Active.Count} active transaction(s).");
            }

            if ((Session.State == SessionState.Playing || Session.State == SessionState.Ready) && HasHeldDolls())
            {
                problems.Add("A passenger is in the system hold while gameplay is running.");
            }

            return problems;
        }
    }
}
