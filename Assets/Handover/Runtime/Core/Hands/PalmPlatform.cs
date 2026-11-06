using System;
using System.Numerics;

namespace Handover.Core
{
    [Flags]
    public enum PalmChange
    {
        None = 0,
        TrackingLost = 1 << 0,
        TrackingRestored = 1 << 1,
        OriginChanged = 1 << 2,
        TiltWarningStarted = 1 << 3,
        TiltWarningCleared = 1 << 4,
        ClingStarted = 1 << 5,
        ClingEnded = 1 << 6,
        ShieldRaised = 1 << 7,
        ShieldLowered = 1 << 8,
    }

    /// <summary>
    /// One hand as a moving platform: filters the palm pose (frame-rate independent),
    /// bounds the visual lag, tracks validity and freshness, and classifies the palm's
    /// role with dwell and hysteresis. It never owns or moves a doll; the ledger does.
    /// </summary>
    public sealed class PalmPlatform
    {
        private InteractionTuning _tuning;
        private float _tiltTimer;
        private float _shieldTimer;
        private bool _hasFiltered;

        public PalmPlatform(HandId hand, InteractionTuning tuning)
        {
            Hand = hand;
            _tuning = tuning;
            LastTrackedTime = double.NegativeInfinity;
            TrackedSince = double.PositiveInfinity;
            RawPose = Pose3.Identity;
            FilteredPose = Pose3.Identity;
        }

        public HandId Hand { get; }

        /// <summary>True when the latest sample is valid, fresh and from the current origin epoch.</summary>
        public bool IsTracked { get; private set; }
        public bool HasEverTracked => _hasFiltered;
        public double LastTrackedTime { get; private set; }
        /// <summary>Start of the current uninterrupted tracked period (infinity when untracked).</summary>
        public double TrackedSince { get; private set; }
        public int OriginEpoch { get; private set; }

        public Pose3 RawPose { get; private set; }
        public Pose3 FilteredPose { get; private set; }
        /// <summary>Filtered support point: where a riding doll stands.</summary>
        public Vector3 SupportCenter { get; private set; }
        public Vector3 SupportVelocity { get; private set; }
        public Vector3 Normal => FilteredPose.Up;
        /// <summary>Filtered angle (degrees) between the palm normal and world up. Gameplay and warnings share it.</summary>
        public float AngleFromUp { get; private set; }
        public float SupportLag { get; private set; }

        public bool CarryEligible { get; private set; }
        public bool TiltWarning { get; private set; }
        public bool ShieldPose { get; private set; }
        public bool Clinging { get; private set; }

        public void SetTuning(InteractionTuning tuning)
        {
            _tuning = tuning;
        }

        public double TrackedDuration(double now)
        {
            return IsTracked ? Math.Max(0.0, now - TrackedSince) : 0.0;
        }

        public double UntrackedDuration(double now)
        {
            if (IsTracked)
            {
                return 0.0;
            }

            return double.IsNegativeInfinity(LastTrackedTime) ? double.PositiveInfinity : now - LastTrackedTime;
        }

        /// <summary>
        /// Processes one sample. Freshness compares the sample time with the provider's own
        /// clock (<paramref name="providerNow"/>); durations use the simulation's real time
        /// (<paramref name="now"/>). <paramref name="realDt"/> drives filtering (which keeps
        /// following the hand while paused); <paramref name="simDt"/> drives gameplay dwell
        /// timers (frozen while paused or recovering).
        /// </summary>
        public PalmChange Update(in PalmSample sample, double providerNow, int providerEpoch, double now, float realDt, float simDt, bool occupied)
        {
            PalmChange changes = PalmChange.None;
            bool wasTracked = IsTracked;

            bool fresh = providerNow - sample.SampleTime <= _tuning.StaleSampleSeconds + 1e-9;
            bool tracked = sample.IsValid && fresh && sample.OriginEpoch == providerEpoch;

            if (tracked && _hasFiltered && sample.OriginEpoch != OriginEpoch)
            {
                // Origin reset: invalidate continuity before processing movement.
                changes |= PalmChange.OriginChanged;
                _hasFiltered = false;
            }

            if (tracked)
            {
                OriginEpoch = sample.OriginEpoch;
                RawPose = sample.Pose;
                Filter(sample.Pose, realDt);
                LastTrackedTime = now;
                if (!wasTracked)
                {
                    TrackedSince = now;
                    changes |= PalmChange.TrackingRestored;
                }
            }
            else
            {
                SupportVelocity = Vector3.Zero;
                TrackedSince = double.PositiveInfinity;
                if (wasTracked)
                {
                    changes |= PalmChange.TrackingLost;
                }
            }

            IsTracked = tracked;
            changes |= ClassifyRoles(occupied, simDt);
            return changes;
        }

        private void Filter(Pose3 raw, float realDt)
        {
            Vector3 previousSupport = SupportCenter;
            bool hadFiltered = _hasFiltered;
            if (!_hasFiltered)
            {
                FilteredPose = raw;
                _hasFiltered = true;
            }
            else
            {
                float aPos = HMath.DampingAlpha(realDt, _tuning.PositionTimeConstant);
                float aRot = HMath.DampingAlpha(realDt, _tuning.RotationTimeConstant);
                Vector3 position = Vector3.Lerp(FilteredPose.Position, raw.Position, aPos);
                Quaternion rotation = HMath.SlerpShortest(FilteredPose.Rotation, raw.Rotation, aRot);

                // Bound the visual separation from the real palm; beyond it, cling.
                Vector3 lagVector = raw.Position - position;
                float lag = lagVector.Length();
                if (lag > _tuning.MaxSupportLag)
                {
                    position = raw.Position - lagVector / lag * _tuning.MaxSupportLag;
                    lag = _tuning.MaxSupportLag;
                    if (!Clinging)
                    {
                        Clinging = true;
                    }
                }
                else if (Clinging && lag < _tuning.ClingReleaseLag)
                {
                    Clinging = false;
                }

                SupportLag = lag;
                FilteredPose = new Pose3(position, rotation);
            }

            SupportCenter = FilteredPose.Position + FilteredPose.Up * _tuning.SupportHeight;
            AngleFromUp = HMath.AngleDegrees(FilteredPose.Up, HMath.Up);

            if (hadFiltered && realDt > 1e-5f)
            {
                Vector3 instantaneous = (SupportCenter - previousSupport) / realDt;
                float a = HMath.DampingAlpha(realDt, 0.05f);
                SupportVelocity = Vector3.Lerp(SupportVelocity, instantaneous, a);
            }
            else
            {
                SupportVelocity = Vector3.Zero;
            }
        }

        private bool _clingReported;
        private bool _tiltReported;
        private bool _shieldReported;

        private PalmChange ClassifyRoles(bool occupied, float simDt)
        {
            PalmChange changes = PalmChange.None;
            float angle = AngleFromUp;

            if (!IsTracked)
            {
                CarryEligible = false;
                TiltWarning = false;
                ShieldPose = false;
                Clinging = false;
                _tiltTimer = 0f;
                _shieldTimer = 0f;
            }
            else
            {
                // Carry eligibility: enter within 35 degrees of palm-up, leave beyond 45.
                if (!CarryEligible && angle <= _tuning.CarryEnterDegrees)
                {
                    CarryEligible = true;
                }
                else if (CarryEligible && angle > _tuning.CarryExitDegrees)
                {
                    CarryEligible = false;
                }

                // Tilt warning on an occupied palm: above 25 degrees after 0.2 s, clear below 20.
                if (occupied)
                {
                    if (!TiltWarning)
                    {
                        if (angle > _tuning.TiltWarnDegrees)
                        {
                            _tiltTimer += simDt;
                            if (_tiltTimer >= _tuning.TiltWarnDwellSeconds)
                            {
                                TiltWarning = true;
                            }
                        }
                        else
                        {
                            _tiltTimer = 0f;
                        }
                    }
                    else if (angle < _tuning.TiltClearDegrees)
                    {
                        TiltWarning = false;
                        _tiltTimer = 0f;
                    }
                }
                else
                {
                    TiltWarning = false;
                    _tiltTimer = 0f;
                }

                // Shield pose: an empty hand within 40 degrees of palm-down for 0.2 s.
                // Carrying takes priority: an occupied hand never becomes a shield.
                float fromDown = 180f - angle;
                if (!occupied)
                {
                    if (!ShieldPose)
                    {
                        if (fromDown <= _tuning.ShieldEnterDegrees)
                        {
                            _shieldTimer += simDt;
                            if (_shieldTimer >= _tuning.ShieldDwellSeconds)
                            {
                                ShieldPose = true;
                            }
                        }
                        else
                        {
                            _shieldTimer = 0f;
                        }
                    }
                    else if (fromDown > _tuning.ShieldExitDegrees)
                    {
                        ShieldPose = false;
                        _shieldTimer = 0f;
                    }
                }
                else
                {
                    ShieldPose = false;
                    _shieldTimer = 0f;
                }
            }

            if (TiltWarning != _tiltReported)
            {
                changes |= TiltWarning ? PalmChange.TiltWarningStarted : PalmChange.TiltWarningCleared;
                _tiltReported = TiltWarning;
            }

            if (Clinging != _clingReported)
            {
                changes |= Clinging ? PalmChange.ClingStarted : PalmChange.ClingEnded;
                _clingReported = Clinging;
            }

            if (ShieldPose != _shieldReported)
            {
                changes |= ShieldPose ? PalmChange.ShieldRaised : PalmChange.ShieldLowered;
                _shieldReported = ShieldPose;
            }

            return changes;
        }

        /// <summary>Clears filter continuity and role timers (used after a checkpoint restore or reposition).</summary>
        public void ResetContinuity()
        {
            _hasFiltered = false;
            _tiltTimer = 0f;
            _shieldTimer = 0f;
            SupportVelocity = Vector3.Zero;
        }
    }
}
