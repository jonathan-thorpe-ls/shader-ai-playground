Shader "Sonosthesia/MYaKqeg/StarfieldPoints"
{
    Properties
    {
        _BaseSize ("Base Size", Float) = 0.15
        _Color ("Tint", Color) = (1,1,1,1)
        _TwinkleIntensity ("Twinkle Intensity", Float) = 0.4
        _TwinkleSpeed ("Twinkle Speed", Float) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Background"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            Cull Off
            ZWrite Off
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma geometry Geom
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                // Per-material controls for star sprite size and twinkle behaviour
                float4 _Color;
                float _BaseSize;
                float _TwinkleIntensity;
                float _TwinkleSpeed;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VertexToGeometry
            {
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct GeometryToFragment
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
            };

            // Pipeline recap:
            // 1. Vertex stage forwards raw star positions in world space (no normals or UVs required).
            // 2. Geometry stage expands each point primitive into a billboard whose size pulses over time.
            // 3. Fragment stage masks the quad into a circular falloff, tinting it with the shared color.

            VertexToGeometry Vert(Attributes input)
            {
                VertexToGeometry output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                // Stars are authored in object space; push them straight into world space
                output.positionWS = TransformObjectToWorld(input.positionOS);
                return output;
            }

            // Geometry shader: produce the four corners of a triangle strip representing a single star sprite.
            [maxvertexcount(4)]
            void Geom(point VertexToGeometry input[1], inout TriangleStream<GeometryToFragment> stream)
            {
                float3 centerWS = input[0].positionWS;
                // Phase each star by its position so twinkle cycles stay decorrelated
                float phase = dot(centerWS, float3(0.01, 0.017, 0.013));
                float twinkle = 1.0 + _TwinkleIntensity * sin((_Time.y * _TwinkleSpeed + phase) * 6.28318);
                float size = _BaseSize * twinkle;

                // Expand the point into a camera-aligned quad for billboard rendering
                float3 right = normalize(float3(UNITY_MATRIX_I_V._m00, UNITY_MATRIX_I_V._m10, UNITY_MATRIX_I_V._m20)) * size * 0.5;
                float3 up = normalize(float3(UNITY_MATRIX_I_V._m01, UNITY_MATRIX_I_V._m11, UNITY_MATRIX_I_V._m21)) * size * 0.5;

                float3 corners[4];
                corners[0] = centerWS - right - up;
                corners[1] = centerWS + right - up;
                corners[2] = centerWS + right + up;
                corners[3] = centerWS - right + up;

                float2 uvs[4];
                uvs[0] = float2(0.0, 0.0);
                uvs[1] = float2(1.0, 0.0);
                uvs[2] = float2(1.0, 1.0);
                uvs[3] = float2(0.0, 1.0);

                for (int i = 0; i < 4; i++)
                {
                    GeometryToFragment output;
                    // TriangleStream emits vertices in order; GPU joins them into two triangles (0-1-2 and 0-2-3)
                    output.positionCS = TransformWorldToHClip(corners[i]);
                    output.uv = uvs[i];
                    output.alpha = twinkle;
                    stream.Append(output);
                }
                stream.RestartStrip();
            }

            // Output a circular glow whose brightness is driven by the twinkle factor
            half4 Frag(GeometryToFragment input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                float dist = length(centered);
                clip(0.5 - dist);
                float alpha = saturate(1.0 - dist * 2.0) * _Color.a * input.alpha;
                return half4(_Color.rgb * input.alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
