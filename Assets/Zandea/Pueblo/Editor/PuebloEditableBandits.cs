using System;
using System.Collections.Generic;
using System.Linq;
using KenshiVR.MeleeCombat;
using MyAssets.PlanetTerrain;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zandea.POI;

namespace Zandea.Pueblo.Editor
{
    /// <summary>Creates scene-authored guards without replacing the hand-built settlement.</summary>
    public static class PuebloEditableBandits
    {
        const string Main = "Assets/Main.unity";
        const string PoiId = "zandea.pueblo.settlement.1";
        const string EncounterName = "Editable Bandit Groups";

        [MenuItem("Zandea/POI/Pueblo/Create Editable Bandit Groups in Main")]
        public static void CreateFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before authoring Pueblo bandits.");
            var scene = SceneManager.GetSceneByPath(Main);
            if (!scene.isLoaded)
                throw new InvalidOperationException("Open Assets/Main.unity before creating editable Pueblo bandits.");
            var poi = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BanditOutpost>(true))
                .FirstOrDefault(p => p.poiId == PoiId);
            if (!poi) throw new InvalidOperationException("Main has no Pueblo settlement POI.");
            CreateOrSelect(poi);
        }

        public static void CreateOrSelect(BanditOutpost poi)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before authoring Pueblo bandits.");
            if (!poi || poi.gameObject.scene.path != Main || poi.poiId != PoiId)
                throw new InvalidOperationException("Select Main's Pueblo settlement POI.");
            var preview = poi.transform.Find("Layout Preview");
            if (!preview) throw new InvalidOperationException("The authored Pueblo Layout Preview is missing.");
            var existing = preview.Find(EncounterName);
            if (existing)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                return;
            }
            if (preview.GetComponentsInChildren<StarvingBandit>(true).Length != 0)
                throw new InvalidOperationException("Pueblo already has authored bandits. No duplicate group was created.");
            if (!poi.definition || !poi.definition.bandit)
                throw new InvalidOperationException("Pueblo needs a bandit prefab in its POI definition.");
            poi.EnsureLayout();
            if (poi.Layout.spawns.Count != 6)
                throw new InvalidOperationException("Pueblo layout must contain exactly six guard positions.");

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create editable Pueblo bandit groups");
            var root = new GameObject(EncounterName);
            Undo.RegisterCreatedObjectUndo(root, "Create editable Pueblo bandit groups");
            root.transform.SetParent(preview, false);
            try
            {
                var patrol = new List<Vector3>();
                foreach (var point in poi.Layout.patrol)
                    patrol.Add(Ground(poi.treadmill, World(preview, point)));
                for (int i = 0; i < 6; i++)
                {
                    Vector3 position = Ground(poi.treadmill, World(preview, poi.Layout.spawns[i]));
                    var role = new GameObject($"Guard role {i + 1}");
                    Undo.RegisterCreatedObjectUndo(role, "Create Pueblo guard role");
                    role.transform.SetParent(root.transform, false);
                    role.transform.position = position;
                    var npcObject = (GameObject)PrefabUtility.InstantiatePrefab(poi.definition.bandit.gameObject, poi.gameObject.scene);
                    if (!npcObject) throw new InvalidOperationException("Could not instantiate the existing bandit prefab.");
                    Undo.RegisterCreatedObjectUndo(npcObject, "Create editable Pueblo bandit");
                    npcObject.name = $"Bandit {i + 1} - {RoleName(i)}";
                    npcObject.transform.SetParent(role.transform, true);
                    npcObject.transform.SetPositionAndRotation(position, preview.rotation);
                    var npc = npcObject.GetComponent<StarvingBandit>();
                    if (!npc) throw new InvalidOperationException("Bandit prefab lacks StarvingBandit.");
                    var group = Undo.AddComponent<BanditGroup>(role);
                    group.banditPrefab = poi.definition.bandit;
                    group.authoredMembers = new[] { npc };
                    group.groupSize = 1;
                    group.buildLocalNavMesh = false;
                    group.patrolRadius = (i == 2 || i == 3) ? 9 : 2;
                    group.patrolWalkSpeed = poi.definition.patrolWalkSpeed;
                    var points = (i == 2 || i == 3) && patrol.Count > 0
                        ? patrol : new List<Vector3> { position };
                    var markers = new List<Transform>();
                    foreach (var destination in points)
                    {
                        var marker = new GameObject("Patrol waypoint");
                        Undo.RegisterCreatedObjectUndo(marker, "Create Pueblo patrol waypoint");
                        marker.transform.SetParent(role.transform, false);
                        marker.transform.position = destination;
                        markers.Add(marker.transform);
                    }
                    group.patrolPoints = markers.ToArray();
                    EditorUtility.SetDirty(group);
                }
                EditorSceneManager.MarkSceneDirty(poi.gameObject.scene);
                EditorSceneManager.SaveScene(poi.gameObject.scene);
                Selection.activeGameObject = root;
                EditorGUIUtility.PingObject(root);
                Debug.Log("[Pueblo] Six editable bandits and their guard roles are saved under Layout Preview/" + EncounterName + ".", root);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
            finally { Undo.CollapseUndoOperations(undoGroup); }
        }

        static string RoleName(int index) => index switch
        {
            0 or 1 => "Gate",
            2 or 3 => "Courtyard",
            4 => "Shades",
            _ => "Tower"
        };

        static Vector3 World(Transform preview, Vector2 local) =>
            preview.position + preview.rotation * new Vector3(local.x, 0, local.y);

        static Vector3 Ground(PlanetTerrainTreadmill treadmill, Vector3 world)
        {
            if (!treadmill) throw new InvalidOperationException("Pueblo has no terrain treadmill.");
            foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true))
            {
                if (!terrain || !terrain.terrainData) continue;
                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                if (world.x < origin.x || world.x > origin.x + size.x ||
                    world.z < origin.z || world.z > origin.z + size.z) continue;
                world.y = terrain.SampleHeight(world) + origin.y;
                return world;
            }
            throw new InvalidOperationException("No authored terrain tile supports Pueblo guard position " + world);
        }
    }
}
