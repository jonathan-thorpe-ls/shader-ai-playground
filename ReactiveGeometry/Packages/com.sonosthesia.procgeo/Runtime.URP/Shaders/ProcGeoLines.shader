Shader "Sonosthesia/ProcGeo/Lines"
{
    Properties
    {
        _Color ("Color", Color) = (0.15, 0.95, 0.85, 0.9)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }
        Pass
        {
            Name "Lines"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset -2, -2
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
