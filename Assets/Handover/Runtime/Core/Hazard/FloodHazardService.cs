using System;
using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// Progress-triggered flood and virtual shelter (Section 5). Water is a visual plane, not
    /// a fluid simulation; phase changes are queued state changes, never timers. R closes after
    /// the first ordinary rescue only once it is empty and unreserved. Exposure is decided by
    /// casting from the carried doll towards the rain source against the canopy proxy of an
    /// empty palm-down hand; particles and the water shader are cosmetic and never consulted.
    /// </summary>
    public sealed class FloodHazardService
    {
        private readonly SimulationContext _ctx;
        private readonly ChapterState _state;
        private readonly float[] _exposed = new float[2];
        private readonly float[] _safe = new float[2];
        private readonly bool[] _cueShown = new bool[2];
        private readonly bool[] _sheltering = new bool[2];
        private readonly double[] _lastEngaged = { double.NegativeInfinity, double.NegativeInfinity };

        public FloodHazardService(SimulationContext context, ChapterState state)
        {
            _ctx = context;
            _state = state;
        }

        public FloodPhase Phase => _state.Phase;
        public RefugeStatus LowRefuge => _state.LowRefuge;
        private ChapterDefinition Def => _ctx.Chapter;
        private bool Relaxed => _ctx.Settings != null && _ctx.Settings.RelaxedMode;
        private bool Assist => _ctx.Settings != null && _ctx.Settings.ShelterAssist;

        /// <summary>Gameplay rain this phase (calm before the lantern and on the last crossing).</summary>
        public bool RainActive => Def.RainActive[(int)_state.Phase];

        /// <summary>Exposure penalties apply only with rain, outside relaxed mode and without shelter assist.</summary>
        public bool PenaltiesActive => RainActive && !Relaxed && !Assist;

        /// <summary>True after two seconds of continuous exposure; the simulation restores a checkpoint.</summary>
        public bool ResetRequested { get; private set; }

        public float ExposedSeconds(HandId carryingHand) => _exposed[(int)carryingHand];

        /// <summary>The open water level above the table (presentation).</summary>
        public float WaterHeight => Def.WaterHeights[(int)_state.Phase];

        /// <summary>Water around R stays at the old height while its closure is queued.</summary>
        public float LowRefugeWaterHeight =>
            _state.LowRefuge == RefugeStatus.Closed && !Relaxed ? Def.WaterHeights[(int)FloodPhase.RisingWater]
                : Def.WaterHeights[(int)FloodPhase.LowWater];

        public Vector3 RainFromWorld => _ctx.Stage != null ? _ctx.Stage.DirectionToWorld(Def.RainFromLocal) : Vector3.Normalize(Def.RainFromLocal);

        // ---- triggers from committed objectives -------------------------------

        public void OnLanternFirstLit()
        {
            if (_state.Phase == FloodPhase.Calm)
            {
                SetPhase(FloodPhase.LowWater);
            }
        }

        public void OnOrdinaryRescued(int ordinaryCount)
        {
            if (ordinaryCount == 1)
            {
                if (Relaxed)
                {
                    // Relaxed mode keeps R open: the water rises elsewhere but the rest stays dry.
                    SetPhase(FloodPhase.RisingWater);
                    return;
                }

                if (_state.LowRefuge == RefugeStatus.Open)
                {
                    _state.LowRefuge = RefugeStatus.ClosurePending;
                    _ctx.Emit(new SimEvent(SimEventKind.RefugeClosurePending, to: SlotRef.DockBerth(DockId.LowRefuge, 0)));
                }

                TryCommitClosure();
            }
            else if (ordinaryCount >= 2)
            {
                // Both passengers are home, so R is necessarily empty: commit any queued closure first.
                TryCommitClosure();
                SetPhase(FloodPhase.LastCrossing);
            }
        }

        /// <summary>Settings changed mid-chapter (relaxed mode toggled).</summary>
        public void OnSettingsChanged()
        {
            int ordinary = _state.OrdinaryRescuedCount;
            if (Relaxed)
            {
                if (_state.LowRefuge != RefugeStatus.Open)
                {
                    _state.LowRefuge = RefugeStatus.Open;
                    _ctx.Emit(new SimEvent(SimEventKind.RefugeReopened, to: SlotRef.DockBerth(DockId.LowRefuge, 0)));
                }
            }
            else if (ordinary == 1 && _state.LowRefuge == RefugeStatus.Open)
            {
                // Leaving relaxed mode after the first rescue queues the closure again.
                _state.LowRefuge = RefugeStatus.ClosurePending;
                if (_state.Phase == FloodPhase.RisingWater)
                {
                    SetPhase(FloodPhase.LowWater);
                }

                _ctx.Emit(new SimEvent(SimEventKind.RefugeClosurePending, to: SlotRef.DockBerth(DockId.LowRefuge, 0)));
                TryCommitClosure();
            }

            ResetExposure();
        }

        public void ResetForChapter()
        {
            ResetExposure();
            ResetRequested = false;
        }

        public void OnRestored()
        {
            ResetExposure();
            ResetRequested = false;
        }

        // ---- the ordered step ---------------------------------------------------

        /// <summary>Resolves queued flood events, then advances exposure with gameplay time.</summary>
        public void Step(float simDt)
        {
            ResetRequested = false;
            TryCommitClosure();
            if (_ctx.Stage == null || simDt <= 0f)
            {
                return;
            }

            UpdateExposure(simDt);
        }

        private void TryCommitClosure()
        {
            if (_state.LowRefuge != RefugeStatus.ClosurePending || Relaxed)
            {
                return;
            }

            // Never close R while it is occupied or reserved.
            if (_ctx.Ledger.CountAtDock(DockId.LowRefuge, includeReserved: true) > 0)
            {
                return;
            }

            _state.LowRefuge = RefugeStatus.Closed;
            _ctx.Emit(new SimEvent(SimEventKind.RefugeClosed, to: SlotRef.DockBerth(DockId.LowRefuge, 0)));
            if (_state.Phase == FloodPhase.LowWater)
            {
                SetPhase(FloodPhase.RisingWater);
            }
        }

        private void SetPhase(FloodPhase phase)
        {
            if (_state.Phase == phase)
            {
                return;
            }

            FloodPhase previous = _state.Phase;
            _state.Phase = phase;
            _ctx.Emit(new SimEvent(SimEventKind.FloodPhaseChanged, code: (int)phase, value: (int)previous));
        }

        // ---- exposure -----------------------------------------------------------

        private void UpdateExposure(float dt)
        {
            InteractionTuning t = _ctx.Tuning;
            double now = _ctx.Clock.RealTime;
            Vector3 rainFrom = RainFromWorld;
            foreach (HandId hand in Ids.Hands)
            {
                int i = (int)hand;
                _sheltering[(int)hand.Other()] = false;
                if (!_ctx.Ledger.PalmIsOccupied(hand))
                {
                    ClearHand(hand);
                    continue;
                }

                PalmPlatform carrier = _ctx.Palm(hand);
                Vector3 head = carrier.SupportCenter + HMath.Up * Def.DollHeadHeight;
                bool exposedZone = PenaltiesActive && IsExposed(head);
                bool sheltered = false;
                if (exposedZone)
                {
                    HandId other = hand.Other();
                    PalmPlatform shield = _ctx.Palm(other);
                    if (shield.IsTracked && shield.ShieldPose && CanopyCovers(shield, head, rainFrom))
                    {
                        sheltered = true;
                        _sheltering[(int)other] = true;
                        _lastEngaged[(int)other] = now;
                    }
                    else if (!shield.IsTracked && now - _lastEngaged[(int)other] <= t.TrackingGraceSeconds + 0.05)
                    {
                        // Keep the last good shelter through the tracking grace period.
                        sheltered = true;
                        _sheltering[(int)other] = true;
                    }
                }

                if (exposedZone && !sheltered)
                {
                    _safe[i] = 0f;
                    _exposed[i] += dt;
                    if (!_cueShown[i] && _exposed[i] >= Def.ShelterCueSeconds)
                    {
                        _cueShown[i] = true;
                        _ctx.Emit(new SimEvent(SimEventKind.ShelterCue, _ctx.Ledger.PalmDoll(hand), hand: hand, value: _exposed[i]));
                    }

                    if (_exposed[i] >= Def.ExposureResetSeconds)
                    {
                        _ctx.Emit(new SimEvent(SimEventKind.ExposureReset, _ctx.Ledger.PalmDoll(hand), hand: hand, value: _exposed[i]));
                        ResetRequested = true;
                        ResetExposure();
                        return;
                    }
                }
                else
                {
                    _safe[i] += dt;
                    if (_safe[i] >= Def.SafeResetSeconds && _exposed[i] > 0f)
                    {
                        if (_cueShown[i])
                        {
                            _ctx.Emit(new SimEvent(SimEventKind.ExposureCleared, _ctx.Ledger.PalmDoll(hand), hand: hand));
                        }

                        _exposed[i] = 0f;
                        _cueShown[i] = false;
                    }
                }
            }
        }

        private void ClearHand(HandId hand)
        {
            int i = (int)hand;
            _exposed[i] = 0f;
            _safe[i] = 0f;
            _cueShown[i] = false;
        }

        private void ResetExposure()
        {
            for (int i = 0; i < 2; i++)
            {
                _exposed[i] = 0f;
                _safe[i] = 0f;
                _cueShown[i] = false;
                _sheltering[i] = false;
            }
        }

        /// <summary>Exposed means inside the rain region and outside every open dry transfer area.</summary>
        public bool IsExposed(Vector3 point)
        {
            StageLayout stage = _ctx.Stage;
            if (stage == null || !stage.IsInRainRegion(point))
            {
                return false;
            }

            foreach (DockId dock in Ids.Docks)
            {
                if (_ctx.Policy.IsDryAreaOpen(dock) && stage.IsInDryArea(dock, point))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The canopy is a bounded disc in the plane of an empty palm-down hand. The doll is
        /// sheltered when the ray from its head towards the rain source hits the disc within range.
        /// </summary>
        public bool CanopyCovers(PalmPlatform shield, Vector3 head, Vector3 rainFrom)
        {
            Vector3 centre = shield.FilteredPose.Position;
            Vector3 normal = shield.Normal;
            float denominator = Vector3.Dot(rainFrom, normal);
            if (Math.Abs(denominator) < 1e-4f)
            {
                return false;
            }

            float distance = Vector3.Dot(centre - head, normal) / denominator;
            if (distance <= 0f || distance > Def.CanopyMaxDistance)
            {
                return false;
            }

            Vector3 hit = head + rainFrom * distance;
            return Vector3.Distance(hit, centre) <= Def.CanopyRadius;
        }

        /// <summary>
        /// For the recovery hook: this hand is the active shield over an exposed passenger (or was,
        /// within the tracking grace period).
        /// </summary>
        public bool IsShieldEngaged(HandId hand)
        {
            return _sheltering[(int)hand]
                || (!_ctx.Palm(hand).IsTracked && _ctx.Clock.RealTime - _lastEngaged[(int)hand] <= _ctx.Tuning.TrackingGraceSeconds + 0.05);
        }

        /// <summary>Where a shield should be for a doll at this head position (presentation hint and tests).</summary>
        public Vector3 IdealCanopyCentre(Vector3 head, float distance)
        {
            return head + RainFromWorld * distance;
        }
    }
}
