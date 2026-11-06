using System.Collections.Generic;

namespace Handover.Core
{
    public enum CueId
    {
        None = 0,
        /// <summary>Wooden knock inviting the first palm in Ready.</summary>
        ReadyKnock,
        /// <summary>One gentle invitation tap as a doll leans towards a palm.</summary>
        BoardingInvite,
        /// <summary>Two-note connection cue when a seam appears.</summary>
        SeamConnect,
        /// <summary>Wooden tap confirming boarding.</summary>
        BoardTap,
        /// <summary>Soft tap confirming a landing.</summary>
        LandTap,
        /// <summary>Subtle creak or softened chime for unsafe tilt (capped volume, never an alarm).</summary>
        TiltWarning,
        /// <summary>Soft refusal when a dock is closed, with a visual explanation.</summary>
        DockRefused,
        ShelterWarning,
        /// <summary>Distinct cue for the system rescue marker.</summary>
        TrackingRecovery,
        LanternLit,
        LanternDimmed,
        LanternLatched,
        WaterRise,
        /// <summary>One phrase of the harbour melody per rescue (variant 1-3).</summary>
        RescuePhrase,
        CompletionMelody,
        CheckpointRestored,
        MenuSelect,
    }

    public enum CuePriority
    {
        Ambient = 0,
        Gameplay = 1,
        Warning = 2,
        Recovery = 3,
        Menu = 4,
    }

    public readonly struct CueRequest
    {
        public readonly CueId Id;
        public readonly CuePriority Priority;
        public readonly DollId Doll;
        public readonly HandId Hand;
        public readonly int Variant;

        public CueRequest(CueId id, CuePriority priority, DollId doll = DollId.None, HandId hand = HandId.Left, int variant = 0)
        {
            Id = id;
            Priority = priority;
            Doll = doll;
            Hand = hand;
            Variant = variant;
        }

        public override string ToString() => $"{Id}({Priority}) {Doll} v{Variant}";
    }

    /// <summary>
    /// The single owner of cue priority and rate limits. Consumes simulation events only; it
    /// never decides rescue counts, forces transfers or writes ownership. Tracking recovery
    /// outranks tilt warnings, menu feedback outranks gameplay cues, warning loops stop while
    /// paused or recovering, and repeats are rate-limited so a noisy threshold cannot chatter.
    /// Every essential meaning also has a visual channel in the presenters.
    /// </summary>
    public sealed class CueArbiter
    {
        private readonly Dictionary<CueId, double> _lastPlayed = new Dictionary<CueId, double>();
        private readonly List<CueRequest> _frame = new List<CueRequest>();
        private double _recoveryUntil = double.NegativeInfinity;
        private double _menuUntil = double.NegativeInfinity;
        private bool _readyKnockPending = true;

        /// <summary>Warning cues are suppressed this long after a recovery cue.</summary>
        public float RecoveryDuckSeconds = 1.0f;
        /// <summary>Gameplay cues are suppressed this long after menu feedback.</summary>
        public float MenuDuckSeconds = 0.25f;

        public bool TiltLoopActive { get; private set; }
        public bool RainLoopActive { get; private set; }
        public FloodPhase RainPhase { get; private set; }

        public static float MinInterval(CueId id)
        {
            switch (id)
            {
                case CueId.BoardTap:
                case CueId.LandTap:
                    return 0.12f;
                case CueId.SeamConnect:
                    return 0.4f;
                case CueId.BoardingInvite:
                    return 1.2f;
                case CueId.TiltWarning:
                    return 1.0f;
                case CueId.DockRefused:
                    return 1.5f;
                case CueId.ShelterWarning:
                    return 1.0f;
                case CueId.TrackingRecovery:
                    return 1.0f;
                case CueId.MenuSelect:
                    return 0.08f;
                default:
                    return 0f;
            }
        }

        public void Reset()
        {
            _lastPlayed.Clear();
            _recoveryUntil = double.NegativeInfinity;
            _menuUntil = double.NegativeInfinity;
            _readyKnockPending = true;
            TiltLoopActive = false;
            RainLoopActive = false;
        }

        /// <summary>Menu feedback from hand UI (outside the simulation).</summary>
        public bool RequestMenu(double now, List<CueRequest> output)
        {
            var cue = new CueRequest(CueId.MenuSelect, CuePriority.Menu);
            if (!PassesRateLimit(cue.Id, now))
            {
                return false;
            }

            _menuUntil = now + MenuDuckSeconds;
            output.Add(cue);
            return true;
        }

        /// <summary>
        /// Turns one step's events into cues. <paramref name="anyTiltWarning"/> and
        /// <paramref name="rainActive"/> drive the loops; both stop whenever gameplay is not running.
        /// </summary>
        public void Process(IReadOnlyList<SimEvent> events, SessionState state, double now, bool anyTiltWarning,
            bool rainActive, FloodPhase phase, List<CueRequest> output)
        {
            _frame.Clear();
            bool running = state == SessionState.Ready || state == SessionState.Playing;

            if (state == SessionState.Ready && _readyKnockPending)
            {
                _readyKnockPending = false;
                _frame.Add(new CueRequest(CueId.ReadyKnock, CuePriority.Gameplay));
            }
            else if (state != SessionState.Ready && state != SessionState.Paused)
            {
                _readyKnockPending = state == SessionState.Results || state == SessionState.Setup;
            }

            foreach (SimEvent e in events)
            {
                Map(e, _frame);
            }

            // Highest priority first; stable for equal priorities.
            _frame.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            foreach (CueRequest cue in _frame)
            {
                if (!running && cue.Priority < CuePriority.Recovery && cue.Id != CueId.CompletionMelody && cue.Id != CueId.RescuePhrase)
                {
                    continue;
                }

                if (cue.Priority == CuePriority.Warning && now < _recoveryUntil)
                {
                    continue;
                }

                if (cue.Priority <= CuePriority.Gameplay && now < _menuUntil)
                {
                    continue;
                }

                if (!PassesRateLimit(cue.Id, now))
                {
                    continue;
                }

                if (cue.Priority == CuePriority.Recovery)
                {
                    _recoveryUntil = now + RecoveryDuckSeconds;
                }

                output.Add(cue);
            }

            TiltLoopActive = running && anyTiltWarning && now >= _recoveryUntil;
            RainLoopActive = running && rainActive;
            RainPhase = phase;
        }

        private bool PassesRateLimit(CueId id, double now)
        {
            float interval = MinInterval(id);
            if (interval > 0f && _lastPlayed.TryGetValue(id, out double last) && now - last < interval)
            {
                return false;
            }

            _lastPlayed[id] = now;
            return true;
        }

        private static void Map(in SimEvent e, List<CueRequest> frame)
        {
            switch (e.Kind)
            {
                case SimEventKind.CandidateOpened:
                    if (e.TransferKind == TransferKind.Handover)
                    {
                        frame.Add(new CueRequest(CueId.SeamConnect, CuePriority.Gameplay, e.Doll, e.To.Hand));
                    }
                    else if (e.TransferKind == TransferKind.Boarding)
                    {
                        frame.Add(new CueRequest(CueId.BoardingInvite, CuePriority.Gameplay, e.Doll, e.To.Hand));
                    }

                    break;
                case SimEventKind.TransferCommitted:
                    frame.Add(new CueRequest(e.TransferKind == TransferKind.Boarding ? CueId.BoardTap : CueId.LandTap,
                        CuePriority.Gameplay, e.Doll));
                    break;
                case SimEventKind.TiltWarningStarted:
                    frame.Add(new CueRequest(CueId.TiltWarning, CuePriority.Warning, e.Doll, e.Hand));
                    break;
                case SimEventKind.DockRejected:
                    frame.Add(new CueRequest(CueId.DockRefused, CuePriority.Gameplay, e.Doll, e.Hand, e.Code));
                    break;
                case SimEventKind.ShelterCue:
                    frame.Add(new CueRequest(CueId.ShelterWarning, CuePriority.Warning, e.Doll, e.Hand));
                    break;
                case SimEventKind.SafetyHoldEntered:
                case SimEventKind.TransferRecovered:
                    frame.Add(new CueRequest(CueId.TrackingRecovery, CuePriority.Recovery, e.Doll, e.Hand));
                    break;
                case SimEventKind.ExposureReset:
                case SimEventKind.CheckpointRestored:
                    frame.Add(new CueRequest(CueId.CheckpointRestored, CuePriority.Recovery, e.Doll));
                    break;
                case SimEventKind.LanternLit:
                    frame.Add(new CueRequest(CueId.LanternLit, CuePriority.Gameplay, DollId.Keeper));
                    break;
                case SimEventKind.LanternDimmed:
                    frame.Add(new CueRequest(CueId.LanternDimmed, CuePriority.Gameplay, DollId.Keeper));
                    break;
                case SimEventKind.LanternLatched:
                    frame.Add(new CueRequest(CueId.LanternLatched, CuePriority.Gameplay, DollId.Keeper));
                    break;
                case SimEventKind.RefugeClosed:
                    frame.Add(new CueRequest(CueId.WaterRise, CuePriority.Ambient));
                    break;
                case SimEventKind.DollRescued:
                    frame.Add(new CueRequest(CueId.RescuePhrase, CuePriority.Gameplay, e.Doll, variant: (int)e.Value));
                    break;
                case SimEventKind.ChapterCompleted:
                    frame.Add(new CueRequest(CueId.CompletionMelody, CuePriority.Gameplay, DollId.Keeper));
                    break;
            }
        }
    }
}
