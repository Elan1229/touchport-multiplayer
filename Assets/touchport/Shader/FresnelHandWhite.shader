Shader "Custom/FresnelHandWhite"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseAlpha ("Base Alpha (center transparency)", Range(0,1)) = 0.12
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power (rim sharpness, larger-narrower)", Range(0.1, 8)) = 3.0
        _RimIntensity ("Rim Intensity", Range(0, 5)) = 2.0
        _FresnelAlpha ("Fresnel Alpha Boost (rim opacity-not transparent)", Range(0,1)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _BaseAlpha;
                float4 _RimColor;
                float  _RimPower;
                float  _RimIntensity;
                float  _FresnelAlpha;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS   = GetWorldSpaceViewDir(posInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);

                // Fresnel: 视线越贴近表面切线方向，值越接近1（边缘）
                float fresnel = 1.0 - saturate(dot(N, V));
                fresnel = pow(fresnel, _RimPower);

                float3 color = _BaseColor.rgb + _RimColor.rgb * fresnel * _RimIntensity;
                float  alpha = saturate(_BaseAlpha + fresnel * _FresnelAlpha);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
