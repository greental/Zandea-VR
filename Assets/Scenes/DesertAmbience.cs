using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Camera-local desert dust and daytime heat shimmer for large terrain scenes.</summary>
public sealed class DesertAmbience : MonoBehaviour
{
    [Header("Windblown sand")]
    [Min(0f)] public float dustParticlesPerSecond = 60f;
    [Min(0f)] public float windSpeed = 2.5f;
    public Vector2 windDirection = new Vector2(1f, 0.35f);
    public Color sandColor = new Color(0.79f, 0.66f, 0.45f, 0.42f);
    [Min(1f)] public float maxHeightAboveGround = 250f;

    [Header("Sand spilling off terrain edges")]
    [Min(0f)] public float edgeDustPerSecond = 48f;
    [Min(0.5f)] public float minimumEdgeDrop = 2.5f;
    [Range(15f, 100f)] public float edgeSearchRadius = 55f;
    [Range(4, 48)] public int edgeChecksPerScan = 28;

    [Header("Daytime mirage")]
    [Min(0f)] public float heatPatchesPerSecond = 4f;
    public bool enableMirage = true;

    Camera viewer;
    DayNightCycle cycle;
    ParticleSystem dust;
    ParticleSystem edgeDust;
    ParticleSystem heat;
    Material dustMaterial;
    Material heatMaterial;
    float nextCameraSearch;
    float nextPlacement;
    float nextTerrainRefresh;
    float nextEdgeScan;
    Terrain[] terrains;
    readonly Vector3[] edgePoints = new Vector3[48];
    readonly float[] edgeStrengths = new float[48];

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))
        {
            Debug.LogWarning("DesertAmbience requires URP.", this);
            enabled = false;
            return;
        }

        var dustShader = Shader.Find("KenshiVR/DesertDust");
        var heatShader = Shader.Find("KenshiVR/DesertHeatHaze");
        if (dustShader == null || !dustShader.isSupported)
        {
            Debug.LogError("Desert dust shader is missing or unsupported.", this);
            enabled = false;
            return;
        }

        dustMaterial = new Material(dustShader) { name = "Runtime Desert Dust", hideFlags = HideFlags.DontSave };
        dust = CreateSystem("Windblown Sand", dustMaterial, 600);
        ConfigureDust(dust);
        edgeDust = CreateSystem("Crest Sand Plumes", dustMaterial, 400);
        ConfigureEdgeDust(edgeDust);

        if (enableMirage && pipeline.supportsCameraOpaqueTexture && heatShader != null && heatShader.isSupported)
        {
            heatMaterial = new Material(heatShader) { name = "Runtime Desert Heat Haze", hideFlags = HideFlags.DontSave };
            heat = CreateSystem("Heat Mirage", heatMaterial, 70);
            ConfigureHeat(heat);
        }
        else if (enableMirage)
            Debug.LogWarning("Heat mirage needs a supported heat shader and Opaque Texture enabled in the URP asset.", this);

        cycle = FindFirstObjectByType<DayNightCycle>();
        dust.Play();
        edgeDust.Play();
        if (heat != null) heat.Play();
    }

    ParticleSystem CreateSystem(string systemName, Material material, int maximum)
    {
        var child = new GameObject(systemName);
        child.transform.SetParent(transform, false);
        var system = child.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 10f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maximum;
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return system;
    }

    void ConfigureDust(ParticleSystem system)
    {
        var main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.startSpeed = 0f;
        main.startColor = sandColor;
        var emission = system.emission;
        emission.rateOverTime = dustParticlesPerSecond;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(26f, 1.6f, 26f);
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        Vector2 direction = windDirection.sqrMagnitude > 0.001f ? windDirection.normalized : Vector2.right;
        velocity.x = new ParticleSystem.MinMaxCurve(direction.x * windSpeed, direction.x * windSpeed);
        velocity.z = new ParticleSystem.MinMaxCurve(direction.y * windSpeed, direction.y * windSpeed);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.15f);
        var noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.18f;
        noise.frequency = 0.55f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        SetSoftLifetime(system);
    }

    void ConfigureHeat(ParticleSystem system)
    {
        var main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5f);
        main.startSpeed = 0f;
        main.startColor = new Color(1f, 1f, 1f, 0.65f);
        var emission = system.emission;
        emission.rateOverTime = heatPatchesPerSecond;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 1.2f, 14f);
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.y = 0.22f;
        SetSoftLifetime(system);
    }

    void ConfigureEdgeDust(ParticleSystem system)
    {
        var main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = 0f;
        main.startColor = sandColor;
        var emission = system.emission;
        emission.rateOverTime = 0f;
        var shape = system.shape;
        shape.enabled = false;
        var noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.65f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.3f;
        renderer.velocityScale = 0.16f;
        SetSoftLifetime(system);
    }

    static void SetSoftLifetime(ParticleSystem system)
    {
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        var colors = system.colorOverLifetime;
        colors.enabled = true;
        colors.color = fade;
    }

    void LateUpdate()
    {
        if (dust == null) return;
        if (Time.unscaledTime < nextPlacement) return;
        nextPlacement = Time.unscaledTime + 0.12f;
        if (terrains == null || Time.unscaledTime >= nextTerrainRefresh)
        {
            terrains = Terrain.activeTerrains;
            nextTerrainRefresh = Time.unscaledTime + 1f;
        }
        if (viewer == null && Time.unscaledTime >= nextCameraSearch)
        {
            viewer = Camera.main;
            if (viewer == null)
                foreach (var camera in Camera.allCameras)
                    if (camera.enabled && camera.cameraType == CameraType.Game) { viewer = camera; break; }
            nextCameraSearch = Time.unscaledTime + 1f;
        }
        if (viewer == null || !TryTerrainHeight(viewer.transform.position, out float groundY) ||
            viewer.transform.position.y - groundY > maxHeightAboveGround)
        {
            var dustEmission = dust.emission;
            dustEmission.rateOverTime = 0f;
            if (heat != null)
            {
                var heatEmission = heat.emission;
                heatEmission.rateOverTime = 0f;
            }
            return;
        }

        Vector3 viewerPosition = viewer.transform.position;
        dust.transform.position = new Vector3(viewerPosition.x, GroundHeight(viewerPosition) + 0.8f, viewerPosition.z);
        dust.transform.rotation = Quaternion.identity;
        var sandEmission = dust.emission;
        sandEmission.rateOverTime = dustParticlesPerSecond;

        if (edgeDust != null && Time.time >= nextEdgeScan)
        {
            nextEdgeScan = Time.time + 0.25f;
            EmitFromEdges(viewerPosition);
        }

        if (heat != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(viewer.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 heatPosition = viewerPosition + forward * 24f;
            heat.transform.position = new Vector3(heatPosition.x, GroundHeight(heatPosition) + 1.1f, heatPosition.z);
            heat.transform.rotation = Quaternion.Euler(0f, viewer.transform.eulerAngles.y, 0f);
            float daylight = cycle == null ? 1f : Mathf.Clamp01(cycle.GetComponent<Light>().intensity /
                Mathf.Max(0.01f, cycle.daylightIntensity));
            var heatEmission = heat.emission;
            heatEmission.rateOverTime = heatPatchesPerSecond * daylight;
        }
    }

    void EmitFromEdges(Vector3 viewerPosition)
    {
        if (edgeDustPerSecond <= 0f || terrains == null || terrains.Length == 0) return;
        Vector3 forward = Vector3.ProjectOnPlane(viewer.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector2 wind2D = windDirection.sqrMagnitude > 0.001f ? windDirection.normalized : Vector2.right;
        Vector3 wind = new Vector3(wind2D.x, 0f, wind2D.y);
        Vector3 acrossWind = new Vector3(-wind.z, 0f, wind.x);
        float threshold = Mathf.Max(0.5f, minimumEdgeDrop);
        int found = 0;
        int checks = Mathf.Clamp(edgeChecksPerScan, 4, edgePoints.Length);

        for (int i = 0; i < checks; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, Random.Range(-100f, 100f), 0f) * forward;
            Vector3 point = viewerPosition + direction * Random.Range(10f, Mathf.Max(15f, edgeSearchRadius));
            if (!TryTerrainHeight(point, out float crest) ||
                !TryTerrainHeight(point - wind * 5f, out float upwind) ||
                !TryTerrainHeight(point + wind * 7f, out float downwind)) continue;

            float drop = upwind - downwind;
            // Place plumes at the lip, where the wind crosses high ground into a drop.
            if (drop < threshold || crest < downwind + drop * 0.55f ||
                Mathf.Abs(crest - upwind) > drop * 0.55f) continue;
            edgePoints[found] = new Vector3(point.x, crest + 0.35f, point.z);
            edgeStrengths[found] = Mathf.Clamp01(drop / (threshold * 3f));
            found++;
        }

        if (found == 0) return;
        int totalParticles = Mathf.Clamp(Mathf.CeilToInt(edgeDustPerSecond * 0.25f), 1, 24);
        for (int i = 0; i < totalParticles; i++)
        {
            int source = i % found;
            var particle = new ParticleSystem.EmitParams();
            particle.position = edgePoints[source] + acrossWind * Random.Range(-1.5f, 1.5f)
                + wind * Random.Range(-0.4f, 0.6f);
            particle.velocity = wind * (windSpeed * Random.Range(1.5f, 2.8f))
                + Vector3.up * Random.Range(0.25f, 1.2f);
            particle.startSize = Random.Range(0.55f, 1.0f) * (1f + edgeStrengths[source]);
            particle.startLifetime = Random.Range(2f, 4f);
            Color color = sandColor;
            color.a = Mathf.Clamp01(color.a * (1.2f + edgeStrengths[source]));
            particle.startColor = color;
            edgeDust.Emit(particle, 1);
        }
    }

    bool TryTerrainHeight(Vector3 position, out float height)
    {
        foreach (var terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            if (position.x < origin.x || position.x > origin.x + size.x ||
                position.z < origin.z || position.z > origin.z + size.z) continue;
            height = origin.y + terrain.SampleHeight(position);
            return true;
        }
        height = 0f;
        return false;
    }

    float GroundHeight(Vector3 position)
    {
        if (TryTerrainHeight(position, out float height)) return height;
        return Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 100f)
            ? hit.point.y : position.y - 1.6f;
    }

    void OnDisable()
    {
        if (dust != null) Destroy(dust.gameObject);
        if (edgeDust != null) Destroy(edgeDust.gameObject);
        if (heat != null) Destroy(heat.gameObject);
        if (dustMaterial != null) Destroy(dustMaterial);
        if (heatMaterial != null) Destroy(heatMaterial);
        dust = null;
        edgeDust = null;
        heat = null;
    }
}
