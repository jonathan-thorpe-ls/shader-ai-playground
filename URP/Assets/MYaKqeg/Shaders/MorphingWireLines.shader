Shader "Sonosthesia/MYaKqeg/MorphingWireLines"
{
    Properties
    {
        _LineWidth ("Line Width", Float) = 0.04
        _BaseOpacity ("Base Opacity", Float) = 0.3
        _PulseOpacityBoost ("Pulse Opacity Boost", Float) = 0.7
        _ColorIntensity ("Color Intensity", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
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
                // Material parameters controlling morph targets and travelling pulses
                float _MorphAmount;
                float _CurrentTime;
                float _PulseSpeed;
                float _PulseLifetime;
                float4 _PulsePositions[3];
                float4 _PulseTimes;
                float _LineWidth;
                float _BaseOpacity;
                float _PulseOpacityBoost;
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
            // 1. Vertex stage morphs endpoints between base and target meshes before moving into world space.
            // 2. Geometry stage ingests a line primitive (two vertices) and widens it into a camera-facing ribbon.
            // 3. Fragment stage attenuates the ribbon edges and applies pulse-driven opacity boosts.

            // Computes pulse strength by intersecting expanding spheres with each vertex
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
                    float shell = saturate((_LineWidth * 15.0 - waveProximity) / max(_LineWidth * 15.0, 1e-3));
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

                // Morph between original vertex and target shape prior to world-space transform
                float3 morphPosOS = lerp(input.positionOS, input.morphTarget, saturate(_MorphAmount));
                float3 positionWS = TransformObjectToWorld(morphPosOS);

                output.positionWS = positionWS;
                output.color = input.color;
                output.pulse = ComputePulse(positionWS);
                return output;
            }

            // Geometry shader: convert a line segment into four vertices laid out as a screen-aligned strip.
            [maxvertexcount(4)]
            void Geom(line VertexToGeometry input[2], inout TriangleStream<GeometryToFragment> stream)
            {
                float3 startWS = input[0].positionWS;
                float3 endWS = input[1].positionWS;
                float3 centerWS = 0.5 * (startWS + endWS);

                float pulse = max(input[0].pulse, input[1].pulse);
                float width = _LineWidth * (1.0 + pulse);

                float3 lineDir = normalize(endWS - startWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - centerWS);
                float3 normal = normalize(cross(viewDir, lineDir));
                // Fall back to orthogonal axes when the camera is colinear with the segment
                if (all(abs(normal) < 1e-5))
                {
                    normal = normalize(cross(float3(0.0, 1.0, 0.0), lineDir));
                }
                if (all(abs(normal) < 1e-5))
                {
                    normal = normalize(cross(float3(1.0, 0.0, 0.0), lineDir));
                }
                float3 offset = normal * width * 0.5;

                float3 corners[4];
                corners[0] = startWS - offset;
                corners[1] = startWS + offset;
                corners[2] = endWS + offset;
                corners[3] = endWS - offset;

                float2 uvs[4];
                uvs[0] = float2(0.0, 0.0);
                uvs[1] = float2(0.0, 1.0);
                uvs[2] = float2(1.0, 1.0);
                uvs[3] = float2(1.0, 0.0);

                float4 colors[2];
                colors[0] = input[0].color;
                colors[1] = input[1].color;

                float pulses[2];
                pulses[0] = input[0].pulse;
                pulses[1] = input[1].pulse;

                for (int i = 0; i < 4; i++)
                {
                    GeometryToFragment output;
                    // Emit vertices in strip order so GPU draws two triangles covering the ribbon
                    output.positionCS = TransformWorldToHClip(corners[i]);
                    output.uv = uvs[i];
                    int endpointIndex = (i < 2) ? 0 : 1;
                    output.color = colors[endpointIndex];
                    output.pulse = pulses[endpointIndex];
                    stream.Append(output);
                }
                stream.RestartStrip();
            }

            // Fade line edges while boosting opacity where pulses are present
            half4 Frag(GeometryToFragment input) : SV_Target
            {
                float blend = saturate(abs(input.uv.y - 0.5) * 2.0);
                float opacity = lerp(_BaseOpacity, _BaseOpacity + _PulseOpacityBoost, input.pulse);
                float3 color = input.color.rgb * _ColorIntensity;
                return half4(color, opacity * (1.0 - blend));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
