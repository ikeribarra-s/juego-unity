Shader "Fluffterror/BlinnPhong"
{
    Properties
    {
        [MainTexture] _MainTex         ("Albedo", 2D)                     = "white" {}
        [MainColor]   _Color           ("Tint", Color)                    = (1, 1, 1, 1)
                      _BumpMap         ("Normal Map", 2D)                 = "bump"  {}
                      _BumpScale       ("Normal Scale", Float)            = 1.0
                      _Glossiness      ("Shininess", Range(8, 256))       = 32
                      _SpecularColor   ("Specular Color", Color)          = (1, 1, 1, 1)
                      _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.5
        [HDR]         _EmissionColor   ("Emission Color", Color)          = (0, 0, 0, 0)
                      _EmissionIntensity("Emission Intensity", Float)     = 0.0
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

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                half4  _Color;
                half   _BumpScale;
                half   _Glossiness;
                half4  _SpecularColor;
                half   _SpecularStrength;
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
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.normalWS    = norInputs.normalWS;
                OUT.tangentWS   = float4(norInputs.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.shadowCoord = GetShadowCoord(posInputs);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Albedo
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color;

                // Normal map → world space via TBN
                half3 normalTS     = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                float3 bitangentWS = cross(IN.normalWS, IN.tangentWS.xyz) * IN.tangentWS.w;
                float3 normalWS    = normalize(mul(normalTS, float3x3(IN.tangentWS.xyz, bitangentWS, IN.normalWS)));

                float3 viewDir = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // Screen-space ambient occlusion
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(IN.positionCS));
                    half ao = aoFactor.indirectAmbientOcclusion;
                #else
                    half ao = 1.0h;
                #endif

                // Spherical harmonics ambient
                half3 ambient = SampleSH(normalWS) * albedo.rgb * ao;

                // Main light — Blinn-Phong
                Light  mainLight = GetMainLight(IN.shadowCoord);
                float3 H         = normalize(mainLight.direction + viewDir);
                float  NdotL     = saturate(dot(normalWS, mainLight.direction));
                float  NdotH     = saturate(dot(normalWS, H));
                float  shadow    = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                half3 diffuse  = mainLight.color * NdotL * shadow;
                half3 specular = mainLight.color * _SpecularColor.rgb
                               * pow(NdotH, _Glossiness) * _SpecularStrength * shadow;

                // Additional lights
                #ifdef _ADDITIONAL_LIGHTS
                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light  addLight = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                    float3 Ha       = normalize(addLight.direction + viewDir);
                    float  NdotLa   = saturate(dot(normalWS, addLight.direction));
                    float  NdotHa   = saturate(dot(normalWS, Ha));
                    float  attn     = addLight.distanceAttenuation * addLight.shadowAttenuation;
                    diffuse  += addLight.color * NdotLa * attn;
                    specular += addLight.color * _SpecularColor.rgb
                              * pow(NdotHa, _Glossiness) * _SpecularStrength * attn;
                LIGHT_LOOP_END
                #endif

                half3 emission = _EmissionColor.rgb * _EmissionIntensity;
                half3 color    = albedo.rgb * diffuse + ambient + specular + emission;

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
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                half4  _Color;
                half   _BumpScale;
                half   _Glossiness;
                half4  _SpecularColor;
                half   _SpecularStrength;
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
                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS   = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 posCS    = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, lightDir));
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
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                half4  _Color;
                half   _BumpScale;
                half   _Glossiness;
                half4  _SpecularColor;
                half   _SpecularStrength;
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
