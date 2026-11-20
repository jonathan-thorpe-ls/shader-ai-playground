using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public enum SpatialNoiseMode
    {
        Simplex,
        VoronoiF1,
        VoronoiEdge
    }

    public enum NoiseOutputMode
    {
        Signed,
        Offset,
        Abs
    }

    public enum SpatialFalloffMode
    {
        Constant,
        Inverse,
        Smoothstep
    }

    public enum DeformationDirectionMode
    {
        Outward,
        RestNormal,
        SourceForward,
        Fixed
    }

    [Serializable]
    public struct SpatialSourceParameters
    {
        public bool enabled;
        public Vector3 localCenter;
        public Vector3 localForward;
        public float innerRadius;
        public float outerRadius;
        public float weight;
        public SpatialFalloffMode falloff;

        public static SpatialSourceParameters Default => new()
        {
            enabled = true,
            localCenter = Vector3.zero,
            localForward = Vector3.forward,
            innerRadius = 0f,
            outerRadius = 4f,
            weight = 1f,
            falloff = SpatialFalloffMode.Smoothstep
        };
    }

    [Serializable]
    public struct NoiseDeformationSettings
    {
        public SpatialNoiseMode noise;
        public NoiseOutputMode output;
        public float spatialFrequency;
        public Vector3 axisScale;
        public float temporalSpeed;
        public float timePhase;
        public float amplitude;
        public int seed;
        public int octaves;
        public float lacunarity;
        public float gain;
        public SpatialSourceParameters source;

        public static NoiseDeformationSettings Default => new()
        {
            noise = SpatialNoiseMode.Simplex,
            output = NoiseOutputMode.Signed,
            spatialFrequency = 1f,
            axisScale = Vector3.one,
            temporalSpeed = 0.2f,
            timePhase = 0f,
            amplitude = 1f,
            seed = 0,
            octaves = 3,
            lacunarity = 2f,
            gain = 0.5f,
            source = SpatialSourceParameters.Default
        };
    }

    public sealed class NoiseDeformationField : IGeometryField
    {
        public const int MaximumOctaves = 6;

        private readonly ComputeShader _shader;
        private readonly int _kernel;

        public NoiseDeformationField(
            NoiseDeformationSettings settings = default,
            ComputeShader shader = null)
        {
            Settings = settings.Equals(default)
                ? NoiseDeformationSettings.Default
                : settings;
            _shader = shader ?? Resources.Load<ComputeShader>("ProcGeo/NoiseFields")
                ?? throw new InvalidOperationException(
                    "ProcGeo/NoiseFields.compute was not found.");
            _kernel = _shader.FindKernel("SpatialNoiseForce");
        }

        public NoiseDeformationSettings Settings { get; set; }
        public string Name => "SpatialNoise";
        public GeometryFieldContribution Contribution =>
            GeometryFieldContribution.Force;
        public IReadOnlyList<AttributeDependency> RequiredAttributes { get; } =
            new[]
            {
                new AttributeDependency(
                    AttributeIds.RestPosition,
                    AttributeDomain.Vertex,
                    AttributeFormat.Float3),
                new AttributeDependency(
                    AttributeIds.RestNormal,
                    AttributeDomain.Vertex,
                    AttributeFormat.Float3)
            };

        public void SetSource(Vector3 localCenter, Vector3 localForward)
        {
            NoiseDeformationSettings settings = Settings;
            settings.source.localCenter = localCenter;
            settings.source.localForward =
                localForward.sqrMagnitude > 1e-8f
                    ? localForward.normalized
                    : Vector3.forward;
            Settings = settings;
        }

        public void SetSource(Transform geometry, Transform source)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (source == null) throw new ArgumentNullException(nameof(source));
            SetSource(
                geometry.InverseTransformPoint(source.position),
                geometry.InverseTransformDirection(source.forward));
        }

        public void Validate(GeometryResource resource)
        {
            SpatialDeformationValidation.ValidateRequired(resource, RequiredAttributes, Name);
            SpatialDeformationValidation.ValidateOptionalWeights(resource, Name);
        }

        public void Record(
            CommandBuffer command,
            GeometryResource resource,
            GraphicsBuffer contribution,
            FieldParameters parameters)
        {
            NoiseDeformationSettings value = ValidateSettings(Settings);
            SpatialSourceParameters source = value.source;
            command.SetComputeIntParam(
                _shader, "_VertexCount", resource.Layout.VertexCount);
            command.SetComputeIntParam(_shader, "_NoiseMode", (int)value.noise);
            command.SetComputeIntParam(_shader, "_OutputMode", (int)value.output);
            command.SetComputeIntParam(_shader, "_Octaves", value.octaves);
            command.SetComputeIntParam(_shader, "_Seed", value.seed);
            command.SetComputeFloatParam(
                _shader, "_SpatialFrequency", value.spatialFrequency);
            command.SetComputeVectorParam(_shader, "_AxisScale", value.axisScale);
            command.SetComputeFloatParam(
                _shader, "_NoiseTime",
                value.timePhase + parameters.simulationTime * value.temporalSpeed);
            command.SetComputeFloatParam(_shader, "_Amplitude", value.amplitude);
            command.SetComputeFloatParam(_shader, "_Lacunarity", value.lacunarity);
            command.SetComputeFloatParam(_shader, "_Gain", value.gain);
            SpatialDeformationValidation.BindSource(
                command, _shader, source, out bool active);
            command.SetComputeIntParam(_shader, "_SourceEnabled", active ? 1 : 0);
            SpatialDeformationValidation.BindWeight(
                command, _shader, _kernel, resource);
            command.SetComputeBufferParam(
                _shader, _kernel, "_RestPositions",
                resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            command.SetComputeBufferParam(
                _shader, _kernel, "_RestNormals",
                resource.Attributes.Require(AttributeIds.RestNormal).Buffer);
            command.SetComputeBufferParam(
                _shader, _kernel, "_ForceBuffer", contribution);
            command.DispatchCompute(
                _shader, _kernel,
                (resource.Layout.VertexCount + 63) / 64, 1, 1);
        }

        private static NoiseDeformationSettings ValidateSettings(
            NoiseDeformationSettings value)
        {
            value.spatialFrequency = Mathf.Max(value.spatialFrequency, 0.0001f);
            value.axisScale = SpatialDeformationValidation.FiniteVector(
                value.axisScale, Vector3.one);
            value.axisScale = new Vector3(
                Mathf.Max(Mathf.Abs(value.axisScale.x), 0.0001f),
                Mathf.Max(Mathf.Abs(value.axisScale.y), 0.0001f),
                Mathf.Max(Mathf.Abs(value.axisScale.z), 0.0001f));
            value.temporalSpeed = SpatialDeformationValidation.Finite(
                value.temporalSpeed, 0f);
            value.timePhase = SpatialDeformationValidation.Finite(
                value.timePhase, 0f);
            value.amplitude = SpatialDeformationValidation.Finite(
                value.amplitude, 0f);
            value.octaves = Mathf.Clamp(value.octaves, 1, MaximumOctaves);
            value.lacunarity = Mathf.Max(
                SpatialDeformationValidation.Finite(value.lacunarity, 2f),
                1f);
            value.gain = Mathf.Clamp(
                SpatialDeformationValidation.Finite(value.gain, 0.5f),
                0f, 1f);
            value.source = SpatialDeformationValidation.ValidateSource(value.source);
            return value;
        }
    }

    public static class SpatialDeformationValidation
    {
        public static void ValidateRequired(
            GeometryResource resource,
            IReadOnlyList<AttributeDependency> required,
            string fieldName)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            foreach (AttributeDependency dependency in required)
            {
                AttributeDescriptor descriptor =
                    resource.Attributes.Require(dependency.Id).Descriptor;
                if (descriptor.Domain != dependency.Domain ||
                    descriptor.Format != dependency.Format)
                    throw new InvalidOperationException(
                        $"Field '{fieldName}' requires '{dependency.Id}' as " +
                        $"{dependency.Domain}/{dependency.Format}.");
            }
        }

        public static void ValidateOptionalWeights(
            GeometryResource resource,
            string fieldName)
        {
            if (resource.Attributes.TryGet(
                    AttributeIds.DeformationWeight, out AttributeSet.Entry entry) &&
                (entry.Descriptor.Domain != AttributeDomain.Vertex ||
                 entry.Descriptor.Format != AttributeFormat.Float))
                throw new InvalidOperationException(
                    $"Field '{fieldName}' optional '{AttributeIds.DeformationWeight}' " +
                    "attribute must be Vertex/Float.");
        }

        public static void BindWeight(
            CommandBuffer command,
            ComputeShader shader,
            int kernel,
            GeometryResource resource)
        {
            bool hasWeights = resource.Attributes.TryGet(
                AttributeIds.DeformationWeight, out AttributeSet.Entry entry);
            command.SetComputeIntParam(
                shader, "_UseDeformationWeights", hasWeights ? 1 : 0);
            command.SetComputeBufferParam(
                shader, kernel, "_DeformationWeights",
                hasWeights ? entry.Buffer : resource.Live.Simulation.Status);
        }

        public static SpatialSourceParameters ValidateSource(
            SpatialSourceParameters source)
        {
            source.localCenter = FiniteVector(source.localCenter, Vector3.zero);
            source.localForward = FiniteVector(source.localForward, Vector3.forward);
            if (source.localForward.sqrMagnitude <= 1e-8f)
                source.localForward = Vector3.forward;
            else
                source.localForward.Normalize();
            source.innerRadius = Mathf.Max(Finite(source.innerRadius, 0f), 0f);
            source.outerRadius = Mathf.Max(Finite(source.outerRadius, 0f), 0f);
            source.innerRadius = Mathf.Min(source.innerRadius, source.outerRadius);
            source.weight = Finite(source.weight, 1f);
            return source;
        }

        public static void BindSource(
            CommandBuffer command,
            ComputeShader shader,
            SpatialSourceParameters source,
            out bool active)
        {
            command.SetComputeVectorParam(shader, "_SourceCenter", source.localCenter);
            command.SetComputeFloatParam(shader, "_InnerRadius", source.innerRadius);
            command.SetComputeFloatParam(shader, "_OuterRadius", source.outerRadius);
            command.SetComputeFloatParam(shader, "_SourceWeight", source.weight);
            command.SetComputeIntParam(shader, "_FalloffMode", (int)source.falloff);
            active = source.enabled && source.outerRadius > 0f && source.weight != 0f;
        }

        public static float Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        public static Vector3 FiniteVector(Vector3 value, Vector3 fallback) =>
            new(
                Finite(value.x, fallback.x),
                Finite(value.y, fallback.y),
                Finite(value.z, fallback.z));

    }
}
