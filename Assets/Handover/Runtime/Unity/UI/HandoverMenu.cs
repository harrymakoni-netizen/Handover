using System;
using Handover.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Handover.Unity
{
    /// <summary>
    /// Hand-operated world-space menu: a small Pause control reachable by either hand, and the
    /// Setup, Pause, Settings, Recovery and Results panels with captions and messages. The
    /// layout is built procedurally (in the Editor for the Meta scene so Interaction SDK can add
    /// ray and poke to the canvas, or at runtime for the greybox); buttons call
    /// <see cref="HandoverApp"/> commands only.
    /// </summary>
    [AddComponentMenu("Handover/Handover Menu")]
    public sealed class HandoverMenu : MonoBehaviour
    {
        private const int Slots = 10;
        private const float Width = 460f;
        private const float Height = 380f;

        [SerializeField] private HandoverApp _app;
        [SerializeField] private Canvas _canvas;
        [Tooltip("Height of the menu above the table at the far edge of the stage.")]
        [SerializeField] private float _height = 0.2f;
        [SerializeField] private float _behindStage = 0.06f;

        private Text _title;
        private Text _body;
        private Text _caption;
        private Text _message;
        private Button _pauseButton;
        private readonly Button[] _buttons = new Button[Slots];
        private readonly Text[] _labels = new Text[Slots];
        private readonly Action[] _actions = new Action[Slots];
        private GameObject _panel;
        private bool _settingsOpen;
        private int _used;

        public Canvas Canvas => _canvas;

        public void Bind(HandoverApp app)
        {
            _app = app;
        }

        private void Awake()
        {
            if (_canvas == null)
            {
                BuildLayout();
            }

            FindParts();
            for (int i = 0; i < Slots; i++)
            {
                int slot = i;
                _buttons[i].onClick.AddListener(() => Press(slot));
            }

            _pauseButton.onClick.AddListener(() =>
            {
                _settingsOpen = false;
                _app?.Pause();
            });
        }

        private void Press(int slot)
        {
            Action action = _actions[slot];
            if (action == null || _app == null)
            {
                return;
            }

            _app.MenuFeedback();
            action();
        }

        // ---------------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------------

        /// <summary>Builds the canvas and its children if they do not exist yet (idempotent; Editor-safe).</summary>
        public void BuildLayout()
        {
            if (_canvas != null)
            {
                return;
            }

            var canvasGo = new GameObject("Handover Menu Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(Width, Height);
            rect.localScale = Vector3.one * 0.001f;
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _pauseButton = MakeButton(rect, "Pause Button", font, new Vector2(-Width * 0.5f + 55f, Height * 0.5f - 24f), new Vector2(100f, 40f), "Pause");

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(rect, false);
            var panelRect = (RectTransform)_panel.transform;
            panelRect.sizeDelta = new Vector2(Width, Height - 52f);
            panelRect.anchoredPosition = new Vector2(0f, -26f);
            _panel.GetComponent<Image>().color = new Color(0.05f, 0.18f, 0.16f, 0.85f);

            _title = MakeText(panelRect, "Title", font, new Vector2(0f, 140f), new Vector2(Width - 30f, 36f), 24, FontStyle.Bold);
            _body = MakeText(panelRect, "Body", font, new Vector2(0f, 100f), new Vector2(Width - 30f, 46f), 15, FontStyle.Normal);
            for (int i = 0; i < Slots; i++)
            {
                int column = i % 2;
                int row = i / 2;
                Vector2 position = new Vector2(column == 0 ? -110f : 110f, 50f - row * 46f);
                _buttons[i] = MakeButton(panelRect, "Slot " + i, font, position, new Vector2(205f, 40f), "");
            }

            _message = MakeText(rect, "Message", font, new Vector2(0f, -Height * 0.5f + 40f), new Vector2(Width - 20f, 30f), 15, FontStyle.Italic);
            _caption = MakeText(rect, "Caption", font, new Vector2(0f, -Height * 0.5f + 12f), new Vector2(Width - 20f, 28f), 16, FontStyle.Normal);
        }

        private void FindParts()
        {
            Transform root = _canvas.transform;
            _pauseButton = root.Find("Pause Button").GetComponent<Button>();
            _panel = root.Find("Panel").gameObject;
            _title = _panel.transform.Find("Title").GetComponent<Text>();
            _body = _panel.transform.Find("Body").GetComponent<Text>();
            for (int i = 0; i < Slots; i++)
            {
                _buttons[i] = _panel.transform.Find("Slot " + i).GetComponent<Button>();
                _labels[i] = _buttons[i].GetComponentInChildren<Text>();
            }

            _message = root.Find("Message").GetComponent<Text>();
            _caption = root.Find("Caption").GetComponent<Text>();
        }

        private static Text MakeText(RectTransform parent, string textName, Font font, Vector2 position, Vector2 size, int fontSize, FontStyle style)
        {
            var go = new GameObject(textName, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.color = new Color(0.97f, 0.95f, 0.9f);
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(RectTransform parent, string buttonName, Font font, Vector2 position, Vector2 size, string label)
        {
            var go = new GameObject(buttonName, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.96f, 0.9f, 0.78f);
            Text text = MakeText(rect, "Label", font, Vector2.zero, size, 17, FontStyle.Bold);
            text.color = new Color(0.08f, 0.2f, 0.18f);
            text.text = label;
            return go.GetComponent<Button>();
        }

        // ---------------------------------------------------------------------
        // Per-frame state
        // ---------------------------------------------------------------------

        private void LateUpdate()
        {
            if (_app == null || _app.Simulation == null || _canvas == null)
            {
                return;
            }

            HandoverSimulation sim = _app.Simulation;
            SessionState state = sim.Session.State;
            Place(state);

            _used = 0;
            for (int i = 0; i < Slots; i++)
            {
                _actions[i] = null;
            }

            bool panel = true;
            if (_settingsOpen && (state == SessionState.Paused || state == SessionState.Results || state == SessionState.Setup))
            {
                ShowSettings(sim.Settings);
            }
            else
            {
                _settingsOpen = false;
                switch (state)
                {
                    case SessionState.Setup:
                        ShowSetup();
                        break;
                    case SessionState.Paused:
                        _title.text = "Paused";
                        _body.text = sim.Session.PauseReason == PauseReason.Menu
                            ? "Everyone is held safely."
                            : "Paused because the app lost focus or the headset was removed.";
                        Add("Resume", _app.Resume);
                        Add("Restart checkpoint", _app.RestartCheckpoint);
                        Add("Reposition stage", _app.Reposition);
                        Add("Settings", () => _settingsOpen = true);
                        Add("Exit", _app.Finish);
                        break;
                    case SessionState.Recovering:
                        _title.text = "Hold on";
                        _body.text = CaptionModel.Current(sim);
                        Add("Return to safety", _app.ConfirmRecovery);
                        Add("Pause", _app.Pause);
                        break;
                    case SessionState.Results:
                        ShowResults(sim);
                        break;
                    case SessionState.Error:
                        _title.text = "Something went wrong";
                        _body.text = sim.Session.ErrorMessage;
                        Add("Restart checkpoint", _app.RestartCheckpoint);
                        Add("Exit", _app.Finish);
                        break;
                    default:
                        panel = false;
                        break;
                }
            }

            _panel.SetActive(panel);
            for (int i = 0; i < Slots; i++)
            {
                _buttons[i].gameObject.SetActive(panel && i < _used);
            }

            _pauseButton.gameObject.SetActive(state == SessionState.Ready || state == SessionState.Playing);
            _caption.gameObject.SetActive(sim.Settings.Captions);
            _caption.text = CaptionModel.Current(sim);
            _message.text = _app.PlayerMessage ?? "";
            _canvas.transform.localScale = Vector3.one * (sim.Settings.LargeCues ? 0.0013f : 0.001f);
        }

        private void ShowSetup()
        {
            StageFitResult fit = _app.LastFit;
            _title.text = "Place the stage";
            if (_app.PreviewStage == null)
            {
                _body.text = _app.SurfaceSource != null ? _app.SurfaceSource.Status + "..." : "Preparing the virtual tabletop...";
                Add("Use virtual tabletop", _app.UseVirtualTabletop);
                return;
            }

            bool virtualStage = _app.PreviewStage.Placement.Source == StageSource.VirtualTabletop;
            string why = fit != null && fit.Rejections.Count > 0 ? " " + CaptionModel.FitProblem(fit.FirstIssue) : "";
            _body.text = virtualStage
                ? "Using a virtual tabletop. It cannot support your arms; rest them when you like." + why
                : "Fitted to your table. Keep its edges clear of hands.";
            Add("Confirm stage", _app.ConfirmStage);
            Add(virtualStage ? "Find my table" : "Virtual tabletop", virtualStage ? (Action)_app.RefitToScene : _app.UseVirtualTabletop);
            Add("Smaller", () => _app.ResizeStage(-0.05f));
            Add("Bigger", () => _app.ResizeStage(0.05f));
            if (_app.HasSavedRescue)
            {
                Add(_app.ContinueRequested ? "Continue rescue: Yes" : "Continue rescue: No", () => _app.SetContinueSavedRescue(!_app.ContinueRequested));
            }

            Add("Settings", () => _settingsOpen = true);
        }

        private void ShowSettings(HandoverSettings s)
        {
            _title.text = "Settings";
            _body.text = "Assistance is part of the game, never a lower result.";
            Add("Steady mode: " + OnOff(s.SteadyMode), () => _app.UpdateSettings(x => x.SteadyMode = !x.SteadyMode));
            Add("Relaxed mode: " + OnOff(s.RelaxedMode), () => _app.UpdateSettings(x => x.RelaxedMode = !x.RelaxedMode));
            Add("Shelter assist: " + OnOff(s.ShelterAssist), () => _app.UpdateSettings(x => x.ShelterAssist = !x.ShelterAssist));
            Add("Large cues: " + OnOff(s.LargeCues), () => _app.UpdateSettings(x => x.LargeCues = !x.LargeCues));
            Add("Captions: " + OnOff(s.Captions), () => _app.UpdateSettings(x => x.Captions = !x.Captions));
            Add("Reduced motion: " + OnOff(s.ReducedMotion), () => _app.UpdateSettings(x => x.ReducedMotion = !x.ReducedMotion));
            Add($"Effects {Mathf.RoundToInt(s.EffectsVolume * 10)} -", () => _app.UpdateSettings(x => x.EffectsVolume -= 0.1f));
            Add($"Effects {Mathf.RoundToInt(s.EffectsVolume * 10)} +", () => _app.UpdateSettings(x => x.EffectsVolume += 0.1f));
            Add($"Music {Mathf.RoundToInt(s.MusicVolume * 10)} -", () => _app.UpdateSettings(x => x.MusicVolume -= 0.1f));
            Add("Back", () => _settingsOpen = false);
        }

        private void ShowResults(HandoverSimulation sim)
        {
            ChapterResults r = sim.Director.Results;
            ProgressData progress = _app.SaveData.Progress;
            _title.text = "Everyone is home";
            string stops = r != null ? $"Low-refuge rests: {r.LowRefugeStops}" : "";
            string best = progress.BestLowRefugeStops >= 0 ? $" (fewest so far: {progress.BestLowRefugeStops})" : "";
            string assist = r != null && r.AssistanceUsed != null && r.AssistanceUsed.AnyAssistance ? " Played with assistance settings." : "";
            _body.text = stops + best + "." + assist;
            Add("Replay", _app.Replay);
            Add("Settings", () => _settingsOpen = true);
            Add("Finish", _app.Finish);
        }

        private static string OnOff(bool value) => value ? "On" : "Off";

        private void Add(string label, Action action)
        {
            if (_used >= Slots)
            {
                return;
            }

            _labels[_used].text = label;
            _actions[_used] = action;
            _used++;
        }

        private void Place(SessionState state)
        {
            StageLayout stage = _app.CurrentStage;
            Transform viewer = _app.Viewer;
            Vector3 position;
            if (stage != null)
            {
                float depth = stage.Variant.Depth * stage.Scale;
                position = stage.ToWorld(new System.Numerics.Vector3(0f, 0f, 0f)).ToUnity()
                    + stage.Pose.Forward.ToUnity() * (depth * 0.5f + _behindStage)
                    + Vector3.up * _height;
            }
            else
            {
                Vector3 flat = Vector3.ProjectOnPlane(viewer.forward, Vector3.up).normalized;
                position = viewer.position + flat * 0.55f + Vector3.down * 0.15f;
            }

            Vector3 away = position - viewer.position;
            if (away.sqrMagnitude > 1e-6f)
            {
                _canvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(away.normalized, Vector3.up));
            }
        }
    }
}
