Shader "Fluffterror/PS2Lit"
{
    // PS2-era environment shader for the authored map chunks (M_Chunks).
    //
    //  albedo = point-sampled atlas (T_Chunks_D, 64 px tileable cells) x _BaseColor x VERTEX COLOUR
    //  The vertex colour is the baked AO + grime + mottling from build_chunks.py / ps2_style.py (linear,
    //  1 m vertex resolution, like PS2 pre-lit geometry). _VertexColorStrength 0 ignores it.
    //  Lighting: plain Lambert (no specular) from the main light + EVERY additional light, real-time
    //  shadows, SH ambient, SSAO, fog. Forward / Forward+ (_CLUSTER_LIGHT_LOOP) / Light Layers, same
    //  structure as ToonOutline (see the CLAUDE.md "skeleton unlit" bug: Forward+ needs the cluster loop).
    //  _ColorDepth: optional 15/16-bit style output (levels per channel) with a 4x4 ordered dither,
    //  0 = off. Applied before fog so distance still fades smoothly.
    Properties
    {
        [MainTexture] _BaseMap ("Atlas (Point, no mips)", 2D)              = "white" {}
        [MainColor] _BaseColor ("Tint", Color)                            = (1, 1, 1, 1)
        _VertexColorStrength ("Baked Vertex AO Strength", Range(0, 1.5)) = 1
        _AmbientStrength("Ambient Strength", Range(0, 2))         = 1
        [Normal] _BumpMap ("Normal Atlas (Point, no mips)", 2D) = "bump" {}
        _BumpScale      ("Normal Strength", Range(0, 2)) = 1
        _NormalFadeNear ("Normal Fade Start (m)", Float) = 12
        _NormalFadeFar  ("Normal Fade End (m)", Float) = 28
        _ColorDepth     ("Colour Levels per Channel (0 = off)", Range(0, 64)) = 32
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _VertexColorStrength;
            half   _AmbientStrength;
            half   _ColorDepth;
            half   _BumpScale;
            float  _NormalFadeNear;
            float  _NormalFadeFar;
        CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half4  color      : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                float4 tangentWS  : TEXCOORD5;   // xyz tangent, w bitangent sign
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                float4 tOS = dot(IN.tangentOS.xyz, IN.tangentOS.xyz) < 1e-8 ? float4(1, 0, 0, 1) : IN.tangentOS;   // Meshy meshes have none
                VertexNormalInputs ni = GetVertexNormalInputs(IN.normalOS, tOS);
                OUT.normalWS   = ni.normalWS;
                OUT.tangentWS  = float4(ni.tangentWS, tOS.w * GetOddNegativeScale());
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color      = lerp(half4(1, 1, 1, 1), IN.color, _VertexColorStrength);
                OUT.fogFactor  = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half3 Lambert(Light l, float3 N)
            {
                #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(l.layerMask, GetMeshRenderingLayer())) return half3(0, 0, 0);
                #endif
                return l.color * (saturate(dot(N, l.direction)) * l.distanceAttenuation * l.shadowAttenuation);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4  albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor * IN.color;
                float3 Ng = normalize(IN.normalWS);
                float3 T  = IN.tangentWS.xyz;
                T = normalize(T - Ng * dot(T, Ng));
                float3 B  = cross(Ng, T) * IN.tangentWS.w;
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                float fade = 1.0 - saturate((distance(IN.positionWS, _WorldSpaceCameraPos) - _NormalFadeNear) / max(_NormalFadeFar - _NormalFadeNear, 0.01));
                nTS.xy *= fade;
                float3 N = normalize(mul(nTS, float3x3(T, B, Ng)));

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS   = N;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = float4(inputData.normalizedScreenSpaceUV, 0, 1);
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);

                half directAO = 1, indirectAO = 1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor aof = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                    directAO = aof.directAmbientOcclusion; indirectAO = aof.indirectAmbientOcclusion;
                #endif

                half3 lightSum = Lambert(mainLight, N);

                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX) || USE_CLUSTER_LIGHT_LOOP
                    half4 shadowMask = half4(1, 1, 1, 1);
                    #if USE_CLUSTER_LIGHT_LOOP
                        [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                        {
                            Light dl = GetAdditionalLight(dirIndex, IN.positionWS, shadowMask);
                            lightSum += Lambert(dl, N);
                        }
                    #endif
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light al = GetAdditionalLight(lightIndex, IN.positionWS, shadowMask);
                        lightSum += Lambert(al, N);
                    LIGHT_LOOP_END
                #endif

                half3 ambient = SampleSH(N) * _AmbientStrength * indirectAO;
                half3 color = albedo.rgb * (lightSum * directAO + ambient);

                // optional reduced colour depth with ordered dither (PS2 16-bit framebuffer feel)
                if (_ColorDepth >= 2)
                {
                    const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                    uint2 px = (uint2)IN.positionCS.xy & 3;
                    float d = (bayer[px.y * 4 + px.x] + 0.5) / 16.0 - 0.5;
                    half3 g = LinearToSRGB(saturate(color));
                    g = floor(g * (_ColorDepth - 1) + 0.5 + d) / (_ColorDepth - 1);
                    color = SRGBToLinear(saturate(g)) + max(color - 1, 0);   // keep HDR overbright for bloom
                }

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, lightDir));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = posCS;
                return OUT;
            }
            half4 ShadowFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half DepthFrag(Varyings IN) : SV_Target { return IN.positionCS.z; }
            ENDHLSL
        }

        // SSAO (PC_Renderer) samples depth + normals
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DNVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }
            half4 DNFrag(Varyings IN) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
