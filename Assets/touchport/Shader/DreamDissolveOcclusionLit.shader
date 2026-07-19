// DreamDissolveOcclusionLit.shader
// URP PBR lit + Meta Depth API occlusion + dissolve, in one shader.
//
// 用法:
//   · 房间/礼物材质的 shader 指到 "DreamTouch/DissolveOcclusionLit"。
//     贴图槽位: _BaseMap(albedo) / _BumpMap(normal) / _MetallicGlossMap(metallic, smoothness在A通道)
//     / _EmissionMap — 对齐 Meshy 四贴图;房间单贴图只填 _BaseMap 即可。
//   · _DissolveProgress = 0 → 视觉等同普通 occlusion lit,平时就一直用它,不换 shader。
//   · 过渡时脚本驱动 _DissolveProgress 0→1(溶出)/ 1→0(凝入),
//     两个 world 各自房间 root 下用 MaterialPropertyBlock 批量设,互不干扰。
//   · occlusion 走 Meta 官方 custom-shader 集成(HARD/SOFT_OCCLUSION keyword 由
//     EnvironmentDepthManager 全局控制,和原来的 Meta Depth URP Lit 行为一致)。
//
// 依赖: com.meta.xr.sdk.core (EnvironmentOcclusionURP.hlsl)。
Shader "DreamTouch/DissolveOcclusionLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor]   _BaseColor ("Color", Color) = (1,1,1,1)

        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}

        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic   ("Metallic Scale", Range(0,1)) = 0
        _Smoothness ("Smoothness Scale", Range(0,1)) = 0.5

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
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            // ── stereo / instancing(Quest 必需)──
            #pragma multi_compile_instancing

            // ── URP 光照常用 keyword(按需精简)──
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            // ── Meta Depth API occlusion(官方 Step 1)──
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

            // ── dissolve 只在换梦过渡期间由全局 keyword 开启(ChangeDreamByGift 控制)。
            //    平时走无 clip 的变体:shader 里只要存在 discard,GPU 就得关 early-Z/隐面剔除,
            //    全屏大 mesh 会把每个像素的 fragment 全跑一遍——这是性能命门。──
            #pragma multi_compile _ _DREAM_DISSOLVING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

            TEXTURE2D(_BaseMap);          SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);          SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_EmissionMap);      SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_DissolveNoise);    SAMPLER(sampler_DissolveNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _DissolveNoise_ST;
                half4  _BaseColor;
                half4  _EmissionColor;
                half4  _DissolveEdgeColor;
                half   _Metallic;
                half   _Smoothness;
                half   _DissolveProgress;
                half   _DissolveEdgeWidth;
                float  _EnvironmentDepthBias;   // 官方 Step 4
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;   // 已含世界坐标 → 官方 Step 2/3 可跳过
                half3  normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;   // w = bitangent sign
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
                VertexNormalInputs   nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);

                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS   = nrm.normalWS;
                o.tangentWS  = half4(nrm.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv         = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);   // required to support stereo

            #if defined(_DREAM_DISSOLVING)
                // ── dissolve clip(仅过渡期间编译进来)──
                // threshold 在 progress=0 时为负 → 永不裁剪,与普通 shader 完全一致;
                // progress=1 时 > 1+edge → 全部裁光。
                float2 noiseUV = i.uv * _DissolveNoise_ST.xy + _DissolveNoise_ST.zw;
                half noise = SAMPLE_TEXTURE2D(_DissolveNoise, sampler_DissolveNoise, noiseUV).r;
                half threshold = lerp(-0.0001h, 1.0001h + _DissolveEdgeWidth, _DissolveProgress);
                clip(noise - threshold);
            #endif

                // ── surface ──
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half4 mg     = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv);
                half3 emis   = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;

            #if defined(_DREAM_DISSOLVING)
                // 溶解边缘亮边(progress=0 时被 step 关死,零残留)
                half edge = (1.0h - smoothstep(threshold, threshold + _DissolveEdgeWidth, noise))
                            * step(0.0001h, _DissolveProgress);
                emis += _DissolveEdgeColor.rgb * edge;
            #endif

                half3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                half3 nWS = normalize(mul(nTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));

                SurfaceData s = (SurfaceData)0;
                s.albedo     = albedo.rgb;
                s.metallic   = mg.r * _Metallic;
                s.smoothness = mg.a * _Smoothness;
                s.emission   = emis;
                s.occlusion  = 1.0h;
                s.alpha      = 1.0h;

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.normalWS   = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.bakedGI     = SampleSH(nWS);

                half4 color = UniversalFragmentPBR(d, s);

                // ── Meta occlusion(官方 Step 5,已有 worldPos 用 WORLDPOS 变体)──
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
                half   _Metallic;
                half   _Smoothness;
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

        // 溶掉的部分不再投影(没有实时阴影的话此 pass 闲置,无副作用)
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
                half   _Metallic;
                half   _Smoothness;
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
