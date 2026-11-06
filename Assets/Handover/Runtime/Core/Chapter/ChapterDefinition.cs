using System.Numerics;

namespace Handover.Core
{
    public sealed class DollDefinition
    {
        public DollId Id;
        public string DisplayName;
        public Personality Personality;
        /// <summary>Departure berth index this doll starts on and always returns to.</summary>
        public int HomeBerth;
        /// <summary>
        /// Visible anticipation variation (Luma hesitates longer). Same transfer system for
        /// every doll; only the anticipation duration scales.
        /// </summary>
        public float AnticipationScale = 1f;
    }

    public enum FloodPhase
    {
        /// <summary>Before Keeper first lights the lantern: tutorial, no rain.</summary>
        Calm = 0,
        /// <summary>Phase 0: Keeper lit U. R is open; Pip or Luma may go first.</summary>
        LowWater = 1,
        /// <summary>Phase 1: the first ordinary passenger is home and R has closed.</summary>
        RisingWater = 2,
        /// <summary>Phase 2: both ordinary passengers are home; rain calms; Keeper is released.</summary>
        LastCrossing = 3,
    }

    /// <summary>Authored chapter data. Runtime state lives in <see cref="ChapterState"/>.</summary>
    public sealed class ChapterDefinition
    {
        public const int SchemaVersion = 1;

        public DollDefinition[] Dolls;
        public StageLayoutVariant[] LayoutVariants;

        /// <summary>Water height above the table per <see cref="FloodPhase"/> (Section 5 starting values).</summary>
        public float[] WaterHeights = { 0.01f, 0.01f, 0.04f, 0.06f };
        /// <summary>Whether gameplay rain (exposure) is active per <see cref="FloodPhase"/>.</summary>
        public bool[] RainActive = { false, true, true, false };

        /// <summary>
        /// Stage-local direction towards the rain source. Angled so the shield sits slightly
        /// behind (away from the player) and beside the carrying hand instead of directly over it.
        /// </summary>
        public Vector3 RainFromLocal = Vector3.Normalize(new Vector3(0.3f, 1f, 0.45f));

        public float ShelterCueSeconds = 0.5f;
        public float ExposureResetSeconds = 2f;
        public float SafeResetSeconds = 0.3f;
        /// <summary>Radius of the virtual canopy attached to a palm-down hand.</summary>
        public float CanopyRadius = 0.085f;
        /// <summary>The canopy only shelters when it is within this distance along the rain ray.</summary>
        public float CanopyMaxDistance = 0.35f;
        /// <summary>Height above the doll's feet used for the exposure ray (doll head).</summary>
        public float DollHeadHeight = 0.03f;

        public DollDefinition Doll(DollId id)
        {
            foreach (DollDefinition d in Dolls)
            {
                if (d.Id == id)
                {
                    return d;
                }
            }

            return null;
        }

        public static ChapterDefinition CreateDefault()
        {
            return new ChapterDefinition
            {
                Dolls = new[]
                {
                    new DollDefinition { Id = DollId.Keeper, DisplayName = "Keeper", Personality = Personality.Deliberate, HomeBerth = 0, AnticipationScale = 1.15f },
                    new DollDefinition { Id = DollId.Pip, DisplayName = "Pip", Personality = Personality.Eager, HomeBerth = 1, AnticipationScale = 0.75f },
                    new DollDefinition { Id = DollId.Luma, DisplayName = "Luma", Personality = Personality.Hesitant, HomeBerth = 2, AnticipationScale = 1.6f },
                },
                LayoutVariants = new[] { StageLayoutVariant.CreateStandard(), StageLayoutVariant.CreateCompact() },
            };
        }
    }
}
