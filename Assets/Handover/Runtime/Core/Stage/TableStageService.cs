using System;
using System.Collections.Generic;
using System.Numerics;

namespace Handover.Core
{
    /// <summary>
    /// A horizontal surface offered by scene data (for example an MRUK table anchor), in
    /// world space: top-surface centre, yaw of its local X axis around world up, and its
    /// extent along local X and Z.
    /// </summary>
    public readonly struct SurfaceCandidate
    {
        public readonly string Id;
        public readonly Vector3 Center;
        public readonly float YawRadians;
        public readonly float SizeX;
        public readonly float SizeZ;

        public SurfaceCandidate(string id, Vector3 center, float yawRadians, float sizeX, float sizeZ)
        {
            Id = id;
            Center = center;
            YawRadians = yawRadians;
            SizeX = sizeX;
            SizeZ = sizeZ;
        }

        public Pose3 Pose => new Pose3(Center, HMath.YawRotation(YawRadians));
    }

    /// <summary>The seated player's head (eye centre) at placement time.</summary>
    public readonly struct ViewerPose
    {
        public readonly Vector3 Position;
        public readonly Vector3 Forward;

        public ViewerPose(Vector3 position, Vector3 forward)
        {
            Position = position;
            Forward = HMath.SafeNormalize(HMath.Horizontal(forward), HMath.Forward);
        }
    }

    public enum FitIssue
    {
        None = 0,
        TooSmall,
        TooFar,
        BadHeight,
        OutOfReach,
        OutsideFieldOfView,
        Obstructed,
        StageShifted,
    }

    public sealed class StageFitSettings
    {
        /// <summary>Clearance kept from every table edge for hands.</summary>
        public float EdgeMargin = 0.04f;
        /// <summary>A table further than this (nearest point, horizontally) is not "near the seated player".</summary>
        public float MaxTableDistance = 0.6f;
        /// <summary>Acceptable table height below the eyes.</summary>
        public float MinDropBelowEyes = 0.15f;
        public float MaxDropBelowEyes = 0.95f;
        /// <summary>Comfortable seated reach from the head to any dock (a proxy for shoulder reach).</summary>
        public float MaxReach = 0.78f;
        public float MaxHorizontalReach = 0.66f;
        /// <summary>Keep the stage forward of the body to avoid prolonged neck flexion.</summary>
        public float MinForwardDistance = 0.2f;
        /// <summary>Essential content stays inside 58 x 54 degrees (Meta's recommendation).</summary>
        public float FieldOfViewHorizontal = 58f;
        public float FieldOfViewVertical = 54f;
        /// <summary>Recenter tolerance before a confirmed stage must be repositioned.</summary>
        public float MaxAnchorDrift = 0.03f;
        public float MaxAnchorRotationDegrees = 4f;
        /// <summary>Virtual tabletop: forward distance and height below the eyes.</summary>
        public float VirtualForward = 0.42f;
        public float VirtualDropBelowEyes = 0.42f;
        public float ScaleStep = 0.05f;
        public float ForwardStep = 0.02f;
    }

    public sealed class StageFitResult
    {
        public bool Success;
        public StagePlacement Placement;
        public StageLayoutVariant Variant;
        public StageLayout Layout;
        public string SurfaceId;
        /// <summary>Why each rejected surface was rejected (surface id, issue).</summary>
        public readonly List<(string surface, FitIssue issue)> Rejections = new List<(string, FitIssue)>();

        public FitIssue FirstIssue => Rejections.Count > 0 ? Rejections[0].issue : FitIssue.None;
    }

    /// <summary>
    /// Fits an authored layout variant to an eligible surface, or to a hand-placeable virtual
    /// tabletop when permissions are declined or nothing fits. The real table stays
    /// recognisable: the stage keeps an edge margin, aligns with the table edge facing the
    /// player and never extends past the surface. Every dock must satisfy comfortable reach
    /// and the 58 x 54 degree visibility budget from the actual seated view.
    /// </summary>
    public sealed class TableStageService
    {
        private readonly IReadOnlyList<StageLayoutVariant> _variants;

        public TableStageService(IReadOnlyList<StageLayoutVariant> variants, StageFitSettings settings = null)
        {
            _variants = variants;
            Settings = settings ?? new StageFitSettings();
        }

        public StageFitSettings Settings { get; }

        /// <summary>Best fit among the surfaces; falls back to a virtual tabletop when none fits.</summary>
        public StageFitResult Fit(IReadOnlyList<SurfaceCandidate> surfaces, ViewerPose viewer, int originEpoch)
        {
            var result = new StageFitResult();
            StageLayout best = null;
            StageLayoutVariant bestVariant = null;
            string bestSurface = null;
            float bestScore = float.MinValue;

            if (surfaces != null)
            {
                foreach (SurfaceCandidate surface in surfaces)
                {
                    FitIssue issue = TryFitSurface(surface, viewer, originEpoch, out StageLayout layout, out StageLayoutVariant variant);
                    if (issue != FitIssue.None)
                    {
                        result.Rejections.Add((surface.Id, issue));
                        continue;
                    }

                    // Prefer the largest comfortable stage, then the nearer table.
                    float score = layout.Scale * 10f - HMath.HorizontalDistance(viewer.Position, layout.Pose.Position);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = layout;
                        bestVariant = variant;
                        bestSurface = surface.Id;
                    }
                }
            }

            if (best != null)
            {
                result.Success = true;
                result.Layout = best;
                result.Placement = best.Placement;
                result.Variant = bestVariant;
                result.SurfaceId = bestSurface;
                return result;
            }

            StageLayout fallback = FitVirtualTabletop(viewer, originEpoch);
            result.Success = true;
            result.Layout = fallback;
            result.Placement = fallback.Placement;
            result.Variant = fallback.Variant;
            result.SurfaceId = null;
            return result;
        }

        /// <summary>The virtual tabletop: same level rules, placed comfortably in front of the seated player.</summary>
        public StageLayout FitVirtualTabletop(ViewerPose viewer, int originEpoch)
        {
            StageLayoutVariant variant = _variants[0];
            Vector3 origin = viewer.Position + viewer.Forward * Settings.VirtualForward;
            origin.Y = viewer.Position.Y - Settings.VirtualDropBelowEyes;
            float yaw = HMath.YawOf(viewer.Forward);
            for (float scale = Math.Min(1f, variant.MaxScale); scale >= variant.MinScale - 1e-4f; scale -= Settings.ScaleStep)
            {
                StageLayout layout = StageLayout.Build(variant, new StagePlacement(origin, yaw, scale, StageSource.VirtualTabletop, originEpoch));
                if (Validate(layout, viewer) == FitIssue.None)
                {
                    return layout;
                }
            }

            return StageLayout.Build(variant, new StagePlacement(origin, yaw, variant.MinScale, StageSource.VirtualTabletop, originEpoch));
        }

        /// <summary>Hand-placed virtual tabletop at a chosen origin and yaw (the player confirms or moves it).</summary>
        public StageLayout PlaceVirtualTabletop(Vector3 origin, float yawRadians, float scale, int originEpoch)
        {
            StageLayoutVariant variant = _variants[0];
            float s = HMath.Clamp(scale, variant.MinScale, variant.MaxScale);
            return StageLayout.Build(variant, new StagePlacement(origin, yawRadians, s, StageSource.VirtualTabletop, originEpoch));
        }

        /// <summary>Resize within the variant's tested limits; returns the issue when the resized stage no longer fits.</summary>
        public FitIssue Resize(StageLayout current, float newScale, ViewerPose viewer, out StageLayout resized)
        {
            StageLayoutVariant v = current.Variant;
            float s = HMath.Clamp(newScale, v.MinScale, v.MaxScale);
            StagePlacement p = current.Placement;
            resized = StageLayout.Build(v, new StagePlacement(p.Origin, p.YawRadians, s, p.Source, p.OriginEpoch));
            return Validate(resized, viewer);
        }

        private FitIssue TryFitSurface(SurfaceCandidate surface, ViewerPose viewer, int originEpoch,
            out StageLayout layout, out StageLayoutVariant chosen)
        {
            layout = null;
            chosen = null;
            StageFitSettings s = Settings;

            float drop = viewer.Position.Y - surface.Center.Y;
            if (drop < s.MinDropBelowEyes || drop > s.MaxDropBelowEyes)
            {
                return FitIssue.BadHeight;
            }

            Pose3 table = surface.Pose;
            Vector3 headLocal = table.InverseTransformPoint(viewer.Position);
            float hx = surface.SizeX * 0.5f;
            float hz = surface.SizeZ * 0.5f;
            float nearestX = HMath.Clamp(headLocal.X, -hx, hx);
            float nearestZ = HMath.Clamp(headLocal.Z, -hz, hz);
            float tableDistance = (float)Math.Sqrt((headLocal.X - nearestX) * (headLocal.X - nearestX) + (headLocal.Z - nearestZ) * (headLocal.Z - nearestZ));
            if (tableDistance > s.MaxTableDistance)
            {
                return FitIssue.TooFar;
            }

            // Align the stage with the table edge facing the player: stage +Z points away from the viewer.
            Vector3 towardsTable = HMath.SafeNormalize(HMath.Horizontal(surface.Center - viewer.Position), viewer.Forward);
            Vector3[] axes = { table.Right, -table.Right, table.Forward, -table.Forward };
            Vector3 stageForward = axes[0];
            float bestDot = float.MinValue;
            foreach (Vector3 axis in axes)
            {
                float d = Vector3.Dot(axis, towardsTable);
                if (d > bestDot)
                {
                    bestDot = d;
                    stageForward = axis;
                }
            }

            float stageYaw = HMath.YawOf(stageForward);
            Vector3 stageRight = Vector3.Cross(HMath.Up, stageForward);
            float extentAlongForward = Math.Abs(Vector3.Dot(stageForward, table.Right)) > 0.5f ? surface.SizeX : surface.SizeZ;
            float extentAlongRight = Math.Abs(Vector3.Dot(stageForward, table.Right)) > 0.5f ? surface.SizeZ : surface.SizeX;
            float availableDepth = extentAlongForward - 2f * s.EdgeMargin;
            float availableWidth = extentAlongRight - 2f * s.EdgeMargin;

            FitIssue lastIssue = FitIssue.TooSmall;
            foreach (StageLayoutVariant variant in _variants)
            {
                float maxScale = Math.Min(variant.MaxScale, Math.Min(availableWidth / variant.Width, availableDepth / variant.Depth));
                if (maxScale < variant.MinScale)
                {
                    continue;
                }

                // Coordinates along the stage axes relative to the table centre.
                float headAlongForward = Vector3.Dot(HMath.Horizontal(viewer.Position - surface.Center), stageForward);
                float headAlongRight = Vector3.Dot(HMath.Horizontal(viewer.Position - surface.Center), stageRight);

                for (float scale = maxScale; scale >= variant.MinScale - 1e-4f; scale -= s.ScaleStep)
                {
                    float halfDepth = variant.Depth * scale * 0.5f;
                    float halfWidth = variant.Width * scale * 0.5f;
                    float nearEdge = -extentAlongForward * 0.5f + s.EdgeMargin + halfDepth;
                    float farEdge = extentAlongForward * 0.5f - s.EdgeMargin - halfDepth;
                    float lateralLimit = extentAlongRight * 0.5f - s.EdgeMargin - halfWidth;
                    float lateral = HMath.Clamp(headAlongRight, -lateralLimit, lateralLimit);

                    // Start as near the player as the table allows, then step away until it fits.
                    float start = Math.Max(nearEdge, headAlongForward + s.MinForwardDistance + halfDepth);
                    for (float along = start; along <= farEdge + 1e-4f; along += s.ForwardStep)
                    {
                        Vector3 origin = surface.Center + stageForward * along + stageRight * lateral;
                        origin.Y = surface.Center.Y;
                        StageLayout candidate = StageLayout.Build(variant,
                            new StagePlacement(origin, stageYaw, scale, StageSource.SceneSurface, originEpoch));
                        FitIssue issue = Validate(candidate, viewer);
                        if (issue == FitIssue.None)
                        {
                            layout = candidate;
                            chosen = variant;
                            return FitIssue.None;
                        }

                        lastIssue = issue;
                        if (issue == FitIssue.OutOfReach)
                        {
                            // Moving further away only makes reach worse.
                            break;
                        }
                    }
                }
            }

            return lastIssue;
        }

        /// <summary>Checks comfortable reach, forward placement and the visibility budget for every dock.</summary>
        public FitIssue Validate(StageLayout layout, ViewerPose viewer)
        {
            StageFitSettings s = Settings;
            Vector3 head = viewer.Position;
            Vector3 centre = layout.Pose.Position;
            Vector3 view = Vector3.Normalize(centre + HMath.Up * (0.06f * layout.Scale) - head);
            Vector3 viewRight = HMath.SafeNormalize(Vector3.Cross(HMath.Up, view), HMath.Right);
            Vector3 viewUp = Vector3.Cross(view, viewRight);

            foreach (DockId id in Ids.Docks)
            {
                DockLayout dock = layout.Dock(id);
                foreach (Vector3 berth in dock.Berths)
                {
                    if (Vector3.Distance(head, berth) > s.MaxReach || HMath.HorizontalDistance(head, berth) > s.MaxHorizontalReach)
                    {
                        return FitIssue.OutOfReach;
                    }
                }

                // Visibility: the dock and its dry area edge (where hands meet) must fit the budget.
                Vector3 toDock = dock.Center - head;
                float forwardComponent = Vector3.Dot(toDock, view);
                if (forwardComponent <= 0.05f)
                {
                    return FitIssue.OutsideFieldOfView;
                }

                float horizontal = (float)Math.Atan2(Math.Abs(Vector3.Dot(toDock, viewRight)) + dock.DryAreaRadius * 0.5f, forwardComponent) * HMath.Rad2Deg;
                float vertical = (float)Math.Atan2(Math.Abs(Vector3.Dot(toDock, viewUp)), forwardComponent) * HMath.Rad2Deg;
                if (horizontal > s.FieldOfViewHorizontal * 0.5f || vertical > s.FieldOfViewVertical * 0.5f)
                {
                    return FitIssue.OutsideFieldOfView;
                }
            }

            float forwardDistance = Vector3.Dot(HMath.Horizontal(centre - head), viewer.Forward) - layout.Variant.Depth * layout.Scale * 0.5f;
            if (forwardDistance < s.MinForwardDistance - 0.05f)
            {
                return FitIssue.OutsideFieldOfView;
            }

            return FitIssue.None;
        }

        /// <summary>
        /// After a recenter or origin change: has the confirmed stage's anchor moved materially?
        /// Never move an occupied stage silently: when true, suspend play and ask to reposition.
        /// </summary>
        public bool NeedsReposition(Pose3 confirmedAnchor, Pose3 currentAnchor)
        {
            float drift = Vector3.Distance(confirmedAnchor.Position, currentAnchor.Position);
            float rotation = HMath.AngleDegrees(confirmedAnchor.Forward, currentAnchor.Forward);
            return drift > Settings.MaxAnchorDrift || rotation > Settings.MaxAnchorRotationDegrees;
        }
    }
}
