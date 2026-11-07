using Handover.Unity;
using UnityEngine;

namespace Handover.MetaPlatform
{
    /// <summary>
    /// Forwards headset lifecycle from <see cref="OVRManager"/> (mount, input focus) to the
    /// simulation, which pauses gameplay and weather and waits for confirmation to resume.
    /// </summary>
    [AddComponentMenu("Handover/Meta Lifecycle Bridge")]
    public sealed class MetaLifecycleBridge : MonoBehaviour
    {
        [SerializeField] private HandoverApp _app;

        public void Bind(HandoverApp app)
        {
            _app = app;
        }

        private void Awake()
        {
            if (_app == null)
            {
                _app = FindAnyObjectByType<HandoverApp>();
            }
        }

        private void OnEnable()
        {
            OVRManager.HMDMounted += OnMounted;
            OVRManager.HMDUnmounted += OnUnmounted;
            OVRManager.InputFocusAcquired += OnFocusAcquired;
            OVRManager.InputFocusLost += OnFocusLost;
        }

        private void OnDisable()
        {
            OVRManager.HMDMounted -= OnMounted;
            OVRManager.HMDUnmounted -= OnUnmounted;
            OVRManager.InputFocusAcquired -= OnFocusAcquired;
            OVRManager.InputFocusLost -= OnFocusLost;
        }

        private void OnMounted() => _app?.Simulation?.NotifyHeadsetMounted(true);

        private void OnUnmounted() => _app?.Simulation?.NotifyHeadsetMounted(false);

        private void OnFocusAcquired() => _app?.Simulation?.NotifyFocus(true);

        private void OnFocusLost() => _app?.Simulation?.NotifyFocus(false);
    }
}
