using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class ImportedCombatAnimationImport : AssetPostprocessor
{
    const string Root = "Assets/MeleeCombat/ImportedAnimations/";

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Root) || !assetPath.EndsWith(".fbx")) return;
        var model = (ModelImporter)assetImporter;
        model.animationType = ModelImporterAnimationType.Human;
        model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        model.importAnimation = true;
        model.materialImportMode = ModelImporterMaterialImportMode.None;
        model.animationCompression = ModelImporterAnimationCompression.Off;
    }

    [MenuItem("Zandea/Melee Combat/Report imported combat animations")]
    public static void Report()
    {
        foreach (var path in Directory.GetFiles(Root, "*.fbx", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var assetPath = path.Replace('\\', '/');
            var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Avatar>().FirstOrDefault();
            Debug.Log($"IMPORTED_COMBAT_CLIP {Path.GetFileNameWithoutExtension(path)} clips={string.Join(",", clips.Select(c => c.name + ":" + c.length.ToString("F2")))} avatar={avatar?.isValid}/{avatar?.isHuman}");
        }
    }

    [MenuItem("Zandea/Melee Combat/Name imported combat clips")]
    public static void NameClips()
    {
        foreach (var path in Directory.GetFiles(Root, "*.fbx", SearchOption.AllDirectories))
        {
            var assetPath = path.Replace('\\', '/');
            var importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            string name = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].name = clips.Length == 1 ? name : name + " " + (i + 1);
                clips[i].loopTime = name.Contains("idle") || name.Contains("run") && !name.Contains("attack") || name.Contains("walk");
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Debug.Log("NAMED_COMBAT_CLIP " + name);
        }
        AssetDatabase.SaveAssets();
    }
}
