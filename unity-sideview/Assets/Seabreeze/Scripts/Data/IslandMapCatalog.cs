using System;
using UnityEngine;

namespace Seabreeze
{
    [Serializable] public sealed class IslandMapNode
    {
        public PlaceId place;
        public string mapId, displayName, sourceFile;
        public bool indoor;
        public int row, column;
        public float width, groundPixelY;
    }
    [Serializable] public sealed class IslandMapLink
    {
        public string id, arrivalId, label;
        public PlaceId from, to;
        public bool automatic;
        public float x;
    }
    [CreateAssetMenu(menuName="Seabreeze/Island Map Catalog")]
    public sealed class IslandMapCatalog : ScriptableObject
    {
        public IslandMapNode[] locations=Array.Empty<IslandMapNode>();
        public IslandMapLink[] routes=Array.Empty<IslandMapLink>();
    }
}
