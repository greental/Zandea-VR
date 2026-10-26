using TMPro;
using UnityEditor;
using UnityEngine;

namespace Zandea.FriendlyNPC.Editor
{
    public static class DaranLedgerFontSetup
    {
        const string Source = "Assets/Zandea/FriendlyNPC/Fonts/Caveat-Regular.ttf";
        const string Destination = "Assets/Zandea/FriendlyNPC/Resources/CaveatLedger.asset";

        [MenuItem("Zandea/Friendly NPC/Generate ledger font")]
        public static void Generate()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Destination)) return;
            var source = AssetDatabase.LoadAssetAtPath<Font>(Source);
            if (!source) throw new System.InvalidOperationException("Caveat font was not imported: " + Source);
            var asset = TMP_FontAsset.CreateFontAsset(source);
            if (!asset) throw new System.InvalidOperationException("TMP could not create Caveat ledger font");
            asset.name = "CaveatLedger";
            AssetDatabase.CreateAsset(asset, Destination);
            asset.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+-:.'!() ");
            if (asset.atlasTexture && !AssetDatabase.Contains(asset.atlasTexture))
                AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            if (asset.material && !AssetDatabase.Contains(asset.material))
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("Generated Caveat ledger font at " + Destination);
        }
    }
}
