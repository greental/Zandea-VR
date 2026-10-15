using UnityEditor;
using UnityEngine;

namespace JunkerEquipment.Editor
{
    [InitializeOnLoad]
    public static class BinocularOpticsSetup
    {
        static BinocularOpticsSetup()
        {
            EditorApplication.delayCall += ConfigureOnce;
        }

        static void ConfigureOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += ConfigureOnce; return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[0]);
            var displayMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (prefab && prefab.GetComponentInChildren<JunkerBinoculars>(true) &&
                (!prefab.GetComponentInChildren<OpticalDevice>(true) || !prefab.GetComponentInChildren<BinocularScannerHUD>(true) ||
                 (displayMaterial && displayMaterial.shader.name != "JunkerEquipment/OpticalEyepiece"))) Configure();
        }

        static readonly string[] Prefabs = {
            "Assets/MyAssets/prefabs/Player/Junker.prefab",
            "Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab"
        };
        const string MaterialPath = "Assets/JunkerEquipment/Generated/BinocularOpticsDisplay.mat";

        [MenuItem("Tools/Junker Equipment/Configure binocular optics on player prefabs")]
        public static void Configure()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var shader = Shader.Find("JunkerEquipment/OpticalEyepiece");
            if (!shader) throw new System.InvalidOperationException("OpticalEyepiece shader has not imported yet.");
            if (!material)
            {
                if (!AssetDatabase.IsValidFolder("Assets/JunkerEquipment/Generated")) AssetDatabase.CreateFolder("Assets/JunkerEquipment", "Generated");
                material = new Material(shader) { name = "BinocularOpticsDisplay" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }
            foreach (string path in Prefabs)
            {
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var binoculars in root.GetComponentsInChildren<JunkerBinoculars>(true))
                    {
                        var parent = binoculars.transform;
                        var optics = binoculars.GetComponent<OpticalDevice>();
                        if (!optics) optics = binoculars.gameObject.AddComponent<OpticalDevice>();
                        optics.eye = binoculars.hmd ? binoculars.hmd.transform : null;
                        optics.eyepiece = binoculars.eyepiece;
                        optics.objective = binoculars.viewingDirection;
                        optics.requiredGrab = binoculars.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                        optics.immersiveViewing = true;
                        optics.stabiliseView = true;
                        optics.viewingShader = shader;
                        optics.flipPhysicalLensHorizontally = true;
                        optics.viewingFocusDistance = 2f;
                        optics.activationDistance = binoculars.faceDistance;
                        optics.activationAngle = binoculars.alignmentAngle;
                        binoculars.opticalDevice = optics;
                        if (!binoculars.GetComponent<BinocularScannerHUD>()) binoculars.gameObject.AddComponent<BinocularScannerHUD>();
                        var cameraTransform = parent.Find("Optics Camera");
                        if (!cameraTransform) { cameraTransform = new GameObject("Optics Camera").transform; cameraTransform.SetParent(parent, false); }
                        cameraTransform.localPosition = binoculars.viewingDirection ? binoculars.viewingDirection.localPosition : new Vector3(0, 0, .13f);
                        cameraTransform.localRotation = binoculars.viewingDirection ? binoculars.viewingDirection.localRotation : Quaternion.identity;
                        var camera = cameraTransform.GetComponent<Camera>();
                        if (!camera) camera = cameraTransform.gameObject.AddComponent<Camera>();
                        camera.enabled = false;
                        camera.depth = -10;
                        camera.clearFlags = CameraClearFlags.Skybox;
                        optics.opticsCamera = camera;
                        var display = parent.Find("Optical Eyepiece Display");
                        if (!display)
                        {
                            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                            quad.name = "Optical Eyepiece Display";
                            Object.DestroyImmediate(quad.GetComponent<Collider>());
                            display = quad.transform;
                            display.SetParent(parent, false);
                        }
                        display.localPosition = binoculars.eyepiece ? binoculars.eyepiece.localPosition + new Vector3(0, 0, -.003f) : new Vector3(0, 0, -.078f);
                        display.localRotation = Quaternion.Euler(0, 180, 0);
                        display.localScale = new Vector3(.105f, .105f, 1f);
                        var renderer = display.GetComponent<MeshRenderer>();
                        renderer.sharedMaterial = material;
                        renderer.enabled = false;
                        optics.opticalDisplayRenderer = renderer;
                        optics.displayMaterialSlot = 0;
                        optics.hideDedicatedDisplayWhenInactive = true;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Configured binocular optics on available player prefabs.");
        }
    }
}
