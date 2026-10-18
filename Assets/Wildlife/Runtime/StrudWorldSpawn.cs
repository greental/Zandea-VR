using UnityEngine;
using UnityEngine.SceneManagement;
namespace Zandea.Wildlife
{
    public static class StrudWorldSpawn
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (SceneManager.GetActiveScene().name != "Main") return;
            new GameObject("Strud wildlife spawn").AddComponent<StrudWorldSpawnRunner>();
        }
    }
}
