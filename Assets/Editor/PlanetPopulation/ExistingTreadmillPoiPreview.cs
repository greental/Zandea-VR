using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ExistingTreadmillPoiPreview
{
    const string PreviewName = "Scavenger POIs (Scene Preview)";

    static ExistingTreadmillPoiPreview()
    {
        EditorApplication.delayCall += EnsurePlanetPopulationPreview;
        EditorSceneManager.sceneOpened += (_, __) => EditorApplication.delayCall += EnsurePlanetPopulationPreview;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += EnsurePlanetPopulationPreview;
        };
    }

    static void EnsurePlanetPopulationPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlanetPopulation_POC") return;
        foreach (var planter in Object.FindObjectsByType<ExistingTreadmillPoiPlanter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (planter.gameObject.scene != scene || planter.sites == null || planter.sites.Count == 0) continue;
            var authoredPreview = planter.transform.Find(PreviewName);
            if (authoredPreview && authoredPreview.CompareTag("EditorOnly"))
            {
                authoredPreview.tag = "Untagged";
                EditorSceneManager.SaveScene(scene);
            }
            if (!planter.terrainPrepared)
            {
                if (ExistingTreadmillPoiTerrainPreparation.Prepare(planter))
                {
                    Rebuild(planter);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            else if (!planter.transform.Find(PreviewName))
            {
                Rebuild(planter);
                EditorSceneManager.SaveScene(scene);
            }
            else if (AddMissingMediumDetails(planter))
                EditorSceneManager.SaveScene(scene);
        }
    }

    public static void Rebuild(ExistingTreadmillPoiPlanter planter)
    {
        if (!planter || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var treadmill = planter.GetComponent<PlanetTerrainTreadmill>();
        if (!treadmill || !treadmill.AuthoredTileSet || !planter.faction ||
            treadmill.AuthoredTileSet.authoredTiles == null) return;
        var old = planter.transform.Find(PreviewName);
        if (old) Object.DestroyImmediate(old.gameObject);
        var preview = new GameObject(PreviewName);
        preview.tag = "Untagged";
        preview.transform.SetParent(planter.transform, false);
        foreach (var site in planter.sites)
        {
            if (site == null) continue;
            TerrainData data = null;
            foreach (var tile in treadmill.AuthoredTileSet.authoredTiles)
                if (tile != null && tile.coord.Equals(site.tile)) { data = tile.terrainData; break; }
            if (!data) continue;
            Vector3 origin = treadmill.GetTileWorldPosition(site.tile);
            foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true))
                if (terrain.terrainData == data) { origin = terrain.transform.position; break; }
            float y = data.GetInterpolatedHeight(site.localPosition.x / data.size.x, site.localPosition.y / data.size.z);
            Vector3 center = origin + new Vector3(site.localPosition.x, y, site.localPosition.y);
            bool medium = site.size == ExistingTreadmillPoiPlanter.Site.SiteSize.Medium;
            bool large = site.size == ExistingTreadmillPoiPlanter.Site.SiteSize.Large;
            var siteRoot = new GameObject($"{(large ? "Large" : medium ? "Medium" : "Small")} Scavenger POI - tile {site.tile.x},{site.tile.z}");
            siteRoot.transform.SetParent(preview.transform, true);
            siteRoot.transform.position = center;
            foreach (var placement in ExistingTreadmillPoiPlanter.CreateLayout(site, planter.faction))
            {
                if (!placement.prefab) continue;
                Vector3 position = center + new Vector3(placement.offset.x, 0, placement.offset.y);
                position.y = Ground(treadmill, position);
                var instance = Add(placement.prefab, siteRoot.transform, position,
                    Quaternion.Euler(0, placement.yaw, 0));
                if (placement.vehicle) ExistingTreadmillPoiPlanter.LiftVehicleToGround(instance, position.y);
            }
            if (planter.faction.npcPrefab)
                for (int i = 0; i < (large ? 8 : medium ? 4 : 3); i++)
                {
                    float angle = i * Mathf.PI * 2f / (large ? 8 : medium ? 4 : 3);
                    Vector3 position = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 7f;
                    position.y = Ground(treadmill, position);
                    Add(planter.faction.npcPrefab.gameObject, siteRoot.transform, position, Quaternion.identity);
                }
        }
        EditorSceneManager.MarkSceneDirty(planter.gameObject.scene);
    }

    static bool AddMissingMediumDetails(ExistingTreadmillPoiPlanter planter)
    {
        var preview = planter.transform.Find(PreviewName);
        if (!preview || !planter.faction || planter.sites.Count < 2 || preview.childCount < 2) return false;
        var medium = preview.GetChild(1);
        if (medium.Find("Canopy and Connected Pipes")) return false;
        var details = new GameObject("Canopy and Connected Pipes");
        details.transform.SetParent(medium, false);
        var treadmill = planter.GetComponent<PlanetTerrainTreadmill>();
        foreach (var placement in ExistingTreadmillPoiPlanter.CreateLayout(planter.sites[1], planter.faction))
        {
            if (!placement.prefab || placement.prefab != planter.faction.canopyPrefab &&
                placement.prefab != planter.faction.pipePrefab && placement.prefab != planter.faction.gatePrefab) continue;
            bool alreadyPresent = false;
            foreach (var child in medium.GetComponentsInChildren<Transform>(true))
                if (child != details.transform && child.name.StartsWith(placement.prefab.name))
                { alreadyPresent = true; break; }
            if (alreadyPresent && placement.prefab != planter.faction.pipePrefab) continue;
            Vector3 position = medium.position + new Vector3(placement.offset.x, 0, placement.offset.y);
            position.y = Ground(treadmill, position);
            Add(placement.prefab, details.transform, position, Quaternion.Euler(0, placement.yaw, 0));
        }
        EditorSceneManager.MarkSceneDirty(planter.gameObject.scene);
        return true;
    }

    static GameObject Add(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation)
    {
        if (!prefab) return null;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        instance.transform.SetParent(parent, true);
        instance.transform.SetPositionAndRotation(position, rotation);
        return instance;
    }

    static float Ground(PlanetTerrainTreadmill treadmill, Vector3 position)
    {
        foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true))
        {
            if (!terrain.terrainData) continue;
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            if (position.x >= origin.x && position.x <= origin.x + size.x && position.z >= origin.z && position.z <= origin.z + size.z)
                return terrain.SampleHeight(position) + origin.y;
        }
        return position.y;
    }
}
