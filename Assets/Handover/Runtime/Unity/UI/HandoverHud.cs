using System.Text;
using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Desktop debug overlay (Editor and development builds only). Shows session, chapter and
    /// palm state plus the keyboard map. Nothing on the headset depends on it.
    /// </summary>
    [AddComponentMenu("Handover/Debug HUD (desktop)")]
    public sealed class HandoverHud : MonoBehaviour
    {
        [SerializeField] private HandoverApp _app;
        [SerializeField] private bool _visible = true;
        private readonly StringBuilder _text = new StringBuilder(1024);
        private GUIStyle _style;

        public void Bind(HandoverApp app)
        {
            _app = app;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (DriverInput.Pressed(DriverKey.O))
            {
                _visible = !_visible;
            }
        }

        private void OnGUI()
        {
            if (!_visible || _app == null || _app.Simulation == null)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, wordWrap = true };
            }

            HandoverSimulation sim = _app.Simulation;
            ChapterState s = sim.Director.State;
            _text.Clear();
            _text.Append("HANDOVER greybox  (O hides)\n");
            _text.Append($"Session: {sim.Session.State}   Phase: {sim.Flood.Phase}   R: {sim.Flood.LowRefuge}\n");
            _text.Append($"Tutorial hop: {s.TutorialHopDone}   Lantern lit: {sim.Director.LanternLit}   Latched: {s.LanternLatched}\n");
            _text.Append($"Rescued: {s.Rescued.Count}/3   Low-refuge rests: {s.LowRefugeStops}   Exposure resets: {s.ExposureResets}\n");
            foreach (DollId d in Ids.Dolls)
            {
                _text.Append($"  {d}: {sim.Ledger.Doll(d).Owner}\n");
            }

            foreach (PalmPlatform p in sim.Palms)
            {
                _text.Append($"  {p.Hand}: {(p.IsTracked ? "tracked" : "LOST")} up {p.AngleFromUp:0}°" +
                             $"{(p.CarryEligible ? " carry" : "")}{(p.TiltWarning ? " TILT" : "")}{(p.ShieldPose ? " shield" : "")}{(p.Clinging ? " cling" : "")}" +
                             $"{(sim.Flood.ExposedSeconds(p.Hand) > 0f ? $" rain {sim.Flood.ExposedSeconds(p.Hand):0.0}s" : "")}\n");
            }

            foreach (ConnectionCandidate c in sim.Resolver.Candidates)
            {
                _text.Append($"  candidate {c}\n");
            }

            _text.Append(_app.HandSource != null ? _app.HandSource.Describe() + "\n" : "");
            _text.Append("Caption: ").Append(CaptionModel.Current(sim)).Append('\n');
            _text.Append("\nHands: Tab switch · WASD/QE move (Shift fine) · Z/X tilt · C level · F palm-down · H hide · R recenter\n");
            _text.Append("       1-4 snap to D/R/U/H · G bring other palm alongside · V raise other hand as shield\n");
            _text.Append("App:   P/Space pause · Enter confirm · Backspace restart checkpoint · T virtual table · B/N resize\n");
            _text.Append("       K steady · L relaxed · M shelter assist · Y large cues · U reduced motion");
            GUI.Box(new Rect(10, 10, 640, 330), _text.ToString(), _style);
        }
#endif
    }
}
