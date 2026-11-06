using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// One authored landing node in stage-local coordinates (metres at scale 1; origin at
    /// the stage centre on the table surface, +X right, +Y up, +Z away from the player).
    /// </summary>
    public sealed class DockDefinition
    {
        public DockId Id;
        /// <summary>Top-centre of the dock's landing surface.</summary>
        public Vector3 LocalCenter;
        /// <summary>Berth surface points relative to <see cref="LocalCenter"/>.</summary>
        public Vector3[] BerthOffsets;
        /// <summary>Optional owner of each berth (Departure's named berths); None means shared.</summary>
        public DollId[] NamedBerths;
        /// <summary>Horizontal radius of the dry transfer area around the dock centre.</summary>
        public float DryAreaRadius = 0.1f;
        /// <summary>Height of the dry transfer area above the table surface.</summary>
        public float DryAreaHeight = 0.3f;

        public int Capacity => BerthOffsets.Length;

        public DockDefinition Clone()
        {
            var copy = (DockDefinition)MemberwiseClone();
            copy.BerthOffsets = (Vector3[])BerthOffsets.Clone();
            copy.NamedBerths = NamedBerths == null ? null : (DollId[])NamedBerths.Clone();
            return copy;
        }
    }

    /// <summary>
    /// An authored layout of D, R, U and H with the same puzzle logic as every other
    /// variant. Fitting picks a variant and a uniform scale; it never generates layouts.
    /// </summary>
    public sealed class StageLayoutVariant
    {
        public string Name = "Standard";
        /// <summary>Footprint along stage X (left-right) at scale 1.</summary>
        public float Width = 0.5f;
        /// <summary>Footprint along stage Z (near-far) at scale 1.</summary>
        public float Depth = 0.44f;
        public float MinScale = 0.8f;
        public float MaxScale = 1.1f;
        /// <summary>Rain covers the footprint plus this margin so going round the edge is not a dry route.</summary>
        public float RainMargin = 0.08f;
        /// <summary>Rain region height above the table.</summary>
        public float RainHeight = 0.45f;
        public DockDefinition[] Docks = new DockDefinition[4];

        public DockDefinition Dock(DockId id) => Docks[(int)id];

        public int[] BerthCounts()
        {
            var counts = new int[4];
            foreach (DockId id in Ids.Docks)
            {
                counts[(int)id] = Dock(id).Capacity;
            }

            return counts;
        }

        /// <summary>
        /// The reference layout from the route diagram: Departure left, Harbour right, the
        /// lantern refuge U high and to the back, the low refuge R low and towards the player.
        /// Heights follow Section 5 (D, U, H about 9 cm; R about 3 cm).
        /// </summary>
        public static StageLayoutVariant CreateStandard()
        {
            var v = new StageLayoutVariant { Name = "Standard", Width = 0.5f, Depth = 0.44f };
            float spacing = 0.05f;
            v.Docks[(int)DockId.Departure] = new DockDefinition
            {
                Id = DockId.Departure,
                LocalCenter = new Vector3(-0.2f, 0.09f, 0f),
                // Keeper's berth is nearest the player.
                BerthOffsets = new[] { new Vector3(0f, 0f, -spacing), Vector3.Zero, new Vector3(0f, 0f, spacing) },
                NamedBerths = new[] { DollId.Keeper, DollId.Pip, DollId.Luma },
            };
            v.Docks[(int)DockId.LowRefuge] = new DockDefinition
            {
                Id = DockId.LowRefuge,
                LocalCenter = new Vector3(0f, 0.03f, -0.12f),
                BerthOffsets = new[] { Vector3.Zero },
            };
            v.Docks[(int)DockId.LanternRefuge] = new DockDefinition
            {
                Id = DockId.LanternRefuge,
                LocalCenter = new Vector3(0f, 0.09f, 0.12f),
                BerthOffsets = new[] { Vector3.Zero },
            };
            v.Docks[(int)DockId.Harbour] = new DockDefinition
            {
                Id = DockId.Harbour,
                LocalCenter = new Vector3(0.2f, 0.09f, 0f),
                BerthOffsets = new[] { new Vector3(0f, 0f, -spacing), Vector3.Zero, new Vector3(0f, 0f, spacing) },
            };
            return v;
        }

        /// <summary>A tighter variant for small tables: same topology, shorter crossings.</summary>
        public static StageLayoutVariant CreateCompact()
        {
            StageLayoutVariant v = CreateStandard();
            v.Name = "Compact";
            v.Width = 0.42f;
            v.Depth = 0.38f;
            v.MinScale = 0.85f;
            v.MaxScale = 1.0f;
            v.Docks[(int)DockId.Departure].LocalCenter = new Vector3(-0.17f, 0.09f, 0f);
            v.Docks[(int)DockId.Harbour].LocalCenter = new Vector3(0.17f, 0.09f, 0f);
            v.Docks[(int)DockId.LowRefuge].LocalCenter = new Vector3(0f, 0.03f, -0.105f);
            v.Docks[(int)DockId.LanternRefuge].LocalCenter = new Vector3(0f, 0.09f, 0.105f);
            foreach (DockDefinition d in v.Docks)
            {
                d.DryAreaRadius = 0.09f;
            }

            return v;
        }

        public StageLayoutVariant Clone()
        {
            var copy = (StageLayoutVariant)MemberwiseClone();
            copy.Docks = new DockDefinition[4];
            for (int i = 0; i < 4; i++)
            {
                copy.Docks[i] = Docks[i].Clone();
            }

            return copy;
        }
    }
}
