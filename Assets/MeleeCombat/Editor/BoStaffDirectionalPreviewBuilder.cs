using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BoStaffDirectionalPreviewBuilder
{
    const string Folder = "Assets/MeleeCombat/MocapTest/GeneratedVariants";
    [MenuItem("Zandea/Melee Combat/Build Bo Staff directional preview")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(Folder + "/BoStaffPreview.unity");
        var grip = Object.FindFirstObjectByType<MocapStaffPreview>();
        if (!grip) throw new System.InvalidOperationException("Bo Staff preview rig missing");
        var animator = grip.GetComponent<Animator>();
        var driver = grip.GetComponent<BanditMocapNpcDriver>();
        if (driver) driver.enabled = false;
        grip.enabled = false;
        grip.driveVisualStaff = true;
        grip.staff.enabled = false;
        grip.staff.GetComponent<Rigidbody>().isKinematic = true;
        grip.staff.GetComponent<CapsuleCollider>().enabled = false;
        var preview = grip.GetComponent<BoStaffDirectionalPreview>();
        if (!preview) preview = grip.gameObject.AddComponent<BoStaffDirectionalPreview>();
        preview.grip = grip;
        preview.directionClips = new[] {
            BoStaffDirectionalClipBuilder.Load("Overhead"),
            BoStaffDirectionalClipBuilder.Load("DiagonalRight"),
            BoStaffDirectionalClipBuilder.Load("HorizontalRight"),
            BoStaffDirectionalClipBuilder.Load("LowRight"),
            BoStaffDirectionalClipBuilder.Load("Thrust"),
            BoStaffDirectionalClipBuilder.Load("LowLeft"),
            BoStaffDirectionalClipBuilder.Load("HorizontalLeft"),
            BoStaffDirectionalClipBuilder.Load("DiagonalLeft") };
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            Folder + "/StarvingBandit_BoStaff.controller");
        EditorSceneManager.SaveScene(scene, Folder + "/BoStaffDirectionalPreview.unity");
        Debug.Log("BO_STAFF_DIRECTIONAL_PREVIEW " + Folder + "/BoStaffDirectionalPreview.unity");
    }
}
