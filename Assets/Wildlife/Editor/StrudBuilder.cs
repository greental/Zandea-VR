using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zandea.Wildlife;

[InitializeOnLoad]
public static class StrudBuilder
{
    public const string Folder = "Assets/Wildlife/Strud";
    const string ModelPath = Folder + "/Art/Strud_Rigged.fbx";
    const string Request = "Temp/StrudBuild.request";
    static StrudBuilder() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Build(); File.WriteAllText("Temp/StrudBuild.result", "PASS Strud prefab, controller and test scene built"); }
        catch (Exception e) { File.WriteAllText("Temp/StrudBuild.result", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/Zandea/Wildlife/Rebuild Strud")]
    public static void Build()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!source) throw new Exception("Run Tools/Wildlife/build_strud.py in Blender first");
        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name);
        foreach (var name in new[] { "Idle", "Walk", "Trot", "Turn", "Graze", "Alert", "Hit", "Death" })
            if (!clips.ContainsKey(name)) throw new Exception("Missing clip " + name + "; imported: " + string.Join(",", clips.Keys));
        string controllerPath = Folder + "/Strud.controller";
        var old = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        var controller = old ? old : AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        if (old)
        {
            var existing = controller.layers[0].stateMachine;
            foreach (var transition in existing.anyStateTransitions) existing.RemoveAnyStateTransition(transition);
            foreach (var state in existing.states) existing.RemoveState(state.state);
            while (controller.parameters.Length > 0) controller.RemoveParameter(0);
        }
        foreach (string p in new[] { "Speed", "Playback", "Turning" }) controller.AddParameter(p, AnimatorControllerParameterType.Float);
        foreach (string p in new[] { "Grazing", "Alert", "Dead" }) controller.AddParameter(p, AnimatorControllerParameterType.Bool);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        var sm = controller.layers[0].stateMachine;
        var states = clips.ToDictionary(c => c.Key, c => { var s = sm.AddState(c.Key); s.motion = c.Value; return s; });
        sm.defaultState = states["Idle"];
        foreach (string n in new[] { "Walk", "Trot" }) { states[n].speedParameter = "Playback"; states[n].speedParameterActive = true; }
        // Explicit states keep gait phase readable by the grounding solver.
        Transition(states["Idle"], states["Walk"], "Speed", AnimatorConditionMode.Greater, .05f);
        Transition(states["Walk"], states["Idle"], "Speed", AnimatorConditionMode.Less, .04f);
        Transition(states["Walk"], states["Trot"], "Speed", AnimatorConditionMode.Greater, 1.83f);
        Transition(states["Trot"], states["Walk"], "Speed", AnimatorConditionMode.Less, 1.72f);
        var turn = Transition(states["Idle"], states["Turn"], "Turning", AnimatorConditionMode.Greater, .12f);
        turn.AddCondition(AnimatorConditionMode.Less, .05f, "Speed");
        Transition(states["Turn"], states["Idle"], "Turning", AnimatorConditionMode.Less, .08f);
        Transition(states["Turn"], states["Walk"], "Speed", AnimatorConditionMode.Greater, .05f);
        foreach (string n in new[] { "Graze", "Alert" })
        {
            string parameter = n == "Graze" ? "Grazing" : n;
            var t = Transition(states["Idle"], states[n], parameter, AnimatorConditionMode.If, 0);
            t.AddCondition(AnimatorConditionMode.Less, .04f, "Speed");
            Transition(states[n], states["Idle"], parameter, AnimatorConditionMode.IfNot, 0);
        }
        var death = sm.AddAnyStateTransition(states["Death"]); death.hasExitTime = false; death.duration = .15f; death.canTransitionToSelf = false; death.AddCondition(AnimatorConditionMode.If, 0, "Dead");
        var hit = sm.AddAnyStateTransition(states["Hit"]); hit.hasExitTime = false; hit.duration = .08f; hit.canTransitionToSelf = false; hit.AddCondition(AnimatorConditionMode.If, 0, "Hit"); hit.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
        var recover = states["Hit"].AddTransition(states["Idle"]); recover.hasExitTime = true; recover.exitTime = .9f; recover.duration = .15f;
        AssetDatabase.SaveAssets();
        var profile = AssetDatabase.LoadAssetAtPath<CreatureProfile>(Folder + "/StrudProfile.asset");
        if (!profile) { profile = ScriptableObject.CreateInstance<CreatureProfile>(); AssetDatabase.CreateAsset(profile, Folder + "/StrudProfile.asset"); }
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Art/Strud.mat");
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, Folder + "/Art/Strud.mat"); }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Art/Strud_BaseColor.png")); material.SetFloat("_Smoothness", .18f);
        var root = new GameObject("Strud");
        try
        {
            var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source); model.transform.SetParent(visual.transform, false);
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (!skin || skin.bones.Length < 20) throw new Exception("Strud has no articulated skin");
            skin.sharedMaterial = material; skin.updateWhenOffscreen = true;
            var animator = model.GetComponent<Animator>(); if (!animator) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            var motor = root.AddComponent<QuadrupedMotor>(); motor.profile = profile; motor.animator = animator; motor.visual = visual.transform;
            var grounding = root.AddComponent<QuadrupedGrounding>(); grounding.motor = motor;
            Transform Find(string n) => model.GetComponentsInChildren<Transform>().First(t => t.name == n);
            grounding.legs = new[] {
                Leg(Find, "FrontL", .25f, .5f), Leg(Find,"FrontR",.75f,0),
                Leg(Find,"HindL",0,0), Leg(Find,"HindR",.5f,.5f) };
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var collider = root.AddComponent<CapsuleCollider>(); collider.direction = 2; collider.center = new Vector3(0, 1.7f, .15f); collider.radius = .68f; collider.height = 3.2f;
            AddHitCollider(Find("Head"), .3f);
            foreach (var leg in grounding.legs) AddHitCollider(leg.upper, .27f);
            // Mount/cargo anchors follow animated bones. Access points remain stable on the motor root.
            Anchor(Find("Spine01"), "SeatAnchor", root.transform.TransformPoint(new Vector3(0, 2.65f, .1f)), root.transform.rotation);
            Anchor(Find("Head"), "ReinsAnchor", root.transform.TransformPoint(new Vector3(0, 3.25f, 2.15f)), root.transform.rotation);
            foreach (int side in new[] { -1, 1 })
            {
                string label = side < 0 ? "Left" : "Right";
                Anchor(root.transform, "Mount" + label + "Point", new Vector3(side * 1.3f, 0, .1f), Quaternion.identity);
                Anchor(root.transform, "Dismount" + label + "Point", new Vector3(side * 1.8f, 0, .1f), Quaternion.identity);
                Anchor(Find("Spine01"), "CargoAnchor_" + label, new Vector3(side * .8f, 2, -.2f), Quaternion.identity);
            }
            Anchor(Find("Pelvis"), "CargoAnchor_Back", new Vector3(0, 2.4f, -.8f), Quaternion.identity);
            // Optional components are added by type so the art build remains usable independently.
            var brainType = typeof(QuadrupedMotor).Assembly.GetType("Zandea.Wildlife.WildlifeBrain");
            var healthType = typeof(QuadrupedMotor).Assembly.GetType("Zandea.Wildlife.CreatureHealth");
            if (brainType != null) root.AddComponent(brainType);
            if (healthType != null) root.AddComponent(healthType);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Strud.prefab");
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Wildlife")) AssetDatabase.CreateFolder("Assets/Resources", "Wildlife");
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Wildlife/Strud.prefab");
            BuildTestScene(prefab);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
    }
    static void AddHitCollider(Transform bone, float radius) { var c = bone.gameObject.AddComponent<SphereCollider>(); c.radius = radius / bone.lossyScale.x; }
    static QuadrupedGrounding.Leg Leg(Func<string, Transform> find, string prefix, float walk, float trot) => new QuadrupedGrounding.Leg { upper = find(prefix + "Upper"), lower = find(prefix + "Lower"), ankle = find(prefix + "Ankle"), foot = find(prefix + "Foot"), walkPhase = walk, trotPhase = trot };
    static void Anchor(Transform parent, string name, Vector3 world, Quaternion rotation) { var t = new GameObject(name).transform; t.SetParent(parent, false); t.SetPositionAndRotation(world, rotation); }
    static AnimatorStateTransition Transition(AnimatorState a, AnimatorState b, string p, AnimatorConditionMode mode, float threshold)
    { var t = a.AddTransition(b); t.hasExitTime = false; t.duration = .2f; t.AddCondition(mode, threshold, p); return t; }
    static void BuildTestScene(GameObject prefab)
    {
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Test ground"; ground.transform.position = new Vector3(0,-.5f,0); ground.transform.localScale = new Vector3(80,1,80);
        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.name = "Moderate slope"; ramp.transform.position = new Vector3(10,.5f,5); ramp.transform.localScale = new Vector3(8,1,16); ramp.transform.rotation = Quaternion.Euler(10,0,0);
        var animal = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); animal.transform.position = Vector3.zero;
        var camera = new GameObject("Review Camera").AddComponent<Camera>(); camera.transform.position = new Vector3(9,5,10); camera.transform.LookAt(new Vector3(0,1.7f,0)); camera.tag = "MainCamera";
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.transform.rotation = Quaternion.Euler(45,-30,0);
        EditorSceneManager.SaveScene(scene, Folder + "/StrudTest.unity");
        EditorSceneManager.CloseScene(scene,true); UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
    }
    [MenuItem("Tools/Zandea/Wildlife/Spawn Strud in Front of Scene View")]
    static void Spawn()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Strud.prefab"); if (!prefab) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Strud.prefab"); }
        Vector3 p = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        if (Physics.Raycast(p + Vector3.up * 100, Vector3.down, out var hit, 500)) p = hit.point;
        var animal = (GameObject)PrefabUtility.InstantiatePrefab(prefab); animal.transform.position = p;
        Undo.RegisterCreatedObjectUndo(animal, "Spawn Strud"); Selection.activeGameObject = animal;
    }
}
