using System;
using System.Collections.Generic;
using System.Numerics;
using Handover.Core;
using NUnit.Framework;

namespace Handover.Tests
{
    /// <summary>T05: fit the tabletop stage, or fall back to a virtual tabletop.</summary>
    public class StageFittingTests
    {
        // Seated eye height about 1.15 m, a desk top about 0.73 m.
        private static readonly ViewerPose Seated = new ViewerPose(new Vector3(0f, 1.15f, 0f), Vector3.UnitZ);

        private static TableStageService Service() =>
            new TableStageService(new[] { StageLayoutVariant.CreateStandard(), StageLayoutVariant.CreateCompact() });

        private static SurfaceCandidate Desk(float sizeX = 1.2f, float sizeZ = 0.7f, float nearEdge = 0.2f, float yawDegrees = 0f, float height = 0.73f)
        {
            return new SurfaceCandidate("desk", new Vector3(0f, height, nearEdge + sizeZ * 0.5f), yawDegrees * HMath.Deg2Rad, sizeX, sizeZ);
        }

        private static void AssertInsideSurface(StageLayout layout, SurfaceCandidate surface, float margin)
        {
            Pose3 table = surface.Pose;
            foreach (DockId id in Ids.Docks)
            {
                foreach (Vector3 berth in layout.Dock(id).Berths)
                {
                    Vector3 local = table.InverseTransformPoint(berth);
                    Assert.That(Math.Abs(local.X), Is.LessThanOrEqualTo(surface.SizeX * 0.5f - margin + 1e-3f), $"{id} berth beyond the table edge");
                    Assert.That(Math.Abs(local.Z), Is.LessThanOrEqualTo(surface.SizeZ * 0.5f - margin + 1e-3f), $"{id} berth beyond the table edge");
                    Assert.That(berth.Y, Is.GreaterThan(surface.Center.Y), "no dock invites reaching through the surface");
                }
            }
        }

        [Test]
        public void PermissionDeclinedFallsBackToAVirtualTabletop()
        {
            TableStageService service = Service();
            StageFitResult r = service.Fit(null, Seated, 0);
            Assert.IsTrue(r.Success);
            Assert.That(r.Placement.Source, Is.EqualTo(StageSource.VirtualTabletop));
            Assert.That(service.Validate(r.Layout, Seated), Is.EqualTo(FitIssue.None));
        }

        [Test]
        public void NoTableFallsBackToAVirtualTabletop()
        {
            StageFitResult r = Service().Fit(new List<SurfaceCandidate>(), Seated, 0);
            Assert.That(r.Placement.Source, Is.EqualTo(StageSource.VirtualTabletop));
            Assert.That(r.Rejections, Is.Empty);
        }

        [Test]
        public void SmallTableIsRejectedWithAReason()
        {
            StageFitResult r = Service().Fit(new[] { Desk(0.3f, 0.25f) }, Seated, 0);
            Assert.That(r.Placement.Source, Is.EqualTo(StageSource.VirtualTabletop));
            Assert.That(r.FirstIssue, Is.EqualTo(FitIssue.TooSmall));
        }

        [Test]
        public void DistantTableIsRejected()
        {
            StageFitResult r = Service().Fit(new[] { Desk(nearEdge: 1.4f) }, Seated, 0);
            Assert.That(r.FirstIssue, Is.EqualTo(FitIssue.TooFar));
        }

        [TestCase(1.05f)]
        [TestCase(0.1f)]
        public void ImplausibleHeightIsRejected(float height)
        {
            StageFitResult r = Service().Fit(new[] { Desk(height: height) }, Seated, 0);
            Assert.That(r.FirstIssue, Is.EqualTo(FitIssue.BadHeight));
        }

        [Test]
        public void TypicalDeskFitsTheStandardLayoutInsideItsEdges()
        {
            TableStageService service = Service();
            SurfaceCandidate desk = Desk();
            StageFitResult r = service.Fit(new[] { desk }, Seated, 0);
            Assert.That(r.Placement.Source, Is.EqualTo(StageSource.SceneSurface));
            Assert.That(r.SurfaceId, Is.EqualTo("desk"));
            Assert.That(r.Variant.Name, Is.EqualTo("Standard"));
            Assert.That(r.Layout.Scale, Is.InRange(r.Variant.MinScale, r.Variant.MaxScale));
            AssertInsideSurface(r.Layout, desk, service.Settings.EdgeMargin);
            Assert.That(service.Validate(r.Layout, Seated), Is.EqualTo(FitIssue.None));
            Assert.That(r.Layout.TableHeight, Is.EqualTo(desk.Center.Y).Within(1e-5f));
        }

        [Test]
        public void SmallerTableUsesTheCompactVariant()
        {
            TableStageService service = Service();
            SurfaceCandidate desk = Desk(0.48f, 0.42f, 0.22f);
            StageFitResult r = service.Fit(new[] { desk }, Seated, 0);
            Assert.That(r.Placement.Source, Is.EqualTo(StageSource.SceneSurface));
            Assert.That(r.Variant.Name, Is.EqualTo("Compact"));
            AssertInsideSurface(r.Layout, desk, service.Settings.EdgeMargin);
        }

        [Test]
        public void RotatedTableAlignsTheStageWithTheEdgeFacingThePlayer()
        {
            TableStageService service = Service();
            SurfaceCandidate desk = Desk(yawDegrees: 25f);
            StageFitResult r = service.Fit(new[] { desk }, Seated, 0);
            Assert.That(r.Placement.Source, Is.EqualTo(StageSource.SceneSurface));
            Assert.That(r.Placement.YawRadians * HMath.Rad2Deg, Is.EqualTo(25f).Within(0.01f));
            AssertInsideSurface(r.Layout, desk, service.Settings.EdgeMargin);
        }

        [Test]
        public void TheBestOfSeveralSurfacesIsChosen()
        {
            var small = new SurfaceCandidate("side", new Vector3(-0.6f, 0.7f, 0.4f), 0f, 0.3f, 0.3f);
            SurfaceCandidate desk = Desk();
            StageFitResult r = Service().Fit(new[] { small, desk }, Seated, 0);
            Assert.That(r.SurfaceId, Is.EqualTo("desk"));
            Assert.That(r.Rejections.Count, Is.EqualTo(1));
        }

        [Test]
        public void EveryFittedDockIsWithinSeatedReachAndTheVisibilityBudget()
        {
            TableStageService service = Service();
            foreach (StageFitResult r in new[] { service.Fit(new[] { Desk() }, Seated, 0), service.Fit(null, Seated, 0) })
            {
                foreach (DockId id in Ids.Docks)
                {
                    foreach (Vector3 berth in r.Layout.Dock(id).Berths)
                    {
                        Assert.That(Vector3.Distance(Seated.Position, berth), Is.LessThanOrEqualTo(service.Settings.MaxReach));
                    }
                }

                Assert.That(service.Validate(r.Layout, Seated), Is.EqualTo(FitIssue.None));
            }
        }

        [Test]
        public void AStageRightUnderTheEyesFailsTheVisibilityBudget()
        {
            TableStageService service = Service();
            StageLayout tooClose = service.PlaceVirtualTabletop(new Vector3(0f, 0.75f, 0.05f), 0f, 1f, 0);
            Assert.That(service.Validate(tooClose, Seated), Is.Not.EqualTo(FitIssue.None));
            StageLayout tooFar = service.PlaceVirtualTabletop(new Vector3(0f, 0.75f, 0.9f), 0f, 1f, 0);
            Assert.That(service.Validate(tooFar, Seated), Is.EqualTo(FitIssue.OutOfReach));
        }

        [Test]
        public void ResizeStaysInsideTestedLimits()
        {
            TableStageService service = Service();
            StageLayout layout = service.Fit(null, Seated, 0).Layout;
            service.Resize(layout, 5f, Seated, out StageLayout big);
            Assert.That(big.Scale, Is.EqualTo(layout.Variant.MaxScale));
            service.Resize(layout, 0.1f, Seated, out StageLayout small);
            Assert.That(small.Scale, Is.EqualTo(layout.Variant.MinScale));
        }

        [Test]
        public void RecenterDriftBeyondToleranceRequiresRepositioning()
        {
            TableStageService service = Service();
            var anchor = new Pose3(new Vector3(0f, 0.73f, 0.5f), Quaternion.Identity);
            Assert.IsFalse(service.NeedsReposition(anchor, new Pose3(anchor.Position + new Vector3(0.01f, 0f, 0f), Quaternion.Identity)));
            Assert.IsTrue(service.NeedsReposition(anchor, new Pose3(anchor.Position + new Vector3(0.05f, 0f, 0f), Quaternion.Identity)));
            Assert.IsTrue(service.NeedsReposition(anchor, new Pose3(anchor.Position, HMath.AngleAxis(6f, Vector3.UnitY))));
        }

        [Test]
        public void AFittedStageRunsTheSameRules()
        {
            // The fitted layout keeps the authored berth counts and puzzle topology.
            StageLayout layout = Service().Fit(new[] { Desk() }, Seated, 0).Layout;
            var rig = new TestRig(confirmStage: false);
            rig.Sim.BeginReposition();
            Assert.IsTrue(rig.Sim.SetStage(layout));
            Assert.IsTrue(rig.Sim.ConfirmStage());
            Assert.That(rig.Sim.Session.State, Is.EqualTo(SessionState.Ready));
        }
    }
}
