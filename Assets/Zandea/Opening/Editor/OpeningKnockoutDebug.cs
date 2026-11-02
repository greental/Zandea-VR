using UnityEditor;
using UnityEngine;

namespace Zandea.Opening.Editor
{
    public static class OpeningKnockoutDebug
    {
        [MenuItem("Zandea/Opening/Debug/Knock out player")]
        static void KnockOut()
        {
            var knockout = Object.FindFirstObjectByType<PlayerKnockout>();
            if (knockout) knockout.RequestKnockout("editor debug", true);
            else Debug.LogWarning("No PlayerKnockout in the playing scene.");
        }

        [MenuItem("Zandea/Opening/Debug/Recover player")]
        static void Recover()
        {
            var knockout = Object.FindFirstObjectByType<PlayerKnockout>();
            if (knockout) knockout.Recover();
            else Debug.LogWarning("No PlayerKnockout in the playing scene.");
        }
    }
}
