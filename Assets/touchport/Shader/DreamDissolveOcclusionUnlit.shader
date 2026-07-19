// DreamDissolveOcclusionUnlit.shader
// URP unlit + Meta Depth API occlusion + dissolve。
// Lit 版的减法: 不算光,albedo × color 直出(可选 emission 叠加)。
// dissolve / occlusion / stereo 逻辑与 Lit 版完全一致,_DissolveProgress=0 时
// 视觉等同普通 occlusion unlit。参数名与 Lit 版对齐,驱动脚本可无差别对待两种材质。
Shader "DreamTouch/DissolveOcclusionUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor]   _BaseColor ("Color", Color) = (1,1,1,1)

        _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)

        [Header(Dissolve)]
        _DissolveNoise ("Dissolve Noise (R)", 2D) = "white" {}
        _DissolveProgress ("Dissolve Progress", Range(0,1)) = 0
        _DissolveEdgeWidth ("Edge Width", Range(0.001, 0.3)) = 0.06
        [HDR] _DissolveEdgeColor ("Edge Color", Color) = (2, 1.2, 3, 1)

        [Header(Meta Depth Occlusion)]
        _EnvironmentDepthBias ("Environment Depth Bias", Float) = 0.0

        // World Labs 房间从外面也要能看到墙面 → 默认双面（Off）。
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            // ── stereo / instancing(Quest 必需)──
            #pragma multi_compile_instancing

            // ── Meta Depth API occlusion ──
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

            // ── dissolve 只在换梦过渡期间由全局 keyword 开启(ChangeDreamByGift 控制)。
            //    平时走无 clip 的变体:shader 里只要存在 discard,GPU 就得关 early-Z/隐面剔除,
            //    全屏大 mesh 会把每个像素的 fragment 全跑一遍——这是性能命门。──
            #pragma multi_compile _ _DREAM_DISSOLVING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

            TEXTURE2D(_BaseMap);       SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap);   SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_DissolveNoise); SAMPLER(sampler_DissolveNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _DissolveNoise_ST;
                half4  _BaseColor;
                half4  _EmissionColor;
                half4  _DissolveEdgeColor;
                half   _DissolveProgress;
                half   _DissolveEdgeWidth;
                float  _EnvironmentDepthBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;   // occlusion 宏需要世界坐标
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO       // required to support stereo
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);   // required to support stereo

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.uv         = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);   // required to support stereo

            #if defined(_DREAM_DISSOLVING)
                // ── dissolve clip(与 Lit 版同一套 remap;仅过渡期间编译进来)──
                float2 noiseUV = i.uv * _DissolveNoise_ST.xy + _DissolveNoise_ST.zw;
                half noise = SAMPLE_TEXTURE2D(_DissolveNoise, sampler_DissolveNoise, noiseUV).r;
                half threshold = lerp(-0.0001h, 1.0001h + _DissolveEdgeWidth, _DissolveProgress);
                clip(noise - threshold);
            #endif

                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                color.rgb += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;

            #if defined(_DREAM_DISSOLVING)
                // 溶解边缘亮边(progress=0 时关死)
                half edge = (1.0h - smoothstep(threshold, threshold + _DissolveEdgeWidth, noise))
                            * step(0.0001h, _DissolveProgress);
                color.rgb += _DissolveEdgeColor.rgb * edge;
            #endif
                color.a = 1.0h;

                // ── Meta occlusion ──
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(i.positionWS, color, _EnvironmentDepthBias);

                return color;
            }
            ENDHLSL
        }

        // URP 深度 pass(depth prepass / _CameraDepthTexture / depth priming 都取这里)。
        // 溶掉的部分同样不写深度,遮挡关系跟颜色 pass 一致。
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #pragma multi_compile _ _DREAM_DISSOLVING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DissolveNoise); SAMPLER(sampler_DissolveNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _DissolveNoise_ST;
                half4  _BaseColor;
                half4  _EmissionColor;
                half4  _DissolveEdgeColor;
                half   _DissolveProgress;
                half   _DissolveEdgeWidth;
                float  _EnvironmentDepthBias;
            CBUFFER_END

            struct AttributesD
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsD
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VaryingsD vertDepth (AttributesD v)
            {
                VaryingsD o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half fragDepth (VaryingsD i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            #if defined(_DREAM_DISSOLVING)
                float2 noiseUV = i.uv * _DissolveNoise_ST.xy + _DissolveNoise_ST.zw;
                half noise = SAMPLE_TEXTURE2D(_DissolveNoise, sampler_DissolveNoise, noiseUV).r;
                half threshold = lerp(-0.0001h, 1.0001h + _DissolveEdgeWidth, _DissolveProgress);
                clip(noise - threshold);
            #endif
                return 0;
            }
            ENDHLSL
        }

        // 溶掉的部分不再投影(不用实时阴影可删)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile _ _DREAM_DISSOLVING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_DissolveNoise); SAMPLER(sampler_DissolveNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _DissolveNoise_ST;
                half4  _BaseColor;
                half4  _EmissionColor;
                half4  _DissolveEdgeColor;
                half   _DissolveProgress;
                half   _DissolveEdgeWidth;
                float  _EnvironmentDepthBias;
            CBUFFER_END

            float3 _LightDirection;

            struct AttributesS
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsS
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VaryingsS vertShadow (AttributesS v)
            {
                VaryingsS o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS   = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 fragShadow (VaryingsS i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            #if defined(_DREAM_DISSOLVING)
                float2 noiseUV = i.uv * _DissolveNoise_ST.xy + _DissolveNoise_ST.zw;
                half noise = SAMPLE_TEXTURE2D(_DissolveNoise, sampler_DissolveNoise, noiseUV).r;
                half threshold = lerp(-0.0001h, 1.0001h + _DissolveEdgeWidth, _DissolveProgress);
                clip(noise - threshold);
            #endif
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
