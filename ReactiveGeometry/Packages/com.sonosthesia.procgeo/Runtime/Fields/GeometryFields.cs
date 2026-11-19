using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public enum GeometryFieldContribution
    {
        Force,
        Acceleration,
        VisualOffset,
        DynamicAttribute
    }

    public interface IGeometryField
    {
        string Name { get; }
        GeometryFieldContribution Contribution { get; }
        IReadOnlyList<AttributeDependency> RequiredAttributes { get; }
        void Validate(GeometryResource resource);
        void Record(
            CommandBuffer command,
            GeometryResource resource,
            GraphicsBuffer contribution,
            FieldParameters parameters);
    }

    [Serializable]
    public struct FieldParameters
    {
        public float phase;
        public float pulseAmplitude;
        public float pulseWidth;
        public float directionalStrength;
        public Vector3 direction;
        public float pulseWeight;
        public float directionalWeight;
        public float simulationTime;
    }

    [Serializable]
    public struct GeometryFieldSettings
    {
        public bool fieldsEnabled;
        public bool pulseEnabled;
        public bool directionalEnabled;
        public int graphSourceVertex;
        public float pulseAmplitude;
        public float pulseWidth;
        public float pulseFrequency;
        public float directionalStrength;
        public Vector3 directionalDirection;
        public float pulseWeight;
        public float directionalWeight;

        public static GeometryFieldSettings Default => new()
        {
            fieldsEnabled = false,
            pulseEnabled = true,
            directionalEnabled = false,
            graphSourceVertex = 0,
            pulseAmplitude = 24f,
            pulseWidth = 0.16f,
            pulseFrequency = 0.65f,
            directionalStrength = 0f,
            directionalDirection = Vector3.up,
            pulseWeight = 1f,
            directionalWeight = 1f
        };
    }

    public sealed class FieldAccumulator : IDisposable
    {
        private readonly GeometryResource _resource;
        private readonly ComputeShader _shader;
        private readonly GeometryCommandScheduler _scheduler;
        private readonly List<IGeometryField> _fields = new();
        private readonly int _clearKernel;
        private readonly int _addKernel;
        private GraphicsBuffer _contribution;
        private bool _disposed;

        public FieldAccumulator(
            GeometryResource resource,
            IEnumerable<IGeometryField> fields,
            ComputeShader shader = null)
        {
            _resource = resource ?? throw new ArgumentNullException(nameof(resource));
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            _fields.AddRange(fields);
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/FieldOps")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/FieldOps.compute was not found.");
            _clearKernel = _shader.FindKernel("ClearForces");
            _addKernel = _shader.FindKernel("AddContribution");
            _contribution = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                resource.Layout.VertexCount,
                3 * sizeof(float));
            GpuBufferRegistry.Register(
                _contribution, $"{resource.Layout.Name}.FieldContribution", this);
            foreach (IGeometryField field in _fields)
                field.Validate(resource);
            _scheduler = new GeometryCommandScheduler($"{resource.Layout.Name} Fields");
        }

        public void Record(FieldParameters parameters)
        {
            ThrowIfDisposed();
            int groups = (_resource.Layout.VertexCount + 63) / 64;
            _scheduler.Record(command =>
            {
                command.SetComputeIntParam(
                    _shader, "_VertexCount", _resource.Layout.VertexCount);
                command.SetComputeBufferParam(
                    _shader, _clearKernel, "_ForceBuffer",
                    _resource.Live.Simulation.ExternalForces);
                command.DispatchCompute(_shader, _clearKernel, groups, 1, 1);
                foreach (IGeometryField field in _fields)
                {
                    command.BeginSample($"ProcGeo.Fields.{field.Name}");
                    command.SetComputeBufferParam(
                        _shader, _clearKernel, "_ForceBuffer", _contribution);
                    command.DispatchCompute(_shader, _clearKernel, groups, 1, 1);
                    field.Record(command, _resource, _contribution, parameters);
                    command.SetComputeBufferParam(
                        _shader, _addKernel, "_Contribution", _contribution);
                    command.SetComputeBufferParam(
                        _shader, _addKernel, "_Combined",
                        _resource.Live.Simulation.ExternalForces);
                    command.DispatchCompute(_shader, _addKernel, groups, 1, 1);
                    command.EndSample($"ProcGeo.Fields.{field.Name}");
                }
            });
            _scheduler.ExecuteOnce();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scheduler.Dispose();
            GpuBufferRegistry.Release(ref _contribution);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FieldAccumulator));
        }
    }

    public sealed class GraphPulseField : IGeometryField
    {
        private readonly ComputeShader _shader;
        private readonly int _kernel;

        public GraphPulseField(ComputeShader shader = null)
        {
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/FieldOps")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/FieldOps.compute was not found.");
            _kernel = _shader.FindKernel("GraphPulse");
        }

        public string Name => "GraphPulse";
        public GeometryFieldContribution Contribution =>
            GeometryFieldContribution.Force;
        public IReadOnlyList<AttributeDependency> RequiredAttributes { get; } =
            new[]
            {
                new AttributeDependency(
                    AttributeIds.GraphDistance01,
                    AttributeDomain.Vertex,
                    AttributeFormat.Float),
                new AttributeDependency(
                    AttributeIds.RestNormal,
                    AttributeDomain.Vertex,
                    AttributeFormat.Float3)
            };

        public void Validate(GeometryResource resource)
        {
            foreach (AttributeDependency dependency in RequiredAttributes)
            {
                AttributeDescriptor descriptor =
                    resource.Attributes.Require(dependency.Id).Descriptor;
                if (descriptor.Domain != dependency.Domain ||
                    descriptor.Format != dependency.Format)
                    throw new InvalidOperationException(
                        $"Field '{Name}' requires '{dependency.Id}' as " +
                        $"{dependency.Domain}/{dependency.Format}.");
            }
        }

        public void Record(
            CommandBuffer command,
            GeometryResource resource,
            GraphicsBuffer contribution,
            FieldParameters parameters)
        {
            command.SetComputeIntParam(
                _shader, "_VertexCount", resource.Layout.VertexCount);
            command.SetComputeFloatParam(_shader, "_PulsePhase", parameters.phase);
            command.SetComputeFloatParam(
                _shader, "_PulseAmplitude", parameters.pulseAmplitude);
            command.SetComputeFloatParam(
                _shader, "_PulseWidth", Mathf.Max(parameters.pulseWidth, 0.001f));
            command.SetComputeFloatParam(
                _shader, "_FieldWeight", parameters.pulseWeight);
            command.SetComputeBufferParam(
                _shader, _kernel, "_GraphDistance01",
                resource.Attributes.Require(AttributeIds.GraphDistance01).Buffer);
            command.SetComputeBufferParam(
                _shader, _kernel, "_RestNormals",
                resource.Attributes.Require(AttributeIds.RestNormal).Buffer);
            command.SetComputeBufferParam(
                _shader, _kernel, "_ForceBuffer", contribution);
            command.DispatchCompute(
                _shader, _kernel,
                (resource.Layout.VertexCount + 63) / 64, 1, 1);
        }
    }

    public sealed class DirectionalField : IGeometryField
    {
        private readonly ComputeShader _shader;
        private readonly int _kernel;

        public DirectionalField(ComputeShader shader = null)
        {
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/FieldOps")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/FieldOps.compute was not found.");
            _kernel = _shader.FindKernel("DirectionalForce");
        }

        public string Name => "Directional";
        public GeometryFieldContribution Contribution =>
            GeometryFieldContribution.Force;
        public IReadOnlyList<AttributeDependency> RequiredAttributes { get; } =
            Array.Empty<AttributeDependency>();

        public void Validate(GeometryResource resource)
        {
        }

        public void Record(
            CommandBuffer command,
            GeometryResource resource,
            GraphicsBuffer contribution,
            FieldParameters parameters)
        {
            Vector3 direction = parameters.direction.sqrMagnitude > 1e-8f
                ? parameters.direction.normalized
                : Vector3.up;
            command.SetComputeIntParam(
                _shader, "_VertexCount", resource.Layout.VertexCount);
            command.SetComputeFloatParam(
                _shader, "_DirectionalStrength", parameters.directionalStrength);
            command.SetComputeFloatParam(
                _shader, "_FieldWeight", parameters.directionalWeight);
            command.SetComputeVectorParam(_shader, "_FieldDirection", direction);
            command.SetComputeBufferParam(
                _shader, _kernel, "_ForceBuffer", contribution);
            command.DispatchCompute(
                _shader, _kernel,
                (resource.Layout.VertexCount + 63) / 64, 1, 1);
        }
    }
}
