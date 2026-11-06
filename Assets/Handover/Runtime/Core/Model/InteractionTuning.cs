using System.Collections.Generic;

namespace Handover.Core
{
    /// <summary>
    /// Every interaction threshold in one place. All values are the specification's
    /// starting values (Sections 6, 12, 13 and 14), not measured results; tune them
    /// with the visible hand and doll together. Distances are metres, times seconds,
    /// angles degrees.
    /// </summary>
    public sealed class InteractionTuning
    {
        /// <summary>Bumped whenever a default changes meaningfully; recorded in checkpoints.</summary>
        public const int ConfigVersion = 1;

        // ---- Palm filtering (Section 12) ---------------------------------------
        public float PositionTimeConstant = 0.04f;
        public float RotationTimeConstant = 0.06f;
        /// <summary>Maximum visual separation between the filtered support and the real palm.</summary>
        public float MaxSupportLag = 0.015f;
        /// <summary>Cling ends once the lag falls below this (hysteresis).</summary>
        public float ClingReleaseLag = 0.0075f;
        /// <summary>Support plane height above the tracked palm centre (skin plus a small margin).</summary>
        public float SupportHeight = 0.02f;
        /// <summary>Half of the 6-8 cm usable support width (across the knuckles).</summary>
        public float SupportHalfWidth = 0.035f;
        /// <summary>Half of the support length (wrist to knuckles).</summary>
        public float SupportHalfLength = 0.04f;
        /// <summary>A sample older than this is stale and treated as untracked.</summary>
        public float StaleSampleSeconds = 0.1f;

        // ---- Role classifier (Sections 6 and 12) --------------------------------
        public float CarryEnterDegrees = 35f;
        public float CarryExitDegrees = 45f;
        public float TiltWarnDegrees = 25f;
        public float TiltClearDegrees = 20f;
        public float TiltWarnDwellSeconds = 0.2f;
        /// <summary>Shield requires the palm within this angle of palm-down.</summary>
        public float ShieldEnterDegrees = 40f;
        public float ShieldExitDegrees = 50f;
        public float ShieldDwellSeconds = 0.2f;

        // ---- Hop candidates (Section 13) ----------------------------------------
        /// <summary>Support-edge gap that opens a palm-to-palm candidate (1-4 cm).</summary>
        public float ConnectGapMin = 0.01f;
        public float ConnectGapMax = 0.04f;
        /// <summary>Overlap tolerated once a candidate exists.</summary>
        public float HoldGapMin = -0.015f;
        /// <summary>Above this gap a candidate or pre-take-off reservation ends.</summary>
        public float DisconnectGap = 0.07f;
        /// <summary>Docks are virtual, so a palm may slightly overlap a berth.</summary>
        public float DockConnectGapMin = -0.02f;
        /// <summary>Overlap tolerated with a berth once a dock candidate exists.</summary>
        public float DockHoldGapMin = -0.045f;
        public float MaxVerticalOffset = 0.03f;
        public float HoldVerticalOffset = 0.045f;
        public float MaxNormalDifferenceDegrees = 30f;
        public float HoldNormalDifferenceDegrees = 40f;
        /// <summary>Dwell only accumulates while the relative edge speed is below this.</summary>
        public float MaxRelativeEdgeSpeed = 0.12f;
        /// <summary>A reserved hop that has not taken off is cancelled above this speed.</summary>
        public float CancelRelativeEdgeSpeed = 0.35f;
        public float CandidateDwellSeconds = 0.3f;
        public float BerthSupportRadius = 0.022f;

        // ---- Transfer transactions (Section 13) ---------------------------------
        public float AnticipationSeconds = 0.15f;
        public float HopSeconds = 0.55f;
        public float ArcHeight = 0.015f;
        public float ReceiveCooldownSeconds = 0.5f;
        /// <summary>After the midpoint the receiver must stay within this distance of the take-off point.</summary>
        public float MaxPostMidpointSpan = 0.18f;

        // ---- Recovery (Section 14) ----------------------------------------------
        /// <summary>Keep the last good support this long before a system hold.</summary>
        public float TrackingGraceSeconds = 0.15f;
        public float StableTrackingSeconds = 0.25f;
        public float BlendBackMaxDisplacement = 0.05f;

        // ---- Steady mode (Section 7) --------------------------------------------
        public float SteadyToleranceScale = 1.35f;
        public float SteadyAnticipationScale = 1.6f;

        public InteractionTuning Clone()
        {
            return (InteractionTuning)MemberwiseClone();
        }

        /// <summary>
        /// Steady mode widens alignment tolerances and slows anticipation while keeping
        /// the same filtering, so the doll stays visually attached to the palm.
        /// </summary>
        public InteractionTuning WithSteadyMode()
        {
            InteractionTuning t = Clone();
            float s = SteadyToleranceScale;
            t.ConnectGapMax *= s;
            t.DisconnectGap *= s;
            t.MaxVerticalOffset *= s;
            t.HoldVerticalOffset *= s;
            t.MaxNormalDifferenceDegrees *= s;
            t.HoldNormalDifferenceDegrees *= s;
            t.MaxRelativeEdgeSpeed *= s;
            t.CancelRelativeEdgeSpeed *= s;
            t.CarryExitDegrees = HMath.Clamp(t.CarryExitDegrees * s, t.CarryExitDegrees, 70f);
            t.AnticipationSeconds *= SteadyAnticipationScale;
            t.MaxPostMidpointSpan *= s;
            return t;
        }

        /// <summary>Returns human-readable problems; empty when the hysteresis bands are consistent.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            void Require(bool condition, string message)
            {
                if (!condition)
                {
                    problems.Add(message);
                }
            }

            Require(PositionTimeConstant >= 0f && RotationTimeConstant >= 0f, "Filter time constants must be non-negative.");
            Require(ClingReleaseLag < MaxSupportLag, "Cling release lag must be below the maximum support lag.");
            Require(CarryEnterDegrees < CarryExitDegrees, "Carry enter angle must be below the exit angle.");
            Require(TiltClearDegrees < TiltWarnDegrees, "Tilt clear angle must be below the warning angle.");
            Require(TiltWarnDegrees < CarryEnterDegrees, "Tilt warning should start before carry eligibility ends.");
            Require(ShieldEnterDegrees < ShieldExitDegrees, "Shield enter angle must be below the exit angle.");
            Require(ConnectGapMin < ConnectGapMax, "Connect gap range is empty.");
            Require(HoldGapMin <= ConnectGapMin, "Hold gap minimum must not exceed the connect minimum.");
            Require(DockHoldGapMin <= DockConnectGapMin, "Dock hold gap minimum must not exceed the dock connect minimum.");
            Require(ConnectGapMax < DisconnectGap, "Disconnect gap must exceed the connect maximum.");
            Require(MaxVerticalOffset <= HoldVerticalOffset, "Hold vertical offset must not be below the connect limit.");
            Require(MaxNormalDifferenceDegrees <= HoldNormalDifferenceDegrees, "Hold normal difference must not be below the connect limit.");
            Require(MaxRelativeEdgeSpeed <= CancelRelativeEdgeSpeed, "Cancel speed must not be below the dwell speed.");
            Require(HopSeconds > 0f, "Hop duration must be positive.");
            Require(CandidateDwellSeconds >= 0f && AnticipationSeconds >= 0f, "Dwell and anticipation must be non-negative.");
            Require(TrackingGraceSeconds >= 0f && StableTrackingSeconds >= 0f, "Recovery timings must be non-negative.");
            return problems;
        }
    }
}
