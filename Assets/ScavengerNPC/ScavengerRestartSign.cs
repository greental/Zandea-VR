using UnityEngine;
using UnityEngine.SceneManagement;
using VRKoombar.WeaponsStaging.Contracts;

namespace ScavengerNPC
{
    // The sign is a regular damageable target, so it responds to laser impact.
    public sealed class ScavengerRestartSign : MonoBehaviour, IDamageable
    {
        bool restarting;
        public void ApplyDamage(in DamagePacket packet)
        {
            if (restarting || packet.BaseDamage <= 0f) return;
            restarting = true;
            var scene = gameObject.scene;
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
#if UNITY_EDITOR
            else if (!string.IsNullOrEmpty(scene.path))
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path,
                    new LoadSceneParameters(LoadSceneMode.Single));
#endif
            else Debug.LogError("Add this scene to Build Settings before using the restart sign in a build.", this);
        }
        public ShipCombatState GetCombatState() => ShipCombatState.CombatReady;
    }
}
