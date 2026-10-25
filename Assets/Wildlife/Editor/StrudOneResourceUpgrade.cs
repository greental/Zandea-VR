using UnityEditor;
using UnityEngine;

namespace Zandea.Wildlife.Editor
{
    [InitializeOnLoad]
    static class StrudOneResourceUpgrade
    {
        const string Source = "Assets/Wildlife/Strud/Strud 1.prefab";
        const string Target = "Assets/Resources/Wildlife/Strud 1.prefab";
        static StrudOneResourceUpgrade() => EditorApplication.delayCall += Ensure;
        static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Target)) return;
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Wildlife")) AssetDatabase.CreateFolder("Assets/Resources", "Wildlife");
            if (!AssetDatabase.CopyAsset(Source, Target)) Debug.LogError("Could not publish Strud 1 to Resources/Wildlife.");
            AssetDatabase.SaveAssets();
        }
    }
}
