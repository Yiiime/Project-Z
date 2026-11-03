using UnityEngine;
namespace Seabreeze
{
    [CreateAssetMenu(menuName = "Seabreeze/Item")]
    public sealed class ItemData : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
    }
}
