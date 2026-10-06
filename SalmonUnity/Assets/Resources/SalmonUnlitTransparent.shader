// Color plano semitransparente con niebla, visible por ambas caras (redes, contornos).
Shader "SalmonSim/UnlitTransparent"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 0.5)
        _DepthFade ("Desvanecer bajo y=0 (1/m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _DepthFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = _BaseColor * i.color;
                // Vista desde la superficie: lo sumergido se pierde con la profundidad.
                c.a *= exp(-max(0.0, -i.positionWS.y) * _DepthFade);
                float4 cs = TransformWorldToHClip(i.positionWS);
                c.rgb = MixFog(c.rgb, ComputeFogFactor(cs.z));
                return c;
            }
            ENDHLSL
        }
    }
}
