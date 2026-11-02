using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    public static class BanditParryProofBatch
    {
        const string Pending = "KenshiVR.BanditParryProofBatch.Pending";
        const string Started = "KenshiVR.BanditParryProofBatch.Started";

        static BanditParryProofBatch() => EditorApplication.update += Tick;

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MeleeCombat/BanditParryProof.unity");
            EditorPrefs.SetBool(Pending, true);
            EditorPrefs.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!EditorPrefs.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup - EditorPrefs.GetFloat(Started, 0f) < 35) return;
            EditorPrefs.DeleteKey(Pending);
            EditorPrefs.DeleteKey(Started);
            var proof = Object.FindFirstObjectByType<BanditParryProof>();
            bool passed = proof && proof.parryAttempts > 0 && proof.parryContacts > 0 &&
                proof.peakContactImpulse > .01f;
            Debug.Log($"PARRY_PROOF_BATCH_{(passed ? "PASS" : "FAIL")} attempts={proof?.parryAttempts ?? 0} " +
                $"contacts={proof?.physicalContacts ?? 0} parryContacts={proof?.parryContacts ?? 0} " +
                $"impulse={proof?.peakContactImpulse ?? 0f:F3}");
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
