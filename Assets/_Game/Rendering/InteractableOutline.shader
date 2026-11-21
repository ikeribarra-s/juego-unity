// Inverted-hull outline for interaction highlights. Drawn by InteractableOutline.cs as a clone of
// the target's meshes. Cull Front draws the back faces only, expanded along the silhouette normal in
// clip space (so the border thickness is constant in screen pixels regardless of the model's scale
// or distance — important here because the generator model is uniformly scaled ~9x). URP-compatible.
Shader "Hidden/InteractableOutline"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (1, 0.82, 0.35, 1)
        _OutlineWidth ("Outline Width (screen px-ish)", Range(0, 30)) = 6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+1" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS);

                // World-space normal projected into clip space, then push the vertex along it.
                // Scaling by positionCS.w keeps the offset a constant fraction of the screen.
                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP, nrmInputs.normalWS);
                float4 posCS = posInputs.positionCS;
                float2 offset = normalize(normalCS.xy + 1e-5) * (_OutlineWidth * 0.001) * posCS.w;
                posCS.xy += offset;

                OUT.positionCS = posCS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
