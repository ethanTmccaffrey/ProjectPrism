// PRISM skull ripple shader (URP)
//
// Displaces the skull surface outward along its normals in response to beats. A beat
// spawns ripples that sweep across the skull and settle, so the head is STRUCK rather than
// continuously animated - it resonates on the beat and is still between beats, which keeps
// the persistent-canvas principle that governs the rest of PRISM. The surface records
// events; it does not drift.
//
// Two origins fire per beat, at the left and right of the skull, their strength set by the
// stereo balance of the mix, so a wide track rings both sides and the waves interfere
// across the crown like vibration through a solid object.
//
// SURFACE-FOLLOWING BY ANGLE. True geodesic distance across arbitrary geometry is
// impractical per-vertex. On a roughly convex skull the ANGLE between a vertex direction
// and a ripple's origin direction (both measured from the head centre) approximates
// surface distance well, and travels around the skull rather than through it. It breaks in
// concavities - eye sockets, under the jaw - but on a mostly convex head that does not
// read.
//
// The C# feeder (SkullRipple.cs) fills the ripple arrays each frame from the analysed beat
// timeline.

Shader "PRISM/SkullRipple"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (0.82, 0.80, 0.76, 1)
        _RippleAmplitude ("Ripple Amplitude", Float) = 0.4
        _RippleWavelength ("Ripple Wavelength (radians)", Float) = 0.9
        _Smoothness ("Smoothness", Range(0,1)) = 0.15
        [Toggle] _Unlit ("Unlit", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off   // double-sided, so the far wall shows through the cut opening

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Up to 8 live ripples. Each: origin direction (xyz, unit, from head centre) and
            // birth-relative data packed in w slots via the paired array below.
            #define MAX_RIPPLES 8

            float4 _RippleDir[MAX_RIPPLES];    // xyz = origin direction, w = strength (0 = dead)
            float4 _RippleData[MAX_RIPPLES];   // x = age (seconds), y = speed (rad/sec), z = lifetime, w unused

            float3 _HeadCentre;                // world-space head centre for angle measurement
            float  _RippleAmplitude;
            float  _RippleWavelength;

            float4 _BaseColor;
            float  _Smoothness;
            float  _Unlit;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            // Displacement of one vertex, summed over all live ripples.
            float RippleDisplacement(float3 dirFromCentre)
            {
                float total = 0.0;

                [unroll]
                for (int i = 0; i < MAX_RIPPLES; i++)
                {
                    float strength = _RippleDir[i].w;
                    if (strength <= 0.0) continue;

                    float3 origin = _RippleDir[i].xyz;
                    float age      = _RippleData[i].x;
                    float speed    = _RippleData[i].y;   // radians per second the front travels
                    float lifetime = _RippleData[i].z;

                    // Angular distance from the ripple origin, in radians (0..pi).
                    float cosA = clamp(dot(dirFromCentre, origin), -1.0, 1.0);
                    float angle = acos(cosA);

                    // The wavefront is at (age * speed). Displacement is a sine wave in the
                    // gap between the front and this vertex, so the ring sweeps outward.
                    float front = age * speed;
                    float phase = (front - angle) / _RippleWavelength;

                    // Only ripple where the front has passed but not yet faded: a moving band.
                    float band = saturate(phase) * saturate(1.0 - (angle / 3.14159));

                    // Fade with age over the ripple's lifetime.
                    float ageFade = saturate(1.0 - age / lifetime);

                    total += sin(phase * 6.28318) * band * ageFade * strength;
                }

                return total * _RippleAmplitude;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                // Direction of this vertex from the head centre - the basis for angular
                // ripple distance.
                float3 dirFromCentre = normalize(posWS - _HeadCentre);

                float disp = max(RippleDisplacement(dirFromCentre),0.0);
                posWS += nrmWS * disp;

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = nrmWS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                //Flat shading independent of vertex normals: reconstruct the face normal
                //from the world-position derivatives across the triangle. This lets the
                //mesh weld its vertices (so it does not tear when displaced) while still
                //rendering crisp facets, since the normal comes from the face itself rather
                //than from averaged vertex normals.
                float3 dx = ddx(IN.positionWS);
                float3 dy = ddy(IN.positionWS);
                float3 n = normalize(cross(dx, dy));

                if (_Unlit > 0.5)
                    return _BaseColor;

                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(n, mainLight.direction));

                float3 ambient = SampleSH(n) * _BaseColor.rgb;
                float3 diffuse = _BaseColor.rgb * mainLight.color * ndotl;

                return half4(ambient + diffuse, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
