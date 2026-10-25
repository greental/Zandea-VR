using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    /// <summary>Extracts every generated combat weapon into one editable gameplay prefab.</summary>
    public static class CanonicalWeaponPrefabBuilder
    {
        const string Output = "Assets/MyAssets/prefabs/weapons";
        const string Variants = "Assets/MeleeCombat/MocapTest/GeneratedVariants";
        const string VersionKey = "KenshiVR.CanonicalWeaponPrefabs.v5";

        [InitializeOnLoadMethod]
        static void BuildOnce()
        {
            if (SessionState.GetBool(VersionKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                SessionState.SetBool(VersionKey, true);
                try { Build(); }
                catch (Exception error) { SessionState.SetBool(VersionKey, false); Debug.LogException(error); }
            };
        }

        [MenuItem("Zandea/Melee Combat/Build canonical editable weapon prefabs")]
        public static void Build()
        {
            Directory.CreateDirectory(Output);
            int created = 0, linked = 0;
            foreach (var file in Directory.GetFiles(Variants, "StarvingBandit_*.prefab").OrderBy(path => path))
            {
                string variantPath = file.Replace('\\', '/');
                string variantName = Path.GetFileNameWithoutExtension(file).Substring("StarvingBandit_".Length);
                if (variantName == "BoStaff_Unified") variantName = "Bo Staff";
                string canonicalPath = Output + "/" + FriendlyName(variantName) + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(variantPath);
                try
                {
                    var source = root.GetComponentsInChildren<PhysicalMeleeWeapon>(true).FirstOrDefault();
                    var generated = root.GetComponent<NpcGeneratedWeapon>();
                    if (!source && generated && generated.weaponPrefab) source = generated.weaponPrefab;
                    if (!source) continue;

                    var canonicalRoot = AssetDatabase.LoadAssetAtPath<GameObject>(canonicalPath);
                    var canonical = canonicalRoot ? canonicalRoot.GetComponent<PhysicalMeleeWeapon>() : null;
                    if (!canonical)
                    {
                        var copy = UnityEngine.Object.Instantiate(source.gameObject);
                        copy.name = FriendlyName(variantName);
                        var weapon = copy.GetComponent<PhysicalMeleeWeapon>();
                        weapon.ownerRoot = null;
                        weapon.npcGripTarget = null;
                        EnsureEditableGrips(weapon);
                        canonicalRoot = PrefabUtility.SaveAsPrefabAsset(copy, canonicalPath);
                        UnityEngine.Object.DestroyImmediate(copy);
                        created++;
                    }
                    canonical = NormalizeCanonical(canonicalPath);

                    // Generated weapons are the gameplay source of truth. This makes
                    // Inspector edits on the canonical prefab reach NPC drops and players.
                    if (!generated)
                    {
                        generated = root.AddComponent<NpcGeneratedWeapon>();
                        generated.bandit = root.GetComponent<StarvingBandit>();
                        generated.health = root.GetComponent<ScavengerNPC.ScavengerHealth>();
                        generated.inventory = root.GetComponent<NpcInventory>();
                        generated.startEquipped = true;
                        generated.acquireNearbyWeapons = true;
                    }
                    generated.weaponPrefab = canonical;
                    if (!generated.gripTarget) generated.gripTarget = source.npcGripTarget;
                    if (!generated.weaponPoseTarget) generated.weaponPoseTarget = source.npcGripTarget;
                    if (!generated.gripTarget)
                        throw new InvalidOperationException(variantName + " has no NPC grip target");

                    if (source.transform.IsChildOf(root.transform))
                        UnityEngine.Object.DestroyImmediate(source.gameObject);
                    PrefabUtility.SaveAsPrefabAsset(root, variantPath);
                    linked++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            // Imported armor-pack weapons are already standalone prefabs; copy them
            // into the same authoring folder so the complete catalog is discoverable.
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MeleeCombat/ImportedArmorWeapons" }))
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (!source || !source.GetComponent<PhysicalMeleeWeapon>()) continue;
                string target = Output + "/" + source.name + ".prefab";
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(target))
                { AssetDatabase.CopyAsset(sourcePath, target); created++; }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Output }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root && root.GetComponent<PhysicalMeleeWeapon>()) NormalizeCanonical(path);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Canonical weapon prefabs ready: {created} created, {linked} NPC variants linked, folder={Output}");
        }

        static string FriendlyName(string value)
        {
            if (value == "BoStaff") return "Bo Staff";
            return string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(value[i - 1]) ? " " + c : c.ToString()));
        }

        static void EnsureEditableGrips(PhysicalMeleeWeapon weapon)
        {
            if (!weapon.grip) weapon.grip = Marker(weapon.transform, "NPC Grip", Vector3.zero, Quaternion.identity);
            Vector3 primaryPosition = weapon.transform.InverseTransformPoint(weapon.grip.position);
            Quaternion primaryRotation = Quaternion.Inverse(weapon.transform.rotation) * weapon.grip.rotation;
            if (!weapon.rightGrip) weapon.rightGrip = Marker(weapon.transform, "Primary Grip Right",
                primaryPosition, primaryRotation);
            if (!weapon.leftGrip || weapon.leftGrip == weapon.rightGrip) weapon.leftGrip = Marker(weapon.transform, "Primary Grip Left",
                primaryPosition, primaryRotation);
            if (!IsTwoHanded(weapon.name))
            {
                RemoveSecondaryMarkers(weapon);
                return;
            }
            if (!weapon.secondaryGrip || weapon.secondaryGrip == weapon.leftGrip || weapon.secondaryGrip == weapon.rightGrip)
            {
                Vector3 position = primaryPosition;
                if (StaffAxisCalibration.TryMeasure(weapon.transform, out var positive, out var negative))
                {
                    var far = Vector3.Distance(position, positive) > Vector3.Distance(position, negative) ? positive : negative;
                    position = Vector3.Lerp(position, far, .3f);
                }
                else position += Vector3.up * .35f;
                weapon.secondaryGrip = Marker(weapon.transform, "Secondary Grip", position, primaryRotation);
            }
            if (!weapon.secondaryRightGrip) weapon.secondaryRightGrip = Marker(weapon.transform,
                "Secondary Grip Right", weapon.secondaryGrip.localPosition, weapon.secondaryGrip.localRotation);
            if (!weapon.secondaryLeftGrip) weapon.secondaryLeftGrip = Marker(weapon.transform,
                "Secondary Grip Left", weapon.secondaryGrip.localPosition, weapon.secondaryGrip.localRotation);
        }

        static bool IsTwoHanded(string value)
        {
            string label = value.Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            return label.Contains("bostaff") || label.Contains("sawcleaver") ||
                label.Contains("rusteddoubleaxe") || label.Contains("poleblade") ||
                label.Contains("longblade") || label.Contains("greatsword") ||
                label.Contains("heavycleaver") || label.Contains("scrapspear");
        }

        static void RemoveSecondaryMarkers(PhysicalMeleeWeapon weapon)
        {
            var markers = new[] { weapon.secondaryGrip, weapon.secondaryRightGrip, weapon.secondaryLeftGrip }
                .Where(marker => marker && marker != weapon.leftGrip && marker != weapon.rightGrip && marker != weapon.grip)
                .Distinct().ToArray();
            weapon.secondaryGrip = weapon.secondaryRightGrip = weapon.secondaryLeftGrip = null;
            foreach (var marker in markers) UnityEngine.Object.DestroyImmediate(marker.gameObject);
        }

        static PhysicalMeleeWeapon NormalizeCanonical(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var weapon = root.GetComponent<PhysicalMeleeWeapon>();
                if (!weapon) throw new InvalidOperationException(path + " has no PhysicalMeleeWeapon");
                EnsureEditableGrips(weapon);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved.GetComponent<PhysicalMeleeWeapon>();
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Transform Marker(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.localPosition = position;
            marker.localRotation = rotation;
            return marker;
        }
    }
}
