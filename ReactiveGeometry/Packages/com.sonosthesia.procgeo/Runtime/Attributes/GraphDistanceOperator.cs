using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class GraphDistanceOperator : IAttributeOperator, IDisposable
    {
        private readonly int[] _sources;
        private readonly int _maximumRounds;
        private readonly ComputeShader _shader;
        private readonly int _initializeKernel;
        private readonly int _expandKernel;
        private readonly int _clearMaximumKernel;
        private readonly int _reduceKernel;
        private readonly int _normaliseKernel;
        private readonly GeometryCommandScheduler _scheduler;
        private GraphicsBuffer _sourcesBuffer;
        private GraphicsBuffer _distanceA;
        private GraphicsBuffer _distanceB;
        private GraphicsBuffer _maximum;
        private bool _disposed;

        public GraphDistanceOperator(
            IEnumerable<int> sources,
            int maximumRounds = -1,
            ComputeShader shader = null)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            _sources = new List<int>(sources).ToArray();
            if (_sources.Length == 0)
                throw new ArgumentException("At least one source is required.", nameof(sources));
            _maximumRounds = maximumRounds;
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/GraphDistance")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/GraphDistance.compute was not found.");
            _initializeKernel = _shader.FindKernel("InitializeDistance");
            _expandKernel = _shader.FindKernel("ExpandDistance");
            _clearMaximumKernel = _shader.FindKernel("ClearMaximum");
            _reduceKernel = _shader.FindKernel("ReduceDistance");
            _normaliseKernel = _shader.FindKernel("PublishDistance");
            _scheduler = new GeometryCommandScheduler("ProcGeo.Attributes.GraphDistance");
            Outputs = new[]
            {
                new AttributeDescriptor(
                    AttributeIds.GraphDistance,
                    AttributeDomain.Vertex,
                    AttributeFormat.Float,
                    lifetime: AttributeLifetime.Dynamic),
                new AttributeDescriptor(
                    AttributeIds.GraphDistance01,
                    AttributeDomain.Vertex,
                    AttributeFormat.Float,
                    lifetime: AttributeLifetime.Dynamic)
            };
        }

        public string Name => "GraphDistance";
        public IReadOnlyList<AttributeDependency> Inputs { get; } =
            Array.Empty<AttributeDependency>();
        public IReadOnlyList<AttributeDescriptor> Outputs { get; }

        public void Validate(AttributeSet attributes)
        {
            int vertexCount = attributes.Layout.VertexCount;
            foreach (int source in _sources)
            {
                if ((uint)source >= (uint)vertexCount)
                    throw new ArgumentOutOfRangeException(
                        nameof(_sources),
                        $"Source vertex {source} is outside 0..{vertexCount - 1}.");
            }
        }

        public void Record(CommandBuffer command, GeometryResource resource)
        {
            Allocate(resource);
            int groups = (resource.Layout.VertexCount + 63) / 64;
            int rounds = _maximumRounds > 0
                ? Mathf.Min(resource.Layout.VertexCount - 1, _maximumRounds)
                : resource.Layout.VertexCount - 1;
            rounds = Mathf.Max(1, rounds);
            command.SetComputeIntParam(
                _shader, "_VertexCount", resource.Layout.VertexCount);
            command.SetComputeIntParam(_shader, "_SourceCount", _sources.Length);
            command.SetComputeBufferParam(
                _shader, _initializeKernel, "_DistanceRead", _distanceA);
            command.SetComputeBufferParam(
                _shader, _initializeKernel, "_DistanceWrite", _distanceA);
            command.SetComputeBufferParam(
                _shader, _initializeKernel, "_Sources", _sourcesBuffer);
            command.DispatchCompute(_shader, _initializeKernel, groups, 1, 1);

            GraphicsBuffer read = _distanceA;
            GraphicsBuffer write = _distanceB;
            for (int round = 0; round < rounds; round++)
            {
                command.SetComputeBufferParam(
                    _shader, _expandKernel, "_DistanceRead", read);
                command.SetComputeBufferParam(
                    _shader, _expandKernel, "_DistanceWrite", write);
                command.SetComputeBufferParam(
                    _shader, _expandKernel, "_NeighbourOffsets",
                    resource.Topology.NeighbourOffsets);
                command.SetComputeBufferParam(
                    _shader, _expandKernel, "_Neighbours",
                    resource.Topology.Neighbours);
                command.DispatchCompute(_shader, _expandKernel, groups, 1, 1);
                (read, write) = (write, read);
            }

            command.SetComputeBufferParam(
                _shader, _normaliseKernel, "_DistanceRead", read);
            command.SetComputeBufferParam(
                _shader, _clearMaximumKernel, "_MaximumDistance", _maximum);
            command.SetComputeBufferParam(
                _shader, _reduceKernel, "_MaximumDistance", _maximum);
            command.SetComputeBufferParam(
                _shader, _reduceKernel, "_DistanceRead", read);
            command.SetComputeBufferParam(
                _shader, _normaliseKernel, "_MaximumDistance", _maximum);
            command.SetComputeBufferParam(
                _shader, _normaliseKernel, "_GraphDistance",
                resource.Attributes.Require(AttributeIds.GraphDistance).Buffer);
            command.SetComputeBufferParam(
                _shader, _normaliseKernel, "_GraphDistance01",
                resource.Attributes.Require(AttributeIds.GraphDistance01).Buffer);
            command.DispatchCompute(_shader, _clearMaximumKernel, 1, 1, 1);
            command.DispatchCompute(_shader, _reduceKernel, groups, 1, 1);
            command.DispatchCompute(_shader, _normaliseKernel, groups, 1, 1);
        }

        public void Execute(GeometryResource resource)
        {
            Validate(resource.Attributes);
            foreach (AttributeDescriptor output in Outputs)
                resource.Attributes.RequireOrCreate(output);
            _scheduler.Record(command => Record(command, resource));
            _scheduler.ExecuteOnce();
        }

        private void Allocate(GeometryResource resource)
        {
            if (_distanceA != null)
                return;
            _sourcesBuffer = Create(
                "GraphDistance.Sources", _sources.Length, sizeof(uint));
            uint[] data = Array.ConvertAll(_sources, value => (uint)value);
            _sourcesBuffer.SetData(data);
            _distanceA = Create(
                "GraphDistance.PingPongA",
                resource.Layout.VertexCount,
                sizeof(uint));
            _distanceB = Create(
                "GraphDistance.PingPongB",
                resource.Layout.VertexCount,
                sizeof(uint));
            _maximum = Create("GraphDistance.Maximum", 1, sizeof(uint));
        }

        private GraphicsBuffer Create(string name, int count, int stride)
        {
            GraphicsBuffer buffer = new(
                GraphicsBuffer.Target.Structured, count, stride);
            GpuBufferRegistry.Register(buffer, name, this);
            return buffer;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scheduler.Dispose();
            GpuBufferRegistry.Release(ref _sourcesBuffer);
            GpuBufferRegistry.Release(ref _distanceA);
            GpuBufferRegistry.Release(ref _distanceB);
            GpuBufferRegistry.Release(ref _maximum);
        }
    }
}
