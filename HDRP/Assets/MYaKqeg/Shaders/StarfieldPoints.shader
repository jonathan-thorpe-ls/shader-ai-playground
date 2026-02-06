Shader "Sonosthesia/MYaKqeg/HDRP/StarfieldPoints"
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
            "RenderPipeline" = "HDRenderPipeline"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma geometry Geom
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

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

            float3 GetCameraRight()
            {
                return normalize(UNITY_MATRIX_I_V._m00_m10_m20);
            }

            float3 GetCameraUp()
            {
                return normalize(UNITY_MATRIX_I_V._m01_m11_m21);
            }

            // Transform from object to world space (HDRP compatible)
            float3 TransformObjectToWorldHDRP(float3 positionOS)
            {
                return mul(UNITY_MATRIX_M, float4(positionOS, 1.0)).xyz;
            }

            // Transform from world to clip space (HDRP compatible)
            float4 TransformWorldToHClipHDRP(float3 positionWS)
            {
                return mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
            }

            VertexToGeometry Vert(Attributes input)
            {
                VertexToGeometry output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                // Stars are authored in object space; push them straight into world space
                output.positionWS = TransformObjectToWorldHDRP(input.positionOS);
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
                float3 right = GetCameraRight() * size * 0.5;
                float3 up = GetCameraUp() * size * 0.5;

                // Triangle strip order: emit in zigzag for proper quad
                // Strip triangles: (0,1,2) and (1,2,3)
                float3 corners[4];
                corners[0] = centerWS - right - up;  // bottom-left
                corners[1] = centerWS + right - up;  // bottom-right
                corners[2] = centerWS - right + up;  // top-left
                corners[3] = centerWS + right + up;  // top-right

                float2 uvs[4];
                uvs[0] = float2(0.0, 0.0);
                uvs[1] = float2(1.0, 0.0);
                uvs[2] = float2(0.0, 1.0);
                uvs[3] = float2(1.0, 1.0);

                for (int i = 0; i < 4; i++)
                {
                    GeometryToFragment output;
                    // TriangleStream emits vertices in order; GPU joins them into two triangles (0-1-2 and 1-2-3)
                    output.positionCS = TransformWorldToHClipHDRP(corners[i]);
                    output.uv = uvs[i];
                    output.alpha = twinkle;
                    stream.Append(output);
                }
                stream.RestartStrip();
            }

            // Output a circular glow whose brightness is driven by the twinkle factor
            float4 Frag(GeometryToFragment input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                float dist = length(centered);
                clip(0.5 - dist);
                float alpha = saturate(1.0 - dist * 2.0) * _Color.a * input.alpha;
                return float4(_Color.rgb * input.alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
