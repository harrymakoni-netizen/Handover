using System.Collections.Generic;
using Handover.Core;
using Handover.Unity;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Handover.MetaPlatform
{
    /// <summary>
    /// Offers MRUK table anchors as <see cref="SurfaceCandidate"/>s. A table's plane rect lies in
    /// the anchor's local XY plane with the normal along local +Z; only upward-facing planes are
    /// offered. When MRUK is absent, permission is declined or no scene loads within the
    /// timeout, the source completes empty and the app falls back to the virtual tabletop.
    /// No room mesh is stored.
    /// </summary>
    [AddComponentMenu("Handover/MRUK Table Surface Source")]
    public sealed class MruTableSurfaceSource : SurfaceSource
    {
        [SerializeField] private float _timeout = 6f;
        [Tooltip("Minimum cosine between a table plane's normal and world up.")]
        [SerializeField] private float _minUpDot = 0.94f;

        private readonly List<SurfaceCandidate> _surfaces = new List<SurfaceCandidate>();
        private bool _complete;
        private bool _unavailable;
        private bool _registered;
        private float _elapsed;

        public override bool IsComplete => _complete;
        public override bool Unavailable => _unavailable;
        public override IReadOnlyList<SurfaceCandidate> Surfaces => _surfaces;

        public override string Status
        {
            get
            {
                if (!_complete)
                {
                    return "Looking for a table (room data may need your permission)";
                }

                return _surfaces.Count > 0 ? $"Found {_surfaces.Count} table(s)" : "No table found in the room data";
            }
        }

        private void Update()
        {
            if (_complete)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            MRUK mruk = MRUK.Instance;
            if (mruk == null)
            {
                if (_elapsed > 1f)
                {
                    Finish();
                }

                return;
            }

            if (!_registered)
            {
                _registered = true;
                mruk.RegisterSceneLoadedCallback(OnSceneLoaded);
            }

            if (_elapsed > _timeout)
            {
                Finish();
            }
        }

        private void OnSceneLoaded()
        {
            Collect();
            Finish();
        }

        private void Finish()
        {
            _complete = true;
            _unavailable = _surfaces.Count == 0;
        }

        public override void Refresh()
        {
            _surfaces.Clear();
            _elapsed = 0f;
            _complete = false;
            if (MRUK.Instance != null && MRUK.Instance.IsInitialized)
            {
                Collect();
                Finish();
            }
        }

        private void Collect()
        {
            _surfaces.Clear();
            MRUK mruk = MRUK.Instance;
            MRUKRoom room = mruk != null ? mruk.GetCurrentRoom() : null;
            if (room == null)
            {
                return;
            }

            foreach (MRUKAnchor anchor in room.Anchors)
            {
                if (anchor == null || !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) || !anchor.PlaneRect.HasValue)
                {
                    continue;
                }

                Transform t = anchor.transform;
                if (Vector3.Dot(t.forward, Vector3.up) < _minUpDot)
                {
                    continue;
                }

                Rect rect = anchor.PlaneRect.Value;
                Vector3 centre = t.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
                Vector3 right = Vector3.ProjectOnPlane(t.right, Vector3.up).normalized;
                // SurfaceCandidate's local X is YawRotation(yaw) * X = (cos yaw, 0, -sin yaw).
                float yaw = Mathf.Atan2(-right.z, right.x);
                _surfaces.Add(new SurfaceCandidate(anchor.name, centre.ToNumerics(), yaw, rect.width, rect.height));
            }
        }
    }
}
