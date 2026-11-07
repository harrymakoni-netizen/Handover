using System.Collections.Generic;
using Handover.Core;
using UnityEngine;

namespace Handover.Unity
{
    /// <summary>
    /// Supplies horizontal surfaces for stage fitting (for example MRUK table anchors). The
    /// simulation never sees scene types; it only receives <see cref="SurfaceCandidate"/>s.
    /// </summary>
    public abstract class SurfaceSource : MonoBehaviour
    {
        /// <summary>True once the source has finished trying (with or without surfaces).</summary>
        public abstract bool IsComplete { get; }

        /// <summary>True when the player declined the permission or scene data is unavailable.</summary>
        public abstract bool Unavailable { get; }

        public abstract IReadOnlyList<SurfaceCandidate> Surfaces { get; }

        /// <summary>A short explanation shown before or after a permission request.</summary>
        public virtual string Status => IsComplete ? (Unavailable ? "No room data" : $"{Surfaces.Count} surface(s)") : "Looking for a table";

        /// <summary>Ask again (for example after the player grants the permission).</summary>
        public virtual void Refresh()
        {
        }
    }
}
