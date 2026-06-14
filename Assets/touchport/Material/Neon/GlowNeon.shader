Shader "touchport/GlowNeon"
{
    Properties
    {
        [HDR] _Color         ("Color",         Color)  = (0, 1, 1, 1)
        _GlowIntensity       ("Glow Intensity", Float)  = 2.0
        [HDR] _RimColor      ("Rim Color",     Color)  = (0.5, 0, 1, 1)
        _RimIntensity        ("Rim Intensity",  Float)  = 3.0
        _FresnelPower        ("Fresnel Power",  Float)  = 3.0
        _PulseSpeed          ("Pulse Speed",    Float)  = 1.0

        [Header(Activated State)]
        // 注意：运行时 GlowNeonController.collisionIntensity 会覆盖此值；这里只是初始默认值
        _ActivatedIntensity  ("Activated Intensity", Float) = 5.0
        // 激活态颜色由代码计算：以 _Color 的 HSV Hue 为起点，以 PulseSpeed 同周期循环 Hue
        // _ActivatedColor 已移除
        // 0 = 正常呼吸; 1 = 完全激活; 中间值平滑过渡 (由 GlowNeonController 设置，勿手动修改)
        _Activated           ("Activated",           Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _GlowIntensity;
                float4 _RimColor;
                float  _RimIntensity;
                float  _FresnelPower;
                float  _PulseSpeed;
                float  _ActivatedIntensity;
                float  _Activated;
            CBUFFER_END

            // ── HSV 工具函数 ────────────────────────────────────────
            float3 RGBtoHSV(float3 c)
            {
                float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float  d = q.x - min(q.w, q.y);
                float  e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 HSVtoRGB(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs  = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS   = normInputs.normalWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 N = normalize(input.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));

                // ── 呼吸：[0.1, GlowIntensity] ──────────────────────
                float t01  = sin(_Time.y * _PulseSpeed) * 0.5 + 0.5;
                float pulse = lerp(0.1, _GlowIntensity, t01);

                // ── Fresnel 边缘光（两种状态共用）──────────────────
                float  fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);
                float3 rim     = _RimColor.rgb * _RimIntensity * fresnel;

                // ── 呼吸状态 ────────────────────────────────────────
                float3 breathingEmission = _Color.rgb * pulse + rim;

                // ── 激活状态：Hue 循环 ──────────────────────────────
                // 颜色不固定，以 _Color 的 HSV Hue 为起点不断循环。
                // S（饱和度）和 V（明度）保持 _Color 原值不变，只有 Hue 在变。
                // 循环周期 = 2π / _PulseSpeed，与呼吸 sine 完全同步。
                // 亮度由 _ActivatedIntensity 控制（运行时被 GlowNeonController.collisionIntensity 覆盖）。
                float3 baseHSV   = RGBtoHSV(_Color.rgb);
                float hueOffset  = frac(_Time.y * _PulseSpeed / (2.0 * 3.14159265));
                float3 cycleHSV  = float3(frac(baseHSV.x + hueOffset), baseHSV.y, baseHSV.z);
                float3 cycleRGB  = HSVtoRGB(cycleHSV);
                float3 activatedEmission = cycleRGB * _ActivatedIntensity + rim;

                // ── 混合：_Activated 0→1 平滑过渡 ──────────────────
                float3 emission = lerp(breathingEmission, activatedEmission, _Activated);

                float alpha = saturate(fresnel + 0.2);

                return half4(emission, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
