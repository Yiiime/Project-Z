using System;
using UnityEngine;

namespace ZFromMoon.World
{
    /// <summary>Initial location identity and spawn anchor; art is a separate child.</summary>
    [DisallowMultipleComponent]
    public sealed class SceneContext : MonoBehaviour
    {
        [SerializeField] private string locationId = "seaside_placeholder";
        [SerializeField] private string displayName = "海边小街（占位）";
        [SerializeField] private string entryId = "from_shore";
        [SerializeField] private Transform initialSpawn;
        [Tooltip("Camera horizontal clamp for this location, in world X.")]
        [SerializeField] private Vector2 cameraBoundsX = new Vector2(-9f, 9f);
        [SerializeField] private EntryPoint[] entries = Array.Empty<EntryPoint>();

        [Serializable]
        public struct EntryPoint
        {
            public string id;
            public Transform anchor;
        }

        public string LocationId => locationId;
        public Vector2 CameraBoundsX => cameraBoundsX;

        public bool TryGetEntry(string id, out Transform anchor)
        {
            anchor = null;
            if (string.IsNullOrWhiteSpace(id)) return false;

            bool isDefault = string.Equals(id, entryId, StringComparison.Ordinal);
            if (isDefault && initialSpawn == null) return false;

            Transform match = isDefault ? initialSpawn : null;
            bool hasExplicitMatch = false;
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (!string.Equals(entry.id, id, StringComparison.Ordinal)) continue;
                    if (hasExplicitMatch || entry.anchor == null
                        || (isDefault && entry.anchor != initialSpawn)) return false;
                    hasExplicitMatch = true;
                    match = entry.anchor;
                }
            }

            anchor = match;
            return anchor != null;
        }

        public Transform GetEntry(string id) => TryGetEntry(id, out Transform anchor) ? anchor : null;
        public string DisplayName => displayName;
        public string EntryId => entryId;
        public Transform InitialSpawn => initialSpawn;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(locationId)
            && !string.IsNullOrWhiteSpace(entryId) && initialSpawn != null;
    }
}
