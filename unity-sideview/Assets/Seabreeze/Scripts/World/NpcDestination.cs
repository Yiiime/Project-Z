using System;
using UnityEngine;

namespace Seabreeze
{
    [Serializable]
    public sealed class NpcDestination
    {
        public string id;
        public Vector2 position;
        [Range(-1, 1)] public int facing;

        public NpcDestination(string id, Vector2 position, int facing = 0)
        {
            this.id = id;
            this.position = position;
            this.facing = facing;
        }
    }
}
