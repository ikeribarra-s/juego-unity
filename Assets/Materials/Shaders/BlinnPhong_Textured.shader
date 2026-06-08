Shader "BlinnPhong_Textured"
{
    Properties
    {
        [NoScaleOffset] _k_d_tex    ("Diffuse",     2D)    = "white" {}
        _k_d_coeff                  ("Diffuse coefficient",  Float) = 1.0
        [NoScaleOffset] _k_s_tex    ("Specular",    2D)    = "white" {}
        _k_s_coeff                  ("Specular coefficient", Float) = 1.0
        [Toggle] _IsRoughness       ("Is roughness map?",    Float) = 1
        [NoScaleOffset] _N_tex      ("Normal map",  2D)    = "bump"  {}
        _N_coeff                    ("Normal coefficient",   Float) = 1.0
        _n                          ("Shininess",            Float) = 32.0
        [HDR] _EmissionColor        ("Emission Color",       Color) = (0, 0, 0, 0)
        _EmissionIntensity          ("Emission Intensity",   Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // ── Forward Lit ──────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_k_d_tex); SAMPLER(sampler_k_d_tex);
            TEXTURE2D(_k_s_tex); SAMPLER(sampler_k_s_tex);
            TEXTURE2D(_N_tex);   SAMPLER(sampler_N_tex);

            CBUFFER_START(UnityPerMaterial)
                float  _k_d_coeff;
                float  _k_s_coeff;
                float  _IsRoughness;
                float  _N_coeff;
                float  _n;
                half4  _EmissionColor;
                half   _EmissionIntensity;
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
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float4 tangentWS   : TEXCOORD3; // w = bitangent sign
                float4 shadowCoord : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   norInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS  = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.uv          = IN.uv;
                OUT.normalWS    = norInputs.normalWS;
                OUT.tangentWS   = float4(norInputs.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.shadowCoord = GetShadowCoord(posInputs);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // ── Textures ─────────────────────────────────────────────────
                float4 k_d     = SAMPLE_TEXTURE2D(_k_d_tex, sampler_k_d_tex, IN.uv);

                // Roughness → specular conversion (preserves original formula)
                // IsRoughness=1: k_s = (1 - roughness)^2   IsRoughness=0: k_s = specular^2
                float4 k_s_raw = SAMPLE_TEXTURE2D(_k_s_tex, sampler_k_s_tex, IN.uv);
                float4 k_s     = pow(abs(_IsRoughness + (-2.0 * _IsRoughness + 1.0) * k_s_raw), 2.0);

                // Normal map — lerp from flat (0,0,1) to sampled, scaled by N_coeff
                // N_coeff > 1 exaggerates; N_coeff = 0 disables normal mapping
                half3 normalTS     = lerp(half3(0, 0, 1), UnpackNormal(SAMPLE_TEXTURE2D(_N_tex, sampler_N_tex, IN.uv)), _N_coeff);
                float3 bitangentWS = cross(IN.normalWS, IN.tangentWS.xyz) * IN.tangentWS.w;
                float3 N           = normalize(mul(normalTS, float3x3(IN.tangentWS.xyz, bitangentWS, IN.normalWS)));

                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // ── Screen-space AO ──────────────────────────────────────────
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(IN.positionCS));
                    half ao = aoFactor.indirectAmbientOcclusion;
                #else
                    half ao = 1.0h;
                #endif

                // Ambient (spherical harmonics)
                half3 ambient = SampleSH(N) * k_d.rgb * ao;

                // ── Main light — Blinn-Phong ─────────────────────────────────
                Light  mainLight = GetMainLight(IN.shadowCoord);
                float3 L         = mainLight.direction;
                float3 H         = normalize(L + V);
                float  NdotL     = saturate(dot(N, L));
                float  NdotH     = saturate(dot(N, H));
                float  shadow    = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                float3 diffuse  = k_d.rgb  * _k_d_coeff * NdotL  * shadow * mainLight.color;
                float3 specular = k_s.rgb  * _k_s_coeff * pow(NdotH, _n) * shadow * mainLight.color;

                // ── Additional lights ────────────────────────────────────────
                #ifdef _ADDITIONAL_LIGHTS
                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light  addLight  = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                    float3 La        = addLight.direction;
                    float3 Ha        = normalize(La + V);
                    float  NdotLa    = saturate(dot(N, La));
                    float  NdotHa    = saturate(dot(N, Ha));
                    float  attn      = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    diffuse  += k_d.rgb * _k_d_coeff * NdotLa * attn * addLight.color;
                    specular += k_s.rgb * _k_s_coeff * pow(NdotHa, _n) * attn * addLight.color;
                LIGHT_LOOP_END
                #endif

                // ── Emission (driven by EvidenceGlow via MaterialPropertyBlock)
                half3 emission = _EmissionColor.rgb * _EmissionIntensity;

                half3 color = ambient + diffuse + specular + emission;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ── Shadow Caster ────────────────────────────────────────────────────
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _k_d_coeff;
                float  _k_s_coeff;
                float  _IsRoughness;
                float  _N_coeff;
                float  _n;
                half4  _EmissionColor;
                half   _EmissionIntensity;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 posCS  = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, lightDir));
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

        // ── Depth Only ───────────────────────────────────────────────────────
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _k_d_coeff;
                float  _k_s_coeff;
                float  _IsRoughness;
                float  _N_coeff;
                float  _n;
                half4  _EmissionColor;
                half   _EmissionIntensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes IN) { Varyings OUT; OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz); return OUT; }
            half     DepthFrag(Varyings   IN) : SV_Target { return IN.positionCS.z; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
