using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JunkerEquipment.Editor
{
    [InitializeOnLoad]
    public static class JunkerPkoSetup
    {
        const string Shot1="Assets/MyAssets/Audio/Weapons/pko/mrfriends-pistol-shot-233473.mp3";
        const string Shot2="Assets/MyAssets/Audio/Weapons/pko/freesound_community-9mm-pistol-shoot-short-reverb-7152.mp3";

        static JunkerPkoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) return;
                var pistol=FindPko();
                if(pistol && (!pistol.GetComponent<JunkerReflexSight>() || !pistol.muzzleCompe || !pistol.shotAudio)) Configure();
            };
        }

        [MenuItem("Tools/Junker Equipment/Configure PKO reflex dot and audio")]
        public static void Configure()
        {
            var pistol=FindPko();
            if(!pistol) { Debug.LogError("No JunkerPistol found in the active scene."); return; }

            var audio=pistol.GetComponent<AudioSource>();
            if(!audio) audio=Undo.AddComponent<AudioSource>(pistol.gameObject);
            audio.playOnAwake=false; audio.spatialBlend=1f; audio.dopplerLevel=.15f;
            audio.minDistance=1.5f; audio.maxDistance=70f; audio.rolloffMode=AudioRolloffMode.Logarithmic;
            pistol.shotAudio=audio;
            pistol.shotClips=new[]{AssetDatabase.LoadAssetAtPath<AudioClip>(Shot1),AssetDatabase.LoadAssetAtPath<AudioClip>(Shot2)}.Where(c=>c).ToArray();

            var sight=pistol.GetComponent<JunkerReflexSight>();
            if(!sight) sight=Undo.AddComponent<JunkerReflexSight>(pistol.gameObject);
            sight.muzzle=pistol.muzzle;
            sight.viewer=Camera.main;
            sight.apertureRadius=.035f;
            sight.maximumEyeDistance=1.5f;
            sight.dotDiameter=.0045f;
            sight.sightWindow=Child(pistol.transform,"Reflex sight window",new Vector3(.058f,.107f,-.005f));
            sight.sightWindow.localRotation=pistol.muzzle.localRotation;

            var dotTransform=pistol.transform.Find("Reflex sight dot");
            if(!dotTransform)
            {
                var dot=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Undo.RegisterCreatedObjectUndo(dot,"Create PKO reflex dot");
                dot.name="Reflex sight dot"; dot.transform.SetParent(pistol.transform,false);
                Object.DestroyImmediate(dot.GetComponent<Collider>());
                dotTransform=dot.transform;
            }
            sight.dot=dotTransform;
            var renderer=dotTransform.GetComponent<Renderer>();
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/JunkerEquipment/Generated/PKO reflex dot.mat");
            if(!material)
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.color=new Color(1f,.015f,.005f,1f);
                material.EnableKeyword("_EMISSION");
                if(material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor",new Color(8f,.03f,.01f));
                AssetDatabase.CreateAsset(material,"Assets/JunkerEquipment/Generated/PKO reflex dot.mat");
            }
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            dotTransform.gameObject.SetActive(false);

            // The FBX names this imported section generically, but its material is
            // explicitly "barrel" and it contains the muzzle compensator/upper.
            pistol.muzzleCompe=pistol.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(r=>r.sharedMaterials.Any(m=>m && m.name.ToLowerInvariant().Contains("barrel")))?.transform;

            EditorUtility.SetDirty(pistol); EditorUtility.SetDirty(sight); EditorUtility.SetDirty(audio);
            EditorSceneManager.MarkSceneDirty(pistol.gameObject.scene);
            EditorSceneManager.SaveScene(pistol.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"PKO configured with reflex dot and {pistol.shotClips.Length} shot clips.",pistol);
        }

        static JunkerPistol FindPko() => Object.FindObjectsByType<JunkerPistol>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.GetComponent<JunkerEquipmentItem>()?.itemId == "PKO");

        static Transform Child(Transform parent,string name,Vector3 localPosition)
        {
            var child=parent.Find(name);
            if(!child) { child=new GameObject(name).transform; Undo.RegisterCreatedObjectUndo(child.gameObject,"Create "+name); child.SetParent(parent,false); }
            child.localPosition=localPosition; return child;
        }
    }
}
