using System.Linq;
using System.IO;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(WeaponGripFittingStation))]
public sealed class WeaponGripFittingStationEditor : Editor
{
    string[] paths, names;
    void OnEnable()
    {
        paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MyAssets/prefabs/weapons" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponentInChildren<PhysicalMeleeWeapon>(true)).OrderBy(path => path).ToArray();
        names = paths.Select(System.IO.Path.GetFileNameWithoutExtension).ToArray();
        var station=(WeaponGripFittingStation)target;
        if(station && !station.weaponPreview) EditorApplication.delayCall+=()=>{if(station&&!station.weaponPreview)Build(station);};
    }
    public override void OnInspectorGUI()
    {
        var station = (WeaponGripFittingStation)target;
        int current = station.weaponPrefab ? System.Array.FindIndex(paths, path => AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponentInChildren<PhysicalMeleeWeapon>(true) == station.weaponPrefab) : -1;
        int chosen = EditorGUILayout.Popup("Weapon", Mathf.Max(0, current), names);
        if (paths.Length > 0 && (current < 0 || chosen != current))
        {
            Undo.RecordObject(station, "Choose fitting weapon");
            station.weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[chosen]).GetComponentInChildren<PhysicalMeleeWeapon>(true);
            Build(station);
        }
        EditorGUILayout.HelpBox("Move and rotate the Primary/Left/Right/Secondary grip marker children in the Scene view. The character hands preview one- or two-hand placement. Apply writes those marker transforms to the canonical prefab.", MessageType.Info);
        if (GUILayout.Button("Refresh character holding preview")) Build(station);
        using (new EditorGUI.DisabledScope(!station.weaponPreview))
            if (GUILayout.Button("Apply grip marker changes to weapon prefab"))
            {
                PrefabUtility.ApplyPrefabInstance(station.weaponPreview.transform.root.gameObject, InteractionMode.UserAction);
                Build(station);
            }
        PoseHands(station);
    }
    internal static void Build(WeaponGripFittingStation station)
    {
        if (station.characterPreview) Undo.DestroyObjectImmediate(station.characterPreview);
        if (station.weaponPreview) Undo.DestroyObjectImmediate(station.weaponPreview.transform.root.gameObject);
        var character = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CharacterCreator/Player/VR player - Man Creator.prefab");
        if (character)
        {
            station.characterPreview = (GameObject)PrefabUtility.InstantiatePrefab(character, station.gameObject.scene);
            station.characterPreview.name = "Grip fitting character preview";
            station.characterPreview.transform.SetPositionAndRotation(station.transform.position, station.transform.rotation);
            foreach (var behaviour in station.characterPreview.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        }
        if (!station.weaponPrefab) return;
        var assetRoot = station.weaponPrefab.transform.root.gameObject;
        station.weaponPreview = ((GameObject)PrefabUtility.InstantiatePrefab(assetRoot, station.gameObject.scene)).GetComponentInChildren<PhysicalMeleeWeapon>(true);
        station.weaponPreview.name = station.weaponPrefab.name + " - EDIT GRIPS";
        var animator = station.characterPreview ? station.characterPreview.GetComponentInChildren<Animator>(true) : null;
        var hand = animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        var primary = station.weaponPreview.rightGrip ? station.weaponPreview.rightGrip : station.weaponPreview.grip;
        if (hand && primary)
        {
            var root = station.weaponPreview.transform;
            root.rotation = hand.rotation * Quaternion.Inverse(primary.rotation) * root.rotation;
            root.position += hand.position - primary.position;
        }
        Selection.activeObject = station.weaponPreview.gameObject;
        PoseHands(station);
        EditorUtility.SetDirty(station);
    }
    static void PoseHands(WeaponGripFittingStation station)
    {
        if(!station||!station.characterPreview||!station.weaponPreview)return;
        var animator=station.characterPreview.GetComponentInChildren<Animator>(true);if(!animator||!animator.isHuman)return;
        var right=animator.GetBoneTransform(HumanBodyBones.RightHand);var left=animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var primary=station.weaponPreview.rightGrip?station.weaponPreview.rightGrip:station.weaponPreview.grip;
        var secondary=station.weaponPreview.secondaryRightGrip?station.weaponPreview.secondaryRightGrip:station.weaponPreview.secondaryGrip;
        if(right&&primary)right.SetPositionAndRotation(primary.position,primary.rotation);
        if(left&&secondary)left.SetPositionAndRotation(secondary.position,secondary.rotation);
        SceneView.RepaintAll();
    }
    [MenuItem("Tools/Melee Combat/Create weapon grip fitting station")]
    static void Create()
    {
        var station = new GameObject("Weapon Grip Fitting Station").AddComponent<WeaponGripFittingStation>();
        Undo.RegisterCreatedObjectUndo(station.gameObject, "Create weapon grip fitting station");
        Selection.activeObject = station.gameObject;
    }

    [MenuItem("Tools/Melee Combat/Open weapon grip fitting scene")]
    static void OpenScene() => EditorSceneManager.OpenScene(WeaponGripFittingSceneBuilder.ScenePath);
}

[InitializeOnLoad]
static class WeaponGripFittingSceneBuilder
{
    internal const string ScenePath = "Assets/MeleeCombat/WeaponGripFitting.unity";
    static WeaponGripFittingSceneBuilder() => EditorApplication.delayCall += EnsureScene;
    [MenuItem("Tools/Melee Combat/Rebuild weapon grip fitting scene")]
    static void EnsureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) && new FileInfo(ScenePath).Length > 5000) return;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name="Fitting floor"; floor.transform.localScale=new Vector3(2,1,2); SceneManager.MoveGameObjectToScene(floor,scene);
            var station=new GameObject("Weapon Grip Fitting Station").AddComponent<WeaponGripFittingStation>(); SceneManager.MoveGameObjectToScene(station.gameObject,scene);
            var first=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/MyAssets/prefabs/weapons"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault(go=>go&&go.GetComponentInChildren<PhysicalMeleeWeapon>(true));
            if(first) station.weaponPrefab=first.GetComponentInChildren<PhysicalMeleeWeapon>(true);
            WeaponGripFittingStationEditor.Build(station);
            var light=new GameObject("Fitting light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.2f; light.transform.rotation=Quaternion.Euler(45,-35,0); SceneManager.MoveGameObjectToScene(light.gameObject,scene);
            var camera=new GameObject("Fitting camera").AddComponent<Camera>(); camera.transform.SetPositionAndRotation(new Vector3(0,1.35f,-3f),Quaternion.Euler(5,0,0)); SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
        finally { EditorSceneManager.CloseScene(scene,true); }
        AssetDatabase.SaveAssets();
    }
}
