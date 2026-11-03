using UnityEngine;

namespace Seabreeze
{
    public class Interactable : MonoBehaviour
    {
        public string id;
        public string label;
        public float radius = 1.7f;
        public bool isDoor;
        public PlaceId destination;
        public bool npc;
        public Vector3 promptOffset = new Vector3(0, 2.8f, 0);
        public bool Available => isDoor || (GameSession.Instance != null && GameSession.Instance.Story.IsAvailable(id));
        public void Activate()
        {
            if (!Available)
                return;
            if (isDoor)
                GameSession.Instance.Travel(destination);
            else
                GameSession.Instance.Story.Interact(id);
        }
    }
}
