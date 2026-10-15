Shader "JunkerEquipment/OpticalEyepiece"
{
    Properties
    {
        _BaseMap ("Optical image", 2D) = "black" {}
        _ImmersiveView ("Black surround viewing mode", Float) = 0
        _ViewingRadius ("Viewing radius", Range(0.1, 0.49)) = 0.43
        _EdgeSoftness ("Viewing edge softness", Range(0.001, 0.05)) = 0.01
        _FlipHorizontal ("Rear lens horizontal correction", Float) = 1
        _ViewingFocusDistance ("Virtual viewing distance", Float) = 2
        _ViewingTangentRadius ("Shared angular radius", Float) = 0.5
        _ReticleEnabled ("Scope reticle", Float) = 0
        _ReticleColor ("Reticle color", Color) = (1, 0.35, 0.12, 0.9)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" "RenderType"="Opaque" }
        Pass
        {
            Name "OpticalEyepiece"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float _ImmersiveView;
                float _ViewingRadius;
                float _EdgeSoftness;
                float _FlipHorizontal;
                float _ViewingFocusDistance;
                float _ViewingTangentRadius;
                float4x4 _ViewingWorldToLocal;
                float _ReticleEnabled;
                float4 _ReticleColor;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                if (_ImmersiveView > 0.5)
                {
                    // Each stereo instance covers its own eye's viewport. UVs are
                    // screen-oriented, independent of the rear lens's rotation.
                    output.positionCS = float4(input.positionOS.xy * 2.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                    output.uv = input.positionOS.xy + 0.5;
                    // Match Unity's full-screen texture coordinates on D3D,
                    // Metal and Vulkan as well as OpenGL/multiview.
                    #if UNITY_UV_STARTS_AT_TOP
                        output.uv.y = 1.0 - output.uv.y;
                    #endif
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                    output.uv = input.uv;
                    if (_FlipHorizontal > 0.5) output.uv.x = 1.0 - output.uv.x;
                }
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
                float mask = 1.0;
                if (_ImmersiveView > 0.5)
                {
                    // A viewport's center is not necessarily its eye's forward
                    // axis. Undo the full asymmetric projection, including its
                    // principal point, before mapping onto ONE shared head-space
                    // plane. Eye separation now gives a coherent focus distance.
                    float uvYSign = 1.0;
                    #if UNITY_UV_STARTS_AT_TOP
                        uvYSign = -1.0;
                    #endif
                    float textureYSign = uvYSign * (UNITY_MATRIX_P._m11 < 0.0 ? -1.0 : 1.0);
                    float2 principalPoint = float2(UNITY_MATRIX_P._m02, uvYSign * UNITY_MATRIX_P._m12);
                    float2 tangent = ((uv * 2.0 - 1.0) + principalPoint) /
                        max(abs(float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11)), 0.0001);
                    float3 rayVS = float3(tangent.x, tangent.y * textureYSign, -1.0);
                    float3 rayWS = mul((float3x3)UNITY_MATRIX_I_V, rayVS);
                    float3 rayHead = mul((float3x3)_ViewingWorldToLocal, rayWS);
                    float3 originWS = mul(UNITY_MATRIX_I_V, float4(0, 0, 0, 1)).xyz;
                    float3 originHead = mul(_ViewingWorldToLocal, float4(originWS, 1)).xyz;
                    float focus = max(0.5, _ViewingFocusDistance);
                    float travel = (focus - originHead.z) / max(0.0001, rayHead.z);
                    float2 pointOnPlane = originHead.xy + rayHead.xy * travel;
                    // Keep the existing texture orientation; only stereo mapping
                    // changes. Both eyes use the same plane scale and source UVs.
                    pointOnPlane.y *= textureYSign;
                    float2 lens = pointOnPlane / (2.0 * focus * max(0.01, _ViewingTangentRadius));
                    mask = 1.0 - smoothstep(0.5 - _EdgeSoftness, 0.5, length(lens));
                    uv = lens + 0.5;
                }
                if (mask <= 0.0) return half4(0, 0, 0, 1);
                float3 image = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv)).rgb * mask;
                if (_ReticleEnabled > 0.5)
                {
                    float2 p = uv - 0.5;
                    float pixel = max(fwidth(uv.x), fwidth(uv.y));
                    float stroke = max(pixel * 1.2, 0.0009);
                    // Four separated posts, a fine range circle and a small center point.
                    float horizontal = (1.0 - smoothstep(stroke, stroke + pixel, abs(p.y))) *
                        smoothstep(0.018, 0.022, abs(p.x)) * (1.0 - smoothstep(0.16, 0.164, abs(p.x)));
                    float vertical = (1.0 - smoothstep(stroke, stroke + pixel, abs(p.x))) *
                        smoothstep(0.018, 0.022, abs(p.y)) * (1.0 - smoothstep(0.16, 0.164, abs(p.y)));
                    float ring = 1.0 - smoothstep(stroke, stroke + pixel, abs(length(p) - 0.095));
                    float center = 1.0 - smoothstep(0.0025, 0.0025 + pixel, length(p));
                    float reticle = saturate(max(max(horizontal, vertical), max(ring * 0.55, center)));
                    image = lerp(image, _ReticleColor.rgb, reticle * _ReticleColor.a);
                }
                return half4(image, 1.0);
            }
            ENDHLSL
        }
    }
}
