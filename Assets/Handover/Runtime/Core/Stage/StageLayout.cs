using System;
using System.Numerics;

namespace Handover.Core
{
    public enum StageSource
    {
        /// <summary>Fitted to a detected horizontal surface (scene data).</summary>
        SceneSurface = 0,
        /// <summary>Hand-placed virtual tabletop (no scan, permission declined or no fit).</summary>
        VirtualTabletop = 1,
    }

    /// <summary>Where a variant is placed: stage origin on the surface, yaw around world up, uniform scale.</summary>
    public readonly struct StagePlacement
    {
        public readonly Vector3 Origin;
        public readonly float YawRadians;
        public readonly float Scale;
        public readonly StageSource Source;
        public readonly int OriginEpoch;

        public StagePlacement(Vector3 origin, float yawRadians, float scale, StageSource source, int originEpoch)
        {
            Origin = origin;
            YawRadians = yawRadians;
            Scale = scale;
            Source = source;
            OriginEpoch = originEpoch;
        }

        public Pose3 Pose => new Pose3(Origin, HMath.YawRotation(YawRadians));
    }

    /// <summary>World-space dock data derived from a definition and a placement.</summary>
    public sealed class DockLayout
    {
        public DockId Id;
        public Vector3 Center;
        public Vector3[] Berths;
        public DollId[] NamedBerths;
        public float DryAreaRadius;
        public float DryAreaHeight;
        public float SurfaceHeight;

        public int Capacity => Berths.Length;

        public DollId BerthOwner(int berth)
        {
            return NamedBerths == null || berth >= NamedBerths.Length ? DollId.None : NamedBerths[berth];
        }

        public int NamedBerthFor(DollId doll)
        {
            if (NamedBerths == null)
            {
                return -1;
            }

            return Array.IndexOf(NamedBerths, doll);
        }
    }

    /// <summary>
    /// The confirmed stage in world space. Immutable once built; a new placement builds
    /// a new layout (placement changes stay outside active gameplay).
    /// </summary>
    public sealed class StageLayout
    {
        private readonly DockLayout[] _docks = new DockLayout[4];

        private StageLayout(StageLayoutVariant variant, StagePlacement placement)
        {
            Variant = variant;
            Placement = placement;
            Pose = placement.Pose;
            Scale = placement.Scale;
        }

        public StageLayoutVariant Variant { get; }
        public StagePlacement Placement { get; }
        public Pose3 Pose { get; }
        public float Scale { get; }
        public float TableHeight => Pose.Position.Y;

        public DockLayout Dock(DockId id) => _docks[(int)id];

        public static StageLayout Build(StageLayoutVariant variant, StagePlacement placement)
        {
            var layout = new StageLayout(variant, placement);
            foreach (DockId id in Ids.Docks)
            {
                DockDefinition def = variant.Dock(id);
                var dock = new DockLayout
                {
                    Id = id,
                    Center = layout.ToWorld(def.LocalCenter),
                    Berths = new Vector3[def.Capacity],
                    NamedBerths = def.NamedBerths,
                    DryAreaRadius = def.DryAreaRadius * placement.Scale,
                    DryAreaHeight = def.DryAreaHeight * placement.Scale,
                };
                dock.SurfaceHeight = dock.Center.Y;
                for (int b = 0; b < def.Capacity; b++)
                {
                    dock.Berths[b] = layout.ToWorld(def.LocalCenter + def.BerthOffsets[b]);
                }

                layout._docks[(int)id] = dock;
            }

            return layout;
        }

        public Vector3 ToWorld(Vector3 stageLocal)
        {
            return Pose.TransformPoint(stageLocal * Scale);
        }

        public Vector3 ToLocal(Vector3 world)
        {
            return Pose.InverseTransformPoint(world) / Scale;
        }

        public Vector3 BerthPosition(SlotRef berth)
        {
            return Dock(berth.Dock).Berths[berth.Berth];
        }

        /// <summary>True when a point lies in the dock's dry transfer area (ignores open/closed state).</summary>
        public bool IsInDryArea(DockId id, Vector3 world)
        {
            DockLayout dock = Dock(id);
            float height = world.Y - TableHeight;
            return height >= -0.05f * Scale
                && height <= dock.DryAreaHeight
                && HMath.HorizontalDistance(world, dock.Center) <= dock.DryAreaRadius;
        }

        /// <summary>The rain region: the footprint plus a margin, up to the rain height.</summary>
        public bool IsInRainRegion(Vector3 world)
        {
            Vector3 local = ToLocal(world);
            float halfWidth = Variant.Width * 0.5f + Variant.RainMargin;
            float halfDepth = Variant.Depth * 0.5f + Variant.RainMargin;
            return Math.Abs(local.X) <= halfWidth
                && Math.Abs(local.Z) <= halfDepth
                && local.Y >= -0.05f
                && local.Y <= Variant.RainHeight;
        }

        /// <summary>Converts a stage-local direction (for example the rain source direction) to world space.</summary>
        public Vector3 DirectionToWorld(Vector3 stageLocalDirection)
        {
            return Vector3.Normalize(Pose.TransformDirection(stageLocalDirection));
        }
    }
}
