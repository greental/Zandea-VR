using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Drives the main directional light and a procedural day/night sky.</summary>
[RequireComponent(typeof(Light))]
public sealed class DayNightCycle : MonoBehaviour
{
    [Header("Clock")]
    [Range(0f, 24f)] public float timeOfDay = 12f;
    [Min(1f)] public float realMinutesPerDay = 20f;
    public bool advanceTime = true;
    [Range(-180f, 180f)] public float sunriseDirection = 0f;

    [Header("Sun and night")]
    [Min(0f)] public float daylightIntensity = 3.14f;
    [Range(0f, 1f)] public float nightAmbientIntensity = 0.12f;
    [Range(0f, 1f)] public float starBrightness = 0.9f;
    [Range(0.5f, 5f)] public float sunDiameterDegrees = 1.5f;
    [Range(2f, 18f)] public float moonDiameterDegrees = 11f;
    public Color daySky = new Color(0.33f, 0.57f, 0.87f);
    public Color nightSky = new Color(0.006f, 0.012f, 0.04f);

    Light sun;
    Material sky;
    Material previousSky;
    float previousAmbientIntensity;
    Light previousSun;

    public float NormalizedTime => Mathf.Repeat(timeOfDay, 24f) / 24f;

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        sun = GetComponent<Light>();
        if (sun.type != LightType.Directional) return;
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
        {
            Debug.LogError("DayNightCycle requires the Universal Render Pipeline. Use a URP scene and pipeline asset.", this);
            enabled = false;
            return;
        }
        var shader = Shader.Find("KenshiVR/DayNightSky");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError("DayNightSky shader is missing or unsupported. No lighting settings were changed.", this);
            enabled = false;
            return;
        }
        previousSky = RenderSettings.skybox;
        previousAmbientIntensity = RenderSettings.ambientIntensity;
        previousSun = RenderSettings.sun;
        sky = new Material(shader) { name = "Runtime Day Night Sky", hideFlags = HideFlags.DontSave };
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        Apply();
    }

    void Update()
    {
        if (Application.isPlaying && advanceTime)
            timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime * 24f / (realMinutesPerDay * 60f), 24f);
        Apply();
    }

    void OnValidate()
    {
        realMinutesPerDay = Mathf.Max(1f, realMinutesPerDay);
        timeOfDay = Mathf.Repeat(timeOfDay, 24f);
        if (Application.isPlaying && isActiveAndEnabled) Apply();
    }

    void Apply()
    {
        if (sun == null) sun = GetComponent<Light>();
        // At 06:00 the sun rises; at 12:00 it is overhead; at 18:00 it sets.
        float angle = (NormalizedTime - 0.25f) * 360f;
        transform.rotation = Quaternion.Euler(angle, sunriseDirection, 0f);
        Vector3 sunDirection = -transform.forward;
        float altitude = sunDirection.y;
        float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.12f, altitude));
        sun.intensity = daylightIntensity * daylight;
        sun.color = Color.Lerp(new Color(1f, 0.52f, 0.30f), Color.white, Mathf.InverseLerp(0f, 0.55f, altitude));
        RenderSettings.ambientIntensity = Mathf.Lerp(nightAmbientIntensity, 1f, daylight);
        if (sky == null) return;
        sky.SetVector("_SunDirection", sunDirection);
        sky.SetVector("_MoonDirection", -sunDirection);
        sky.SetColor("_DayColor", daySky);
        sky.SetColor("_NightColor", nightSky);
        sky.SetFloat("_Daylight", daylight);
        sky.SetFloat("_Stars", starBrightness * (1f - daylight));
        sky.SetFloat("_SunRadius", Mathf.Sin(sunDiameterDegrees * 0.5f * Mathf.Deg2Rad));
        sky.SetFloat("_MoonRadius", Mathf.Sin(moonDiameterDegrees * 0.5f * Mathf.Deg2Rad));
    }

    void OnDisable()
    {
        if (sky == null) return;
        if (sky != null && RenderSettings.skybox == sky) RenderSettings.skybox = previousSky;
        if (RenderSettings.sun == sun) RenderSettings.sun = previousSun;
        RenderSettings.ambientIntensity = previousAmbientIntensity;
        if (sky != null)
        {
            if (Application.isPlaying) Destroy(sky);
            else DestroyImmediate(sky);
        }
        sky = null;
    }
}
