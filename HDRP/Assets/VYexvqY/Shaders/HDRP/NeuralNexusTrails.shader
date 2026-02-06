Shader "Sonosthesia/VYexvqY/HDRP/NeuralNexusTrails"
{
    Properties
    {
        _TrailSoftness("Edge Softness", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="ForwardOnly" }

            Blend One One
            ZWrite Off
            Cull Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            StructuredBuffer<float4> _TrailPositionSize;
            StructuredBuffer<float4> _TrailColor;
            float4x4 _LocalToWorld;
            float _TrailSizeScale;
            float _TrailOpacity;
            float _TrailSoftness;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : TEXCOORD0;
                float size : PSIZE;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                uint index = input.vertexID;
                float4 data = _TrailPositionSize[index];
                float4 color = _TrailColor[index];

                float3 worldPos = mul(_LocalToWorld, float4(data.xyz, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.color = color;

                float distanceToCamera = distance(GetCameraPositionWS(), worldPos);
                float perspective = 115.0 / max(distanceToCamera, 0.1);
                output.size = max(1.0, data.w * _TrailSizeScale * perspective);
                return output;
            }

            float4 Frag(Varyings input, float2 pointCoord : POINT_COORD) : SV_Target
            {
                float2 uv = pointCoord * 2.0 - 1.0;
                float radiusSq = dot(uv, uv);
                float falloff = saturate(1.0 - radiusSq);
                falloff = pow(falloff, lerp(1.0, 7.5, saturate(_TrailSoftness)));

                float3 rgb = input.color.rgb * falloff;
                float alpha = input.color.a * _TrailOpacity * falloff;

                return float4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
