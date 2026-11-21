// Plays a sprite-sheet (flipbook) on a quad — your "animated gif" as a card. Unlit + HDR-tinted so it
// blooms through HorrorPostFX. Pick the blend mode in the material:
//   • Additive (Src=One, Dst=One)            — fire authored on a BLACK background (black → invisible).
//   • Alpha    (Src=SrcAlpha, Dst=OneMinusSrcAlpha) — fire authored with a transparent background.
// FlipbookAnimator drives _Frame per-instance (via MaterialPropertyBlock) so many torches share one
// material but animate out of sync. Set _Columns/_Rows to match your sheet grid.
Shader "Fluffterror/Flipbook"
{
    Properties
    {
        [MainTexture] _BaseMap ("Sprite Sheet", 2D) = "white" {}
        [HDR] _Color ("Tint (HDR)", Color) = (1, 1, 1, 1)
        _Columns ("Columns", Float) = 4
        _Rows ("Rows", Float) = 4
        _Frame ("Frame", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1   // One  (additive default)
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1   // One
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Flipbook"
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off          // billboard can face either way; show both sides

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float  _Columns;
                float  _Rows;
                float  _Frame;
                float  _SrcBlend;
                float  _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);

                float cols = max(_Columns, 1.0);
                float rows = max(_Rows, 1.0);
                float frame = floor(_Frame);
                float col = fmod(frame, cols);
                float row = floor(frame / cols);

                // Frame 0 = top-left. UV v=0 is the bottom, so flip the row order.
                float2 cell   = float2(1.0 / cols, 1.0 / rows);
                float2 offset = float2(col, (rows - 1.0 - row)) * cell;
                OUT.uv = IN.uv * cell + offset;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _Color;
            }
            ENDHLSL
        }
    }
}
