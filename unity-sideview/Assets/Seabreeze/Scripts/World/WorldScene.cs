using UnityEngine;

namespace Seabreeze
{
    public class WorldScene : MonoBehaviour
    {
        public PlaceId place;
        public float width = 32, floorImageFraction = .165f;
        public float streetSpawn = -8, fromCafe = -8.1f, fromLighthouse = 13.5f;
        public float cameraSize = 6.5f, cameraY = 4.25f;
        public SpriteRenderer backdrop;
        void Start()
        {
            if (WorldDirector.Instance != null)
                WorldDirector.Instance.Attach(this);
        }
    }
}
