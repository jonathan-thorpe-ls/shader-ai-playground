using System;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public sealed class TopologyBuffers : IDisposable
    {
        private GraphicsBuffer _neighbourOffsets;
        private GraphicsBuffer _neighbours;
        private GraphicsBuffer _neighbourEdge;
        private GraphicsBuffer _edges;
        private GraphicsBuffer _edgeRestLength;
        private GraphicsBuffer _edgeTypes;
        private GraphicsBuffer _vertexFaceOffsets;
        private GraphicsBuffer _vertexFaces;
        private GraphicsBuffer _edgeFaceCounts;
        private bool _disposed;

        public TopologyBuffers(GeometryLayout layout, object owner)
        {
            Layout = layout;
            _neighbourOffsets = Create(
                $"{layout.Name}.NeighbourOffsets", owner, layout.VertexCount + 1, sizeof(uint));
            _neighbours = Create(
                $"{layout.Name}.Neighbours", owner, Math.Max(1, layout.EdgeCapacity * 2), sizeof(uint));
            _neighbourEdge = Create(
                $"{layout.Name}.NeighbourEdge", owner, Math.Max(1, layout.EdgeCapacity * 2), sizeof(uint));
            _edges = Create(
                $"{layout.Name}.Edges", owner, Math.Max(1, layout.EdgeCapacity), 2 * sizeof(uint));
            _edgeRestLength = Create(
                $"{layout.Name}.EdgeRestLength", owner, Math.Max(1, layout.EdgeCapacity), sizeof(float));
            _edgeTypes = Create(
                $"{layout.Name}.EdgeTypes", owner, Math.Max(1, layout.EdgeCapacity), sizeof(uint));
            _vertexFaceOffsets = Create(
                $"{layout.Name}.VertexFaceOffsets", owner, layout.VertexCount + 1, sizeof(uint));
            _vertexFaces = Create(
                $"{layout.Name}.VertexFaces", owner, Math.Max(1, layout.CornerCount), sizeof(uint));
            _edgeFaceCounts = Create(
                $"{layout.Name}.EdgeFaceCounts", owner, Math.Max(1, layout.EdgeCapacity), sizeof(uint));
            Scratch = new TopologyScratchBuffers(layout, owner);
        }

        public GeometryLayout Layout { get; }
        public GraphicsBuffer NeighbourOffsets => _neighbourOffsets;
        public GraphicsBuffer Neighbours => _neighbours;
        public GraphicsBuffer NeighbourEdge => _neighbourEdge;
        public GraphicsBuffer Edges => _edges;
        public GraphicsBuffer EdgeRestLength => _edgeRestLength;
        public GraphicsBuffer EdgeTypes => _edgeTypes;
        public GraphicsBuffer VertexFaceOffsets => _vertexFaceOffsets;
        public GraphicsBuffer VertexFaces => _vertexFaces;
        public GraphicsBuffer EdgeFaceCounts => _edgeFaceCounts;
        public TopologyScratchBuffers Scratch { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Scratch.Dispose();
            Release(ref _neighbourOffsets);
            Release(ref _neighbours);
            Release(ref _neighbourEdge);
            Release(ref _edges);
            Release(ref _edgeRestLength);
            Release(ref _edgeTypes);
            Release(ref _vertexFaceOffsets);
            Release(ref _vertexFaces);
            Release(ref _edgeFaceCounts);
        }

        public static GraphicsBuffer Create(string name, object owner, int count, int stride)
        {
            GraphicsBuffer buffer = new(GraphicsBuffer.Target.Structured, count, stride);
            GpuBufferRegistry.Register(buffer, name, owner);
            return buffer;
        }

        private static void Release(ref GraphicsBuffer buffer)
        {
            GpuBufferRegistry.Release(ref buffer);
        }
    }

    public sealed class TopologyScratchBuffers : IDisposable
    {
        private GraphicsBuffer _degrees;
        private GraphicsBuffer _faceDegrees;
        private GraphicsBuffer _uniqueCounts;
        private GraphicsBuffer _provisionalOffsets;
        private GraphicsBuffer _cursors;
        private GraphicsBuffer _faceCursors;
        private GraphicsBuffer _provisionalNeighbours;
        private GraphicsBuffer _scanA;
        private GraphicsBuffer _scanB;
        private GraphicsBuffer _directedMarks;
        private GraphicsBuffer _directedSources;
        private GraphicsBuffer _edgePositions;
        private GraphicsBuffer _failure;
        private bool _disposed;

        public TopologyScratchBuffers(GeometryLayout layout, object owner)
        {
            int directedCapacity = Math.Max(1, layout.EdgeCapacity * 2);
            int provisionalCapacity = layout.Topology == PrimitiveTopology.Triangles
                ? Math.Max(1, layout.PrimitiveCount * 6)
                : Math.Max(1, layout.PrimitiveCount * 2);
            int scanCapacity = Math.Max(
                Math.Max(layout.VertexCount + 1, directedCapacity),
                Math.Max(1, layout.CornerCount));

            _degrees = Create("Topology.Degrees", owner, layout.VertexCount, sizeof(uint));
            _faceDegrees = Create("Topology.FaceDegrees", owner, layout.VertexCount, sizeof(uint));
            _uniqueCounts = Create("Topology.UniqueCounts", owner, layout.VertexCount, sizeof(uint));
            _provisionalOffsets = Create(
                "Topology.ProvisionalOffsets", owner, layout.VertexCount + 1, sizeof(uint));
            _cursors = Create("Topology.Cursors", owner, layout.VertexCount, sizeof(uint));
            _faceCursors = Create("Topology.FaceCursors", owner, layout.VertexCount, sizeof(uint));
            _provisionalNeighbours = Create(
                "Topology.ProvisionalNeighbours", owner, provisionalCapacity, sizeof(uint));
            _scanA = Create("Topology.ScanA", owner, scanCapacity, sizeof(uint));
            _scanB = Create("Topology.ScanB", owner, scanCapacity, sizeof(uint));
            _directedMarks = Create("Topology.DirectedMarks", owner, directedCapacity, sizeof(uint));
            _directedSources = Create("Topology.DirectedSources", owner, directedCapacity, sizeof(uint));
            _edgePositions = Create("Topology.EdgePositions", owner, directedCapacity + 1, sizeof(uint));
            _failure = Create("Topology.Failure", owner, 1, sizeof(uint));
        }

        public GraphicsBuffer Degrees => _degrees;
        public GraphicsBuffer FaceDegrees => _faceDegrees;
        public GraphicsBuffer UniqueCounts => _uniqueCounts;
        public GraphicsBuffer ProvisionalOffsets => _provisionalOffsets;
        public GraphicsBuffer Cursors => _cursors;
        public GraphicsBuffer FaceCursors => _faceCursors;
        public GraphicsBuffer ProvisionalNeighbours => _provisionalNeighbours;
        public GraphicsBuffer ScanA => _scanA;
        public GraphicsBuffer ScanB => _scanB;
        public GraphicsBuffer DirectedMarks => _directedMarks;
        public GraphicsBuffer DirectedSources => _directedSources;
        public GraphicsBuffer EdgePositions => _edgePositions;
        public GraphicsBuffer Failure => _failure;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Release(ref _degrees);
            Release(ref _faceDegrees);
            Release(ref _uniqueCounts);
            Release(ref _provisionalOffsets);
            Release(ref _cursors);
            Release(ref _faceCursors);
            Release(ref _provisionalNeighbours);
            Release(ref _scanA);
            Release(ref _scanB);
            Release(ref _directedMarks);
            Release(ref _directedSources);
            Release(ref _edgePositions);
            Release(ref _failure);
        }

        private static GraphicsBuffer Create(string name, object owner, int count, int stride) =>
            TopologyBuffers.Create(name, owner, count, stride);

        private static void Release(ref GraphicsBuffer buffer)
        {
            GpuBufferRegistry.Release(ref buffer);
        }
    }
}
