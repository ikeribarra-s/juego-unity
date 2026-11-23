Shader "Fluffterror/ToonOutline"
{
    // Cel-shaded (banded) lit shader + inverted-hull outline.
    //
    // Lighting: main light + EVERY additional light (point/spot torches, the carried lantern) +
    // ambient/SH, with real-time shadows and fog. Works under Forward, Forward+ and the URP 17
    // clustered light loop (_CLUSTER_LIGHT_LOOP) — the project renders in Forward+, where URP does
    // NOT enable _ADDITIONAL_LIGHTS, so a shader that only compiles its light loop under
    // _ADDITIONAL_LIGHTS never sees a single point light (that was the "skeleton is just dark" bug).
    //
    // Each light contributes  albedo * lightColor * lerp(_ShadowTint, 1, Band(N·L)) * attenuation * shadow,
    // i.e. the shadow tint is a fraction OF THE LIGHT, never a constant self-illumination: unlit
    // surfaces only get ambient, exactly like the environment's URP/Lit materials.
    Properties
    {
        [Header(Surface)]
        _BaseMap        ("Base Map",          2D)              = "white" {}
        _BaseColor      ("Base Color",         Color)          = (1, 1, 1, 1)

        [Header(Cel Shading)]
        _Steps          ("Light Bands",        Range(1, 6))    = 3
        _ShadowTint     ("Shadow Tint (fraction of light on the dark side)", Color) = (0.35, 0.35, 0.45, 1)
        _AmbientStrength("Ambient Strength",   Range(0, 2))    = 1
        _SpecColor2     ("Specular Color",     Color)          = (1, 1, 1, 1)
        _SpecSize       ("Specular Size",      Range(0, 1))    = 0.5
        _RimColor       ("Rim Color",          Color)          = (0, 0, 0, 0)
        _RimAmount      ("Rim Amount",         Range(0, 1))    = 0.3

        [Header(Outline)]
        _OutlineColor   ("Outline Color",      Color)          = (0, 0, 0, 1)
        _OutlineWidth   ("Outline Width",      Range(0, 0.05)) = 0.02

        [Header(First Person Cull)]
        _CullRadius     ("Camera Cull Radius (0 = off)", Float) = 0
        _HideHead       ("Hide Head (camera passes only; mask in UV3.x)", Float) = 0

        [Header(Emission)]
        [HDR] _EmissionColor ("Emission Color", Color)         = (0, 0, 0, 0)
        _EmissionIntensity   ("Emission Intensity", Float)     = 0.0
    }

    HLSLINCLUDE
        // Shared by every pass so the SRP Batcher sees one identical UnityPerMaterial layout.
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float  _Steps;
            float4 _ShadowTint;
            float  _AmbientStrength;
            float4 _SpecColor2;
            float  _SpecSize;
            float4 _RimColor;
            float  _RimAmount;
            float4 _OutlineColor;
            float  _OutlineWidth;
            float  _CullRadius;
            float  _HideHead;
            half4  _EmissionColor;
            half   _EmissionIntensity;
        CBUFFER_END

        // First-person hiding, camera-visible passes ONLY (Outline, ForwardLit, DepthOnly) — never the
        // ShadowCaster, so the local player's hidden head still casts its shadow.
        //  • _CullRadius: discard anything within that distance of the camera (chest stub).
        //  • _HideHead:   discard vertices skinned to the Head bone (or its children). The per-vertex head
        //                 weight is baked into UV3.x by FirstPersonBodyCull on the local player's mesh copy;
        //                 other meshes have no UV3 (reads 0) and are never cut.
        void ClipFirstPerson(float3 positionWS, float headMask)
        {
            clip(distance(positionWS, _WorldSpaceCameraPos) - _CullRadius);
            // Threshold 0.25: the skull's skin weights are spread with the neck (every skull vertex
            // measured >= 0.3 on the Head chain), so 0.5 left holes at the back of the skull.
            clip(0.25 - headMask * _HideHead);
        }
    ENDHLSL

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
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv3 : TEXCOORD3; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; float headMask : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionWS = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                OUT.headMask   = IN.uv3.x;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                ClipFirstPerson(IN.positionWS, IN.headMask);
                return half4(MixFog(_OutlineColor.rgb, IN.fogFactor), _OutlineColor.a);
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
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv3        : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                float  headMask   : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.headMask   = IN.uv3.x;
                OUT.fogFactor  = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            // Quantize a 0..1 value into _Steps hard bands.
            float Band(float v)
            {
                float steps = max(_Steps, 1.0);
                return floor(saturate(v) * steps) / steps;
            }

            // Cel response of one light: full light on the lit bands, _ShadowTint × light on the dark side.
            // Rendering-layer filter (URP Light Layers are ON in PC_RPAsset): a light only reaches this
            // renderer if their layers overlap — used to keep the held lantern's own light off the
            // first-person arm/lantern (see HeldTorchLighting).
            half3 CelLight(Light l, float3 N)
            {
                #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(l.layerMask, GetMeshRenderingLayer())) return half3(0, 0, 0);
                #endif
                float ramp = Band(dot(N, l.direction));
                half3 shade = lerp(_ShadowTint.rgb, half3(1, 1, 1), ramp);
                return l.color * shade * (l.distanceAttenuation * l.shadowAttenuation);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // First-person cull (chest stub near the camera + the local player's head).
                ClipFirstPerson(IN.positionWS, IN.headMask);

                half4  albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                float3 N = normalize(IN.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // The clustered light loop macros read inputData.positionWS / normalizedScreenSpaceUV.
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS   = N;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                // ── Main light ──
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = float4(inputData.normalizedScreenSpaceUV, 0, 1);
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #endif
                Light  mainLight = GetMainLight(shadowCoord);
                half3  lightSum  = CelLight(mainLight, N);

                // Cel specular (main light only)
                float  mainVis = Band(dot(N, mainLight.direction)) * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer())) mainVis = 0;
                #endif
                float3 H       = normalize(mainLight.direction + V);
                float  ndh     = saturate(dot(N, H));
                float  specRaw = pow(ndh, exp2(lerp(1.0, 11.0, 1.0 - _SpecSize)));
                half3  specular = _SpecColor2.rgb * mainLight.color * step(0.5, specRaw) * mainVis;

                // ── Additional lights: torches, lanterns, generator status lights … ──
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX) || USE_CLUSTER_LIGHT_LOOP
                    half4 shadowMask = half4(1, 1, 1, 1);
                    #if USE_CLUSTER_LIGHT_LOOP
                        // Forward+: extra directional lights are listed before the clustered ones.
                        [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                        {
                            Light dl = GetAdditionalLight(dirIndex, IN.positionWS, shadowMask);
                            lightSum += CelLight(dl, N);
                        }
                    #endif
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light al = GetAdditionalLight(lightIndex, IN.positionWS, shadowMask);
                        lightSum += CelLight(al, N);
                    LIGHT_LOOP_END
                #endif

                // Rim light (toon edge glow)
                float rim = 1.0 - saturate(dot(N, V));
                rim = smoothstep(1.0 - _RimAmount, 1.0, rim);
                half3 rimC = _RimColor.rgb * rim;

                half3 ambient  = SampleSH(N) * _AmbientStrength;
                half3 emission = _EmissionColor.rgb * _EmissionIntensity;

                half3 color = albedo.rgb * (lightSum + ambient) + specular + rimC + emission;
                color = MixFog(color, IN.fogFactor);
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

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

            struct Attributes { float4 positionOS : POSITION; float2 uv3 : TEXCOORD3; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float headMask : TEXCOORD1; };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.headMask   = IN.uv3.x;
                return OUT;
            }
            half DepthFrag(Varyings IN) : SV_Target
            {
                ClipFirstPerson(IN.positionWS, IN.headMask); // no ghost head in the depth prepass / depth texture
                return IN.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
