#if UNITY_EDITOR
using System;
using JunkerEquipment;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.Bandaging.Editor
{
    [InitializeOnLoad]
    public static class BandageValidation
    {
        const string Player = "Assets/MyAssets/prefabs/Player/VR player - JunkerWeapon.prefab";
        const string Roll = "Assets/MyAssets/prefabs/medical/BandageRoll.prefab";
        static BandageValidation()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Roll)) Run();
            };
        }
        [MenuItem("Kenshi VR/Bandaging/Validate setup and geometry")]
        public static void Run()
        {
            try
            {
                var roll = AssetDatabase.LoadAssetAtPath<GameObject>(Roll);
                Require(roll && roll.GetComponent<BandageRoll>() && roll.GetComponent<XRGrabInteractable>() &&
                    roll.GetComponent<Rigidbody>() && roll.GetComponent<VRInventoryItem>() &&
                    roll.GetComponent<JunkerEquipmentItem>(), "roll lifecycle");
                Require(roll.GetComponentInChildren<MeshRenderer>(true), "supplied roll mesh");
                var player = AssetDatabase.LoadAssetAtPath<GameObject>(Player);
                var playerWounds = player ? player.GetComponentInChildren<PlayerWounds>(true) : null;
                Require(playerWounds && playerWounds.startingRollPrefab == roll.GetComponent<BandageRoll>(),
                    "player wound receiver and starting medical roll");
                var bone = new GameObject("Bandage validation bone");
                try
                {
                    bone.transform.position = new Vector3(2f, 1f, -3f);
                    bone.transform.rotation = Quaternion.Euler(14f, 38f, 7f);
                    var zone = bone.AddComponent<WoundWrapZone>();
                    zone.bone = bone.transform;
                    zone.localCenter = new Vector3(.02f, .04f, -.01f);
                    zone.localFrame = Quaternion.Euler(10f, 0f, 30f);
                    Vector3 local = new Vector3(.055f, .01f, 0f);
                    Require(Vector3.Distance(zone.ToFrame(zone.ToWorld(local)), local) < .0001f, "bone local coordinates");
                    Require(zone.Contains(zone.ToWorld(local)), "zone surface eligibility");
                    var strip = new GameObject("Bandage validation ribbon");
                    strip.transform.SetParent(bone.transform, false);
                    try
                    {
                        var ribbon = strip.AddComponent<BandageRibbon>();
                        ribbon.Initialize(zone, null, .04f, .002f, 40, 3, 170f, 0f);
                        ribbon.UpdateRibbon(540f, zone.ToWorld(local));
                        var mesh = strip.GetComponent<MeshFilter>().sharedMesh;
                        Require(mesh && mesh.vertexCount > 100 && mesh.triangles.Length > 300, "unrolled fabric geometry");
                        ribbon.UpdateRibbon(1080f, zone.ToWorld(local), true);
                        Require(mesh.vertexCount == 242, "three completed rotations");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(strip); }
                }
                finally { UnityEngine.Object.DestroyImmediate(bone); }
                Debug.Log("Bandage validation passed: prefab, inventory, player receiver, bone frame, and three-turn ribbon.");
            }
            catch (Exception exception) { Debug.LogError("Bandage validation failed: " + exception); }
        }
        static void Require(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
        }
    }
}
#endif
