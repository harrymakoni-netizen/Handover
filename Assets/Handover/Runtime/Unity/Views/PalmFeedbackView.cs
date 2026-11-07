using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Palm feedback: the support outline, an amber stability mark on unsafe tilt, the palm
    /// outline that invites boarding, the seam between adjacent palms (feedback, not a collider)
    /// and the shield canopy. Colour is never the only signal: captions and sound pair with it.
    /// </summary>
    [AddComponentMenu("Handover/Palm Feedback View")]
    public sealed class PalmFeedbackView : MonoBehaviour
    {
        [SerializeField] private HandoverApp _app;

        private readonly GameObject[] _support = new GameObject[2];
        private readonly GameObject[] _invite = new GameObject[2];
        private readonly GameObject[] _canopy = new GameObject[2];
        private GameObject _seam;
        private GameObject _assistCanopy;
        private float _clock;

        public void Bind(HandoverApp app)
        {
            _app = app;
        }

        private void Start()
        {
            for (int i = 0; i < 2; i++)
            {
                _support[i] = Greybox.Primitive(PrimitiveType.Cylinder, transform, $"Support {(HandId)i}",
                    Greybox.WithAlpha(Color.white, 0.22f), Vector3.zero, Vector3.one, transparent: true);
                _invite[i] = Greybox.Primitive(PrimitiveType.Cylinder, transform, $"Invite {(HandId)i}",
                    Greybox.WithAlpha(Greybox.Amber, 0.3f), Vector3.zero, Vector3.one, transparent: true);
                _canopy[i] = Greybox.Primitive(PrimitiveType.Cylinder, transform, $"Canopy {(HandId)i}",
                    Greybox.WithAlpha(new Color(0.35f, 0.7f, 0.45f), 0.35f), Vector3.zero, Vector3.one, transparent: true);
            }

            _seam = Greybox.Primitive(PrimitiveType.Cube, transform, "Seam", Greybox.WithAlpha(new Color(1f, 0.95f, 0.7f), 0.8f),
                Vector3.zero, Vector3.one, transparent: true);
            _assistCanopy = Greybox.Primitive(PrimitiveType.Cylinder, transform, "Shelter assist canopy",
                Greybox.WithAlpha(new Color(0.35f, 0.7f, 0.45f), 0.3f), Vector3.zero, Vector3.one, transparent: true);
        }

        private void LateUpdate()
        {
            if (_app == null || _app.Simulation == null || _seam == null)
            {
                return;
            }

            HandoverSimulation sim = _app.Simulation;
            InteractionTuning t = sim.Tuning;
            float large = sim.Settings.LargeCues ? 1.25f : 1f;
            _clock += Time.unscaledDeltaTime;
            bool running = sim.Session.IsGameplayRunning || sim.Session.State == SessionState.Recovering;

            ConnectionCandidate seam = null;
            bool[] invited = { false, false };
            foreach (ConnectionCandidate c in sim.Resolver.Candidates)
            {
                if (c.Kind == TransferKind.Handover)
                {
                    seam = c;
                }
                else if (c.Kind == TransferKind.Boarding && c.Target.IsPalm)
                {
                    invited[(int)c.Target.Hand] = true;
                }
            }

            foreach (HandId hand in Ids.Hands)
            {
                int i = (int)hand;
                PalmPlatform palm = sim.Palm(hand);
                bool visible = running && palm.IsTracked && (palm.CarryEligible || !sim.Ledger.PalmIsEmpty(hand));
                _support[i].SetActive(visible);
                if (visible)
                {
                    Place(_support[i], palm.SupportCenter.ToUnity() + Vector3.up * 0.0005f, palm.FilteredPose.Rotation.ToUnity(),
                        new Vector3(2f * t.SupportHalfWidth * large, 0.0006f, 2f * t.SupportHalfLength * large));
                    Color colour = palm.TiltWarning ? Greybox.WithAlpha(Greybox.Amber, 0.75f) : Greybox.WithAlpha(Color.white, 0.22f);
                    Greybox.Tint(_support[i], colour);
                }

                bool invite = visible && invited[i];
                _invite[i].SetActive(invite);
                if (invite)
                {
                    float pulse = 1f + 0.08f * Mathf.Sin(_clock * 6f);
                    Place(_invite[i], palm.SupportCenter.ToUnity() + Vector3.up * 0.001f, palm.FilteredPose.Rotation.ToUnity(),
                        new Vector3(2.4f * t.SupportHalfWidth * pulse * large, 0.0005f, 2.4f * t.SupportHalfLength * pulse * large));
                }

                bool shield = running && palm.IsTracked && palm.ShieldPose;
                _canopy[i].SetActive(shield);
                if (shield)
                {
                    float r = sim.Chapter.CanopyRadius * 2f;
                    Place(_canopy[i], palm.FilteredPose.Position.ToUnity(), palm.FilteredPose.Rotation.ToUnity(), new Vector3(r, 0.0015f, r));
                    float alpha = sim.Flood.IsShieldEngaged(hand) ? 0.55f : 0.25f;
                    Greybox.Tint(_canopy[i], Greybox.WithAlpha(new Color(0.35f, 0.7f, 0.45f), alpha));
                }
            }

            _seam.SetActive(running && seam != null);
            if (seam != null && running)
            {
                Vector3 a = sim.Palm(seam.Source.Hand).SupportCenter.ToUnity();
                Vector3 b = sim.Palm(seam.Target.Hand).SupportCenter.ToUnity();
                Vector3 mid = (a + b) * 0.5f + Vector3.up * 0.004f;
                Vector3 across = b - a;
                Vector3 seamDirection = Vector3.Cross(Vector3.up, across).normalized;
                if (seamDirection.sqrMagnitude < 1e-6f)
                {
                    seamDirection = Vector3.forward;
                }

                float dwell = sim.Resolver.DwellProgress(seam);
                Place(_seam, mid, Quaternion.LookRotation(seamDirection, Vector3.up), new Vector3(0.004f * large, 0.002f, (0.05f + 0.03f * dwell) * large));
                Greybox.Tint(_seam, Greybox.WithAlpha(new Color(1f, 0.95f, 0.7f), 0.35f + 0.6f * dwell));
            }

            bool assist = running && sim.Settings.ShelterAssist && sim.Flood.RainActive;
            HandId carrier = sim.Ledger.PalmIsOccupied(HandId.Left) ? HandId.Left : HandId.Right;
            assist &= sim.Ledger.PalmIsOccupied(carrier);
            _assistCanopy.SetActive(assist);
            if (assist)
            {
                Vector3 head = sim.Palm(carrier).SupportCenter.ToUnity() + Vector3.up * sim.Chapter.DollHeadHeight;
                Vector3 above = head + sim.Flood.RainFromWorld.ToUnity() * 0.09f;
                Place(_assistCanopy, above, Quaternion.identity, new Vector3(0.1f, 0.0015f, 0.1f));
            }
        }

        private static void Place(GameObject go, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
        }
    }
}
