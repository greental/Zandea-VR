using System;
using TMPro;
using UnityEngine;

namespace Zandea.Opening
{
    // Head-relative, stationary stereo geometry. No artificial camera motion.
    public sealed class OpeningTitlePresentation : MonoBehaviour
    {
        public Camera viewCamera;
        public string title = "WELCOME TO ZANDEA";
        public TMP_FontAsset font;
        public AudioClip titleSound;
        [Min(0f)] public float fadeToBlackSeconds = .7f;
        [Min(0f)] public float titleHoldSeconds = 2.5f;
        [Min(0f)] public float fadeTitleSeconds = .5f;
        MeshRenderer backdrop;
        Material material;
        Mesh mesh;
        TextMeshPro titleText;
        AudioSource sound;
        Action finished;
        float opacity, target, titleUntil, titleFadeStart;
        bool titleRunning, titleShown;

        void Awake()
        {
            if (!viewCamera) viewCamera = GetComponentInChildren<Camera>(true);
            CreateVisuals();
        }

        public void BeginAwakening(float seconds)
        {
            opacity = target = 1f;
            if (material) material.color = Color.black;
            if (backdrop) backdrop.enabled = true;
            target = 0f;
            awakeningFade = Mathf.Max(.01f, seconds);
        }

        float awakeningFade = 2f;

        public bool ShowTitle(Action onFinished)
        {
            if (titleShown) return false;
            titleShown = true;
            titleRunning = true;
            finished = onFinished;
            target = 1f;
            titleUntil = Time.unscaledTime + fadeToBlackSeconds + titleHoldSeconds + fadeTitleSeconds;
            titleFadeStart = Time.unscaledTime + fadeToBlackSeconds + titleHoldSeconds;
            if (titleSound)
            {
                if (!sound) { sound = gameObject.AddComponent<AudioSource>(); sound.spatialBlend = 0f; }
                sound.PlayOneShot(titleSound);
            }
            return true;
        }

        void LateUpdate()
        {
            float rate = target > opacity ? 1f / Mathf.Max(.01f, fadeToBlackSeconds) : 1f / awakeningFade;
            opacity = Mathf.MoveTowards(opacity, target, Time.unscaledDeltaTime * rate);
            if (material) material.color = new Color(0f, 0f, 0f, opacity);
            if (backdrop) backdrop.enabled = opacity > .001f;
            if (titleRunning && titleText)
            {
                float textOpacity = Time.unscaledTime < titleFadeStart ?
                    Mathf.InverseLerp(0f, 1f, opacity) :
                    1f - Mathf.Clamp01((Time.unscaledTime - titleFadeStart) / Mathf.Max(.01f, fadeTitleSeconds));
                titleText.color = new Color(1f, 1f, 1f, textOpacity);
                titleText.enabled = opacity > .999f && textOpacity > .001f;
            }
            if (titleRunning && Time.unscaledTime >= titleUntil)
            {
                titleRunning = false;
                if (titleText) titleText.enabled = false;
                var callback = finished;
                finished = null;
                callback?.Invoke();
            }
        }

        void CreateVisuals()
        {
            if (!viewCamera || backdrop) return;
            var shader = Shader.Find("Sprites/Default");
            if (!shader) { Debug.LogError("Opening blackout shader missing", this); return; }
            material = new Material(shader) { name = "Opening black", renderQueue = 5000 };
            mesh = new Mesh { name = "Opening stereo backdrop" };
            mesh.vertices = new[] { new Vector3(-1f,-1f), new Vector3(1f,-1f), new Vector3(1f,1f), new Vector3(-1f,1f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            var quad = new GameObject("Opening black (both eyes)");
            quad.transform.SetParent(viewCamera.transform, false);
            quad.transform.localPosition = Vector3.forward * .55f;
            quad.transform.localScale = Vector3.one * 5f;
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            backdrop = quad.AddComponent<MeshRenderer>();
            backdrop.sharedMaterial = material;
            backdrop.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            backdrop.receiveShadows = false;
            backdrop.enabled = false;
            var textObject = new GameObject("Opening title");
            textObject.transform.SetParent(viewCamera.transform, false);
            textObject.transform.localPosition = Vector3.forward * .45f;
            titleText = textObject.AddComponent<TextMeshPro>();
            titleText.text = title;
            if (font) titleText.font = font;
            titleText.fontSize = 0.09f;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.rectTransform.sizeDelta = new Vector2(1.2f, .2f);
            titleText.enabled = false;
        }

        void OnDisable()
        {
            finished = null;
            titleRunning = false;
            if (sound) sound.Stop();
            if (backdrop) backdrop.enabled = false;
            if (titleText) titleText.enabled = false;
        }

        void OnDestroy()
        {
            if (material) Destroy(material);
            if (mesh) Destroy(mesh);
        }
    }
}
