using System;
using System.Collections.Generic;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    [ExecuteAlways]
    public sealed class CharacterCosmeticsController : MonoBehaviour
    {
        public CharacterAppearanceController appearance;
        public Transform skeletonRoot;
        public CosmeticCatalog catalog;
        [Header("Clothing and armor")]
        public AppearanceItemDefinition headwear, torso, legs, feet, hands, outerArmor;
        readonly Dictionary<AppearanceSlot, AppearanceSpawnedItem> spawned = new Dictionary<AppearanceSlot, AppearanceSpawnedItem>();
        readonly Dictionary<Renderer, bool> hiddenByAppearance = new Dictionary<Renderer, bool>();
        static readonly int BaseColor = Shader.PropertyToID("baseColorFactor");
        static readonly int DiffuseColor = Shader.PropertyToID("diffuseFactor");
        static readonly int UnityBaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int UnityColor = Shader.PropertyToID("_Color");

        void OnEnable() { if (!appearance) appearance = GetComponent<CharacterAppearanceController>(); Refresh(); }
        void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => { if (this && isActiveAndEnabled) Refresh(); };
#endif
        }
        public void Refresh()
        {
            if (!appearance || !skeletonRoot) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(gameObject)) return;
#endif
            spawned.Clear();
            foreach (var marker in GetComponentsInChildren<AppearanceSpawnedItem>(true)) spawned[marker.slot] = marker;
            var data = appearance.Data;
            Equip(AppearanceSlot.Hair, headwear && headwear.hideHair ? null : catalog ? catalog.ForHair(data.hairStyle, data.hairVariant) : null);
            Equip(AppearanceSlot.Beard, catalog ? catalog.ForBeard(data.beardStyle, data.beardVariant) : null);
            Equip(AppearanceSlot.Headwear, headwear); Equip(AppearanceSlot.Torso, outerArmor && outerArmor.coversOutfit ? null : torso);
            Equip(AppearanceSlot.Legs, outerArmor && outerArmor.coversOutfit ? null : legs); Equip(AppearanceSlot.Feet, feet);
            Equip(AppearanceSlot.Hands, hands); Equip(AppearanceSlot.OuterArmor, outerArmor);
            Tint(AppearanceSlot.Hair, data.hairColor);
            Tint(AppearanceSlot.Beard, data.linkBeardColor ? data.hairColor : data.beardColor);
            ApplyClothingMorphs(data);
            UpdateBodyVisibility();
        }
        void Equip(AppearanceSlot slot, AppearanceItemDefinition definition)
        {
            var currentItems = new List<AppearanceSpawnedItem>();
            foreach (var existing in GetComponentsInChildren<AppearanceSpawnedItem>(true))
                if (existing.slot == slot) currentItems.Add(existing);
            if (definition && currentItems.Count > 0 &&
                currentItems.TrueForAll(existing => existing && existing.definition == definition))
            {
                // A clothing prefab can contain duplicate bone names. Repair old
                // rigid accessories that were attached to one of those copies.
                if (!definition.skinned)
                {
                    string boneName = definition.attachBoneName;
                    if (string.IsNullOrEmpty(boneName) && (slot == AppearanceSlot.Hair || slot == AppearanceSlot.Beard || slot == AppearanceSlot.Headwear))
                        boneName = "CC_Base_Head_038";
                    Transform correctBone = boneName == "$CharacterRoot" ? transform : FindBone(boneName);
                    if (correctBone)
                        foreach (var existing in currentItems)
                            if (existing.transform.parent != correctBone)
                                existing.transform.SetParent(correctBone, true);
                }
                spawned[slot] = currentItems[0]; return;
            }
            foreach (var current in currentItems) if (current) DestroyObject(current.gameObject);
            spawned.Remove(slot);
            if (!definition || !definition.prefab) return;
            if (definition.slot != slot) { Debug.LogWarning(definition.name + " belongs to " + definition.slot + ", not " + slot, this); return; }
            string attachName = definition.attachBoneName;
            if (string.IsNullOrEmpty(attachName) && (slot == AppearanceSlot.Hair || slot == AppearanceSlot.Beard || slot == AppearanceSlot.Headwear))
                attachName = "CC_Base_Head_038";
            Transform bone = definition.skinned ? skeletonRoot :
                attachName == "$CharacterRoot" ? transform : FindBone(attachName);
            if (!bone) { Debug.LogWarning("Missing attach bone " + attachName + " for " + definition.name, this); return; }
            GameObject instance;
#if UNITY_EDITOR
            if (!Application.isPlaying) instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(definition.prefab);
            else
#endif
                instance = Instantiate(definition.prefab);
            instance.name = "Appearance " + slot + " - " + definition.name;
            instance.transform.SetParent(definition.skinned ? transform : bone, false);
            // Prefab-local position, rotation, and scale are the authored fit.
            if (definition.skinned && !BindSkin(instance, definition)) { DestroyObject(instance); return; }
            if (definition.skinned && (definition.requiredMorphs == null || definition.requiredMorphs.Length == 0))
                Debug.LogWarning(definition.name + " has no declared fit morphs; validate it at skinny, normal, fat, and muscular extremes.", this);
            var marker = instance.AddComponent<AppearanceSpawnedItem>(); marker.slot = slot; marker.definition = definition;
            spawned[slot] = marker;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(instance, "Equip appearance item");
#endif
        }
        bool BindSkin(GameObject instance, AppearanceItemDefinition definition)
        {
            var bones = skeletonRoot.GetComponentsInChildren<Transform>(true);
            var lookup = new Dictionary<string, Transform>(bones.Length);
            foreach (var bone in bones) if (!lookup.ContainsKey(bone.name)) lookup.Add(bone.name, bone);
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mapped = new Transform[renderer.bones.Length];
                for (int i = 0; i < mapped.Length; i++)
                {
                    if (!renderer.bones[i] || !lookup.TryGetValue(renderer.bones[i].name, out mapped[i]))
                    { Debug.LogWarning("Clothing bone mismatch in " + definition.name, this); return false; }
                }
                renderer.bones = mapped;
                if (renderer.rootBone && lookup.TryGetValue(renderer.rootBone.name, out var root)) renderer.rootBone = root;
                if (renderer.sharedMesh && definition.requiredMorphs != null)
                    foreach (string morph in definition.requiredMorphs)
                        if (renderer.sharedMesh.GetBlendShapeIndex(morph) < 0)
                            Debug.LogWarning(definition.name + " lacks clothing morph " + morph + "; fit is unverified.", this);
            }
            return true;
        }
        Transform FindBone(string boneName)
        {
            if (boneName == "CC_Base_Head_038")
            {
                var animator = skeletonRoot.GetComponent<Animator>();
                if (!animator) animator = skeletonRoot.GetComponentInParent<Animator>();
                if (animator && animator.isHuman)
                {
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    if (head) return head;
                }
            }
            foreach (var bone in skeletonRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == boneName && !bone.GetComponentInParent<AppearanceSpawnedItem>()) return bone;
            return null;
        }
        void Tint(AppearanceSlot slot, Color color)
        {
            if (!spawned.TryGetValue(slot, out var item) || !item) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, color); block.SetColor(DiffuseColor, color);
                block.SetColor(UnityBaseColor, color); block.SetColor(UnityColor, color);
                renderer.SetPropertyBlock(block);
            }
        }
        void UpdateBodyVisibility()
        {
            var hidden = new HashSet<string>();
            foreach (var item in spawned.Values)
                if (item && item.definition && item.definition.hideRendererNames != null)
                    foreach (string name in item.definition.hideRendererNames) hidden.Add(name);
            foreach (var renderer in skeletonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<AppearanceSpawnedItem>()) continue;
                if (hidden.Contains(renderer.name))
                {
                    if (!hiddenByAppearance.ContainsKey(renderer)) hiddenByAppearance[renderer] = renderer.enabled;
                    renderer.enabled = false;
                }
                else if (hiddenByAppearance.TryGetValue(renderer, out bool wasEnabled))
                { renderer.enabled = wasEnabled; hiddenByAppearance.Remove(renderer); }
            }
        }
        void ApplyClothingMorphs(CharacterAppearanceData data)
        {
            foreach (var item in spawned.Values)
            {
                if (!item || !item.definition || !item.definition.skinned) continue;
                foreach (var renderer in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.sharedMesh) continue;
                    SetPair(renderer, "WeightThin", "WeightHeavy", data.weight);
                    SetPair(renderer, "MuscularitySlim", "MuscularityMuscular", data.muscularity);
                    SetPair(renderer, "NoseNarrow", "NoseWide", data.noseWidth);
                    SetPair(renderer, "JawNarrow", "JawWide", data.jawWidth);
                    SetPair(renderer, "EarSmall", "EarLarge", data.earSize);
                    SetPair(renderer, "ForeheadLow", "ForeheadHigh", data.foreheadShape);
                }
            }
        }
        static void SetPair(SkinnedMeshRenderer renderer, string low, string high, float value)
        {
            int lo = renderer.sharedMesh.GetBlendShapeIndex(low), hi = renderer.sharedMesh.GetBlendShapeIndex(high);
            float signed = (Mathf.Clamp01(value) - .5f) * 2f;
            if (lo >= 0) renderer.SetBlendShapeWeight(lo, Mathf.Max(0f, -signed));
            if (hi >= 0) renderer.SetBlendShapeWeight(hi, Mathf.Max(0f, signed));
        }
        static void DestroyObject(GameObject obj)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEditor.Undo.DestroyObjectImmediate(obj); return; }
#endif
            Destroy(obj);
        }
    }
}
