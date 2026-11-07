using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Greybox tabletop: the four landing nodes as small harbour pillars, berth markers, dry
    /// transfer areas, a bounded translucent water patch, the lantern and the harbour glow.
    /// Rebuilt whenever the confirmed or previewed stage changes; refreshed from state each
    /// frame. Presentation only.
    /// </summary>
    [AddComponentMenu("Handover/Greybox Stage View")]
    public sealed class GreyboxStageView : MonoBehaviour
    {
        [SerializeField] private HandoverApp _app;

        private StageLayout _built;
        private Transform _root;
        private GameObject _water;
        private GameObject _lantern;
        private GameObject _harbourGlow;
        private GameObject _previewOutline;
        private readonly GameObject[] _pillars = new GameObject[4];
        private readonly GameObject[] _dryAreas = new GameObject[4];

        public void Bind(HandoverApp app)
        {
            _app = app;
        }

        private void LateUpdate()
        {
            if (_app == null || _app.Simulation == null)
            {
                return;
            }

            StageLayout stage = _app.CurrentStage;
            if (!ReferenceEquals(stage, _built))
            {
                Rebuild(stage);
            }

            if (stage != null)
            {
                Refresh(stage);
            }
        }

        private void Rebuild(StageLayout stage)
        {
            if (_root != null)
            {
                Destroy(_root.gameObject);
            }

            _built = stage;
            if (stage == null)
            {
                return;
            }

            _root = new GameObject("Stage (greybox)").transform;
            _root.SetParent(transform, false);
            _root.SetPositionAndRotation(stage.Pose.Position.ToUnity(), stage.Pose.Rotation.ToUnity());

            float s = stage.Scale;
            float width = stage.Variant.Width * s;
            float depth = stage.Variant.Depth * s;

            if (stage.Placement.Source == StageSource.VirtualTabletop)
            {
                Greybox.Primitive(PrimitiveType.Cube, _root, "Virtual tabletop", Greybox.Wood * 0.75f,
                    new Vector3(0f, -0.01f, 0f), new Vector3(width + 0.08f, 0.02f, depth + 0.08f));
            }

            _previewOutline = Greybox.Primitive(PrimitiveType.Cube, _root, "Placement outline", Greybox.WithAlpha(Color.white, 0.18f),
                new Vector3(0f, 0.001f, 0f), new Vector3(width, 0.001f, depth), transparent: true);

            _water = Greybox.Primitive(PrimitiveType.Cube, _root, "Water", Greybox.Water,
                Vector3.zero, new Vector3(width + 0.06f, 0.01f, depth + 0.06f), transparent: true);

            foreach (DockId id in Ids.Docks)
            {
                DockLayout dock = stage.Dock(id);
                Vector3 top = _root.InverseTransformPoint(dock.Center.ToUnity());
                float radius = (id == DockId.LowRefuge ? 0.075f : 0.09f) * s;
                _pillars[(int)id] = Greybox.Primitive(PrimitiveType.Cylinder, _root, $"Dock {id.Code()}", DockColour(id),
                    new Vector3(top.x, top.y * 0.5f, top.z), new Vector3(radius, top.y * 0.5f, radius));

                _dryAreas[(int)id] = Greybox.Primitive(PrimitiveType.Cylinder, _root, $"Dry area {id.Code()}",
                    Greybox.WithAlpha(new Color(0.95f, 0.9f, 0.75f), 0.12f),
                    new Vector3(top.x, 0.002f, top.z), new Vector3(dock.DryAreaRadius * 2f, 0.0008f, dock.DryAreaRadius * 2f), transparent: true);

                for (int b = 0; b < dock.Capacity; b++)
                {
                    Vector3 berth = _root.InverseTransformPoint(dock.Berths[b].ToUnity());
                    Greybox.Primitive(PrimitiveType.Cylinder, _root, $"Berth {id.Code()}{b}", BerthColour(dock.BerthOwner(b)),
                        berth + new Vector3(0f, 0.0012f, 0f), new Vector3(0.034f, 0.0012f, 0.034f));
                }
            }

            Vector3 lantern = _root.InverseTransformPoint(stage.Dock(DockId.LanternRefuge).Center.ToUnity());
            _lantern = Greybox.Primitive(PrimitiveType.Sphere, _root, "Lantern socket", Greybox.Stone,
                lantern + new Vector3(0.028f, 0.016f, 0.02f), Vector3.one * 0.018f);
            Vector3 harbour = _root.InverseTransformPoint(stage.Dock(DockId.Harbour).Center.ToUnity());
            _harbourGlow = Greybox.Primitive(PrimitiveType.Cylinder, _root, "Harbour glow", Greybox.WithAlpha(Greybox.Amber, 0.2f),
                harbour + new Vector3(0f, 0.002f, 0f), new Vector3(0.24f * s, 0.001f, 0.24f * s), transparent: true);
        }

        private static Color DockColour(DockId id)
        {
            switch (id)
            {
                case DockId.Departure: return Greybox.Wood;
                case DockId.LowRefuge: return Greybox.Stone;
                case DockId.LanternRefuge: return new Color(0.7f, 0.55f, 0.35f);
                default: return Greybox.Harbour;
            }
        }

        private static Color BerthColour(DollId owner)
        {
            switch (owner)
            {
                case DollId.Keeper: return Greybox.KeeperColour;
                case DollId.Pip: return Greybox.PipColour;
                case DollId.Luma: return Greybox.LumaColour;
                default: return new Color(0.92f, 0.9f, 0.85f);
            }
        }

        private void Refresh(StageLayout stage)
        {
            HandoverSimulation sim = _app.Simulation;
            bool setup = sim.Session.State == SessionState.Setup;
            float s = stage.Scale;

            _previewOutline.SetActive(setup);
            float water = (setup ? 0.004f : sim.Flood.WaterHeight) * s;
            _water.transform.localScale = new Vector3(_water.transform.localScale.x, Mathf.Max(0.001f, water), _water.transform.localScale.z);
            _water.transform.localPosition = new Vector3(0f, water * 0.5f, 0f);

            bool rClosed = !sim.Context.Policy.IsDryAreaOpen(DockId.LowRefuge);
            Greybox.Tint(_pillars[(int)DockId.LowRefuge], rClosed ? Greybox.Deep : Greybox.Stone);
            foreach (DockId id in Ids.Docks)
            {
                _dryAreas[(int)id].SetActive(!setup && sim.Context.Policy.IsDryAreaOpen(id));
            }

            bool lit = sim.Director.LanternLit;
            Greybox.Tint(_lantern, lit ? Greybox.Amber : Greybox.Stone);
            Greybox.Tint(_harbourGlow, Greybox.WithAlpha(Greybox.Amber, lit ? 0.35f : 0.05f));
        }
    }
}
