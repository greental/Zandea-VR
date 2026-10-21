using System;
using System.Collections.Generic;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    public enum AppearanceProperty { Height, Weight, Muscularity, NoseWidth, NoseTip, JawWidth, EarSize, ForeheadShape, EyeSize, EyeSpacing,
        EyebrowHeight, EyebrowThickness, EyebrowAngle, EyebrowSpacing, EyeShape, EyeTilt, MouthWidth, LipFullness, SkinColor, EyeColor }

    [ExecuteAlways]
    public sealed class CharacterAppearanceController : MonoBehaviour
    {
        [SerializeField] CharacterAppearanceData appearance = new CharacterAppearanceData();
        [SerializeField, HideInInspector] int appearanceVersion;
        [Tooltip("Visual model root only. Never assign the XR Origin here.")]
        public Transform visualRoot;
        [Tooltip("Optional gameplay capsule; assign only when its height should follow this character.")]
        public CharacterController characterController;
        [Tooltip("Optional VR body. Height changes its physical player height, not HMD tracking calibration.")]
        public JunkerBody.VRBodyController vrBody;
        [Tooltip("Blend shape names on future imported meshes. Neutral is slider midpoint.")]
        public MorphBinding[] morphBindings = {
            new MorphBinding(AppearanceProperty.Weight, "WeightThin", "WeightHeavy"),
            new MorphBinding(AppearanceProperty.Muscularity, "MuscularitySlim", "MuscularityMuscular"),
            new MorphBinding(AppearanceProperty.NoseWidth, "NoseNarrow", "NoseWide"),
            new MorphBinding(AppearanceProperty.NoseTip, "NoseTipDown", "NoseTipUp"),
            new MorphBinding(AppearanceProperty.JawWidth, "JawNarrow", "JawWide"),
            new MorphBinding(AppearanceProperty.EarSize, "EarSmall", "EarLarge"),
            new MorphBinding(AppearanceProperty.ForeheadShape, "ForeheadLow", "ForeheadHigh"),
            new MorphBinding(AppearanceProperty.EyeSize, "EyeSizeSmall", "EyeSizeLarge"),
            new MorphBinding(AppearanceProperty.EyeSpacing, "EyeSpacingClose", "EyeSpacingWide"),
            new MorphBinding(AppearanceProperty.EyebrowHeight, "BrowLow", "BrowHigh"),
            new MorphBinding(AppearanceProperty.EyebrowThickness, "BrowThin", "BrowThick"),
            new MorphBinding(AppearanceProperty.EyebrowAngle, "BrowAngry", "BrowRaised"),
            new MorphBinding(AppearanceProperty.EyebrowSpacing, "BrowClose", "BrowWide"),
            new MorphBinding(AppearanceProperty.EyeShape, "EyeNarrow", "EyeRound"),
            new MorphBinding(AppearanceProperty.EyeTilt, "EyeTiltDown", "EyeTiltUp"),
            new MorphBinding(AppearanceProperty.MouthWidth, "MouthNarrow", "MouthWide"),
            new MorphBinding(AppearanceProperty.LipFullness, "LipThin", "LipFull")
        };

        [Serializable] public sealed class MorphBinding
        {
            public AppearanceProperty property;
            public string lowName, highName;
            public MorphBinding(AppearanceProperty property, string lowName, string highName)
            { this.property = property; this.lowName = lowName; this.highName = highName; }
        }

        SkinnedMeshRenderer[] renderers;
        Renderer[] colorRenderers;
        [SerializeField, HideInInspector] Transform baselineVisualRoot;
        [SerializeField, HideInInspector] Vector3 baseScale;
        [SerializeField, HideInInspector] CharacterController baselineCapsule;
        [SerializeField, HideInInspector] float baseCapsuleHeight;
        [SerializeField, HideInInspector] Vector3 baseCapsuleCenter;
        [SerializeField, HideInInspector] JunkerBody.VRBodyController baselineVRBody;
        [SerializeField, HideInInspector] float baseVRHeight;
        readonly Dictionary<AppearanceProperty, bool> supported = new Dictionary<AppearanceProperty, bool>();
        readonly Dictionary<AppearanceProperty, List<(SkinnedMeshRenderer renderer, int low, int high)>> morphs = new Dictionary<AppearanceProperty, List<(SkinnedMeshRenderer, int, int)>>();
        bool bindingsChecked;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");
        static readonly int GltfDiffuse = Shader.PropertyToID("diffuseFactor");
        static readonly int GltfDiffuseTexture = Shader.PropertyToID("diffuseTexture");
        static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int GltfBaseColorTexture = Shader.PropertyToID("baseColorTexture");

        public CharacterAppearanceData Data => appearance.Copy();
        void Awake() { Preview(); CreatorPlayerHands.Ensure(this); }
        void OnEnable() { Preview(); }
        public void Preview()
        {
            if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(gameObject)) return;
#endif
            if (appearanceVersion < 2)
            {
                appearance.noseTip = appearance.eyebrowHeight = appearance.eyebrowThickness =
                    appearance.eyebrowAngle = appearance.eyebrowSpacing = appearance.eyeShape = appearance.eyeTilt =
                    appearance.mouthWidth = appearance.lipFullness = .5f;
                appearance.hairColor = appearance.beardColor = new Color(.16f, .12f, .09f);
                appearance.linkBeardColor = true;
                appearanceVersion = 2;
            }
            if (appearanceVersion < 3)
            {
                appearance.mouthWidth = appearance.lipFullness = .5f;
                appearanceVersion = 3;
            }
            Initialize();
            Apply(appearance);
        }
        public void Initialize()
        {
            EnsureMorphBindings();
            if (!visualRoot) visualRoot = transform;
            renderers = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            colorRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
#if UNITY_EDITOR
            RestoreImportedEyeMaterials();
#endif
            if (baselineVisualRoot != visualRoot) { baselineVisualRoot = visualRoot; baseScale = visualRoot.localScale; }
            if (characterController && baselineCapsule != characterController)
            { baselineCapsule = characterController; baseCapsuleHeight = characterController.height; baseCapsuleCenter = characterController.center; }
            if (vrBody && baselineVRBody != vrBody) { baselineVRBody = vrBody; baseVRHeight = vrBody.playerHeight; }
            supported.Clear(); morphs.Clear();
            supported[AppearanceProperty.Height] = true;
            supported[AppearanceProperty.SkinColor] = HasColorSlot(false);
            supported[AppearanceProperty.EyeColor] = HasColorSlot(true);
            foreach (var binding in morphBindings)
            {
                var matches = new List<(SkinnedMeshRenderer, int, int)>();
                foreach (var renderer in renderers)
                {
                    if (!renderer.sharedMesh) continue;
                    int low = renderer.sharedMesh.GetBlendShapeIndex(binding.lowName);
                    int high = renderer.sharedMesh.GetBlendShapeIndex(binding.highName);
                    if (low >= 0 || high >= 0) matches.Add((renderer, low, high));
                }
                morphs[binding.property] = matches;
                supported[binding.property] = matches.Count > 0;
            }
        }
        void EnsureMorphBindings()
        {
            if (bindingsChecked) return;
            bindingsChecked = true;
            var required = new[] {
                new MorphBinding(AppearanceProperty.NoseTip, "NoseTipDown", "NoseTipUp"),
                new MorphBinding(AppearanceProperty.EyebrowHeight, "BrowLow", "BrowHigh"),
                new MorphBinding(AppearanceProperty.EyebrowThickness, "BrowThin", "BrowThick"),
                new MorphBinding(AppearanceProperty.EyebrowAngle, "BrowAngry", "BrowRaised"),
                new MorphBinding(AppearanceProperty.EyebrowSpacing, "BrowClose", "BrowWide"),
                new MorphBinding(AppearanceProperty.EyeShape, "EyeNarrow", "EyeRound"),
                new MorphBinding(AppearanceProperty.EyeTilt, "EyeTiltDown", "EyeTiltUp"),
                new MorphBinding(AppearanceProperty.MouthWidth, "MouthNarrow", "MouthWide"),
                new MorphBinding(AppearanceProperty.LipFullness, "LipThin", "LipFull")
            };
            var list = new List<MorphBinding>(morphBindings ?? Array.Empty<MorphBinding>());
            foreach (var binding in required)
                if (!list.Exists(value => value != null && value.property == binding.property)) list.Add(binding);
            if (list.Count != (morphBindings == null ? 0 : morphBindings.Length)) morphBindings = list.ToArray();
        }
        bool HasColorSlot(bool eye)
        {
            foreach (var renderer in colorRenderers)
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    if (IsColorSlot(renderer, i, eye)) return true;
            return false;
        }
        static bool IsColorSlot(Renderer renderer, int slot, bool eye)
        {
            var materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length) return false;
            string material = materials[slot] ? materials[slot].name : "";
            string mesh = renderer is SkinnedMeshRenderer skin && skin.sharedMesh ? skin.sharedMesh.name : "";
            string node = renderer.name;
            if (eye)
                return material.IndexOf("Std_Eye_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.IndexOf("Std_Cornea_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mesh == "7" || mesh == "8" || mesh == "9" || mesh == "10" ||
                    node == "Object_16" || node == "Object_17" || node == "Object_18" || node == "Object_19";
            return material.IndexOf("Std_Skin_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mesh == "1" || mesh == "2" || mesh == "3" || mesh == "4" ||
                node == "Object_9" || node == "Object_10" || node == "Object_11" || node == "Object_12";
        }
        static bool IsEyeRenderer(Renderer renderer)
        {
            if (renderer.name == "Object_16" || renderer.name == "Object_17" ||
                renderer.name == "Object_18" || renderer.name == "Object_19") return true;
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
            {
                string name = skin.sharedMesh.name;
                return name.IndexOf("_Std_Eye_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("_Std_Cornea_", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }
        public bool IsSupported(AppearanceProperty property) => supported.TryGetValue(property, out bool value) && value;
        public void ResetBaseline()
        {
            appearance.height = 1f;
            baselineVisualRoot = null; baselineCapsule = null; baselineVRBody = null;
            Preview();
        }
        public void Apply(CharacterAppearanceData data)
        {
            appearance = data.Copy();
            float height = Mathf.Clamp(appearance.height, .8f, 1.2f);
            if (visualRoot) visualRoot.localScale = baseScale * height;
            if (characterController)
            {
                characterController.height = baseCapsuleHeight * height;
                characterController.center = baseCapsuleCenter * height;
            }
            if (vrBody) vrBody.playerHeight = baseVRHeight * height;
            ApplyMorph(AppearanceProperty.Weight, appearance.weight);
            ApplyMorph(AppearanceProperty.Muscularity, appearance.muscularity);
            ApplyMorph(AppearanceProperty.NoseWidth, appearance.noseWidth);
            ApplyMorph(AppearanceProperty.NoseTip, appearance.noseTip);
            ApplyMorph(AppearanceProperty.JawWidth, appearance.jawWidth);
            ApplyMorph(AppearanceProperty.EarSize, appearance.earSize);
            ApplyMorph(AppearanceProperty.ForeheadShape, appearance.foreheadShape);
            ApplyMorph(AppearanceProperty.EyeSize, appearance.eyeSize);
            ApplyMorph(AppearanceProperty.EyeSpacing, appearance.eyeSpacing);
            ApplyMorph(AppearanceProperty.EyebrowHeight, appearance.eyebrowHeight);
            ApplyMorph(AppearanceProperty.EyebrowThickness, appearance.eyebrowThickness);
            ApplyMorph(AppearanceProperty.EyebrowAngle, appearance.eyebrowAngle);
            ApplyMorph(AppearanceProperty.EyebrowSpacing, appearance.eyebrowSpacing);
            ApplyMorph(AppearanceProperty.EyeShape, appearance.eyeShape);
            ApplyMorph(AppearanceProperty.EyeTilt, appearance.eyeTilt);
            ApplyMorph(AppearanceProperty.MouthWidth, appearance.mouthWidth);
            ApplyMorph(AppearanceProperty.LipFullness, appearance.lipFullness);
            ApplyTeethFit();
            ApplyColor(false, appearance.skinColor);
            ApplyEyeColor(appearance.eyeColor);
            var cosmetics = GetComponent<CharacterCosmeticsController>();
            if (cosmetics) cosmetics.Refresh();
        }
        void ApplyMorph(AppearanceProperty property, float value)
        {
            if (!morphs.TryGetValue(property, out var targets)) return;
            // glTFast imports these glTF morphs as Unity frames at weight 1, not 100.
            float signed = (Mathf.Clamp01(value) - .5f) * 2f;
            foreach (var target in targets)
            {
                if (target.low >= 0) target.renderer.SetBlendShapeWeight(target.low, Mathf.Max(0f, -signed));
                if (target.high >= 0) target.renderer.SetBlendShapeWeight(target.high, Mathf.Max(0f, signed));
            }
        }
        void ApplyTeethFit()
        {
            float narrowJaw = Mathf.Max(0f, (.5f - appearance.jawWidth) * 2f);
            float thinFace = Mathf.Max(0f, (.5f - appearance.weight) * 2f);
            float slimFace = Mathf.Max(0f, (.5f - appearance.muscularity) * 2f);
            float fit = Mathf.Clamp01(narrowJaw * .72f + thinFace * .18f + slimFace * .1f);
            foreach (var renderer in renderers)
            {
                if (!renderer.sharedMesh) continue;
                int target = renderer.sharedMesh.GetBlendShapeIndex("TeethNarrow");
                if (target >= 0) renderer.SetBlendShapeWeight(target, fit);
            }
        }
        void ApplyColor(bool eye, Color color)
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in colorRenderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!IsColorSlot(renderer, i, eye)) continue;
                    renderer.GetPropertyBlock(block, i);
                    block.SetColor(BaseColor, color); block.SetColor(ColorId, color);
                    block.SetColor(GltfBaseColor, color); block.SetColor(GltfDiffuse, color);
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }
        void ApplyEyeColor(Color color)
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in colorRenderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!IsColorSlot(renderer, i, true)) continue;
                    bool right = materials[i] && materials[i].name.IndexOf("_R", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool cornea = materials[i] && materials[i].name.IndexOf("Cornea", StringComparison.OrdinalIgnoreCase) >= 0;
                    var irisTexture = IrisTexturePalette.Get(color, right, cornea);
                    if (!irisTexture) continue;
                    renderer.GetPropertyBlock(block, i);
                    block.SetColor(BaseColor, Color.white); block.SetColor(ColorId, Color.white);
                    block.SetColor(GltfBaseColor, Color.white); block.SetColor(GltfDiffuse, Color.white);
                    block.SetTexture(GltfDiffuseTexture, irisTexture);
                    // glTFast shader variants use different texture references.
                    block.SetTexture(GltfBaseColorTexture, irisTexture);
                    block.SetTexture(BaseMap, irisTexture);
                    block.SetTexture(MainTexture, irisTexture);
                    renderer.SetPropertyBlock(block, i);
                    if (materials.Length == 1) renderer.SetPropertyBlock(block);
                }
            }
        }
#if UNITY_EDITOR
        void RestoreImportedEyeMaterials()
        {
            GameObject imported = null;
            foreach (var renderer in colorRenderers)
            {
                if (!renderer || !IsEyeRenderer(renderer)) continue;
                var current = renderer.sharedMaterial;
                if (current && current.shader && current.shader.name != "Hidden/InternalErrorShader" &&
                    current.name.IndexOf("iris preview", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(renderer) as Renderer;
                if (!source || !source.sharedMaterial)
                {
                    if (!imported) imported = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/CharacterCreator/Models/male_creator.glb");
                    if (imported)
                        foreach (var candidate in imported.GetComponentsInChildren<Renderer>(true))
                            if (candidate.name == renderer.name && candidate.sharedMaterial)
                            { source = candidate; break; }
                }
                if (!source || !source.sharedMaterial) continue;
                UnityEditor.Undo.RecordObject(renderer, "Restore eye material");
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.SetPropertyBlock(null);
                UnityEditor.EditorUtility.SetDirty(renderer);
            }
        }
#endif
    }
}
