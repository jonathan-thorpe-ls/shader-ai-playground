using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class GeometryResource : IDisposable
    {
        private GraphicsBuffer _header;
        private GraphicsBuffer _indices;
        private GraphicsBuffer _drawArgs;
        private GraphicsBuffer _validationResult;
        private bool _disposed;

        public GeometryResource(GeometryLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Topology = new TopologyBuffers(layout, this);
            Attributes = new AttributeSet(layout, this);
            _header = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 48);
            GpuBufferRegistry.Register(_header, $"{layout.Name}.Header", this);
            _drawArgs = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 1, 20);
            GpuBufferRegistry.Register(_drawArgs, $"{layout.Name}.DrawArgs", this);
            _validationResult = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, 8);
            GpuBufferRegistry.Register(_validationResult, $"{layout.Name}.ValidationResult", this);

            if (layout.UseMeshBacking)
            {
                Mesh = new Mesh
                {
                    name = layout.Name,
                    indexFormat = IndexFormat.UInt32,
                    hideFlags = HideFlags.DontSave
                };
                Mesh.SetVertexBufferParams(
                    layout.VertexCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 1),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1));
                Mesh.SetIndexBufferParams(layout.IndexCount, Mesh.indexFormat);
                Mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                Mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
                _indices = Mesh.GetIndexBuffer();
                GpuBufferRegistry.Register(_indices, $"{layout.Name}.MeshIndices", this, false);
                GraphicsBuffer dynamicVertices = Mesh.GetVertexBuffer(0);
                GpuBufferRegistry.Register(dynamicVertices, $"{layout.Name}.MeshDynamicVertices", this, false);
                Live = new LiveStateBuffers(layout, dynamicVertices, this);
                GraphicsBuffer staticVertices = Mesh.GetVertexBuffer(1);
                GpuBufferRegistry.Register(staticVertices, $"{layout.Name}.MeshStaticVertices", this, false);
                StaticVertices = staticVertices;
                Mesh.SetSubMesh(
                    0,
                    new SubMeshDescriptor(0, layout.IndexCount, MeshTopology.Triangles),
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                Mesh.subMeshCount = 1;
                Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            }
            else
            {
                throw new NotSupportedException("Phase 1 resources require mesh backing.");
            }
        }

        public GeometryLayout Layout { get; }
        public GraphicsBuffer Header => _header;
        public GraphicsBuffer Indices => _indices;
        public GraphicsBuffer DrawArgs => _drawArgs;
        public GraphicsBuffer ValidationResult => _validationResult;
        public TopologyBuffers Topology { get; }
        public AttributeSet Attributes { get; }
        public LiveStateBuffers Live { get; }
        public GraphicsBuffer StaticVertices { get; private set; }
        public Mesh Mesh { get; }
        public Bounds LocalBounds
        {
            get => Mesh.bounds;
            set => Mesh.bounds = value;
        }
        public int Version { get; private set; }

        public void MarkUpdated(Bounds bounds)
        {
            LocalBounds = bounds;
            Version++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Attributes.Dispose();
            Topology.Dispose();
            Live.Dispose();
            if (StaticVertices != null)
            {
                GpuBufferRegistry.Unregister(StaticVertices);
                StaticVertices.Release();
                StaticVertices = null;
            }
            if (_indices != null)
            {
                GpuBufferRegistry.Unregister(_indices);
                _indices.Release();
                _indices = null;
            }
            GpuBufferRegistry.Release(ref _header);
            GpuBufferRegistry.Release(ref _drawArgs);
            GpuBufferRegistry.Release(ref _validationResult);
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(Mesh);
            else
                UnityEngine.Object.DestroyImmediate(Mesh);
        }
    }

    public sealed class LiveStateBuffers : IDisposable
    {
        private GraphicsBuffer _dynamicVertices;
        private GraphicsBuffer _prevPositions;
        private bool _disposed;

        internal LiveStateBuffers(GeometryLayout layout, GraphicsBuffer dynamicVertices, object owner)
        {
            _dynamicVertices = dynamicVertices;
            _prevPositions = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                layout.VertexCount,
                3 * sizeof(float));
            GpuBufferRegistry.Register(_prevPositions, $"{layout.Name}.PrevPositions", owner);
        }

        public GraphicsBuffer DynamicVertices => _dynamicVertices;
        public GraphicsBuffer PreviousPositions => _prevPositions;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GpuBufferRegistry.Unregister(_dynamicVertices);
            _dynamicVertices?.Release();
            _dynamicVertices = null;
            GpuBufferRegistry.Release(ref _prevPositions);
        }
    }
}
