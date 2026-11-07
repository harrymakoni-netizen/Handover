using System;
using System.Collections.Generic;
using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Placeholder sounds synthesised at start-up so the greybox needs no audio assets:
    /// wooden taps, a two-note seam cue, soft refusals, lantern chimes, a harbour melody in
    /// phrases and a rain loop. Warnings stay quiet (capped volume, never an alarm). Replace
    /// with authored clips by assigning them on <see cref="HandoverAudio"/>.
    /// </summary>
    public sealed class ProceduralCueBank
    {
        private readonly int _rate;
        private readonly Dictionary<CueId, AudioClip[]> _clips = new Dictionary<CueId, AudioClip[]>();
        private readonly System.Random _random = new System.Random(7);

        public ProceduralCueBank(int sampleRate)
        {
            _rate = Mathf.Clamp(sampleRate, 22050, 48000);
            Add(CueId.ReadyKnock, Concat(WoodTap(0.09f, 760f), Silence(0.11f), WoodTap(0.09f, 700f)));
            Add(CueId.BoardingInvite, Chime(0.22f, 660f, 0.25f));
            Add(CueId.SeamConnect, Concat(Chime(0.12f, 587.3f, 0.3f), Chime(0.2f, 880f, 0.3f)));
            Add(CueId.BoardTap, WoodTap(0.1f, 820f));
            Add(CueId.LandTap, WoodTap(0.08f, 620f, 0.6f));
            Add(CueId.TiltWarning, Creak(0.35f));
            Add(CueId.DockRefused, Concat(Chime(0.12f, 440f, 0.2f), Chime(0.16f, 349.2f, 0.2f)));
            Add(CueId.ShelterWarning, Concat(Chime(0.08f, 523.3f, 0.15f), Silence(0.06f), Chime(0.08f, 523.3f, 0.15f)));
            Add(CueId.TrackingRecovery, Bell(0.7f, 698.5f));
            Add(CueId.LanternLit, Arpeggio(new[] { 392f, 493.9f, 587.3f }, 0.14f, 0.3f));
            Add(CueId.LanternDimmed, Arpeggio(new[] { 587.3f, 493.9f, 392f }, 0.12f, 0.2f));
            Add(CueId.LanternLatched, Arpeggio(new[] { 392f, 493.9f, 587.3f, 784f }, 0.16f, 0.35f));
            Add(CueId.WaterRise, Swell(1.2f));
            Add(CueId.CheckpointRestored, Swell(0.6f));
            Add(CueId.MenuSelect, WoodTap(0.04f, 1200f, 0.4f));
            // One harbour phrase per rescue (variants 1-3), then the full melody.
            _clips[CueId.RescuePhrase] = new[]
            {
                Make("Harbour phrase 1", Arpeggio(new[] { 523.3f, 587.3f, 659.3f }, 0.18f, 0.32f)),
                Make("Harbour phrase 2", Arpeggio(new[] { 659.3f, 784f, 880f }, 0.18f, 0.32f)),
                Make("Harbour phrase 3", Arpeggio(new[] { 880f, 784f, 1046.5f }, 0.2f, 0.34f)),
            };
            Add(CueId.CompletionMelody, Arpeggio(new[] { 523.3f, 659.3f, 784f, 659.3f, 880f, 1046.5f }, 0.22f, 0.35f));
            RainLoop = Noise(2f, 0.08f, true);
            TiltLoop = Make("Tilt loop", Creak(1.2f, loop: true));
        }

        public AudioClip RainLoop { get; }
        public AudioClip TiltLoop { get; }

        public AudioClip Get(CueId id, int variant)
        {
            if (!_clips.TryGetValue(id, out AudioClip[] clips) || clips.Length == 0)
            {
                return null;
            }

            int index = Mathf.Clamp(variant - 1, 0, clips.Length - 1);
            return clips.Length == 1 ? clips[0] : clips[index];
        }

        private void Add(CueId id, float[] samples)
        {
            _clips[id] = new[] { Make(id.ToString(), samples) };
        }

        private AudioClip Make(string clipName, float[] samples)
        {
            AudioClip clip = AudioClip.Create("Handover " + clipName, Math.Max(1, samples.Length), 1, _rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private float[] Arpeggio(float[] notes, float noteSeconds, float level)
        {
            var parts = new List<float[]>();
            foreach (float n in notes)
            {
                parts.Add(Chime(noteSeconds, n, level));
            }

            return Concat(parts.ToArray());
        }

        // ---- synthesis -------------------------------------------------------------

        private float[] Silence(float seconds) => new float[(int)(seconds * _rate)];

        private float[] WoodTap(float seconds, float pitch, float level = 0.7f)
        {
            int n = (int)(seconds * _rate);
            var s = new float[n];
            float low = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)_rate;
                float env = Mathf.Exp(-t * 55f);
                float noise = (float)(_random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.25f;
                float body = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-t * 35f);
                s[i] = (0.5f * low * env + 0.6f * body) * level;
            }

            return s;
        }

        private float[] Chime(float seconds, float pitch, float level)
        {
            int n = (int)(seconds * _rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)_rate;
                float attack = Mathf.Clamp01(t / 0.008f);
                float env = attack * Mathf.Exp(-t * 7f);
                float tone = Mathf.Sin(2f * Mathf.PI * pitch * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * pitch * 2f * t);
                s[i] = tone * env * level;
            }

            return s;
        }

        private float[] Bell(float seconds, float pitch)
        {
            int n = (int)(seconds * _rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)_rate;
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-t * 3.5f);
                float tone = Mathf.Sin(2f * Mathf.PI * pitch * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * pitch * 1.5f * t)
                    + 0.2f * Mathf.Sin(2f * Mathf.PI * pitch * 2.76f * t);
                s[i] = tone * env * 0.25f;
            }

            return s;
        }

        private float[] Creak(float seconds, bool loop = false)
        {
            int n = (int)(seconds * _rate);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)_rate;
                float wobble = 1f + 0.03f * Mathf.Sin(2f * Mathf.PI * 5f * t);
                phase += 2f * Mathf.PI * 210f * wobble / _rate;
                float env = loop ? 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * t / seconds) : Mathf.Sin(Mathf.PI * t / seconds);
                s[i] = Mathf.Sin(phase) * env * 0.12f;
            }

            return s;
        }

        private float[] Swell(float seconds)
        {
            float[] s = NoiseSamples(seconds, 0.05f);
            for (int i = 0; i < s.Length; i++)
            {
                s[i] *= Mathf.Sin(Mathf.PI * i / (float)s.Length);
            }

            return s;
        }

        private AudioClip Noise(float seconds, float smoothing, bool loop)
        {
            float[] s = NoiseSamples(seconds, smoothing);
            if (loop)
            {
                // Crossfade the ends so the loop is seamless.
                int fade = Mathf.Min(s.Length / 4, _rate / 10);
                for (int i = 0; i < fade; i++)
                {
                    float a = i / (float)fade;
                    s[i] = s[i] * a + s[s.Length - fade + i] * (1f - a);
                }

                Array.Resize(ref s, s.Length - fade);
            }

            return Make("Rain", s);
        }

        private float[] NoiseSamples(float seconds, float smoothing)
        {
            int n = (int)(seconds * _rate);
            var s = new float[n];
            float low = 0f;
            for (int i = 0; i < n; i++)
            {
                float noise = (float)(_random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * smoothing;
                s[i] = (noise * 0.25f + low) * 0.35f;
            }

            return s;
        }

        private static float[] Concat(params float[][] parts)
        {
            int total = 0;
            foreach (float[] p in parts)
            {
                total += p.Length;
            }

            var result = new float[total];
            int offset = 0;
            foreach (float[] p in parts)
            {
                Array.Copy(p, 0, result, offset, p.Length);
                offset += p.Length;
            }

            return result;
        }
    }
}
