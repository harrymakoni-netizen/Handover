using System;
using System.Collections.Generic;
using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Scene composition root. Owns the deterministic <see cref="HandoverSimulation"/>, steps
    /// it exactly once per frame (sample palms, resolve, advance, apply objectives, flood,
    /// cues), drives stage placement, persists settings, results and safe checkpoints, and
    /// forwards focus and headset lifecycle. Views read from it; they never write gameplay state.
    /// </summary>
    [AddComponentMenu("Handover/Handover App")]
    [DefaultExecutionOrder(-40)]
    public sealed class HandoverApp : MonoBehaviour, ICheckpointSink
    {
        [Header("Input")]
        [Tooltip("The single active hand adapter. Leave empty to use the first one in the scene, or the keyboard driver.")]
        [SerializeField] private HandPoseSource _handSource;
        [Tooltip("Optional scene surfaces (MRUK tables). Without one the virtual tabletop is used.")]
        [SerializeField] private SurfaceSource _surfaceSource;
        [Tooltip("The player's head (centre eye). Defaults to the main camera.")]
        [SerializeField] private Transform _viewer;

        [Header("Setup")]
        [Tooltip("Confirm the fitted stage automatically (Editor greybox convenience).")]
        [SerializeField] private bool _autoConfirmStage;
        [SerializeField] private bool _preferVirtualTabletop;
        [Tooltip("Seconds to wait for scene data before offering the virtual tabletop.")]
        [SerializeField] private float _surfaceTimeout = 5f;

        [Header("Presentation")]
        [SerializeField] private bool _createGreyboxViews = true;
        [Tooltip("Keyboard shortcuts for desktop testing (pause, confirm, settings). Never required on device.")]
        [SerializeField] private bool _desktopShortcuts = true;
        [Tooltip("Editor only: treat the Unity window losing OS focus as losing app focus. Off by default because " +
                 "the Meta XR Simulator window takes focus whenever you use it; the headset's own focus and mount " +
                 "signals (MetaLifecycleBridge) still pause the game. On device this setting is ignored.")]
        [SerializeField] private bool _editorWindowFocusPauses;

        [Header("Persistence")]
        [SerializeField] private bool _useSaveFile = true;

        private readonly List<CueRequest> _frameCues = new List<CueRequest>();
        private readonly CueArbiter _cues = new CueArbiter();
        private TableStageService _fitter;
        private SaveStore _store;
        private SaveData _save = new SaveData();
        private float _setupTimer;
        private bool _continueRequested;
        private SessionState _lastState = SessionState.Setup;
        private float _messageTimer;

        public HandoverSimulation Simulation { get; private set; }
        public HandPoseSource HandSource => _handSource;
        public SurfaceSource SurfaceSource => _surfaceSource;
        /// <summary>The confirmed stage, or the preview during setup.</summary>
        public StageLayout CurrentStage => Simulation != null && Simulation.Stage != null ? Simulation.Stage : PreviewStage;
        public StageLayout PreviewStage { get; private set; }
        public StageFitResult LastFit { get; private set; }
        public SaveData SaveData => _save;
        public CueArbiter Cues => _cues;
        public IReadOnlyList<CueRequest> FrameCues => _frameCues;
        /// <summary>A recoverable message for the player (save problems, refusals), or null.</summary>
        public string PlayerMessage { get; private set; }
        public bool HasSavedRescue => _save.Checkpoint != null && !_save.Checkpoint.State.Completed;
        public bool ContinueRequested => _continueRequested;
        public Transform Viewer => _viewer != null ? _viewer : (Camera.main != null ? Camera.main.transform : transform);

        public event Action<StageLayout> StageChanged;

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void Awake()
        {
            ChapterDefinition chapter = ChapterDefinition.CreateDefault();
            if (_handSource == null)
            {
                _handSource = FindAnyObjectByType<HandPoseSource>();
            }

            if (_handSource == null)
            {
                _handSource = gameObject.AddComponent<SyntheticHandDriver>();
            }

            if (_handSource is SyntheticHandDriver driver)
            {
                driver.Bind(this);
            }

            if (_surfaceSource == null)
            {
                _surfaceSource = FindAnyObjectByType<SurfaceSource>();
            }

            Simulation = new HandoverSimulation(chapter, new InteractionTuning(), _handSource);
            _fitter = new TableStageService(chapter.LayoutVariants);
            _store = new SaveStore(new PhysicalFileSystem(), Application.persistentDataPath, c => Simulation.Checkpoints.Validate(c));
            if (_useSaveFile)
            {
                SaveLoadResult loaded = _store.Load();
                _save = loaded.Data;
                if (loaded.Message != null)
                {
                    ShowMessage(loaded.Message, 8f);
                }
            }

            Simulation.ApplySettings(_save.Settings);
            Simulation.SetCheckpointSink(this);

            if (_createGreyboxViews)
            {
                CreateGreyboxViews();
            }
        }

        private void CreateGreyboxViews()
        {
            if (GetComponentInChildren<GreyboxStageView>() == null)
            {
                Child("Greybox Stage").AddComponent<GreyboxStageView>().Bind(this);
            }

            if (GetComponentInChildren<DollView>() == null)
            {
                foreach (DollId doll in Ids.Dolls)
                {
                    Child("Doll " + doll).AddComponent<DollView>().Bind(this, doll);
                }
            }

            if (GetComponentInChildren<PalmFeedbackView>() == null)
            {
                Child("Palm Feedback").AddComponent<PalmFeedbackView>().Bind(this);
            }

            if (GetComponentInChildren<HandoverAudio>() == null)
            {
                Child("Audio").AddComponent<HandoverAudio>().Bind(this);
            }

            if (FindAnyObjectByType<HandoverMenu>() == null)
            {
                Child("Menu").AddComponent<HandoverMenu>().Bind(this);
            }

            if (GetComponentInChildren<HandoverHud>() == null)
            {
                Child("Debug HUD").AddComponent<HandoverHud>().Bind(this);
            }
        }

        private GameObject Child(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            return go;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _handSource.Poll();

            if (Simulation.Session.State == SessionState.Setup)
            {
                UpdateSetup(dt);
            }

            // The one ordered simulation update. Rendering never commits gameplay events.
            Simulation.Step(dt);

            DetectTransitions();

            _frameCues.Clear();
            _cues.Process(Simulation.Events, Simulation.Session.State, Simulation.Clock.RealTime, AnyTiltWarning(),
                Simulation.Flood.RainActive, Simulation.Flood.Phase, _frameCues);
            NoteRefusals();

            if (_messageTimer > 0f)
            {
                _messageTimer -= dt;
                if (_messageTimer <= 0f)
                {
                    PlayerMessage = null;
                }
            }

            if (_desktopShortcuts)
            {
                DesktopShortcuts();
            }
        }

        private bool AnyTiltWarning()
        {
            foreach (PalmPlatform palm in Simulation.Palms)
            {
                if (palm.TiltWarning && !Simulation.Ledger.PalmIsEmpty(palm.Hand))
                {
                    return true;
                }
            }

            return false;
        }

        private void NoteRefusals()
        {
            foreach (SimEvent e in Simulation.Events)
            {
                if (e.Kind == SimEventKind.DockRejected)
                {
                    ShowMessage(CaptionModel.Refusal(e.Reason), 2.5f);
                }
                else if (e.Kind == SimEventKind.ShelterCue)
                {
                    ShowMessage(CaptionModel.ShelterHint, 2.5f);
                }
            }
        }

        private void DetectTransitions()
        {
            SessionState state = Simulation.Session.State;
            if (state == _lastState)
            {
                return;
            }

            if (state == SessionState.Results)
            {
                ChapterResults results = Simulation.Director.Results;
                if (results != null)
                {
                    _save.Progress.RecordCompletion(results.LowRefugeStops);
                }

                _save.Checkpoint = null;
                Persist();
            }

            _lastState = state;
        }

        // On device these follow the app lifecycle (system menu, headset off). In the Editor they
        // follow the Unity window's OS focus, which is not the player's focus, so they are opt-in.
        private bool IgnoresWindowFocus => Application.isEditor && !_editorWindowFocusPauses;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (Simulation == null || IgnoresWindowFocus)
            {
                return;
            }

            Simulation.NotifyFocus(hasFocus);
        }

        private void OnApplicationPause(bool paused)
        {
            if (Simulation == null || IgnoresWindowFocus)
            {
                return;
            }

            Simulation.NotifyFocus(!paused);
        }

        private void OnDestroy()
        {
            Simulation?.Unload();
        }

        // ---------------------------------------------------------------------
        // Stage setup
        // ---------------------------------------------------------------------

        public ViewerPose CurrentViewer()
        {
            Transform v = Viewer;
            return new ViewerPose(v.position.ToNumerics(), v.forward.ToNumerics());
        }

        private void UpdateSetup(float dt)
        {
            if (PreviewStage == null)
            {
                _setupTimer += dt;
                bool sceneDone = _surfaceSource == null || _surfaceSource.IsComplete;
                if (!_preferVirtualTabletop && !sceneDone && _setupTimer < _surfaceTimeout)
                {
                    return;
                }

                IReadOnlyList<SurfaceCandidate> surfaces = !_preferVirtualTabletop && _surfaceSource != null && _surfaceSource.IsComplete
                    ? _surfaceSource.Surfaces
                    : null;
                LastFit = _fitter.Fit(surfaces, CurrentViewer(), _handSource.OriginEpoch);
                SetPreview(LastFit.Layout);
            }

            if (_autoConfirmStage && PreviewStage != null)
            {
                ConfirmStage();
            }
        }

        private void SetPreview(StageLayout layout)
        {
            PreviewStage = layout;
            StageChanged?.Invoke(layout);
        }

        /// <summary>Confirm the previewed stage; continues a saved rescue when the player chose to.</summary>
        public void ConfirmStage()
        {
            if (Simulation.Session.State != SessionState.Setup || PreviewStage == null)
            {
                return;
            }

            Simulation.SetStage(PreviewStage);
            if (_continueRequested && HasSavedRescue && !Simulation.Session.ChapterStarted)
            {
                if (!Simulation.ContinueFromSave(_save.Checkpoint))
                {
                    ShowMessage("The saved rescue could not be resumed, so the chapter starts from the beginning.", 6f);
                }
            }

            _continueRequested = false;
            Simulation.ConfirmStage();
            StageChanged?.Invoke(Simulation.Stage);
        }

        public void UseVirtualTabletop()
        {
            if (Simulation.Session.State != SessionState.Setup)
            {
                return;
            }

            SetPreview(_fitter.FitVirtualTabletop(CurrentViewer(), _handSource.OriginEpoch));
        }

        public void RefitToScene()
        {
            if (Simulation.Session.State != SessionState.Setup)
            {
                return;
            }

            _surfaceSource?.Refresh();
            _setupTimer = 0f;
            PreviewStage = null;
        }

        public void ResizeStage(float delta)
        {
            if (Simulation.Session.State != SessionState.Setup || PreviewStage == null)
            {
                return;
            }

            FitIssue issue = _fitter.Resize(PreviewStage, PreviewStage.Scale + delta, CurrentViewer(), out StageLayout resized);
            if (issue != FitIssue.None)
            {
                ShowMessage(CaptionModel.FitProblem(issue), 3f);
            }

            SetPreview(resized);
        }

        public void SetContinueSavedRescue(bool resume)
        {
            _continueRequested = resume && HasSavedRescue;
        }

        // ---------------------------------------------------------------------
        // Menu commands (hand UI, keyboard)
        // ---------------------------------------------------------------------

        public void Pause() => Simulation.Pause(PauseReason.Menu);

        public void Resume()
        {
            if (!Simulation.Resume())
            {
                ShowMessage("Put the headset back on and return to the app to resume.", 3f);
            }
        }

        public void TogglePause()
        {
            if (Simulation.Session.State == SessionState.Paused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }

        public void RestartCheckpoint() => Simulation.RestartFromCheckpoint();

        public void ConfirmRecovery() => Simulation.ConfirmRecovery();

        /// <summary>Reposition: restore a consistent checkpoint, then place the stage again.</summary>
        public void Reposition()
        {
            Simulation.BeginReposition();
            PreviewStage = null;
            _setupTimer = 0f;
            _autoConfirmStage = false;
        }

        public void Replay()
        {
            if (Simulation.Session.State == SessionState.Setup)
            {
                return;
            }

            Simulation.Replay();
        }

        public void Finish()
        {
            Persist();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void UpdateSettings(Action<HandoverSettings> change)
        {
            HandoverSettings next = Simulation.Settings.Clone();
            change(next);
            next.EffectsVolume = Mathf.Clamp01(next.EffectsVolume);
            next.MusicVolume = Mathf.Clamp01(next.MusicVolume);
            Simulation.ApplySettings(next);
            Persist();
        }

        /// <summary>Menu feedback outranks gameplay cues (played by the audio view this frame).</summary>
        public void MenuFeedback()
        {
            _cues.RequestMenu(Simulation.Clock.RealTime, _frameCues);
        }

        public void ShowMessage(string message, float seconds)
        {
            PlayerMessage = message;
            _messageTimer = seconds;
        }

        // ---------------------------------------------------------------------
        // Persistence
        // ---------------------------------------------------------------------

        void ICheckpointSink.OnCheckpoint(Checkpoint checkpoint)
        {
            _save.Checkpoint = checkpoint.State.Completed ? null : checkpoint.Clone();
            Persist();
        }

        private void Persist()
        {
            if (!_useSaveFile || Simulation == null)
            {
                return;
            }

            _save.Settings = Simulation.Settings.Clone();
            if (!_store.Save(_save, out string error))
            {
                Debug.LogWarning("[Handover] " + error);
                ShowMessage("Progress could not be saved this time. Play continues.", 4f);
            }
        }

        // ---------------------------------------------------------------------
        // Desktop shortcuts (Editor and desktop testing only)
        // ---------------------------------------------------------------------

        private void DesktopShortcuts()
        {
            SessionState state = Simulation.Session.State;
            if (DriverInput.Pressed(DriverKey.P) || DriverInput.Pressed(DriverKey.Space))
            {
                TogglePause();
            }

            if (DriverInput.Pressed(DriverKey.Enter))
            {
                switch (state)
                {
                    case SessionState.Setup: ConfirmStage(); break;
                    case SessionState.Recovering: ConfirmRecovery(); break;
                    case SessionState.Results: Replay(); break;
                    case SessionState.Paused: Resume(); break;
                }
            }

            if (DriverInput.Pressed(DriverKey.Backspace) && state != SessionState.Setup)
            {
                RestartCheckpoint();
            }

            if (state == SessionState.Setup)
            {
                if (DriverInput.Pressed(DriverKey.B)) ResizeStage(0.05f);
                if (DriverInput.Pressed(DriverKey.N)) ResizeStage(-0.05f);
                if (DriverInput.Pressed(DriverKey.T)) UseVirtualTabletop();
            }

            if (DriverInput.Pressed(DriverKey.K)) UpdateSettings(s => s.SteadyMode = !s.SteadyMode);
            if (DriverInput.Pressed(DriverKey.L)) UpdateSettings(s => s.RelaxedMode = !s.RelaxedMode);
            if (DriverInput.Pressed(DriverKey.M)) UpdateSettings(s => s.ShelterAssist = !s.ShelterAssist);
            if (DriverInput.Pressed(DriverKey.Y)) UpdateSettings(s => s.LargeCues = !s.LargeCues);
            if (DriverInput.Pressed(DriverKey.U)) UpdateSettings(s => s.ReducedMotion = !s.ReducedMotion);
        }
    }
}
