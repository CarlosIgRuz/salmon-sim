// Agua del lago: color profundo + reflejo del cielo con Fresnel, normales animadas
// por suma de ondas (calculadas por píxel, sin desplazar vértices) y brillo del sol.
Shader "SalmonSim/Water"
{
    Properties
    {
        _DeepColor ("Color profundo", Color) = (0.04, 0.20, 0.24, 1)
        _SkyColor ("Reflejo del cielo", Color) = (0.62, 0.74, 0.82, 1)
        _Alpha ("Opacidad (vista cenital)", Range(0, 1)) = 0.7
        _WaveScale ("Escala de las ondas", Float) = 1
        _WaveSpeed ("Velocidad de las ondas", Float) = 1
        _NormalStrength ("Intensidad de las ondas", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _SkyColor;
                half _Alpha;
                float _WaveScale;
                float _WaveSpeed;
                half _NormalStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // Gradiente (dh/dx, dh/dz) de una onda sinusoidal.
            float2 WaveGrad(float2 p, float2 dir, float len, float amp, float spd, float t)
            {
                dir = normalize(dir);
                float k = 6.2831853 / len;
                return dir * (amp * k * cos((dot(dir, p) + t * spd) * k));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float2 p = i.positionWS.xz / _WaveScale;
                float2 g = WaveGrad(p, float2( 1.0,  0.35), 9.0, 0.10, 1.6, t)
                         + WaveGrad(p, float2(-0.4,  1.0 ), 5.3, 0.06, 1.2, t)
                         + WaveGrad(p, float2( 0.8, -0.7 ), 2.7, 0.03, 0.9, t)
                         + WaveGrad(p, float2(-1.0, -0.2 ), 1.4, 0.012, 0.7, t);

                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float dist = length(toCam);
                float3 v = toCam / dist;
                // A lo lejos las ondas se aplanan: evita parpadeo (aliasing) en el horizonte.
                g *= _NormalStrength * saturate(1.0 - dist / 350.0);
                float3 n = normalize(float3(-g.x, 1.0, -g.y));
                if (v.y < 0) n.y = -n.y; // vista desde abajo

                Light sun = GetMainLight();
                float fres = lerp(0.04, 1.0, pow(1.0 - saturate(dot(n, v)), 5.0));
                half3 col = lerp(_DeepColor.rgb * (0.6 + 0.4 * saturate(sun.direction.y)), _SkyColor.rgb, fres);
                float spec = pow(saturate(dot(n, normalize(sun.direction + v))), 220.0) * 0.9;
                col += sun.color * spec;
                half alpha = saturate(lerp(_Alpha, 1.0, fres) + spec);

                float4 cs = TransformWorldToHClip(i.positionWS);
                col = MixFog(col, ComputeFogFactor(cs.z));
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
