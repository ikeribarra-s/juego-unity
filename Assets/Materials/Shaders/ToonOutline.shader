Shader "Fluffterror/ToonOutline"
{
    Properties
    {
        [Header(Surface)]
        _BaseMap        ("Base Map",          2D)              = "white" {}
        _BaseColor      ("Base Color",         Color)          = (1, 1, 1, 1)

        [Header(Cel Shading)]
        _Steps          ("Light Bands",        Range(1, 6))    = 3
        _ShadowTint     ("Shadow Tint",        Color)          = (0.35, 0.35, 0.45, 1)
        _SpecColor2     ("Specular Color",     Color)          = (1, 1, 1, 1)
        _SpecSize       ("Specular Size",      Range(0, 1))    = 0.5
        _RimColor       ("Rim Color",          Color)          = (0, 0, 0, 0)
        _RimAmount      ("Rim Amount",         Range(0, 1))    = 0.3

        [Header(Outline)]
        _OutlineColor   ("Outline Color",      Color)          = (0, 0, 0, 1)
        _OutlineWidth   ("Outline Width",      Range(0, 0.05)) = 0.02

        [Header(First Person Cull)]
        _CullRadius     ("Camera Cull Radius (0 = off)", Float) = 0

        [Header(Emission)]
        [HDR] _EmissionColor ("Emission Color", Color)         = (0, 0, 0, 0)
        _EmissionIntensity   ("Emission Intensity", Float)     = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // ── Outline (inverted hull) ──────────────────────────────────────────
        // Backfaces extruded along the normal, drawn solid. SRPDefaultUnlit is
        // rendered by URP in addition to the UniversalForward pass below.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Steps;
                float4 _ShadowTint;
                float4 _SpecColor2;
                float  _SpecSize;
                float4 _RimColor;
                float  _RimAmount;
                float4 _OutlineColor;
                float  _OutlineWidth;
                float  _CullRadius;
                half4  _EmissionColor;
                half   _EmissionIntensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionWS = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                clip(distance(IN.positionWS, _WorldSpaceCameraPos) - _CullRadius);
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ── Forward Lit (cel shaded) ─────────────────────────────────────────
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Steps;
                float4 _ShadowTint;
                float4 _SpecColor2;
                float  _SpecSize;
                float4 _RimColor;
                float  _RimAmount;
                float4 _OutlineColor;
                float  _OutlineWidth;
                float  _CullRadius;
                half4  _EmissionColor;
                half   _EmissionIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            // Quantize a 0..1 value into _Steps hard bands.
            float Band(float v)
            {
                float steps = max(_Steps, 1.0);
                return floor(saturate(v) * steps) / steps;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // First-person near-camera cull (hides the neck/chest the FP camera sits inside).
                clip(distance(IN.positionWS, _WorldSpaceCameraPos) - _CullRadius);

                half4  albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                float3 N = normalize(IN.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // ── Main light ──
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);
                float  ndl   = dot(N, mainLight.direction);
                float  toon  = Band(ndl) * mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                half3 baseLit = albedo.rgb * mainLight.color;
                half3 diffuse = lerp(albedo.rgb * _ShadowTint.rgb, baseLit, toon);

                // Cel specular
                float3 H       = normalize(mainLight.direction + V);
                float  ndh     = saturate(dot(N, H));
                float  specRaw = pow(ndh, exp2(lerp(1.0, 11.0, 1.0 - _SpecSize)));
                float  spec    = step(0.5, specRaw);
                half3  specular = _SpecColor2.rgb * spec * toon;

                // Rim light (toon edge glow)
                float rim = 1.0 - saturate(dot(N, V));
                rim = smoothstep(1.0 - _RimAmount, 1.0, rim);
                half3 rimC = _RimColor.rgb * rim;

                // ── Additional lights (Forward+) ──
                #ifdef _ADDITIONAL_LIGHTS
                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light al = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                    float aToon = Band(dot(N, al.direction)) * al.distanceAttenuation * al.shadowAttenuation;
                    diffuse += albedo.rgb * al.color * aToon;
                LIGHT_LOOP_END
                #endif

                half3 ambient  = SampleSH(N) * albedo.rgb;
                half3 emission = _EmissionColor.rgb * _EmissionIntensity;

                half3 color = diffuse + specular + rimC + ambient * 0.4h + emission;
                return half4(color, albedo.a);
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
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Steps;
                float4 _ShadowTint;
                float4 _SpecColor2;
                float  _SpecSize;
                float4 _RimColor;
                float  _RimAmount;
                float4 _OutlineColor;
                float  _OutlineWidth;
                float  _CullRadius;
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
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Steps;
                float4 _ShadowTint;
                float4 _SpecColor2;
                float  _SpecSize;
                float4 _RimColor;
                float  _RimAmount;
                float4 _OutlineColor;
                float  _OutlineWidth;
                float  _CullRadius;
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
