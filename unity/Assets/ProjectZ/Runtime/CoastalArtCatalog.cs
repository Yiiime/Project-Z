using UnityEngine;

namespace ProjectZ.Runtime
{
    // Scene assembly depends on this catalog; models/materials can be replaced independently.
    [CreateAssetMenu(menuName = "Project Z/Coastal Art Catalog")]
    public sealed class CoastalArtCatalog : ScriptableObject
    {
        public GameObject Z;
        public GameObject Bench;
        public GameObject Cottage;
        public GameObject MapTable;
        public GameObject[] Trees;
        public GameObject[] Bushes;
        public GameObject[] Grasses;
        public GameObject[] Flowers;
        public GameObject[] Rocks;
        public Material Ground;
        public Material Path;
        public Material Water;
        public Material Sky;
        public UnityEngine.Rendering.VolumeProfile PostProcessing;
    }
}
