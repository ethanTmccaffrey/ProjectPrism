Shader "PRISM/SkullRipple"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (0.82, 0.80, 0.76, 1)
        _RippleAmplitude ("Ripple Amplitude", Float) = 0.4
        _RippleWavelength ("Ripple Wavelength (radians)", Float) = 0.9
        _RippleTintStrength ("Ripple Tint Strength", Range(0,1)) = 0.85
        _PaintEmissive ("Paint Emissive Boost", Float) = 1.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.15
        [Toggle] _Unlit ("Unlit", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off   

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

            #define MAX_RIPPLES 8

            float4 _RippleDir[MAX_RIPPLES];    
            float4 _RippleData[MAX_RIPPLES];  
            float4 _RippleColour[MAX_RIPPLES]; 

            float3 _HeadCentre;                
            float  _RippleAmplitude;
            float  _RippleWavelength;
            float  _RippleTintStrength;
            float  _PaintEmissive;

            float4 _BaseColor;
            float  _Smoothness;
            float  _Unlit;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 colour : COLOR;   
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 tint       : TEXCOORD2;
                float  tintWeight : TEXCOORD3;
                float4 vcolour    : TEXCOORD4;   
            };

            float RippleDisplacement(float3 dirFromCentre, out float3 tint, out float tintWeight)
            {
                float total = 0.0;
                tint = float3(0,0,0);
                tintWeight = 0.0;

                [unroll]
                for (int i = 0; i < MAX_RIPPLES; i++)
                {
                    float strength = _RippleDir[i].w;
                    if (strength <= 0.0) continue;

                    float3 origin = _RippleDir[i].xyz;
                    float age      = _RippleData[i].x;
                    float speed    = _RippleData[i].y; 
                    float lifetime = _RippleData[i].z;

                    float cosA = clamp(dot(dirFromCentre, origin), -1.0, 1.0);
                    float angle = acos(cosA);

                    float front = age * speed;
                    float phase = (front - angle) / _RippleWavelength;

                    float band = saturate(phase) * saturate(1.0 - (angle / 3.14159));

                    float ageFade = saturate(1.0 - age / lifetime);

                    float contribution = band * ageFade * strength;
                    total += sin(phase * 6.28318) * contribution;

                    float w = saturate(band * ageFade) * strength;
                    tint += _RippleColour[i].rgb * w;
                    tintWeight += w;
                }

                return total * _RippleAmplitude;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                float3 dirFromCentre = normalize(posWS - _HeadCentre);

                float3 tint; float tintWeight;
                float disp = max(RippleDisplacement(dirFromCentre, tint, tintWeight),0.0);
                posWS += nrmWS * disp;

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = nrmWS;
                OUT.tint = tint;
                OUT.tintWeight = saturate(tintWeight);
                OUT.vcolour = IN.colour;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 dx = ddx(IN.positionWS);
                float3 dy = ddy(IN.positionWS);
                float3 n = normalize(cross(dx, dy));

                if (_Unlit > 0.5)
                {
                    float paintA = smoothstep(0.35, 0.65, IN.vcolour.a);
                    float3 paintCol = IN.vcolour.rgb * _PaintEmissive;   
                    float3 base = lerp(_BaseColor.rgb, paintCol, paintA);
                    float3 c = lerp(base, IN.tint, saturate(IN.tintWeight) * _RippleTintStrength);
                    return half4(c, _BaseColor.a);
                }

                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(n, mainLight.direction));

                float paintA = smoothstep(0.35, 0.65, IN.vcolour.a);
                float3 paintCol = IN.vcolour.rgb * _PaintEmissive;  
                float3 painted = lerp(_BaseColor.rgb, paintCol, paintA);

                float3 ambient = SampleSH(n) * painted;
                float3 diffuse = painted * mainLight.color * ndotl;
                float3 lit = ambient + diffuse;

                lit = lerp(lit, IN.tint, saturate(IN.tintWeight) * _RippleTintStrength);

                return half4(lit, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}


