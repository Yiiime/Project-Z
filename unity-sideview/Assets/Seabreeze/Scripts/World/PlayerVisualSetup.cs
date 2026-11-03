using UnityEngine;

namespace Seabreeze
{
    [DefaultExecutionOrder(-50)]
    public class PlayerVisualSetup : MonoBehaviour
    {
        void Awake()
        {
            var visual = GetComponentInChildren<CharacterVisual>();
            if (visual != null)
            {
                visual.Build(0);
                visual.transform.localScale = Vector3.one * 1.17f;
            }
        }
    }
}
