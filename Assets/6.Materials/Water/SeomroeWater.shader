// 관상용 물 (연못 · 웅덩이). URP 전용 · 반투명
// 깊이 텍스처로 물가는 투명하게 · 깊을수록 진하게, 두 겹 노멀이 흐르며 하늘(반사 프로브) · 해 · 등불 빛을 반사
Shader "SEOMROE/Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.22, 0.32, 0.28, 0.35)
        _DeepColor ("Deep Color", Color) = (0.04, 0.09, 0.09, 0.92)
        _DepthDistance ("Depth Distance", Float) = 0.9
        _EdgeFade ("Edge Fade", Float) = 0.12
        [Normal][NoScaleOffset] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.45
        _Tiling ("Tiling (world)", Float) = 0.22
        _FlowSpeed ("Flow Speed", Float) = 0.035
        _Reflection ("Reflection", Range(0, 1)) = 0.85
        _Roughness ("Reflection Roughness", Range(0, 1)) = 0.06
        _SpecPower ("Specular Power", Float) = 380
        _SpecStrength ("Specular Strength", Float) = 2.2
        _ShoreColor ("Shore Line Color", Color) = (0.75, 0.8, 0.72, 0.25)
        _ShoreWidth ("Shore Line Width", Float) = 0.06
        _PlanarDistort ("Planar Reflection Distort", Float) = 0.035
        _F0 ("Base Reflectance", Range(0, 1)) = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            // PlanarWaterReflection이 넘겨 주는 거울 반사 (없으면 반사 프로브)
            TEXTURE2D(_SeomroePlanarReflection); SAMPLER(sampler_SeomroePlanarReflection);
            float _SeomroePlanarOn;

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthDistance;
                float _EdgeFade;
                half _NormalStrength;
                float _Tiling;
                float _FlowSpeed;
                half _Reflection;
                half _Roughness;
                half _SpecPower;
                half _SpecStrength;
                half4 _ShoreColor;
                float _ShoreWidth;
                float _PlanarDistort;
                half _F0;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // 두 겹 노멀을 서로 다른 방향 · 크기로 흘려 겹침 → 반복 무늬가 덜 보임
            half3 WaterNormal(float2 xz)
            {
                float t = _Time.y * _FlowSpeed;
                float2 uv = xz * _Tiling;
                half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv + float2(t, t * 0.6)), _NormalStrength);
                half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 1.73 + float2(-t * 0.7, t * 1.1)), _NormalStrength * 0.7);
                half3 n = normalize(half3(a.xy + b.xy, a.z * b.z));
                return normalize(half3(n.x, n.z, n.y)); // 수평면: 접선 X · 법선 Y · 종접선 Z
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float thickness = max(0.0, sceneDepth - i.screenPos.w); // 물 표면 뒤 바닥까지 거리

                half3 n = WaterNormal(i.positionWS.xz);
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half deep = saturate(thickness / _DepthDistance);
                half4 water = lerp(_ShallowColor, _DeepColor, deep);

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = sun.shadowAttenuation;
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 col = water.rgb * (ambient + sun.color * shadow * saturate(sun.direction.y) * 0.35);

                // 반사: 프레넬로 비스듬히 볼수록 하늘이 비침
                half fresnel = _F0 + (1.0 - _F0) * pow(1.0 - saturate(dot(n, v)), 5.0);
                half3 r = reflect(-v, n);
                half3 env = GlossyEnvironmentReflection(r, i.positionWS, _Roughness, 1.0h, screenUV);
                if (_SeomroePlanarOn > 0.5)
                {
                    float2 ruv = screenUV + n.xz * _PlanarDistort;
                    env = SAMPLE_TEXTURE2D(_SeomroePlanarReflection, sampler_SeomroePlanarReflection, ruv).rgb;
                }
                col = lerp(col, env, saturate(fresnel * _Reflection));

                half3 h = normalize(sun.direction + v);
                col += sun.color * shadow * pow(saturate(dot(n, h)), _SpecPower) * _SpecStrength;

                // 등불 · 포탈 같은 점광원도 물에 반짝임
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
                inputData.normalizedScreenSpaceUV = screenUV;
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, i.positionWS);
                    half3 lh = normalize(light.direction + v);
                    half atten = light.distanceAttenuation * light.shadowAttenuation;
                    col += light.color * atten * (pow(saturate(dot(n, lh)), _SpecPower * 0.25) * _SpecStrength * 0.6 + water.rgb * 0.15);
                LIGHT_LOOP_END
                #endif

                // 물가: 얕은 곳은 투명하게 사라지고, 경계에 옅은 선
                half shore = 1.0 - saturate(thickness / max(0.001, _ShoreWidth));
                col = lerp(col, _ShoreColor.rgb, shore * _ShoreColor.a);
                half alpha = saturate(max(water.a, fresnel * _Reflection)) * saturate(thickness / max(0.001, _EdgeFade));

                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
