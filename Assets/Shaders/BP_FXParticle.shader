// Battle Plan — Shuriken particle shader for sparks, debris, dust and smoke cards.
//
// Masks are authored greyscale and tinted at runtime from the palette constants, so a palette
// change is a C# edit and never a texture re-export (ArtDirection §16). Vertex colour carries the
// per-particle hue ramp; the material carries the HDR magnitude, because particle vertex colours
// are only eight bits per channel.
//
// Three composite modes. Additive is sparks and hot debris on a dark board. Alpha is opaque smoke
// and dust. Multiply is dust and smoke on a LIGHT board, where a puff has to read by shading the
// floor rather than by out-glowing it — the same card, the same mask, a different material.
// See BP_FXComposite.hlsl.
Shader "BattlePlan/FXParticle"
{
    Properties
    {
        [MainTexture] _MainTex ("Mask (greyscale)", 2D) = "white" {}
        [HDR] _TintColor ("Tint (HDR)", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 16)) = 1
        _MaskContrast ("Mask Contrast", Range(0.25, 4)) = 1

        _NoiseTex ("Breakup Noise", 2D) = "white" {}
        _NoiseStrength ("Breakup Strength", Range(0, 1)) = 0
        _NoiseScrollSpeed ("Breakup Scroll", Float) = 0

        _SoftFadeDistance ("Soft Fade Distance (world)", Range(0.01, 4)) = 0.5

        [Header(Compositing)]
        [Enum(Additive, 0, Alpha, 1, Multiply, 2)] _CompositeMode ("Composite Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        [Toggle(_SOFTFADE_ON)] _SoftFade ("Soft Particle Fade", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _SOFTFADE_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BP_FXComposite.hlsl"
            #if defined(_SOFTFADE_ON)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NoiseTex_ST;
                half4 _TintColor;
                half _Intensity;
                half _MaskContrast;
                half _NoiseStrength;
                half _NoiseScrollSpeed;
                half _SoftFadeDistance;
                half _CompositeMode;
                half _SrcBlend;
                half _DstBlend;
                half _SoftFade;
            CBUFFER_END

            // Position/Normal/Color/UV is Shuriken's default vertex stream layout. NORMAL is
            // declared even though it is unused so the layout matches and the renderer does not
            // warn about a stream mismatch.
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float4 screenPos : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                OUT.screenPos = ComputeScreenPos(positions.positionCS);
                OUT.eyeDepth = -TransformWorldToView(positions.positionWS).z;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                mask = pow(saturate(mask), _MaskContrast);

                if (_NoiseStrength > 0.0)
                {
                    float2 noiseUV = TRANSFORM_TEX(IN.uv, _NoiseTex)
                        + float2(_Time.y * _NoiseScrollSpeed, _Time.y * _NoiseScrollSpeed * 0.6);
                    half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;
                    mask *= lerp(1.0, noise * 2.0, _NoiseStrength);
                }

                half coverage = saturate(mask) * _TintColor.a * IN.color.a;

                #if defined(_SOFTFADE_ON)
                    float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    coverage *= saturate((sceneDepth - IN.eyeDepth) / max(_SoftFadeDistance, 1e-4));
                #endif

                // Intensity rides the tint rather than the coverage, so a card stays as opaque as
                // its mask says it is in alpha mode and as dark as its tint says it is in multiply
                // mode, while additive still gets its HDR headroom. Fades come through _TintColor.a.
                half3 tint = _TintColor.rgb * IN.color.rgb * _Intensity;
                return BP_Composite(_CompositeMode, tint, coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
