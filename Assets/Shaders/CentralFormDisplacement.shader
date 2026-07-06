//PRISM Central Form Displacement Shader//
//Displaces sphere vertices on the GPU using realtime audio frequency data//
//Passed from MeshVolumeLayer.cs each frame//

//Vertext displacement strategy//
//The sphere surface is divided into three latitude bands//
//Poles (high latitudes) - displaced by high frequency energy//
//Mid latitudes - displaced by mid frequency energy//
//Equator (low latitudes) - displaced by bass energy//

//this means the form physically reshapes based on the frequency character of the music at each moment//
//A noise function modulates the displacement spatially so the surface feels organic rather than unifomrly inflated//


Shader "PRISM/CentralFormDisplacement"
{
    Properties
    {
        //Static Qualities set at Init//
        _BaseRadius ("Base Radius", Float) = 8.0
        _DisplacementStrength ("Displacement Strength", Float) = 3.0
        _FormQuality ("Form Quality", Float) = 0.5
        _SpaceQuality ("Space Quality", Float) = 0.5

        //Colours from PRISMGenerator//
        _PrimaryColour ("Primary Colour", Color) = (1, 0.5, 0.1, 1)
        _SecondaryColour ("Secondary Colour", Color) = (0.1, 0.3, 0.8, 1)
        _RealtimeColour ("Realtime Colour", Color) = (1, 1, 1, 1)

        //Realtime audio, updated every frame from C#//
        _Bass ("Bass Energy", Float) = 0.0
        _Mid ("Mid Energy", Float) = 0.0
        _High ("High Energy", Float) = 0.0
        _Energy ("Total Energy", Float) = 0.0
        _AudioTime ("Time", Float) = 0.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}
            
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert 
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            //Properties//
            CBUFFER_START(UnityPerMaterial)
                float  _BaseRadius;
                float  _DisplacementStrength; 
                float  _FormQuality;
                float  _SpaceQuality;
                float4 _PrimaryColour;
                float4 _SecondaryColour;
                float4 _RealtimeColour;
                float  _Bass;
                float  _Mid;
                float  _High;
                float  _Energy;
                float  _AudioTime;
            CBUFFER_END

            //Spectrum array: 64 floats, passed from c# via SetFloatArray//
            float _Spectrum[64];

            //Structs//
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float displacement : TEXCOORD3; //Pass displacement amount to fragment//
            };

            //Noise Functions//
            //Simple hash-based value noise, used to break up the displacement so it looks organic//
            float hash(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float smoothNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f); // Smoothstep

                return lerp(
                    lerp(lerp(hash(i + float3(0,0,0)), hash(i + float3(1,0,0)), u.x), lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), u.x), u.y),
                    lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), u.x), lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), u.x), u.y), u.z);
            }

            //Fractional Brownian Motion: layered octaves of noise//
            //FormQuality drives how many effective octaves we blend//
            //High Form = more octaves = more angular, jagged surface//
            //Low Form = fewer octaves = smoother, rounder surface//
            float fbm(float3 p, float FormQuality)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;

                //Always run 4 octaves but weight them by FormQuality//
                //Low form: later octaves contribute very little (smooth)//
                //High form: all octaves contribute equally (angular)//
                for(int i = 0; i < 4; i++)
                {
                    float octaveWeight = pow(FormQuality + 0.1, (float)i);
                    value += smoothNoise(p * frequency) * amplitude * octaveWeight;
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }

                return value;
            }

            //Vertex Shader//
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                //Get the vertex normal, on a sphere this is also the normalised position, pointing outward from the centre//
                float3 normal = normalize(IN.normalOS);

                //UV Coords: u maps longitude (0-1 around equator), v maps latitude (0 = south pole, 1 = north pole)//
                float latitude = IN.uv.y; // 0 = south pole, 1 = north pole//
                float longitude = IN.uv.x;

                //Latitude band blending//
                //Each frequency band displaces a different part of the sphere//
                //Bass: equatorial buldge//
                //Mid: mid-latitude swell//
                //High: polar spikes//

                //Equatorial weight: peaks at latitude 0.5, falls off toward poles//
                float equatorialWeight = 1.0 - abs(latitude - 0.5) * 2.0;
                equatorialWeight = pow(max(equatorialWeight, 0.0), 1.5);

                //Polar weight: peaks at both poles, falls off toward equator//
                float polarWeight = abs(latitude - 0.5) * 2.0;
                polarWeight = pow(polarWeight, 1.5);

                //Mid weight: broad coverage across the whole surface//
                float midWeight = sin(latitude * PI); //Peaks at equator, smooth//

                //Noise modulated displacement//
                //Animate noise coordinates slowly over time for organic movement//
                //FormQuality controls how complex/jagged the noise pattern is//
                float noiseFreq = lerp(1.5, 4.0, _FormQuality);
                float3 noiseCoord = normal * noiseFreq + float3(0, _AudioTime * 0.4, 0);
                float noiseSample = fbm(noiseCoord, _FormQuality);

                //Use longitude-based spectrum sampling for fine detail//
                //Maps the 64-spectrum values around the equator of the sphere//
                int spectrumIndex = (int)(longitude * 63.0);
                float spectrumSample = _Spectrum[spectrumIndex] * 0.3;

                //Combine displacement components//
                float bassDisplace = _Bass * equatorialWeight * noiseSample;
                float midDisplace = _Mid * midWeight * noiseSample;
                float highDisplace = _High * polarWeight * (noiseSample + spectrumSample);

                float totalDisplace = (bassDisplace + midDisplace +  highDisplace) * _DisplacementStrength;
                // Mask out displacement at poles to prevent tearing//
                // latitude 0 = south pole, 1 = north pole, 0.5 = equator//
                float poleMask = sin(latitude * PI);
                poleMask = pow(poleMask, 0.3); // Gentle falloff, keeps most of the surface active//
                totalDisplace *= poleMask;

                //Clamp to prevent extreme spikes on very loud transients//
                totalDisplace = clamp(totalDisplace, 0.0, _BaseRadius * 0.8);

                //Displace vertex along its normal//
                float3 displacedPos = IN.positionOS.xyz + normal * totalDisplace;

                //Transform to clip space//
                OUT.positionHCS = TransformObjectToHClip(displacedPos);
                OUT.positionWS = TransformObjectToWorld(displacedPos);

                //Recaculate normal after displacement for correct lighting//
                //Aproximite by pertrubing normal in the displacement direction//
                OUT.normalWS = TransformObjectToWorldNormal(normal + normal * totalDisplace * 0.1);
                OUT.normalWS = normalize(OUT.normalWS);

                OUT.uv = IN.uv;
                OUT.displacement = totalDisplace / _DisplacementStrength;

                return OUT;
            }

            //Fragment Shader//
            half4 frag(Varyings IN) : SV_Target
            {
                //Blend between primary and secondary colour based on displacement//
                //Highly displaced areas (peaks/spikes) show the realtime colour//
                //Base surface shows the primary colour//
                //Secondary colour fills the mid-range//
                float d = saturate(IN.displacement);

                float3 baseColour = lerp(_PrimaryColour.rgb, _SecondaryColour.rgb, d * 0.6);

                //Energy-driven emission - surface brightens on loud moments//
                float emission = _Energy * 0.02;
                float3 emissive = _RealtimeColour.rgb * emission;

                //Basic diffuse lighting from the main directional light//
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float3 diffuse =  baseColour * mainLight.color.rgb * (NdotL * 0.8 + 0.2);

                //Rim lighting - edges glow with realtime colour//
                //Creates the impression of light coming from within the form//
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float rim = 1.0 - saturate(dot(viewDir, normalize(IN.normalWS)));
                rim = pow(rim, 2.5);
               float3 rimColour = _RealtimeColour.rgb * rim * (_Energy * 0.05 + 0.3);
               float3 finalColour = diffuse + emissive + rimColour;

                return half4(finalColour, 1.0);
            }
            ENDHLSL
        }
    //Shadow Caster pass so the form casts shadows//
    Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _BaseRadius;
                float _DisplacementStrength;
                float _FormQuality;
                float _SpaceQuality;
                float4 _PrimaryColour;
                float4 _SecondaryColour;
                float4 _RealtimeColour;
                float _Bass;
                float _Mid;
                float _High;
                float _Energy;
                float _AudioTime;
            CBUFFER_END

            float _Spectrum[64];

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            float hash(float3 p) { p = frac(p * float3(443.897, 441.423, 437.195)); p += dot(p, p.yzx + 19.19); return frac((p.x + p.y) * p.z); }
            float smoothNoise(float3 p) { float3 i = floor(p); float3 f = frac(p); float3 u = f*f*(3.0-2.0*f); return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),u.x),lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),u.x),u.y),lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),u.x),lerp(hash(i+float3(0,1,1)),hash(i+float3(1,1,1)),u.x),u.y),u.z); }
            float fbm(float3 p, float fq) { float v=0,a=0.5,f=1; for(int i=0;i<4;i++){v+=smoothNoise(p*f)*a*pow(fq+0.1,(float)i);a*=0.5;f*=2.0;} return v; }

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                float3 normal = normalize(IN.normalOS);
                float  latitude = IN.uv.y;
                float  eqW = pow(max(1.0 - abs(latitude - 0.5) * 2.0, 0.0), 1.5);
                float  polW = pow(abs(latitude - 0.5) * 2.0, 1.5);
                float  midW = sin(latitude * PI);
                float3 nc = normal * lerp(1.5, 4.0, _FormQuality) + float3(0, _AudioTime * 0.15, 0);
                float  ns = fbm(nc, _FormQuality);
                float  disp = (_Bass * eqW + _Mid * midW + _High * polW) * ns * _DisplacementStrength * 0.01;
                disp = clamp(disp, 0.0, _DisplacementStrength);
                float3 pos = IN.positionOS.xyz + normal * disp;
                OUT.positionHCS = TransformObjectToHClip(pos);
                return OUT;
            }

        half4 fragShadow(Varyings IN) : SV_Target { return 0; }
        ENDHLSL
        }
    }
}

