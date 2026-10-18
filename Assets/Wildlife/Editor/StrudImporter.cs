using UnityEditor;
public sealed class StrudImporter : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.EndsWith("/Strud_Rigged.fbx")) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.isReadable = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
    }
    void OnPreprocessAnimation()
    {
        if (!assetPath.EndsWith("/Strud_Rigged.fbx")) return;
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            int split = clip.name.LastIndexOf('|');
            if (split >= 0) clip.name = clip.name.Substring(split + 1);
            clip.loopTime = clip.name != "Hit" && clip.name != "Death";
            clip.lockRootRotation = true; clip.lockRootHeightY = true; clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true; clip.keepOriginalPositionY = true; clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }
}
