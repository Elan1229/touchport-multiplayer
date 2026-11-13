// 礼物换梦预告用的小光点：最便宜的 Additive unlit。单贴图采样、无光照、无雾、无深度写入。
// 透明队列——portal 的 stencil 管线只在 Transparent 的 RenderObjects pass 里画世界层的透明物体。
// 颜色和 alpha 都用 One One 叠加：alpha 也加上去，passthrough 上才看得见（Quest 用 eye buffer
// 的 alpha 决定盖不盖住现实）。
Shader "DreamTouch/HeraldGlow"
{
    Properties
    {
        _MainTex ("Glow Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 0.85, 0.5, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "HeraldGlow"
            Tags { "LightMode"="UniversalForward" }
            Blend One One, One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Intensity;
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

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half a = t.a * max(t.r, max(t.g, t.b));
                half3 c = t.rgb * t.a * _Color.rgb * _Intensity;
                return half4(c, saturate(a * _Intensity));
            }
            ENDHLSL
        }
    }
}
