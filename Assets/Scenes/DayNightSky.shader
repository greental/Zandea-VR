Shader "KenshiVR/DayNightSky"
{
    Properties
    {
        _DayColor ("Day Sky", Color) = (0.33,0.57,0.87,1)
        _NightColor ("Night Sky", Color) = (0.006,0.012,0.04,1)
        [HideInInspector] _SunDirection ("Sun Direction", Vector) = (0,1,0,0)
        [HideInInspector] _MoonDirection ("Moon Direction", Vector) = (0,-1,0,0)
        [HideInInspector] _Daylight ("Daylight", Float) = 1
        [HideInInspector] _Stars ("Stars", Float) = 0
        [HideInInspector] _SunRadius ("Sun Radius", Float) = 0.013
        [HideInInspector] _MoonRadius ("Moon Radius", Float) = 0.095
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _DayColor, _NightColor;
            float3 _SunDirection, _MoonDirection;
            float _Daylight, _Stars, _SunRadius, _MoonRadius;

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.direction = i.positionOS.xyz;
                return o;
            }
            float hash(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 c = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash(float3(c, 19.0));
                float b = hash(float3(c + float2(1, 0), 19.0));
                float e = hash(float3(c + float2(0, 1), 19.0));
                float g = hash(float3(c + 1.0, 19.0));
                return lerp(lerp(a, b, f.x), lerp(e, g, f.x), f.y);
            }
            float crater(float2 uv, float2 center, float radius)
            {
                float r = length(uv - center) / radius;
                return -0.20 * (1.0 - smoothstep(0.35, 0.9, r))
                    + 0.10 * (smoothstep(0.78, 0.96, r) - smoothstep(1.02, 1.18, r));
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.direction);
                float horizon = smoothstep(-0.15, 0.4, d.y);
                float3 day = lerp(float3(0.88, 0.54, 0.35), _DayColor.rgb, horizon);
                float3 night = lerp(float3(0.025, 0.033, 0.067), _NightColor.rgb, horizon);
                float3 col = lerp(night, day, _Daylight);

                // Wider, softly filtered points avoid the subpixel shimmer in the old star field.
                float3 starCoord = d * 160.0;
                float3 cell = floor(starCoord);
                float chance = hash(cell);
                float3 offset = 0.25 + 0.5 * float3(hash(cell + 11.0), hash(cell + 29.0), hash(cell + 47.0));
                float dist = length(frac(starCoord) - offset);
                float starWidth = max(0.06, fwidth(dist));
                float star = step(0.985, chance) * (1.0 - smoothstep(0.16 - starWidth, 0.16 + starWidth, dist));
                col += star * _Stars * smoothstep(-0.08, 0.08, d.y) * float3(0.65, 0.76, 0.95);

                float sunDistance = length(d - normalize(_SunDirection));
                float sunAA = max(0.0007, fwidth(sunDistance));
                float sunDisc = 1.0 - smoothstep(_SunRadius - sunAA, _SunRadius + sunAA, sunDistance);
                float sunVisible = _Daylight * smoothstep(-0.02, 0.05, _SunDirection.y);
                col += sunVisible * (sunDisc * float3(3.8, 3.1, 1.9)
                    + exp(-sunDistance * 38.0) * float3(0.35, 0.19, 0.07));

                float3 moonDirection = normalize(_MoonDirection);
                float3 reference = abs(moonDirection.y) > 0.9 ? float3(1, 0, 0) : float3(0, 1, 0);
                float3 moonRight = normalize(cross(reference, moonDirection));
                float3 moonUp = cross(moonDirection, moonRight);
                float2 uv = float2(dot(d, moonRight), dot(d, moonUp)) / max(0.001, _MoonRadius);
                float r = length(uv);
                float moonAA = max(0.002, fwidth(r));
                float moonEdge = 1.0 - smoothstep(1.0 - moonAA, 1.0 + moonAA, r);
                float largeDetail = noise(uv * 4.0 + 8.3);
                float fineDetail = noise(uv * 18.0 - 12.7);
                float shade = 0.72 + 0.22 * largeDetail + 0.07 * fineDetail;
                shade += crater(uv, float2(-0.34, 0.24), 0.21);
                shade += crater(uv, float2(0.41, -0.12), 0.16);
                shade += crater(uv, float2(0.03, -0.48), 0.12);
                shade += 0.12 * sqrt(saturate(1.0 - r * r));
                float3 moon = shade * float3(0.83, 0.88, 1.0);
                float moonDot = dot(d, moonDirection);
                float moonVisibility = (1.0 - _Daylight) * smoothstep(-0.05, 0.08, _MoonDirection.y);
                col += exp((moonDot - 1.0) * 110.0) * moonVisibility * 0.18 * float3(0.35, 0.55, 1.0);
                col = lerp(col, moon, moonEdge * moonVisibility);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
