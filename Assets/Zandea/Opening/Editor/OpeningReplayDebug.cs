using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zandea.Opening.Editor
{
    public static class OpeningReplayDebug
    {
        [MenuItem("Zandea/Opening/Debug/Replay active scene")]
        static void Replay()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("Enter Play mode before replaying the opening."); return; }
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) { Debug.LogError("Active scene has no saved path."); return; }
            EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
        }
    }
}
