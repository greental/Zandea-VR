using UnityEngine;

namespace Zandea.Opening
{
    // Camera-child geometry renders for both XR eyes. It never moves the camera or origin.
    public sealed class KnockoutPresentation : MonoBehaviour
    {
        public PlayerKnockout knockout;
        public Camera viewCamera;
        public AudioClip impactClip, ringingClip;
        [Range(100f, 22000f)] public float muffledCutoff = 850f;
        [Range(0f, 1f)] public float blackoutOpacity = 1f;
        [Min(.01f)] public float fadeOutSeconds = .4f, fadeInSeconds = .6f;
        MeshRenderer overlay;
        Material material;
        Mesh mesh;
        AudioLowPassFilter lowPass;
        AudioSource source;
        float alpha, target;

        void Awake()
        {
            if (!knockout) knockout = GetComponentInParent<PlayerKnockout>();
            if (!viewCamera) viewCamera = GetComponentInChildren<Camera>(true);
            if (!viewCamera && knockout) viewCamera = knockout.GetComponentInChildren<Camera>(true);
        }

        void OnEnable()
        {
            if (knockout) knockout.StateChanged += OnStateChanged;
            CreateOverlay();
        }

        void OnDisable()
        {
            if (knockout) knockout.StateChanged -= OnStateChanged;
            target = alpha = 0f;
            if (lowPass) lowPass.enabled = false;
            if (overlay) overlay.enabled = false;
            if (source) source.Stop();
        }

        void OnDestroy()
        {
            if (overlay) Destroy(overlay.gameObject);
            if (material) Destroy(material);
            if (mesh) Destroy(mesh);
        }

        void OnStateChanged(PlayerConsciousness previous, PlayerConsciousness next)
        {
            if (next == PlayerConsciousness.KnockoutStarting)
            {
                target = blackoutOpacity;
                if (impactClip) Play(impactClip);
                if (ringingClip) Play(ringingClip);
                if (viewCamera && !lowPass) lowPass = viewCamera.gameObject.AddComponent<AudioLowPassFilter>();
                if (lowPass) { lowPass.cutoffFrequency = muffledCutoff; lowPass.enabled = true; }
            }
            else if (next == PlayerConsciousness.Recovering || next == PlayerConsciousness.Conscious)
            {
                target = 0f;
                if (lowPass) lowPass.enabled = false;
            }
        }

        void Play(AudioClip clip)
        {
            if (!source) { source = gameObject.AddComponent<AudioSource>(); source.spatialBlend = 0f; }
            source.PlayOneShot(clip);
        }

        void LateUpdate()
        {
            float seconds = target > alpha ? fadeOutSeconds : fadeInSeconds;
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / Mathf.Max(.01f, seconds));
            if (!overlay || !material) return;
            overlay.enabled = alpha > .001f;
            material.color = new Color(0f, 0f, 0f, alpha);
        }

        void CreateOverlay()
        {
            if (overlay || !viewCamera) return;
            var shader = Shader.Find("Sprites/Default");
            if (!shader) { Debug.LogWarning("XR blackout shader unavailable", this); return; }
            material = new Material(shader) { name = "XR knockout blackout", renderQueue = 5000 };
            mesh = new Mesh { name = "XR blackout quad" };
            mesh.vertices = new[] { new Vector3(-1f,-1f), new Vector3(1f,-1f), new Vector3(1f,1f), new Vector3(-1f,1f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            var quad = new GameObject("Knockout blackout (both eyes)");
            quad.transform.SetParent(viewCamera.transform, false);
            quad.transform.localPosition = Vector3.forward * .5f;
            quad.transform.localScale = Vector3.one * 4f;
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            overlay = quad.AddComponent<MeshRenderer>();
            overlay.sharedMaterial = material;
            overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.enabled = false;
        }
    }
}
