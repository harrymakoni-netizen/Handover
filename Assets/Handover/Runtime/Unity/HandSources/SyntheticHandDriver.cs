using Handover.Core;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Handover.Unity
{
    /// <summary>
    /// Deterministic synthetic palms driven from the keyboard, for Editor and desktop testing
    /// without a headset or the XR Simulator (whose hand poses are discrete). Uses the same
    /// <see cref="IHandPoseProvider"/> contract as the SDK adapter. Never needed on device.
    ///
    /// Tab switch hand · WASD move · Q/E down/up · Shift fine · Z/X tilt · C level · F palm-down
    /// · H hide/show (tracking loss) · R recenter · 1-4 lift and snap beside D/R/U/H · G raise the
    /// active palm and bring the other alongside (handover) · V raise the other hand as a shield.
    /// </summary>
    [AddComponentMenu("Handover/Synthetic Hand Driver (keyboard)")]
    [DefaultExecutionOrder(-50)]
    public sealed class SyntheticHandDriver : HandPoseSource
    {
        [SerializeField] private HandoverApp _app;
        [SerializeField] private bool _readKeyboard = true;
        [SerializeField] private float _speed = 0.25f;
        [SerializeField] private float _fineSpeed = 0.06f;
        [SerializeField] private float _rollSpeed = 90f;

        private readonly SyntheticHandPoseProvider _provider = new SyntheticHandPoseProvider();
        private readonly Vector3[] _support = new Vector3[2];
        private readonly float[] _roll = new float[2];
        private readonly bool[] _palmDown = new bool[2];
        private readonly bool[] _hidden = new bool[2];
        private bool _initialised;

        // A snap first lifts the palm clear (as a real hand would move between docks), then lands
        // beside the berth a few frames later, so a dock the palm just used can re-arm.
        private const int SnapLiftFrames = 3;
        private int _snapFrames;
        private HandId _snapHand;
        private Vector3 _snapTarget;

        public HandId ActiveHand { get; private set; } = HandId.Right;
        public override int OriginEpoch => _provider.OriginEpoch;
        public override double Now => _provider.Now;
        public bool ReadKeyboard { get => _readKeyboard; set => _readKeyboard = value; }

        public override PalmSample GetSample(HandId hand) => _provider.GetSample(hand);

        public override string Describe()
        {
            return $"Keyboard hands (active {ActiveHand}{(_hidden[(int)ActiveHand] ? ", hidden" : "")})";
        }

        public void Bind(HandoverApp app)
        {
            _app = app;
        }

        public override void Poll()
        {
            if (!_initialised)
            {
                ParkBoth();
                _initialised = true;
            }

            if (_readKeyboard)
            {
                ReadInput(Time.unscaledDeltaTime);
            }

            if (_snapFrames > 0 && --_snapFrames == 0)
            {
                _support[(int)_snapHand] = _snapTarget;
            }

            for (int i = 0; i < 2; i++)
            {
                Apply((HandId)i);
            }

            _provider.Tick(Time.unscaledDeltaTime);
        }

        // ---- scripted control (also used by the keyboard) -----------------------

        public void Place(HandId hand, Vector3 support, float rollDegrees = 0f, bool palmDown = false)
        {
            _support[(int)hand] = support;
            _roll[(int)hand] = rollDegrees;
            _palmDown[(int)hand] = palmDown;
            _hidden[(int)hand] = false;
            Apply(hand);
        }

        public void SetHidden(HandId hand, bool hidden)
        {
            _hidden[(int)hand] = hidden;
            Apply(hand);
        }

        public void Recenter()
        {
            _provider.Recenter();
        }

        public Vector3 SupportOf(HandId hand) => _support[(int)hand];

        private void Apply(HandId hand)
        {
            int i = (int)hand;
            if (_hidden[i])
            {
                _provider.Hide(hand);
                return;
            }

            float yaw = StageYawDegrees();
            Core.Pose3 oriented = _palmDown[i]
                ? PalmBasis.PalmDown(NVector3.Zero, yaw)
                : PalmBasis.PalmUp(NVector3.Zero, yaw, _roll[i]);
            float height = Tuning != null ? Tuning.SupportHeight : 0.02f;
            NVector3 centre = _support[i].ToNumerics() - oriented.Up * height;
            _provider.SetPose(hand, new Core.Pose3(centre, oriented.Rotation));
        }

        private InteractionTuning Tuning => _app != null && _app.Simulation != null ? _app.Simulation.Tuning : null;

        private StageLayout Stage => _app != null ? _app.CurrentStage : null;

        private float StageYawDegrees() => Stage != null ? Stage.Placement.YawRadians * Mathf.Rad2Deg : 0f;

        private Vector3 StageRight => Stage != null ? Stage.Pose.Right.ToUnity() : Vector3.right;

        private Vector3 StageForward => Stage != null ? Stage.Pose.Forward.ToUnity() : Vector3.forward;

        private void ParkBoth()
        {
            Vector3 origin;
            if (Stage != null)
            {
                origin = Stage.Pose.Position.ToUnity();
            }
            else if (Camera.main != null)
            {
                Transform cam = Camera.main.transform;
                Vector3 flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                origin = cam.position + flat * 0.45f + Vector3.down * 0.42f;
            }
            else
            {
                origin = new Vector3(0f, 0.75f, 0.45f);
            }

            _support[0] = origin + StageRight * -0.14f + StageForward * -0.2f + Vector3.up * 0.2f;
            _support[1] = origin + StageRight * 0.14f + StageForward * -0.2f + Vector3.up * 0.2f;
        }

        private void ReadInput(float dt)
        {
            if (DriverInput.Pressed(DriverKey.Tab))
            {
                ActiveHand = ActiveHand.Other();
            }

            int i = (int)ActiveHand;
            float speed = DriverInput.Held(DriverKey.Shift) ? _fineSpeed : _speed;
            Vector3 move = Vector3.zero;
            if (DriverInput.Held(DriverKey.W)) move += StageForward;
            if (DriverInput.Held(DriverKey.S)) move -= StageForward;
            if (DriverInput.Held(DriverKey.D)) move += StageRight;
            if (DriverInput.Held(DriverKey.A)) move -= StageRight;
            if (DriverInput.Held(DriverKey.E)) move += Vector3.up;
            if (DriverInput.Held(DriverKey.Q)) move -= Vector3.up;
            _support[i] += move * (speed * dt);

            if (DriverInput.Held(DriverKey.Z)) _roll[i] -= _rollSpeed * dt;
            if (DriverInput.Held(DriverKey.X)) _roll[i] += _rollSpeed * dt;
            if (DriverInput.Pressed(DriverKey.C)) _roll[i] = 0f;
            if (DriverInput.Pressed(DriverKey.F)) _palmDown[i] = !_palmDown[i];
            if (DriverInput.Pressed(DriverKey.H)) _hidden[i] = !_hidden[i];
            if (DriverInput.Pressed(DriverKey.R)) Recenter();

            if (DriverInput.Pressed(DriverKey.Digit1)) SnapToDock(ActiveHand, DockId.Departure);
            if (DriverInput.Pressed(DriverKey.Digit2)) SnapToDock(ActiveHand, DockId.LowRefuge);
            if (DriverInput.Pressed(DriverKey.Digit3)) SnapToDock(ActiveHand, DockId.LanternRefuge);
            if (DriverInput.Pressed(DriverKey.Digit4)) SnapToDock(ActiveHand, DockId.Harbour);
            if (DriverInput.Pressed(DriverKey.G)) BringAlongside(ActiveHand.Other(), ActiveHand);
            if (DriverInput.Pressed(DriverKey.V)) RaiseShield(ActiveHand.Other(), ActiveHand);
        }

        /// <summary>Places a palm beside the most relevant berth of a dock.</summary>
        public void SnapToDock(HandId hand, DockId dock)
        {
            StageLayout stage = Stage;
            HandoverSimulation sim = _app != null ? _app.Simulation : null;
            if (stage == null || sim == null)
            {
                return;
            }

            DockLayout layout = stage.Dock(dock);
            RescueLedger ledger = sim.Ledger;
            DollId riding = ledger.PalmDoll(hand);
            int berth = 0;
            float best = float.MaxValue;
            for (int b = 0; b < layout.Capacity; b++)
            {
                SlotState s = ledger.Get(SlotRef.DockBerth(dock, b));
                bool wanted = riding == DollId.None
                    ? s.IsOccupied
                    : s.IsEmpty && (layout.NamedBerths == null || layout.BerthOwner(b) == riding);
                float score = wanted ? b : 100 + b;
                if (score < best)
                {
                    best = score;
                    berth = b;
                }
            }

            InteractionTuning t = sim.Tuning;
            Vector3 berthPos = layout.Berths[berth].ToUnity();
            Vector3 towardsCentre = Vector3.ProjectOnPlane(stage.Pose.Position.ToUnity() - layout.Center.ToUnity(), Vector3.up);
            Vector3 side = towardsCentre.sqrMagnitude > 1e-6f ? towardsCentre.normalized : -StageForward;
            float offset = t.SupportHalfWidth + t.BerthSupportRadius + 0.015f;

            // Lift clear first, then land beside the berth.
            _roll[(int)hand] = 0f;
            _palmDown[(int)hand] = false;
            _hidden[(int)hand] = false;
            _support[(int)hand] += Vector3.up * 0.15f;
            _snapHand = hand;
            _snapTarget = berthPos + side * offset;
            _snapFrames = SnapLiftFrames;
        }

        /// <summary>
        /// Handover pose: raises <paramref name="anchor"/> a little (palm to palm happens above the
        /// berths, still inside the dock's dry area) and puts <paramref name="mover"/> side by side.
        /// </summary>
        public void BringAlongside(HandId mover, HandId anchor)
        {
            InteractionTuning t = Tuning ?? new InteractionTuning();
            _support[(int)anchor] += Vector3.up * 0.08f;
            _roll[(int)anchor] = 0f;
            _palmDown[(int)anchor] = false;
            float side = mover == HandId.Left ? -1f : 1f;
            Vector3 target = _support[(int)anchor] + StageRight * (side * (2f * t.SupportHalfWidth + 0.02f));
            Place(mover, target);
        }

        /// <summary>Puts <paramref name="shield"/> palm-down in the rain line above the carrier's doll.</summary>
        public void RaiseShield(HandId shield, HandId carrier)
        {
            HandoverSimulation sim = _app != null ? _app.Simulation : null;
            if (sim == null)
            {
                return;
            }

            NVector3 head = _support[(int)carrier].ToNumerics() + NVector3.UnitY * sim.Chapter.DollHeadHeight;
            Vector3 centre = sim.Flood.IdealCanopyCentre(head, 0.15f).ToUnity();
            float height = sim.Tuning.SupportHeight;
            // Place() expects a support point; for a palm-down hand the support sits below the centre.
            Place(shield, centre + Vector3.down * height, 0f, true);
        }
    }
}
