using System;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public sealed class PathGeometryData
    {
        public PathGeometryData(
            Vector3[] positions,
            Vector3[] tangents,
            Vector3[] normals,
            Vector3[] binormals,
            float[] distances,
            bool closed,
            uint[] vertexPathIndices,
            float[] vertexPathBlends,
            Vector2[] crossSectionPositions)
        {
            Positions = positions ?? throw new ArgumentNullException(nameof(positions));
            Tangents = tangents ?? throw new ArgumentNullException(nameof(tangents));
            Normals = normals ?? throw new ArgumentNullException(nameof(normals));
            Binormals = binormals ?? throw new ArgumentNullException(nameof(binormals));
            Distances = distances ?? throw new ArgumentNullException(nameof(distances));
            VertexPathIndices = vertexPathIndices ??
                throw new ArgumentNullException(nameof(vertexPathIndices));
            VertexPathBlends = vertexPathBlends ??
                throw new ArgumentNullException(nameof(vertexPathBlends));
            CrossSectionPositions = crossSectionPositions ??
                throw new ArgumentNullException(nameof(crossSectionPositions));
            if (positions.Length < 2)
                throw new ArgumentException("A path requires at least two samples.");
            if (tangents.Length != positions.Length ||
                normals.Length != positions.Length ||
                binormals.Length != positions.Length ||
                distances.Length != positions.Length)
            {
                throw new ArgumentException("All path buffers must have matching sample counts.");
            }
            if (vertexPathBlends.Length != vertexPathIndices.Length ||
                crossSectionPositions.Length != vertexPathIndices.Length)
            {
                throw new ArgumentException("All path binding buffers must match vertex count.");
            }
            Closed = closed;
        }

        public Vector3[] Positions { get; }
        public Vector3[] Tangents { get; }
        public Vector3[] Normals { get; }
        public Vector3[] Binormals { get; }
        public float[] Distances { get; }
        public bool Closed { get; }
        public uint[] VertexPathIndices { get; }
        public float[] VertexPathBlends { get; }
        public Vector2[] CrossSectionPositions { get; }
    }

    public readonly struct PathHeader
    {
        public readonly uint SampleCount;
        public readonly uint Closed;
        public readonly float RestLength;
        public readonly float Reserved;

        public PathHeader(uint sampleCount, bool closed, float restLength)
        {
            SampleCount = sampleCount;
            Closed = closed ? 1u : 0u;
            RestLength = restLength;
            Reserved = 0f;
        }
    }

    public readonly struct PathDeformerContext
    {
        public PathDeformerContext(
            PathResource path,
            PathDeformationBuffers buffers,
            ComputeShader shader,
            float deltaTime,
            float time)
        {
            Path = path;
            Buffers = buffers;
            Shader = shader;
            DeltaTime = deltaTime;
            Time = time;
            ThreadGroups = (path.SampleCount + 63) / 64;
        }

        public PathResource Path { get; }
        public PathDeformationBuffers Buffers { get; }
        public ComputeShader Shader { get; }
        public float DeltaTime { get; }
        public float Time { get; }
        public int ThreadGroups { get; }
    }

    public readonly struct VertexDeformerContext
    {
        public VertexDeformerContext(
            GeometryResource geometry,
            PathResource path,
            PathDeformationBuffers buffers,
            ComputeShader shader,
            float deltaTime,
            float time)
        {
            Geometry = geometry;
            Path = path;
            Buffers = buffers;
            Shader = shader;
            DeltaTime = deltaTime;
            Time = time;
            ThreadGroups = (geometry.Layout.VertexCount + 63) / 64;
        }

        public GeometryResource Geometry { get; }
        public PathResource Path { get; }
        public PathDeformationBuffers Buffers { get; }
        public ComputeShader Shader { get; }
        public float DeltaTime { get; }
        public float Time { get; }
        public int ThreadGroups { get; }
    }

    public interface IPathDeformer
    {
        bool Enabled { get; }
        void Record(UnityEngine.Rendering.CommandBuffer command, PathDeformerContext context);
    }

    public interface IDynamicPathDeformer : IPathDeformer
    {
    }

    public interface IVertexDeformer
    {
        bool Enabled { get; }
        void Record(UnityEngine.Rendering.CommandBuffer command, VertexDeformerContext context);
    }
}
