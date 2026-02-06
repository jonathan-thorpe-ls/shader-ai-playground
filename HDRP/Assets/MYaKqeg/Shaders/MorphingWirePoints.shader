Shader "Sonosthesia/MYaKqeg/HDRP/MorphingWirePoints"
{
    Properties
    {
        _BaseSize ("Base Size", Float) = 1.0
        _PulseSizeBoost ("Pulse Size Boost", Float) = 5.0
        _PulseThickness ("Pulse Thickness", Float) = 4.0
        _PulseBrightness ("Pulse Brightness", Float) = 0.5
        _ColorIntensity ("Color Intensity", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
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
                // Parameters shared across the point cloud and pulse system
                float _MorphAmount;
                float _CurrentTime;
                float _PulseSpeed;
                float _PulseLifetime;
                float4 _PulsePositions[3];
                float4 _PulseTimes;
                float _BaseSize;
                float _PulseSizeBoost;
                float _PulseThickness;
                float _PulseBrightness;
                float _ColorIntensity;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 morphTarget : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VertexToGeometry
            {
                float3 positionWS : TEXCOORD0;
                float4 color : COLOR;
                float pulse : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct GeometryToFragment
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float pulse : TEXCOORD1;
            };

            // Pipeline recap:
            // 1. Vertex stage morphs each point between two meshes and sends world-space data forward.
            // 2. Geometry stage receives one point primitive at a time and fabricates a screen-facing quad.
            // 3. Fragment stage shades that quad as a circular sprite, masking pixels outside the disc.

            float3 GetCameraRight()
            {
                return normalize(UNITY_MATRIX_I_V._m00_m10_m20);
            }

            float3 GetCameraUp()
            {
                return normalize(UNITY_MATRIX_I_V._m01_m11_m21);
            }

            float3 GetCameraPosition()
            {
                return _WorldSpaceCameraPos;
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

            // Accumulates spherical shell pulses triggered at runtime
            float ComputePulse(float3 positionWS)
            {
                float intensity = 0.0;
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    if (_PulsePositions[i].w < 0.5)
                    {
                        continue;
                    }

                    float startTime = _PulseTimes[i];
                    float elapsed = _CurrentTime - startTime;
                    if (elapsed < 0.0 || elapsed > _PulseLifetime)
                    {
                        continue;
                    }

                    float pulseRadius = elapsed * _PulseSpeed;
                    float dist = distance(positionWS, _PulsePositions[i].xyz);
                    float waveProximity = abs(dist - pulseRadius);
                    float shell = saturate((_PulseThickness - waveProximity) / max(_PulseThickness, 1e-3));
                    float fade = saturate(1.0 - elapsed / max(_PulseLifetime, 1e-3));
                    intensity = saturate(intensity + shell * fade);
                }
                return intensity;
            }

            VertexToGeometry Vert(Attributes input)
            {
                VertexToGeometry output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Blend between the bind pose and morph target before moving to world space
                float3 morphPosOS = lerp(input.positionOS, input.morphTarget, saturate(_MorphAmount));
                float3 positionWS = TransformObjectToWorldHDRP(morphPosOS);

                output.positionWS = positionWS;
                output.color = input.color;
                output.pulse = ComputePulse(positionWS);
                return output;
            }

            // Geometry shader: promote a single point into four vertices that form a billboarded triangle strip.
            [maxvertexcount(4)]
            void Geom(point VertexToGeometry input[1], inout TriangleStream<GeometryToFragment> stream)
            {
                float3 centerWS = input[0].positionWS;
                float pulse = input[0].pulse;
                float size = _BaseSize + pulse * _PulseSizeBoost;

                // Build a camera-facing quad so each vertex renders as a round billboard
                float3 right = GetCameraRight() * size * 0.5;
                float3 up = GetCameraUp() * size * 0.5;

                float3 corners[4];
                // Triangle strip order: emit in zigzag for proper quad
                // Strip triangles: (0,1,2) and (1,2,3)
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
                    // Transform each corner into clip space; stream order follows a triangle strip (0-1-2, 1-2-3)
                    output.positionCS = TransformWorldToHClipHDRP(corners[i]);
                    output.uv = uvs[i];
                    output.color = input[0].color;
                    output.pulse = pulse;
                    stream.Append(output);
                }
                stream.RestartStrip();
            }

            // Render a soft circular sprite modulated by the active pulse
            float4 Frag(GeometryToFragment input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                float dist = length(centered);
                clip(0.5 - dist);

                float falloff = saturate(1.0 - dist * 2.0);
                float brightness = 1.0 + input.pulse * _PulseBrightness;
                float alpha = falloff * (1.0 + input.pulse) * input.color.a;
                float3 color = input.color.rgb * brightness * _ColorIntensity;
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
