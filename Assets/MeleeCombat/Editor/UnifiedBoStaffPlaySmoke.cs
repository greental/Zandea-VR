using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    public static class UnifiedBoStaffPlaySmoke
    {
        const string Pending = "KenshiVR.UnifiedBoStaffPlaySmoke.Pending";
        static PhysicalMeleeWeapon weapon;
        static GameObject firstNpc, secondNpc;
        static Transform firstTarget, secondTarget;
        static int stage, stageFrame;
        static float dropHeight;

        static UnifiedBoStaffPlaySmoke() => EditorApplication.update += Tick;

        public static void Run()
        {
            EditorPrefs.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!EditorPrefs.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
            if (Time.frameCount <= stageFrame) return;
            try
            {
                switch (stage)
                {
                    case 0:
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                            "Assets/MyAssets/prefabs/weapons/Bo Staff.prefab");
                        if (!prefab) throw new InvalidOperationException("Shared staff prefab missing");
                        weapon = UnityEngine.Object.Instantiate(prefab, new Vector3(0, 3, 0), Quaternion.identity)
                            .GetComponent<PhysicalMeleeWeapon>();
                        firstNpc = new GameObject("First NPC");
                        secondNpc = new GameObject("Second NPC");
                        firstTarget = new GameObject("First grip target").transform;
                        secondTarget = new GameObject("Second grip target").transform;
                        firstTarget.SetParent(firstNpc.transform);
                        secondTarget.SetParent(secondNpc.transform);
                        firstTarget.position = secondTarget.position = weapon.transform.position;
                        Check(weapon.State == PhysicalMeleeWeapon.HoldState.World, "initial world state");
                        Check(weapon.TryReserve(firstNpc.transform), "first reservation");
                        Check(!weapon.TryReserve(secondNpc.transform), "second reservation rejected");
                        stage = 1; break;
                    case 1:
                        Check(weapon.TryEquipNpc(firstNpc.transform, firstTarget), "first NPC equip");
                        Check(weapon.State == PhysicalMeleeWeapon.HoldState.HeldByNpc, "NPC held state");
                        Check(!weapon.GetComponent<Rigidbody>().isKinematic, "NPC body remains dynamic for joint follow");
                        Check(!weapon.GetComponent<Rigidbody>().useGravity, "NPC gravity off");
                        stage = 2; break;
                    case 2:
                        weapon.Drop();
                        weapon.Drop();
                        Check(weapon.State == PhysicalMeleeWeapon.HoldState.World, "idempotent drop");
                        Check(weapon.GetComponent<Rigidbody>().useGravity, "world gravity restored");
                        Check(weapon.TryReserve(secondNpc.transform), "second NPC reservation after drop");
                        stage = 3; break;
                    case 3:
                        Check(weapon.TryEquipNpc(secondNpc.transform, secondTarget), "second NPC equips same instance");
                        Check(weapon.Owner == secondNpc.transform, "second NPC owns original instance");
                        stage = 4; break;
                    case 4:
                        weapon.Drop();
                        dropHeight = weapon.transform.position.y;
                        stage = 5; stageFrame = Time.frameCount + 40; return;
                    case 5:
                        var previousSimulation = Physics.simulationMode;
                        Physics.simulationMode = SimulationMode.Script;
                        for (int i = 0; i < 30; i++) Physics.Simulate(.02f);
                        Physics.simulationMode = previousSimulation;
                        Debug.Log($"Drop check: y={weapon.transform.position:F3}, start={dropHeight:F3}, gravity={weapon.GetComponent<Rigidbody>().useGravity}, kinematic={weapon.GetComponent<Rigidbody>().isKinematic}, velocity={weapon.GetComponent<Rigidbody>().linearVelocity}");
                        Check(weapon.transform.position.y < dropHeight - .05f, "dropped staff falls under gravity");
                        Debug.Log("Unified Bo Staff play smoke passed: reservation, NPC equip, idempotent drop, re-equip, gravity");
                        Finish(0); return;
                }
                stageFrame = Time.frameCount;
            }
            catch (Exception error)
            {
                Debug.LogError("Unified Bo Staff play smoke failed: " + error);
                Finish(1);
            }
        }

        static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
        }

        static void Finish(int code)
        {
            EditorPrefs.DeleteKey(Pending);
            stage = 0;
            if (weapon) UnityEngine.Object.Destroy(weapon.gameObject);
            if (firstNpc) UnityEngine.Object.Destroy(firstNpc);
            if (secondNpc) UnityEngine.Object.Destroy(secondNpc);
            EditorApplication.Exit(code);
        }
    }
}
