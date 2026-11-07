using System;
using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Plays the cues chosen by the <see cref="CueArbiter"/> (the single owner of priority and
    /// rate limits). Effects and music have separate volumes. Loops stop while paused or
    /// recovering because the arbiter says so; this component never decides gameplay.
    /// </summary>
    [AddComponentMenu("Handover/Handover Audio")]
    public sealed class HandoverAudio : MonoBehaviour
    {
        [Serializable]
        public struct AuthoredCue
        {
            public CueId Cue;
            public AudioClip Clip;
        }

        [SerializeField] private HandoverApp _app;
        [Tooltip("Optional authored clips; any cue without one uses the procedural placeholder.")]
        [SerializeField] private AuthoredCue[] _authored = new AuthoredCue[0];
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 0.6f;

        private const int Voices = 6;
        private ProceduralCueBank _bank;
        private AudioSource[] _voices;
        private int _nextVoice;
        private AudioSource _rain;
        private AudioSource _tilt;

        public void Bind(HandoverApp app)
        {
            _app = app;
        }

        private void Start()
        {
            _bank = new ProceduralCueBank(AudioSettings.outputSampleRate);
            _voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                _voices[i] = CreateSource("Cue voice " + i, false, null);
            }

            _rain = CreateSource("Rain loop", true, _bank.RainLoop);
            _tilt = CreateSource("Tilt loop", true, _bank.TiltLoop);
        }

        private AudioSource CreateSource(string sourceName, bool loop, AudioClip clip)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.clip = clip;
            source.spatialBlend = loop ? 0f : _spatialBlend;
            source.volume = 0f;
            if (loop && clip != null)
            {
                source.Play();
            }

            return source;
        }

        private void LateUpdate()
        {
            if (_app == null || _app.Simulation == null || _voices == null)
            {
                return;
            }

            HandoverSimulation sim = _app.Simulation;
            HandoverSettings settings = sim.Settings;
            foreach (CueRequest cue in _app.FrameCues)
            {
                AudioClip clip = Find(cue.Id, cue.Variant);
                if (clip == null)
                {
                    continue;
                }

                bool music = cue.Id == CueId.RescuePhrase || cue.Id == CueId.CompletionMelody;
                AudioSource voice = _voices[_nextVoice];
                _nextVoice = (_nextVoice + 1) % Voices;
                voice.transform.position = CuePosition(sim, cue);
                voice.PlayOneShot(clip, music ? settings.MusicVolume : settings.EffectsVolume);
            }

            float dt = Time.unscaledDeltaTime;
            CueArbiter arbiter = _app.Cues;
            float rainTarget = arbiter.RainLoopActive ? (arbiter.RainPhase == FloodPhase.RisingWater ? 0.32f : 0.2f) * settings.EffectsVolume : 0f;
            float tiltTarget = arbiter.TiltLoopActive ? 0.12f * settings.EffectsVolume : 0f;
            _rain.volume = Mathf.MoveTowards(_rain.volume, rainTarget, dt * 0.5f);
            _tilt.volume = Mathf.MoveTowards(_tilt.volume, tiltTarget, dt * 0.8f);
        }

        private AudioClip Find(CueId id, int variant)
        {
            foreach (AuthoredCue a in _authored)
            {
                if (a.Cue == id && a.Clip != null)
                {
                    return a.Clip;
                }
            }

            return _bank.Get(id, variant);
        }

        private Vector3 CuePosition(HandoverSimulation sim, CueRequest cue)
        {
            if (cue.Doll != DollId.None)
            {
                return PresentationModel.Compute(sim, cue.Doll).Position.ToUnity();
            }

            StageLayout stage = _app.CurrentStage;
            return stage != null ? stage.Pose.Position.ToUnity() : _app.Viewer.position;
        }
    }
}
