using UnityEngine;
using UnityEngine.UI;

namespace Seabreeze
{
    /// <summary>Continuous coastal ambience; architecture and menu text remain stationary.</summary>
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
            ownedMaterial = new Material(shader) { name = "Title coastal ambience · instance", hideFlags = HideFlags.DontSave };
            var sea = config != null ? config.sea : new Rect(.65f, .015f, .35f, .17f);
            ownedMaterial.SetVector("_SeaRect", new Vector4(sea.xMin, sea.yMin, sea.xMax, sea.yMax));
            ownedMaterial.SetFloat("_WaveStrength", .0022f);
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
