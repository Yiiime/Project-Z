using System;
using UnityEngine;

namespace Seabreeze
{
    // Logical location metadata is independent of the replacement background art.
    public sealed class IslandLocation : MonoBehaviour
    {
        public string mapId, displayName;
        public int row, column;
        public float groundPixelY;
        public string sourceAsset;
    }
}
