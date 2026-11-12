using UnityEngine;

namespace Seabreeze
{
    /// <summary>References the current island artwork without duplicating scene textures.</summary>
    [CreateAssetMenu(menuName = "Seabreeze/Ending Presentation", fileName = "EndingPresentation")]
    public sealed class EndingPresentationConfig : ScriptableObject
    {
        public Texture2D harbor, boardwalk, cafe, square, forest, seafront, summit;
    }
}
