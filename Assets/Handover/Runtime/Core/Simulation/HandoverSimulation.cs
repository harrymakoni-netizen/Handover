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
        private bool _steadyMode;

        public HandoverSimulation(ChapterDefinition chapter, InteractionTuning tuning, IHandPoseProvider provider,
            IDockPolicy policyOverride = null)
        {
            Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
            _baseTuning = (tuning ?? new InteractionTuning()).Clone();
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _policyOverride = policyOverride;

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
        public StageLayout Stage => Context.Stage;
        public InteractionTuning Tuning => Context.Tuning;

        /// <summary>Events produced by the most recent <see cref="Step"/> (cleared at the start of each step).</summary>
        public IReadOnlyList<SimEvent> Events => _events;

        public PalmPlatform Palm(HandId hand) => Palms[(int)hand];

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

            if (layout.Variant.Docks[0].Capacity != Ledger.BerthCount(DockId.Departure))
            {
                throw new ArgumentException("Layout berth counts must match the chapter's ledger.");
            }

            Context.Stage = layout;
            return true;
        }

        /// <summary>Stage confirmed: enter Ready with Keeper on the nearest boarding berth.</summary>
        public bool ConfirmStage()
        {
            if (Session.State != SessionState.Setup || Stage == null)
            {
                return false;
            }

            Session.ConfirmStage(continueChapter: false);
            return true;
        }

        public bool SteadyMode
        {
            get => _steadyMode;
            set
            {
                _steadyMode = value;
                ApplyTuning();
            }
        }

        private void ApplyTuning()
        {
            InteractionTuning effective = _steadyMode ? _baseTuning.WithSteadyMode() : _baseTuning;
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
            _events.Clear();
            Transfers.BeginStep();

            bool running = Session.IsGameplayRunning;
            Clock.Advance(realDeltaSeconds, running);
            float realDt = Clock.RealDelta;
            float simDt = Clock.GameplayDelta;

            // 1. Sample and filter palms.
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

            // 2. Resolve candidates (dwell uses gameplay time).
            Resolver.Step(simDt);

            // 3. Advance transactions.
            Transfers.Advance(simDt);

            // 4. Apply committed objectives exactly once per transaction id.
            foreach (TransferTransaction tx in Transfers.CommittedThisStep)
            {
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
        // Commands
        // ---------------------------------------------------------------------

        /// <summary>Replay: reset objectives and occupancy, keep settings and the confirmed stage, return to Ready.</summary>
        public void Replay()
        {
            Transfers.CancelAll(TransferEndReason.Unloaded);
            Transfers.ClearCooldowns();
            Resolver.Clear();
            Resolver.ResetHysteresis();
            Ledger.ResetToDeparture();
            Director.ResetChapter();
            Session.Replay();
        }

        /// <summary>Unload: end every transaction and release every reservation exactly once.</summary>
        public void Unload()
        {
            Transfers.CancelAll(TransferEndReason.Unloaded);
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

            return problems;
        }
    }
}
