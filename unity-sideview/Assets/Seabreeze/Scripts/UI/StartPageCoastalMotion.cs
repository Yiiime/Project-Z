using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Source-image water motion, isolated from the painted land and all menu text.</summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class StartPageCoastalMotion : MonoBehaviour
    {
        public bool MotionEnabled = true;
        RawImage picture;
        Material ownedMaterial;
        float elapsed;

        public void Initialize(StartPageConfig config)
        {
            picture = GetComponent<RawImage>();
            var shader = Resources.Load<Shader>("UI/CoastalTitle");
            if (shader == null || !shader.isSupported) return;
            ownedMaterial = new Material(shader) { name = "Title water · instance", hideFlags = HideFlags.DontSave };
            var sea = config != null ? config.sea : new Rect(.65f, .015f, .35f, .17f);
            ownedMaterial.SetVector("_SeaRect", new Vector4(sea.xMin, sea.yMin, sea.xMax, sea.yMax));
            ownedMaterial.SetFloat("_WaveStrength", .00065f);
            picture.material = ownedMaterial;
        }

        void Update()
        {
            if (!MotionEnabled || ownedMaterial == null) return;
            elapsed += Time.unscaledDeltaTime;
            ownedMaterial.SetFloat("_CoastTime", elapsed);
        }

        void OnDestroy()
        {
            if (ownedMaterial == null) return;
            if (picture != null && picture.material == ownedMaterial) picture.material = null;
            Destroy(ownedMaterial);
        }
    }
}
