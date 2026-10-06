// Salmón para GPU instancing (FishSchool): contrasombreado lomo/vientre, luz del sol
// + ambiente, niebla y aleteo de la cola en el vértice (fase distinta por instancia).
Shader "SalmonSim/FishInstanced"
{
    Properties
    {
        _BaseColor ("Lomo", Color) = (0.36, 0.45, 0.53, 1)
        _BellyColor ("Vientre", Color) = (0.80, 0.84, 0.86, 1)
        _Emission ("Emisión", Color) = (0, 0, 0, 0)
        _WagAmp ("Amplitud de la cola (unidades de malla)", Float) = 0.07
        _WagFreq ("Frecuencia de la cola (rad/s)", Float) = 7
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "FishForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _BellyColor;
                half4 _Emission;
                float _WagAmp;
                float _WagFreq;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float back : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float phase = 0;
                #if defined(UNITY_INSTANCING_ENABLED)
                    phase = unity_InstanceID * 2.39996; // ángulo áureo: fases bien repartidas
                #endif
                // El pez mira a +Z; la ondulación crece hacia la cola (z negativo).
                float3 p = v.positionOS.xyz;
                float tail = saturate((0.1 - p.z) / 0.5);
                p.x += sin(_Time.y * _WagFreq + phase - p.z * 5.0) * _WagAmp * tail * tail;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.back = saturate(v.normalOS.y * 0.5 + 0.5);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light sun = GetMainLight();
                half3 albedo = lerp(_BellyColor.rgb, _BaseColor.rgb, smoothstep(0.35, 0.65, i.back));
                half3 light = sun.color * saturate(dot(n, sun.direction)) + SampleSH(n);
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                half spec = pow(saturate(dot(n, normalize(sun.direction + v))), 40.0) * 0.35;
                half3 col = albedo * light + spec * sun.color + _Emission.rgb;
                float4 cs = TransformWorldToHClip(i.positionWS);
                col = MixFog(col, ComputeFogFactor(cs.z));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
