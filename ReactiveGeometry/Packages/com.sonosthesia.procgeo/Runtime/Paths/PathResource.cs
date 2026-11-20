using System;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public sealed class PathResource : IDisposable
    {
        private GraphicsBuffer _header;
        private GraphicsBuffer _restPositions;
        private GraphicsBuffer _restDistances;
        private GraphicsBuffer _restTangents;
        private GraphicsBuffer _restNormals;
        private GraphicsBuffer _restBinormals;
        private GraphicsBuffer _livePositions;
        private GraphicsBuffer _liveTangents;
        private GraphicsBuffer _liveNormals;
        private GraphicsBuffer _liveBinormals;
        private GraphicsBuffer _liveVelocities;
        private bool _disposed;

        public PathResource(string name, PathGeometryData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Name = string.IsNullOrWhiteSpace(name) ? "Path" : name;
            SampleCount = data.Positions.Length;
            Closed = data.Closed;
            RestLength = data.Closed
                ? data.Distances[data.Distances.Length - 1] +
                  Vector3.Distance(data.Positions[0], data.Positions[data.Positions.Length - 1])
                : data.Distances[data.Distances.Length - 1];

            _header = Create(
                new[] { new PathHeader((uint)SampleCount, Closed, RestLength) },
                $"{Name}.Header", this);
            _restPositions = Create(data.Positions, $"{Name}.RestPositions", this);
            _restDistances = Create(data.Distances, $"{Name}.RestDistances", this);
            _restTangents = Create(data.Tangents, $"{Name}.RestTangents", this);
            _restNormals = Create(data.Normals, $"{Name}.RestNormals", this);
            _restBinormals = Create(data.Binormals, $"{Name}.RestBinormals", this);
            _livePositions = Empty<Vector3>(SampleCount, $"{Name}.LivePositions", this);
            _liveTangents = Empty<Vector3>(SampleCount, $"{Name}.LiveTangents", this);
            _liveNormals = Empty<Vector3>(SampleCount, $"{Name}.LiveNormals", this);
            _liveBinormals = Empty<Vector3>(SampleCount, $"{Name}.LiveBinormals", this);
            _liveVelocities = Empty<Vector3>(SampleCount, $"{Name}.LiveVelocities", this);
        }

        public string Name { get; }
        public int SampleCount { get; }
        public bool Closed { get; }
        public float RestLength { get; }
        public GraphicsBuffer Header => _header;
        public GraphicsBuffer RestPositions => _restPositions;
        public GraphicsBuffer RestDistances => _restDistances;
        public GraphicsBuffer RestTangents => _restTangents;
        public GraphicsBuffer RestNormals => _restNormals;
        public GraphicsBuffer RestBinormals => _restBinormals;
        public GraphicsBuffer LivePositions => _livePositions;
        public GraphicsBuffer LiveTangents => _liveTangents;
        public GraphicsBuffer LiveNormals => _liveNormals;
        public GraphicsBuffer LiveBinormals => _liveBinormals;
        public GraphicsBuffer LiveVelocities => _liveVelocities;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GpuBufferRegistry.Release(ref _header);
            GpuBufferRegistry.Release(ref _restPositions);
            GpuBufferRegistry.Release(ref _restDistances);
            GpuBufferRegistry.Release(ref _restTangents);
            GpuBufferRegistry.Release(ref _restNormals);
            GpuBufferRegistry.Release(ref _restBinormals);
            GpuBufferRegistry.Release(ref _livePositions);
            GpuBufferRegistry.Release(ref _liveTangents);
            GpuBufferRegistry.Release(ref _liveNormals);
            GpuBufferRegistry.Release(ref _liveBinormals);
            GpuBufferRegistry.Release(ref _liveVelocities);
        }

        internal static GraphicsBuffer Create<T>(T[] values, string name, object owner)
            where T : struct
        {
            int stride = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            GraphicsBuffer result = new(GraphicsBuffer.Target.Structured, values.Length, stride);
            result.SetData(values);
            GpuBufferRegistry.Register(result, name, owner);
            return result;
        }

        internal static GraphicsBuffer Empty<T>(int count, string name, object owner)
            where T : struct
        {
            int stride = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            GraphicsBuffer result = new(GraphicsBuffer.Target.Structured, count, stride);
            GpuBufferRegistry.Register(result, name, owner);
            return result;
        }
    }

    public sealed class PathBinding : IDisposable
    {
        private GraphicsBuffer _pathIndices;
        private GraphicsBuffer _pathBlends;
        private GraphicsBuffer _crossSections;
        private bool _disposed;

        public PathBinding(PathGeometryData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            VertexCount = data.VertexPathIndices.Length;
            _pathIndices = PathResource.Create(
                data.VertexPathIndices, "PathBinding.Indices", this);
            _pathBlends = PathResource.Create(
                data.VertexPathBlends, "PathBinding.Blends", this);
            _crossSections = PathResource.Create(
                data.CrossSectionPositions, "PathBinding.CrossSections", this);
        }

        public int VertexCount { get; }
        public GraphicsBuffer PathIndices => _pathIndices;
        public GraphicsBuffer PathBlends => _pathBlends;
        public GraphicsBuffer CrossSections => _crossSections;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GpuBufferRegistry.Release(ref _pathIndices);
            GpuBufferRegistry.Release(ref _pathBlends);
            GpuBufferRegistry.Release(ref _crossSections);
        }
    }
}
