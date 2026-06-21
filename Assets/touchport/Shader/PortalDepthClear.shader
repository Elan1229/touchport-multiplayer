// Shader "Custom/PortalDepthClear"
// {
//     SubShader
//     {
//         Tags
//         {
//             "RenderType" = "Opaque"
//             "Queue" = "Geometry"
//             "RenderPipeline" = "UniversalPipeline"
//         }
//         Pass
//         {
//             ColorMask 0      // 不写颜色，这一步只管深度
//             ZWrite On        // 要写深度
//             ZTest Always     // 不管原来深度是什么，无条件执行

//             HLSLPROGRAM
//             #pragma vertex vert
//             #pragma fragment frag
//             #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

//             struct appdata { float4 positionOS : POSITION; };
//             struct v2f { float4 positionHCS : SV_POSITION; };

//             v2f vert(appdata v)
//             {
//                 v2f o;
//                 o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
//                 return o;
//             }

//             // 强制把这个像素的深度写成"最远"，跟几何体实际位置无关
//             float frag(v2f i) : SV_Depth
//             {
//                 return UNITY_RAW_FAR_CLIP_VALUE;
//             }
//             ENDHLSL
//         }
//     }
// }

Shader "Custom/PortalDepthClear"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ColorMask 0
            ZWrite On
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            float frag(v2f i) : SV_Depth { return UNITY_RAW_FAR_CLIP_VALUE; }
            ENDHLSL
        }
    }
}