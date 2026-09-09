// 커스텀 카툰(셀) 셰이더 — URP용
// - N·L 기반 램프 셰이딩(하드 엣지 그림자)
// - 프레넬 기반 림 라이트
// - Inverted Hull 방식 아웃라인 (Pass 0, Cull Front)
// Shader Graph를 쓰지 않고 HLSL로 직접 작성해 라이팅 계산 원리를 보여주는 것이 목적.
Shader "ToonBossRush/ToonLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)

        _ShadowColor ("Shadow Tint", Color) = (0.55, 0.5, 0.65, 1) // 검정 대신 보색 톤
        _RampThreshold ("Ramp Threshold (N.L)", Range(-1,1)) = 0.15
        _RampSmoothness ("Ramp Softness", Range(0.001, 0.5)) = 0.05

        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0.1, 8)) = 3
        _RimIntensity ("Rim Intensity", Range(0,2)) = 0.6

        _HitFlashColor ("Hit Flash Color", Color) = (1,1,1,1)
        _HitFlashAmount ("Hit Flash Amount", Range(0,1)) = 0 // 코드에서 히트 시 0->1->0 으로 조작

        _OutlineColor ("Outline Color", Color) = (0.1,0.1,0.12,1)
        _OutlineWidth ("Outline Width", Range(0.0, 0.05)) = 0.012
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ---------- Pass 0: 아웃라인 (Inverted Hull) ----------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                // 카메라 거리에 따라 두께가 일정해 보이도록 클립 공간 기준으로 살짝 보정
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                float distToCam = distance(positionWS, _WorldSpaceCameraPos);
                float width = _OutlineWidth * saturate(distToCam * 0.15 + 0.5);

                positionWS += normalWS * width;
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ---------- Pass 1: 카툰 라이팅 ----------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex LitVert
            #pragma fragment LitFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float _RampThreshold;
                float _RampSmoothness;
                float4 _RimColor;
                float _RimPower;
                float _RimIntensity;
                float4 _HitFlashColor;
                float _HitFlashAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            Varyings LitVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 LitFrag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - IN.positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float NdotL = dot(normalWS, mainLight.direction);

                // ---- 램프 셰이딩: 하드 엣지에 약간의 스무스스텝만 섞어 앤티에일리어싱 ----
                float ramp = smoothstep(_RampThreshold - _RampSmoothness, _RampThreshold + _RampSmoothness, NdotL);
                ramp *= mainLight.shadowAttenuation; // 리얼타임 그림자도 같은 하드엣지로 취급

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 baseColor = texColor.rgb * _BaseColor.rgb;

                half3 litColor = baseColor * mainLight.color;
                half3 shadowedColor = baseColor * _ShadowColor.rgb;
                half3 color = lerp(shadowedColor, litColor, ramp);

                // ---- 림 라이트 (프레넬) ----
                float fresnel = pow(saturate(1.0 - dot(normalWS, viewDirWS)), _RimPower);
                float rimMask = fresnel * saturate(NdotL * 0.5 + 0.5); // 빛을 등진 실루엣에서만 과하게 뜨지 않도록
                color += _RimColor.rgb * rimMask * _RimIntensity;

                // ---- 피격 플래시 ----
                color = lerp(color, _HitFlashColor.rgb, _HitFlashAmount);

                return half4(color, texColor.a * _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
