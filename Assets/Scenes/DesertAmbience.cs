using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Camera-local desert dust and daytime heat shimmer for large terrain scenes.</summary>
public sealed class DesertAmbience : MonoBehaviour
{
    public enum Quality { Low, Medium, High, Ultra }
    public enum Weather { Calm, Breeze, Windy, Sandstorm, MiddayHeat }
    [Header("Atmosphere controls")]
    public Quality quality = Quality.High;
    public Weather preset = Weather.Breeze;
    public bool enableSaltation = true, enableSurfaceSheets = true, enableSuspendedDust = true;
    public bool enableHaze = true;
    [Range(0f, 3f)] public float sandTransportDensity = 1f;
    [Range(0f, 3f)] public float surfaceSheetCoverage = 1f;
    [Range(0f, 3f)] public float airborneDustDensity = 1f;
    [Range(0f, 3f)] public float atmosphericHaze = 1f;
    [Range(0f, 2f)] public float heatIntensity = 1f;
    [Range(0f, 2f)] public float mirageIntensity = 1f;
    [Min(50f)] public float maximumVisibilityDistance = 1800f;
    [Range(0.01f, 0.5f)] public float gustFrequency = 0.09f;
    [Range(0f, 2f)] public float gustIntensity = 0.7f;
    [Range(0.01f, 1f)] public float turbulenceScale = 0.08f;
    [Range(0f, 1f)] public float inferiorMirage = 0.15f;
    [Header("Localized crest pickup")]
    [Range(1, 8)] public int maximumActivePatches = 4;
    [Min(0.5f)] public float gustDuration = 3.5f;
    [Min(1f)] public float gustInterval = 7f;
    [Range(0f, 1f)] public float crestActivationChance = 0.45f;
    [Range(0.1f, 3f)] public float minimumCrestRise = 0.18f;
    [Range(0.5f, 5f)] public float patchRadius = 2.2f;
    [Header("Windblown sand")]
    [Min(0f)] public float dustParticlesPerSecond = 60f;
    [Min(0f)] public float windSpeed = 2.5f;
    public Vector2 windDirection = new Vector2(1f, 0.35f);
    public Color sandColor = new Color(0.79f, 0.66f, 0.45f, 0.42f);
    [Min(1f)] public float maxHeightAboveGround = 250f;

    [Header("Crest search")]
    [Range(15f, 100f)] public float edgeSearchRadius = 55f;
    [Range(4, 48)] public int edgeChecksPerScan = 28;

    [Header("Daytime mirage")]
    [Min(0f)] public float heatPatchesPerSecond = 4f;
    public bool enableMirage = true;

    Camera viewer;
    DayNightCycle cycle;
    ParticleSystem dust;
    ParticleSystem grains;
    ParticleSystem sheets;
    ParticleSystem heat;
    Material dustMaterial;
    Material heatMaterial;
    float nextCameraSearch;
    float nextPlacement;
    float nextTerrainRefresh;
    float nextEdgeScan;
    float nextGust;
    float gustEnd;
    struct SandPatch { public Vector3 position; public float starts; public float expires; public float strength; }
    readonly SandPatch[] patches = new SandPatch[8];
    float previousFogDensity;
    Color previousFogColor;
    bool previousFogEnabled;
    bool ownsFog;
    Terrain[] terrains;

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
        grains = CreateSystem("Saltating Grains", dustMaterial, 800);
        ConfigureGrains(grains);
        sheets = CreateSystem("Surface Sand Sheets", dustMaterial, 180);
        ConfigureSheets(sheets);

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
        grains.Play();
        sheets.Play();
        if (heat != null) heat.Play();
        previousFogEnabled = RenderSettings.fog;
        previousFogDensity = RenderSettings.fogDensity;
        previousFogColor = RenderSettings.fogColor;
        ownsFog = !previousFogEnabled;
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
        main.scalingMode = ParticleSystemScalingMode.Local;
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
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.12f);
        main.startSpeed = 0f;
        main.startColor = sandColor;
        var emission = system.emission;
        emission.rateOverTime = 0f;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.enabled = false;
        var velocity = system.velocityOverLifetime;
        velocity.enabled = false;
        var noise = system.noise;
        noise.enabled = false;
        SetSoftLifetime(system);
    }

    void ConfigureGrains(ParticleSystem system)
    {
        var main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
        main.startSpeed = 0f;
        main.gravityModifier = 0.3f;
        var shape = system.shape;
        shape.enabled = false;
        var emission = system.emission;
        emission.rateOverTime = 0f;
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 0.35f;
        renderer.velocityScale = 0f;
        SetSoftLifetime(system);
    }

    void ConfigureSheets(ParticleSystem system)
    {
        var main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
        main.startSpeed = 0f;
        var shape = system.shape;
        shape.enabled = false;
        var emission = system.emission;
        emission.rateOverTime = 0f;
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 1.25f;
        renderer.velocityScale = 0f;
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
        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.35f), new Keyframe(0.2f, 1f),
            new Keyframe(0.75f, 0.8f), new Keyframe(1f, 0f)));
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
        if (viewer == null ||
            (TryTerrainHeight(viewer.transform.position, out float groundY) &&
             viewer.transform.position.y - groundY > maxHeightAboveGround))
        {
            var dustEmission = dust.emission;
            dustEmission.rateOverTime = 0f;
            if (enableHaze && ownsFog) RenderSettings.fog = previousFogEnabled;
            if (heat != null)
            {
                var heatEmission = heat.emission;
                heatEmission.rateOverTime = 0f;
            }
            return;
        }

        Vector3 viewerPosition = viewer.transform.position;
        // World-space particles stay put when these renderers follow the viewer for reliable culling.
        grains.transform.position = viewerPosition;
        sheets.transform.position = viewerPosition;
        dust.transform.position = viewerPosition;
        float time = Time.time;
        float gust = Mathf.PerlinNoise(time * gustFrequency, 11.3f);
        float strength = Mathf.Max(0f, windSpeed * (0.6f + gustIntensity * gust));
        Vector2 direction2 = windDirection.sqrMagnitude > 0.001f ? windDirection.normalized : Vector2.right;
        Vector3 wind = new Vector3(direction2.x, 0f, direction2.y) * strength;
        float density = quality == Quality.Low ? 0.35f : quality == Quality.Medium ? 0.65f : quality == Quality.Ultra ? 1.4f : 1f;
        if (enableHaze && ownsFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = Color.Lerp(previousFogColor, sandColor, 0.45f);
            RenderSettings.fogDensity = atmosphericHaze * airborneDustDensity * (0.00035f + strength / Mathf.Max(100f, maximumVisibilityDistance * 800f));
        }
        if (Time.time >= nextGust)
        {
            gustEnd = Time.time + gustDuration;
            nextGust = Time.time + Mathf.Max(gustDuration + 1f, gustInterval) * Random.Range(0.8f, 1.3f);
        }
        if (Time.time < gustEnd && Time.time >= nextEdgeScan)
        {
            nextEdgeScan = Time.time + 0.6f;
            FindCrestPatches(viewerPosition, wind.normalized);
        }
        for (int i = 0; i < Mathf.Min(maximumActivePatches, patches.Length); i++)
        {
            SandPatch patch = patches[i];
            if (patch.expires <= Time.time) continue;
            float fade = Mathf.Min(1f, Mathf.Min((Time.time - patch.starts) / 0.45f,
                (patch.expires - Time.time) / 0.75f));
            float lod = Vector3.Distance(viewerPosition, patch.position) > 45f ? 0.7f : 1f;
            float activity = density * patch.strength * fade * lod;
            EmitTerrainLayer(grains, patch.position, wind, activity * sandTransportDensity * dustParticlesPerSecond, patchRadius, 0.025f, 0.65f, enableSaltation, true);
            EmitTerrainLayer(sheets, patch.position, wind, activity * surfaceSheetCoverage * 65f, patchRadius, 0.035f, 0.72f, enableSurfaceSheets, false);
            EmitTerrainLayer(dust, patch.position, wind * 0.55f, activity * airborneDustDensity * 18f, patchRadius, 0.2f, 0.24f, enableSuspendedDust, false);
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

    void EmitTerrainLayer(ParticleSystem system, Vector3 center, Vector3 wind, float rate,
        float radius, float height, float alpha, bool enabledLayer, bool ballistic)
    {
        if (system == null || !enabledLayer || rate <= 0f) return;
        int count = Mathf.Min(48, Mathf.FloorToInt(rate * 0.12f + Random.value));
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 point = center + new Vector3(offset.x, 0f, offset.y);
            if (!TryTerrainHeight(point, out float ground)) continue;
            float coherent = Mathf.PerlinNoise(point.x * turbulenceScale + Time.time * gustFrequency,
                point.z * turbulenceScale + 3.1f);
            if (coherent < 0.32f) continue;
            float edgeFade = Mathf.Clamp01((radius - offset.magnitude) / Mathf.Max(2f, radius * 0.3f));
            var particle = new ParticleSystem.EmitParams();
            particle.position = new Vector3(point.x, ground + height + (ballistic ? Random.Range(0f, 0.09f) : 0f), point.z);
            Vector3 sideways = new Vector3(-wind.z, 0f, wind.x) * ((coherent - 0.5f) * 0.2f);
            particle.velocity = wind * Random.Range(0.7f, 1.25f) + sideways +
                Vector3.up * (ballistic ? Random.Range(0.15f, 0.4f) : 0f);
            particle.startColor = new Color(sandColor.r, sandColor.g, sandColor.b, alpha * coherent * edgeFade);
            system.Emit(particle, 1);
        }
    }

    void FindCrestPatches(Vector3 viewerPosition, Vector3 wind)
    {
        if (terrains == null || terrains.Length == 0 || wind.sqrMagnitude < 0.01f) return;
        Vector3 forward = Vector3.ProjectOnPlane(viewer.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        int checks = Mathf.Clamp(edgeChecksPerScan, 4, 48);
        for (int i = 0; i < checks; i++)
        {
            if (Random.value > crestActivationChance) continue;
            Vector3 direction = Quaternion.Euler(0f, Random.Range(-85f, 85f), 0f) * forward;
            Vector3 point = viewerPosition + direction * Random.Range(7f, Mathf.Max(25f, edgeSearchRadius * 1.25f));
            if (!TryTerrainHeight(point, out float crest) ||
                !TryTerrainHeight(point - wind * 3f, out float upwind) ||
                !TryTerrainHeight(point + wind * 3f, out float downwind) ||
                !TryTerrainHeight(point + Vector3.Cross(wind, Vector3.up) * 3f, out float across)) continue;
            float rise = crest - (upwind + downwind + across) / 3f;
            float leeDrop = crest - downwind;
            // Exposed shoulders also shed sand; an exact local maximum is too rare to sample.
            if (leeDrop < minimumCrestRise || rise < -minimumCrestRise * 0.5f) continue;
            int slot = -1;
            for (int j = 0; j < Mathf.Min(maximumActivePatches, patches.Length); j++)
            {
                if (patches[j].expires > Time.time &&
                    Vector3.Distance(patches[j].position, point) < patchRadius * 3f) { slot = -2; break; }
                if (slot < 0 && patches[j].expires <= Time.time) slot = j;
            }
            if (slot == -2 || slot < 0) continue;
            patches[slot] = new SandPatch
            {
                position = new Vector3(point.x, crest, point.z),
                starts = Time.time,
                expires = Mathf.Min(gustEnd + 0.8f, Time.time + Random.Range(1.6f, 3.5f)),
                strength = Mathf.Clamp(0.75f + Mathf.Max(rise, leeDrop) * 0.35f, 0.75f, 1.4f)
            };
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
        if (grains != null) Destroy(grains.gameObject);
        if (sheets != null) Destroy(sheets.gameObject);
        if (heat != null) Destroy(heat.gameObject);
        if (dustMaterial != null) Destroy(dustMaterial);
        if (heatMaterial != null) Destroy(heatMaterial);
        dust = null;
        grains = null;
        sheets = null;
        heat = null;
        if (ownsFog)
        {
            RenderSettings.fog = previousFogEnabled;
            RenderSettings.fogDensity = previousFogDensity;
            RenderSettings.fogColor = previousFogColor;
        }
    }
}
