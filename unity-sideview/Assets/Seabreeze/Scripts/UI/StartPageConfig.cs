using UnityEngine;

namespace Seabreeze
{
    [CreateAssetMenu(menuName = "Seabreeze/Start Page", fileName = "StartPage")]
    public sealed class StartPageConfig : ScriptableObject
    {
        public Texture2D background;
        [Range(0, 1)] public float focusX = .48f;
        [Range(1, 1.15f)] public float overscan = 1.04f;
        // Normalized source-image coordinates, bottom-left origin. Keep glints on open sea.
        public Rect sea = new Rect(.05f, .32f, .40f, .17f);
        public Rect sky = new Rect(.10f, .68f, .45f, .19f);
        public Color shade = new Color(.045f, .10f, .17f, .76f);
    }
}
