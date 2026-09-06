Shader "Custom/SeamlessWater2D"
{
    Properties
    {
        [MainColor] _Color ("Tint Color", Color) = (1, 1, 1, 1)
        [HDR] _WaterColor ("Water Color", Color) = (0.05, 0.85, 1.15, 1.0)
        _DeepWaterColor ("Deep Water Color", Color) = (0.04, 0.25, 0.38, 1.0)
        [HDR] _FoamColor ("Foam / Highlight Color", Color) = (1.2, 1.5, 1.8, 0.95)

        _WaterTexture ("Water Texture", 2D) = "white" {}
        _GradientNoise ("Gradient / Value Noise", 2D) = "white" {}

        _Tiling ("Water Tiling (X, Y)", Vector) = (0.5, 0.5, 0, 0)
        _WaveSpeed ("Wave Speed (X, Y)", Vector) = (-0.04, 0.02, 0, 0)
        _NoiseScale ("Noise Scale (X, Y)", Vector) = (0.15, 0.15, 0, 0)
        _DistortionStrength ("Distortion Strength", Range(0, 0.5)) = 0.14
        _WaveBlend ("Dual Wave Blend", Range(0, 1)) = 0.35
        _HighlightStrength ("Highlight / Sparkle Strength", Range(0, 2)) = 0.55
        _FoamGlow ("Foam Glow Multiplier", Range(1, 4)) = 1.6
        _ShallowGlow ("Shallow Water Glow Multiplier", Range(1, 3)) = 1.35

        [HideInInspector] _MainTex ("Sprite Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Unlit2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 worldPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            TEXTURE2D(_WaterTexture);
            SAMPLER(sampler_WaterTexture);

            TEXTURE2D(_GradientNoise);
            SAMPLER(sampler_GradientNoise);

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _WaterColor;
                float4 _DeepWaterColor;
                float4 _FoamColor;
                float4 _Tiling;
                float4 _WaveSpeed;
                float4 _NoiseScale;
                float _DistortionStrength;
                float _WaveBlend;
                float _HighlightStrength;
                float _FoamGlow;
                float _ShallowGlow;
                float4 _MainTex_ST;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.color = input.color * _Color;
                output.worldPos = vertexInput.positionWS.xy;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 wp = input.worldPos;
                float t = _Time.y;

                // 1. World-Space Noise Distortion (Seamless across all tiles)
                float2 noiseUV1 = wp * _NoiseScale.xy + t * _WaveSpeed.xy;
                float2 noiseUV2 = wp * _NoiseScale.xy * 1.4 - t * _WaveSpeed.yx * 0.8 + float2(0.42, 0.17);

                float4 noiseSample1 = SAMPLE_TEXTURE2D(_GradientNoise, sampler_GradientNoise, noiseUV1);
                float4 noiseSample2 = SAMPLE_TEXTURE2D(_GradientNoise, sampler_GradientNoise, noiseUV2);
                float2 combinedNoise = (noiseSample1.rg + noiseSample2.rg * 0.6 - 0.8) * _DistortionStrength;

                // 2. Sample Water Texture with Seamless World Coordinates
                float2 waterUV1 = wp * _Tiling.xy + combinedNoise + t * _WaveSpeed.xy * 0.5;
                float2 waterUV2 = wp * _Tiling.xy * 1.25 - combinedNoise * 0.7 - t * _WaveSpeed.yx * 0.4 + float2(0.5, 0.5);

                float4 waterTex1 = SAMPLE_TEXTURE2D(_WaterTexture, sampler_WaterTexture, waterUV1);
                float4 waterTex2 = SAMPLE_TEXTURE2D(_WaterTexture, sampler_WaterTexture, waterUV2);

                // Blend the two animated water layers
                float4 waterTex = lerp(waterTex1, waterTex2, _WaveBlend);

                // 3. Color Grading & Glowing Depth Tinting
                float lum = dot(waterTex.rgb, float3(0.299, 0.587, 0.114));
                float shallowFactor = saturate(lum * 1.4);
                
                // Glowing shallow water
                float4 glowingShallow = _WaterColor * _ShallowGlow;
                float4 baseGradient = lerp(_DeepWaterColor, glowingShallow, shallowFactor);
                float4 finalColor = baseGradient * waterTex;

                // Glowing foam highlights on wave crests & sparkles
                float highlight = saturate((waterTex1.r * waterTex2.r - 0.26) * 3.4) * _HighlightStrength;
                float4 glowingFoam = _FoamColor * _FoamGlow;
                finalColor = lerp(finalColor, glowingFoam, highlight);

                finalColor *= input.color;

                // Preserve alpha from sprite if present
                float4 spriteTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                finalColor.a *= spriteTex.a;

                return finalColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 worldPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            TEXTURE2D(_WaterTexture);
            SAMPLER(sampler_WaterTexture);

            TEXTURE2D(_GradientNoise);
            SAMPLER(sampler_GradientNoise);

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _WaterColor;
                float4 _DeepWaterColor;
                float4 _FoamColor;
                float4 _Tiling;
                float4 _WaveSpeed;
                float4 _NoiseScale;
                float _DistortionStrength;
                float _WaveBlend;
                float _HighlightStrength;
                float _FoamGlow;
                float _ShallowGlow;
                float4 _MainTex_ST;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.color = input.color * _Color;
                output.worldPos = vertexInput.positionWS.xy;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 wp = input.worldPos;
                float t = _Time.y;

                float2 noiseUV1 = wp * _NoiseScale.xy + t * _WaveSpeed.xy;
                float2 noiseUV2 = wp * _NoiseScale.xy * 1.4 - t * _WaveSpeed.yx * 0.8 + float2(0.42, 0.17);

                float4 noiseSample1 = SAMPLE_TEXTURE2D(_GradientNoise, sampler_GradientNoise, noiseUV1);
                float4 noiseSample2 = SAMPLE_TEXTURE2D(_GradientNoise, sampler_GradientNoise, noiseUV2);
                float2 combinedNoise = (noiseSample1.rg + noiseSample2.rg * 0.6 - 0.8) * _DistortionStrength;

                float2 waterUV1 = wp * _Tiling.xy + combinedNoise + t * _WaveSpeed.xy * 0.5;
                float2 waterUV2 = wp * _Tiling.xy * 1.25 - combinedNoise * 0.7 - t * _WaveSpeed.yx * 0.4 + float2(0.5, 0.5);

                float4 waterTex1 = SAMPLE_TEXTURE2D(_WaterTexture, sampler_WaterTexture, waterUV1);
                float4 waterTex2 = SAMPLE_TEXTURE2D(_WaterTexture, sampler_WaterTexture, waterUV2);

                float4 waterTex = lerp(waterTex1, waterTex2, _WaveBlend);

                float lum = dot(waterTex.rgb, float3(0.299, 0.587, 0.114));
                float shallowFactor = saturate(lum * 1.4);
                
                float4 glowingShallow = _WaterColor * _ShallowGlow;
                float4 baseGradient = lerp(_DeepWaterColor, glowingShallow, shallowFactor);
                float4 finalColor = baseGradient * waterTex;

                float highlight = saturate((waterTex1.r * waterTex2.r - 0.26) * 3.4) * _HighlightStrength;
                float4 glowingFoam = _FoamColor * _FoamGlow;
                finalColor = lerp(finalColor, glowingFoam, highlight);

                finalColor *= input.color;

                float4 spriteTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                finalColor.a *= spriteTex.a;

                return finalColor;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
