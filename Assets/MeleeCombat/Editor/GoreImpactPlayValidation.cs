using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

[InitializeOnLoad]
public static class GoreImpactPlayValidation
{
    const string Request = "Temp/GoreImpactPlayValidation.request";
    const string Result = "Temp/GoreImpactPlayValidation.result";
    const string Running = "GoreImpactPlayValidation.Running";
    const string StartedPlay = "GoreImpactPlayValidation.StartedPlay";
    static GameObject source;

    static GoreImpactPlayValidation() => EditorApplication.update += Tick;

    [MenuItem("Tools/Melee Combat/Validate hit blood particles in Play mode")]
    public static void Run() { File.WriteAllText(Request, "run"); Tick(); }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (!SessionState.GetBool(Running, false))
        {
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            SessionState.SetBool(Running, true);
            SessionState.SetBool(StartedPlay, !EditorApplication.isPlaying);
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        }
        if (!EditorApplication.isPlaying) return;
        try
        {
            var terrain = Terrain.activeTerrains.FirstOrDefault(item => item && item.enabled && item.GetComponent<TerrainCollider>());
            if (!terrain) throw new Exception("No active terrain collider");
            Vector3 point = terrain.transform.position + terrain.terrainData.size * .5f;
            point.y = terrain.SampleHeight(point) + terrain.transform.position.y + 1f;
            source = new GameObject("Blood validation source");
            var blood = source.AddComponent<GoreImpactSystem>();
            blood.RegisterImpact(new DamagePacket { HitPointWorld = point, HitNormalWorld = Vector3.back },
                Vector3.forward * 5f, 24f);
            var sprays = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
                .Where(particle => particle.name == "Pooled blood droplets" && particle.particleCount > 0).ToArray();
            if (sprays.Length == 0) throw new Exception("A flesh hit emitted no visible blood particles");
            var mark = GameObject.Find("Blood surface mark");
            if (!mark) throw new Exception("The hit made no floor mark");
            float floor = terrain.SampleHeight(mark.transform.position) + terrain.transform.position.y;
            if (Mathf.Abs(mark.transform.position.y - floor) > .05f)
                throw new Exception("The hit mark floated above the floor by " + (mark.transform.position.y - floor));
            var renderer = sprays[0].GetComponent<ParticleSystemRenderer>();
            if (!renderer || !renderer.sharedMaterial || !renderer.sharedMaterial.shader)
                throw new Exception("Blood particles have no render material");
            File.WriteAllText(Result, "PASS hit emitted " + sprays[0].particleCount +
                " blood particles with render material; blood mark landed on terrain");
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL hit blood particles\n" + error);
            Debug.LogException(error);
        }
        finally
        {
            if (source) UnityEngine.Object.Destroy(source);
            source = null;
            SessionState.SetBool(Running, false);
            bool startedPlay = SessionState.GetBool(StartedPlay, false);
            SessionState.SetBool(StartedPlay, false);
            if (startedPlay) EditorApplication.isPlaying = false;
        }
    }
}
