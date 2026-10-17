using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using VRKoombar.WeaponsStaging.Contracts;
using KenshiVR.MeleeCombat;

namespace ScavengerNPC
{
    // Attach to the player CharacterController root so NPC ray hits reach this receiver.
    public sealed class ScavengerPlayerHealth : MonoBehaviour, IDamageable
    {
        [Min(1)] public float maxHealth = 100;
        [Header("Headset damage display")]
        public Camera viewCamera;
        [Range(0, 1)] public float lowHealthEdgeOpacity = .68f;
        [Range(0, 1)] public float hitFlashOpacity = .42f;
        [Min(.1f)] public float hitFlashFadeSeconds = .8f;
        [Header("Blood shape")]
        [Range(.7f, 1.5f), Tooltip("Screen quad size relative to headset view. Smaller values pull the blood farther inward.")]
        public float screenScale = 1f;
        [Range(.1f, 1f), Tooltip("Where the blood starts, measured from the middle of the view.")]
        public float edgeStart = .3f;
        [Range(.05f, 1f)] public float edgeSoftness = .55f;
        [Range(0, 16)] public int dripCount = 9;
        [Range(0, 1)] public float dripLength = .38f;
        [Range(.01f, .2f)] public float dripWidth = .065f;
        public Color bloodColor = new Color(.48f, .025f, .02f);
        public UnityEvent onDamaged = new UnityEvent(), onDeath = new UnityEvent();
        public event System.Action onDirectHit;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0;
        ContinuousMoveProvider moveProvider;
        JumpProvider jumpProvider;
        bool restoreMove, restoreJump;
        Material vignetteMaterial;
        Mesh vignetteMesh;
        Texture2D vignetteTexture;
        MeshRenderer vignetteRenderer;
        float hitFlash;
        int shapeHash = int.MinValue;
        void Awake() { Restore(); }
        void Start() { CreateVignette(); }
        public void Restore()
        {
            Current = Mathf.Max(1, maxHealth);
            hitFlash = 0;
            if (moveProvider && restoreMove) moveProvider.enabled = true;
            if (jumpProvider && restoreJump) jumpProvider.enabled = true;
            restoreMove = restoreJump = false;
        }
        public void ApplyDamage(in DamagePacket packet)
        {
            if (Current <= 0 || packet.BaseDamage <= 0 || float.IsNaN(packet.BaseDamage)) return;
            float multiplier = 1f;
            if (packet.Source)
                foreach (var hand in GetComponentsInChildren<PlayerMartialHand>(true))
                {
                    if (!hand.TryDefend(packet.HitPointWorld, packet.Source.transform.position, out bool parry)) continue;
                    multiplier = parry ? .1f : .35f;
                    if (parry)
                    {
                        var martial = packet.Source.GetComponent<NpcMartialArts>();
                        if (martial) martial.ReceiveStrikeStagger(1f);
                    }
                    break;
                }
            Current = Mathf.Max(0, Current - packet.BaseDamage * multiplier *
                CharacterCombatMath.DamageTaken(GetComponent<CharacterAttributes>()));
            if (packet.PhysicalImpact == PhysicalImpactKind.Sharp && Current > 0)
            {
                var wounds = GetComponent<KenshiVR.Bandaging.PlayerWounds>();
                if (wounds) wounds.RegisterCut(packet.HitPointWorld, packet.BaseDamage * multiplier);
            }
            hitFlash = 1;
            onDirectHit?.Invoke();
            onDamaged.Invoke();
            if (Current <= 0)
            {
                moveProvider = GetComponentInChildren<ContinuousMoveProvider>(true);
                jumpProvider = GetComponentInChildren<JumpProvider>(true);
                restoreMove = moveProvider && moveProvider.enabled;
                restoreJump = jumpProvider && jumpProvider.enabled;
                if (moveProvider) moveProvider.enabled = false;
                if (jumpProvider) jumpProvider.enabled = false;
                onDeath.Invoke();
            }
        }
        public void ApplyBleeding(float damage)
        {
            if (Current <= 0 || damage <= 0f) return;
            Current = Mathf.Max(0f, Current - damage);
            onDamaged.Invoke();
            if (Current <= 0f)
            {
                moveProvider = GetComponentInChildren<ContinuousMoveProvider>(true);
                jumpProvider = GetComponentInChildren<JumpProvider>(true);
                restoreMove = moveProvider && moveProvider.enabled;
                restoreJump = jumpProvider && jumpProvider.enabled;
                if (moveProvider) moveProvider.enabled = false;
                if (jumpProvider) jumpProvider.enabled = false;
                onDeath.Invoke();
            }
        }
        void CreateVignette()
        {
            if (!viewCamera) viewCamera = GetComponentInChildren<Camera>(true);
            if (!viewCamera) return;
            var shader = Shader.Find("Sprites/Default");
            if (!shader) { Debug.LogWarning("Player damage vignette shader was not found.", this); return; }
            vignetteTexture = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            vignetteTexture.name = "Player blood edges";
            vignetteTexture.wrapMode = TextureWrapMode.Clamp;
            RefreshBloodTexture();
            vignetteMesh = new Mesh { name = "Player damage screen quad" };
            vignetteMesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
            vignetteMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            vignetteMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            vignetteMesh.RecalculateBounds();
            vignetteMaterial = new Material(shader) { name = "Player damage edges", mainTexture = vignetteTexture };
            var screen = new GameObject("Damage edges (headset)");
            screen.transform.SetParent(viewCamera.transform, false);
            screen.transform.localPosition = Vector3.forward * 1.2f;
            screen.AddComponent<MeshFilter>().sharedMesh = vignetteMesh;
            vignetteRenderer = screen.AddComponent<MeshRenderer>();
            vignetteRenderer.sharedMaterial = vignetteMaterial;
            vignetteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            vignetteRenderer.receiveShadows = false;
            vignetteRenderer.enabled = false;
        }
        void LateUpdate()
        {
            if (!vignetteRenderer || !viewCamera) return;
            RefreshBloodTexture();
            hitFlash = Mathf.MoveTowards(hitFlash, 0f, Time.deltaTime / Mathf.Max(.1f, hitFlashFadeSeconds));
            float missing = 1f - Mathf.Clamp01(Current / Mathf.Max(1f, maxHealth));
            float opacity = Mathf.Clamp01(missing * lowHealthEdgeOpacity + hitFlash * hitFlashOpacity);
            vignetteRenderer.enabled = opacity > .005f;
            if (!vignetteRenderer.enabled) return;
            vignetteMaterial.color = new Color(1, 1, 1, opacity);
            float height = 2.4f * Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad) * screenScale;
            vignetteRenderer.transform.localScale = new Vector3(height * viewCamera.aspect, height, 1f);
        }
        void RefreshBloodTexture()
        {
            if (!vignetteTexture) return;
            int hash = edgeStart.GetHashCode();
            unchecked
            {
                hash = hash * 31 + edgeSoftness.GetHashCode();
                hash = hash * 31 + dripCount;
                hash = hash * 31 + dripLength.GetHashCode();
                hash = hash * 31 + dripWidth.GetHashCode();
                hash = hash * 31 + bloodColor.GetHashCode();
            }
            if (hash == shapeHash) return;
            shapeHash = hash;
            var pixels = new Color32[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float horizontal = (x + .5f) / 128f - 1f;
                    float vertical = (y + .5f) / 128f - 1f;
                    float radius = Mathf.Sqrt(horizontal * horizontal + vertical * vertical);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edgeStart, edgeStart + edgeSoftness, radius));
                    float drips = 0f;
                    for (int i = 0; i < dripCount; i++)
                    {
                        float center = Mathf.Repeat(Mathf.Sin(i * 78.233f + 12.345f) * 43758.5453f, 1f) * 1.8f - .9f;
                        float length = dripLength * (.5f + Mathf.Repeat(Mathf.Sin(i * 37.719f + 4.123f) * 17231.37f, 1f) * .5f);
                        float width = dripWidth * (.6f + Mathf.Repeat(Mathf.Sin(i * 15.013f + 7.7f) * 9271.71f, 1f) * .8f);
                        float across = 1f - Mathf.SmoothStep(.35f, 1f, Mathf.Abs(horizontal - center) / width);
                        float down = Mathf.SmoothStep(0f, .3f, Mathf.InverseLerp(1f - length, 1f, vertical));
                        drips = Mathf.Max(drips, across * down);
                    }
                    float alpha = Mathf.Clamp01(Mathf.Max(edge, drips));
                    pixels[y * 256 + x] = new Color(bloodColor.r, bloodColor.g, bloodColor.b, alpha);
                }
            vignetteTexture.SetPixels32(pixels);
            vignetteTexture.Apply(false, false);
        }
        void OnDestroy()
        {
            if (vignetteMaterial) Destroy(vignetteMaterial);
            if (vignetteMesh) Destroy(vignetteMesh);
            if (vignetteTexture) Destroy(vignetteTexture);
        }
        public ShipCombatState GetCombatState() => Current <= 0 ? ShipCombatState.Destroyed : ShipCombatState.CombatReady;
    }
}
