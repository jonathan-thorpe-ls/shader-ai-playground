using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class IcosphereGenerator : IDisposable
    {
        private readonly IcospherePlan _plan;
        private readonly ComputeShader _shader;
        private readonly GraphicsBuffer _corners;
        private readonly GraphicsBuffer _faces;
        private readonly GraphicsBuffer _locators;
        private readonly GraphicsBuffer _triangles;
        private readonly GraphicsBuffer _triangleRegions;
        private readonly int _verticesKernel;
        private readonly int _indicesKernel;
        private bool _disposed;

        public IcosphereGenerator(
            int frequency = 4,
            float radius = 1f,
            ComputeShader shader = null,
            bool recursiveProjection = false)
        {
            Radius = radius > 0f ? radius : throw new ArgumentOutOfRangeException(nameof(radius));
            RecursiveProjection = recursiveProjection;
            if (recursiveProjection && !IsPowerOfTwo(frequency))
                throw new ArgumentException(
                    "Recursive projection requires a power-of-two frequency.",
                    nameof(frequency));
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/Icosphere")
                    ?? throw new InvalidOperationException("ProcGeo/Icosphere.compute was not found.");
            _plan = IcospherePlan.Create(frequency);
            _verticesKernel = _shader.FindKernel("GenerateVertices");
            _indicesKernel = _shader.FindKernel("GenerateIndicesAndAttributes");

            _corners = Create(_plan.Corners, "Icosphere.Corners", this);
            _faces = Create(_plan.Faces, "Icosphere.Faces", this);
            _locators = Create(_plan.Locators, "Icosphere.Locators", this);
            _triangles = Create(_plan.Triangles, "Icosphere.Triangles", this);
            _triangleRegions = Create(_plan.TriangleRegions, "Icosphere.TriangleRegions", this);
        }

        public float Radius { get; }
        public bool RecursiveProjection { get; }
        public int Frequency => _plan.Frequency;

        public GeometryLayout Plan()
        {
            AttributeDescriptor[] attributes =
            {
                new(AttributeIds.RestPosition, AttributeDomain.Vertex, AttributeFormat.Float3),
                new(AttributeIds.RestNormal, AttributeDomain.Vertex, AttributeFormat.Float3),
                new(AttributeIds.RestTangent, AttributeDomain.Vertex, AttributeFormat.Float4),
                new(AttributeIds.LocalCoord, AttributeDomain.Corner, AttributeFormat.Float2),
                new(AttributeIds.RegionId, AttributeDomain.Primitive, AttributeFormat.UInt,
                    AttributeInterpolation.Flat),
                new(AttributeIds.BoundaryWeight, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.NeighbourCount, AttributeDomain.Vertex, AttributeFormat.UInt,
                    AttributeInterpolation.Flat),
                new(AttributeIds.Curvature, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.EdgeRestLength, AttributeDomain.Edge, AttributeFormat.Float)
            };
            return new GeometryLayout(
                $"Icosphere_{_plan.Frequency}",
                PrimitiveTopology.Triangles,
                _plan.VertexCount,
                _plan.PrimitiveCount,
                _plan.EdgeCount,
                20,
                32,
                attributes);
        }

        public void Record(CommandBuffer command, GeometryResource resource)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IcosphereGenerator));
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (resource.Layout.VertexCount != _plan.VertexCount ||
                resource.Layout.PrimitiveCount != _plan.PrimitiveCount)
                throw new ArgumentException("The target resource does not match this generator.", nameof(resource));

            BindVertices(resource);
            command.DispatchCompute(
                _shader, _verticesKernel, ThreadGroups(_plan.VertexCount), 1, 1);

            BindIndices(resource);
            command.DispatchCompute(
                _shader, _indicesKernel, ThreadGroups(_plan.PrimitiveCount), 1, 1);

            resource.MarkUpdated(new Bounds(Vector3.zero, Vector3.one * (2f * Radius)));
        }

        public Vector3Int[] DebugTriangles => _plan.Triangles;
        public int DebugEdgeCount => _plan.EdgeCount;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Release(_corners);
            Release(_faces);
            Release(_locators);
            Release(_triangles);
            Release(_triangleRegions);
        }

        private void BindVertices(GeometryResource resource)
        {
            _shader.SetInt("_Frequency", _plan.Frequency);
            _shader.SetInt("_RecursiveProjection", RecursiveProjection ? 1 : 0);
            _shader.SetFloat("_Radius", Radius);
            _shader.SetInt("_VertexCount", _plan.VertexCount);
            _shader.SetBuffer(_verticesKernel, "_Corners", _corners);
            _shader.SetBuffer(_verticesKernel, "_Faces", _faces);
            _shader.SetBuffer(_verticesKernel, "_Locators", _locators);
            _shader.SetBuffer(
                _verticesKernel, "_DynamicVertices", resource.Live.DynamicVertices);
            _shader.SetBuffer(_verticesKernel, "_StaticVertices", resource.StaticVertices);
            resource.Attributes.Bind(
                _shader, _verticesKernel, AttributeIds.RestPosition, "_RestPositions");
            resource.Attributes.Bind(
                _shader, _verticesKernel, AttributeIds.RestNormal, "_RestNormals");
            resource.Attributes.Bind(
                _shader, _verticesKernel, AttributeIds.RestTangent, "_RestTangents");
        }

        private void BindIndices(GeometryResource resource)
        {
            _shader.SetInt("_PrimitiveCount", _plan.PrimitiveCount);
            _shader.SetInt("_EdgeCount", _plan.EdgeCount);
            _shader.SetFloat("_Radius", Radius);
            _shader.SetBuffer(_indicesKernel, "_Triangles", _triangles);
            _shader.SetBuffer(_indicesKernel, "_TriangleRegions", _triangleRegions);
            _shader.SetBuffer(_indicesKernel, "_Indices", resource.Indices);
            _shader.SetBuffer(_indicesKernel, "_Header", resource.Header);
            _shader.SetBuffer(_indicesKernel, "_DrawArgs", resource.DrawArgs);
            resource.Attributes.Bind(
                _shader, _indicesKernel, AttributeIds.LocalCoord, "_CornerCoords");
            resource.Attributes.Bind(
                _shader, _indicesKernel, AttributeIds.RegionId, "_RegionIds");
        }

        private static int ThreadGroups(int count) => (count + 63) / 64;
        private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

        private static GraphicsBuffer Create<T>(T[] values, string name, object owner)
            where T : struct
        {
            int stride = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            GraphicsBuffer buffer = new(GraphicsBuffer.Target.Structured, values.Length, stride);
            buffer.SetData(values);
            GpuBufferRegistry.Register(buffer, name, owner);
            return buffer;
        }

        private static void Release(GraphicsBuffer buffer)
        {
            if (buffer == null) return;
            GpuBufferRegistry.Unregister(buffer);
            buffer.Release();
        }
    }
}
