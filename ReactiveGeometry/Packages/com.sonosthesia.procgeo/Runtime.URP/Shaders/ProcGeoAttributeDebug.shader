Shader "Sonosthesia/ProcGeo/AttributeDebug"
{
    Properties
    {
        _LowColor ("Low Color", Color) = (0.03, 0.05, 0.12, 1)
        _HighColor ("High Color", Color) = (0.15, 0.95, 0.75, 1)
        _DebugMinimum ("Minimum", Float) = 0
        _DebugRange ("Range", Float) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }
        Pass
        {
            Name "AttributeDebug"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float> _DebugAttribute;
            CBUFFER_START(UnityPerMaterial)
                half4 _LowColor;
                half4 _HighColor;
                float _DebugMinimum;
                float _DebugRange;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                uint vertexId : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float value : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.value = saturate(
                    (_DebugAttribute[input.vertexId] - _DebugMinimum) /
                    max(_DebugRange, 0.0001));
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return lerp(
                    _LowColor, _HighColor, input.value);
            }
            ENDHLSL
        }
    }
}
