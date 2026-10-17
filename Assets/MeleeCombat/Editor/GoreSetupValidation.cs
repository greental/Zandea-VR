using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;

public static class GoreSetupValidation
{
    [MenuItem("Tools/Melee Combat/Validate gore setup")]
    public static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MeleeCombat/CombinedStarvingBandit.prefab");
        if (!prefab) { Debug.LogError("Gore: CombinedStarvingBandit prefab is missing"); return; }
        var anatomy = prefab.GetComponent<DismemberableCharacter>();
        var blood = prefab.GetComponent<GoreImpactSystem>();
        var animator = prefab.GetComponentInChildren<Animator>();
        if (!anatomy || !blood)
            Debug.LogError("Gore: bandit needs DismemberableCharacter and GoreImpactSystem", prefab);
        int joints = 0;
        if (anatomy)
            foreach (SeverPoint point in System.Enum.GetValues(typeof(SeverPoint)))
            {
                if (anatomy.GetJointBone(point)) joints++;
                else Debug.LogError($"Gore: missing {point} joint on bandit", prefab);
            }
        int total = 0, readable = 0;
        foreach (var skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!skin.sharedMesh) continue;
            total++;
            if (skin.sharedMesh.isReadable) readable++;
            Debug.Log($"Gore rig mesh: {skin.name} / {skin.sharedMesh.name}, " +
                $"vertices={skin.sharedMesh.vertexCount}, bones={skin.bones.Length}, readable={skin.sharedMesh.isReadable}", skin);
        }
        if (readable == 0) Debug.LogError("Gore: no readable skinned meshes; bone mask severing cannot run", prefab);
        var sword = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/MeleeCombat/MocapTest/GeneratedVariants/PlayerPracticeLongBlade.prefab");
        var weapon = sword ? sword.GetComponent<PhysicalMeleeWeapon>() : null;
        if (!weapon || weapon.strikeType != PhysicalMeleeWeapon.StrikeType.Cutting)
            Debug.LogError("Gore: practice long blade is missing or not cutting", sword);
        Debug.Log($"Gore setup: {readable}/{total} skinned meshes readable; " +
            $"joints={joints}/11; humanoid={animator && animator.isHuman}; " +
            $"cutting sword={weapon && weapon.strikeType == PhysicalMeleeWeapon.StrikeType.Cutting}", prefab);
    }
}
