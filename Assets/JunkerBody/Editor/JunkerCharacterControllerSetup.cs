using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JunkerBody.Editor
{
    public static class JunkerCharacterControllerSetup
    {
        [MenuItem("Tools/JunkerBody/Apply player Character Controller crouch fit")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            var body = Object.FindFirstObjectByType<VRBodyController>();
            if (!body) { Debug.LogError("No VRBodyController found in the active scene."); return; }
            var fit = body.GetComponent<JunkerCharacterControllerFit>();
            if (!fit) fit = Undo.AddComponent<JunkerCharacterControllerFit>(body.gameObject);
            fit.body = body;
            fit.followPhysicalCrouch = true;
            fit.minimumHeight = 1f;
            fit.groundingCenterOffset = .30f;
            if (body.xrOrigin) fit.characterController = body.xrOrigin.GetComponent<CharacterController>();
            EditorUtility.SetDirty(fit);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Junker Character Controller follows standing height and crouch on {fit.name}.");
        }
    }
}
