using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// A greybox peg doll animated from <see cref="PresentationModel"/>: whole-body lean, a head
    /// turn, squash on landing, a nod, an anticipatory rock, crouch, the system rescue marker
    /// and a harbour bounce. Personality only changes rhythm (Keeper deliberate, Pip eager,
    /// Luma hesitant). Never decides ownership; it only renders it.
    /// </summary>
    [AddComponentMenu("Handover/Doll View")]
    public sealed class DollView : MonoBehaviour
    {
        [SerializeField] private HandoverApp _app;
        [SerializeField] private DollId _doll = DollId.Keeper;

        private Transform _root;
        private Transform _body;
        private Transform _head;
        private GameObject _marker;
        private GameObject _lanternProp;
        private Vector3 _position;
        private bool _hasPosition;
        private DollPose _lastPose;
        private float _squash;
        private float _nod;
        private float _clock;

        public DollId Doll => _doll;

        public void Bind(HandoverApp app, DollId doll)
        {
            _app = app;
            _doll = doll;
        }

        private void Start()
        {
            Build();
        }

        private void Build()
        {
            _root = new GameObject("Peg doll " + _doll).transform;
            _root.SetParent(transform, false);
            _body = new GameObject("Body pivot").transform;
            _body.SetParent(_root, false);
            Greybox.Primitive(PrimitiveType.Capsule, _body, "Body", Greybox.Peg, new Vector3(0f, 0.015f, 0f), new Vector3(0.022f, 0.015f, 0.022f));
            _head = new GameObject("Head pivot").transform;
            _head.SetParent(_body, false);
            _head.localPosition = new Vector3(0f, 0.039f, 0f);
            Greybox.Primitive(PrimitiveType.Sphere, _head, "Head", Greybox.Peg, Vector3.zero, Vector3.one * 0.019f);
            Greybox.Primitive(PrimitiveType.Sphere, _head, "Eye L", Color.black, new Vector3(-0.0045f, 0.0015f, 0.0085f), Vector3.one * 0.0035f);
            Greybox.Primitive(PrimitiveType.Sphere, _head, "Eye R", Color.black, new Vector3(0.0045f, 0.0015f, 0.0085f), Vector3.one * 0.0035f);

            switch (_doll)
            {
                case DollId.Keeper:
                    Greybox.Primitive(PrimitiveType.Cylinder, _head, "Amber cap", Greybox.KeeperColour, new Vector3(0f, 0.0085f, 0f), new Vector3(0.0175f, 0.0028f, 0.0175f));
                    _lanternProp = Greybox.Primitive(PrimitiveType.Sphere, _body, "Lantern", Greybox.Amber, new Vector3(0.016f, 0.017f, 0.004f), Vector3.one * 0.009f);
                    break;
                case DollId.Pip:
                    Greybox.Primitive(PrimitiveType.Cylinder, _body, "Red scarf", Greybox.PipColour, new Vector3(0f, 0.029f, 0f), new Vector3(0.025f, 0.0028f, 0.025f));
                    Greybox.Primitive(PrimitiveType.Cube, _body, "Scarf tail", Greybox.PipColour, new Vector3(-0.009f, 0.024f, -0.01f), new Vector3(0.004f, 0.012f, 0.002f));
                    break;
                default:
                    Greybox.Primitive(PrimitiveType.Cylinder, _body, "Blue band", Greybox.LumaColour, new Vector3(0f, 0.017f, 0f), new Vector3(0.0235f, 0.0032f, 0.0235f));
                    break;
            }

            _marker = Greybox.Primitive(PrimitiveType.Cylinder, transform, "System rescue marker", Greybox.WithAlpha(new Color(0.55f, 0.85f, 1f), 0.6f),
                Vector3.zero, new Vector3(0.055f, 0.0008f, 0.055f), transparent: true);
            _marker.SetActive(false);
        }

        private float Rhythm()
        {
            switch (_doll)
            {
                case DollId.Pip: return 1.35f;
                case DollId.Luma: return 0.7f;
                default: return 1f;
            }
        }

        private void LateUpdate()
        {
            if (_app == null || _app.Simulation == null || _root == null || _app.CurrentStage == null)
            {
                return;
            }

            HandoverSimulation sim = _app.Simulation;
            HandoverSettings settings = sim.Settings;
            float dt = Time.unscaledDeltaTime;
            _clock += dt * Rhythm();
            DollPresentation p = PresentationModel.Compute(sim, _doll);
            Vector3 target = p.Position.ToUnity();

            // Attached poses follow the palm exactly; docked poses glide (system returns).
            bool attached = p.Pose == DollPose.Riding || p.Pose == DollPose.Hopping || p.Pose == DollPose.Sheltering
                || sim.Ledger.Doll(_doll).Owner.Kind == OwnerKind.Transfer;
            if (!_hasPosition || attached || (target - _position).sqrMagnitude > 1f)
            {
                _position = target;
                _hasPosition = true;
            }
            else
            {
                _position = Vector3.MoveTowards(_position, target, 1.2f * dt);
            }

            if (_lastPose == DollPose.Hopping && p.Pose != DollPose.Hopping)
            {
                _squash = 1f;
                _nod = 1f;
            }

            _lastPose = p.Pose;
            _squash = Mathf.Max(0f, _squash - dt / 0.2f);
            _nod = Mathf.Max(0f, _nod - dt / 0.35f);

            Vector3 offset = Vector3.zero;
            bool reducedMotion = settings.ReducedMotion;
            if (p.Pose == DollPose.Rescued && !reducedMotion)
            {
                float celebrate = sim.Session.State == SessionState.Results ? 2f : 1f;
                offset.y += Mathf.Abs(Mathf.Sin(_clock * Mathf.PI / 0.7f)) * 0.006f * celebrate;
            }

            if (p.Pose == DollPose.Suspended)
            {
                offset.y += Mathf.Sin(_clock * 3f) * 0.003f;
            }

            _root.position = _position + offset;

            // Face what the doll is looking at, otherwise the player.
            Vector3 look = p.HasLookTarget ? p.LookTarget.ToUnity() : _app.Viewer.position;
            Vector3 flat = Vector3.ProjectOnPlane(look - _position, Vector3.up);
            Vector3 up = p.Pose == DollPose.Riding || p.Pose == DollPose.Sheltering
                ? Vector3.Slerp(Vector3.up, p.SupportUp.ToUnity(), 0.4f)
                : Vector3.up;
            if (flat.sqrMagnitude > 1e-6f)
            {
                _root.rotation = Quaternion.Slerp(_root.rotation, Quaternion.LookRotation(flat.normalized, up), 1f - Mathf.Exp(-dt / 0.08f));
            }

            // Lean, anticipatory rock and crouch on the body pivot.
            float lean = p.Lean * 16f;
            float rock = 0f;
            if (p.Pose == DollPose.Anticipating && !reducedMotion)
            {
                float amplitude = _doll == DollId.Luma ? 5f : 3f;
                rock = Mathf.Sin(_clock * Mathf.PI * 2f * 1.6f) * amplitude;
            }

            float shiver = p.Crouch && !reducedMotion ? Mathf.Sin(_clock * 40f) * 1.2f : 0f;
            _body.localRotation = Quaternion.Euler(lean, 0f, rock + shiver);

            float squashY = 1f - 0.22f * _squash;
            float squashXZ = 1f + 0.14f * _squash;
            if (p.Pose == DollPose.Hopping)
            {
                float apex = 4f * p.HopProgress * (1f - p.HopProgress);
                squashY *= 1f + 0.08f * apex;
            }

            if (p.Crouch)
            {
                squashY *= 0.74f;
            }

            _body.localScale = new Vector3(squashXZ, squashY, squashXZ);
            _head.localRotation = Quaternion.Euler(Mathf.Sin(_nod * Mathf.PI) * 18f, 0f, 0f);
            _root.localScale = Vector3.one * (settings.LargeCues ? 1.3f : 1f);

            _marker.SetActive(p.Pose == DollPose.Suspended);
            if (_marker.activeSelf)
            {
                _marker.transform.position = _position + Vector3.up * 0.001f;
            }

            if (_lanternProp != null)
            {
                _lanternProp.SetActive(!sim.Ledger.DollIsAtDock(DollId.Keeper, DockId.LanternRefuge));
            }
        }
    }
}
