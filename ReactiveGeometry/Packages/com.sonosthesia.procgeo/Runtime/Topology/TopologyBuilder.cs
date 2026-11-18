using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class TopologyBuilder
    {
        private readonly ComputeShader _shader;
        private readonly int _clear;
        private readonly int _countPrimitives;
        private readonly int _prepareScatter;
        private readonly int _scatter;
        private readonly int _sortDedupe;
        private readonly int _compactNeighbours;
        private readonly int _markEdges;
        private readonly int _compactEdges;
        private readonly int _reverseEdges;
        private readonly int _sortFaces;
        private readonly int _edgeData;
        private readonly int _boundary;

        public TopologyBuilder(ComputeShader shader = null)
        {
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/TopologyBuild")
                    ?? throw new InvalidOperationException("ProcGeo/TopologyBuild.compute was not found.");
            _clear = _shader.FindKernel("ClearVertices");
            _countPrimitives = _shader.FindKernel("CountPrimitives");
            _prepareScatter = _shader.FindKernel("PrepareProvisionalScatter");
            _scatter = _shader.FindKernel("ScatterNeighboursAndFaces");
            _sortDedupe = _shader.FindKernel("SortDedupeNeighbours");
            _compactNeighbours = _shader.FindKernel("CompactNeighbours");
            _markEdges = _shader.FindKernel("MarkEdges");
            _compactEdges = _shader.FindKernel("CompactEdges");
            _reverseEdges = _shader.FindKernel("AssignReverseEdgeIds");
            _sortFaces = _shader.FindKernel("SortFaces");
            _edgeData = _shader.FindKernel("ComputeEdgeData");
            _boundary = _shader.FindKernel("ComputeBoundaryAndDegrees");
        }

        public void Record(CommandBuffer command, GeometryResource resource)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (resource.Layout.Topology != PrimitiveTopology.Triangles)
                throw new NotSupportedException("Phase 1 topology build supports triangle meshes only.");

            GeometryLayout layout = resource.Layout;
            TopologyBuffers topology = resource.Topology;
            TopologyScratchBuffers scratch = topology.Scratch;
            int vertices = layout.VertexCount;
            int primitives = layout.PrimitiveCount;
            int directedCapacity = layout.EdgeCapacity * 2;

            SetCommon(command, resource);
            BindKernel(command, _clear, resource);
            Dispatch(command, _clear, vertices);
            BindKernel(command, _countPrimitives, resource);
            Dispatch(command, _countPrimitives, primitives);

            Scan(command, scratch.Degrees, vertices, scratch.ProvisionalOffsets);
            Scan(command, scratch.FaceDegrees, vertices, topology.VertexFaceOffsets);
            BindKernel(command, _prepareScatter, resource);
            Dispatch(command, _prepareScatter, vertices);
            BindKernel(command, _scatter, resource);
            Dispatch(command, _scatter, primitives);
            BindKernel(command, _sortDedupe, resource);
            Dispatch(command, _sortDedupe, vertices);

            Scan(command, scratch.UniqueCounts, vertices, topology.NeighbourOffsets);
            BindKernel(command, _compactNeighbours, resource);
            Dispatch(command, _compactNeighbours, vertices);

            BindKernel(command, _markEdges, resource);
            Dispatch(command, _markEdges, directedCapacity);
            Scan(command, scratch.DirectedMarks, directedCapacity, scratch.EdgePositions);
            BindKernel(command, _compactEdges, resource);
            Dispatch(command, _compactEdges, directedCapacity);
            BindKernel(command, _reverseEdges, resource);
            Dispatch(command, _reverseEdges, directedCapacity);

            BindKernel(command, _sortFaces, resource);
            Dispatch(command, _sortFaces, vertices);
            BindKernel(command, _edgeData, resource);
            Dispatch(command, _edgeData, Math.Max(primitives, layout.EdgeCapacity));
            BindKernel(command, _boundary, resource);
            Dispatch(command, _boundary, vertices);
        }

        private void SetCommon(CommandBuffer command, GeometryResource resource)
        {
            GeometryLayout layout = resource.Layout;
            command.SetComputeIntParam(_shader, "_VertexCount", layout.VertexCount);
            command.SetComputeIntParam(_shader, "_PrimitiveCount", layout.PrimitiveCount);
            command.SetComputeIntParam(_shader, "_DirectedCapacity", layout.EdgeCapacity * 2);
            command.SetComputeIntParam(_shader, "_EdgeCapacity", layout.EdgeCapacity);
            command.SetComputeIntParam(_shader, "_MaxSortDegree", layout.MaxSortDegree);
        }

        private void Scan(
            CommandBuffer command,
            GraphicsBuffer input,
            int count,
            GraphicsBuffer offsets)
        {
            int init = _shader.FindKernel("ScanInit");
            int stepKernel = _shader.FindKernel("ScanStep");
            int offsetsKernel = _shader.FindKernel("WriteOffsets");
            GraphicsBuffer scanA = _scanScratchA;
            GraphicsBuffer scanB = _scanScratchB;

            command.SetComputeIntParam(_shader, "_ScanCount", count);
            command.SetComputeBufferParam(_shader, init, "_ScanInput", input);
            command.SetComputeBufferParam(_shader, init, "_ScanInclusiveA", scanA);
            Dispatch(command, init, Math.Max(count, 1));

            GraphicsBuffer read = scanA;
            GraphicsBuffer write = scanB;
            for (int stride = 1; stride < count; stride <<= 1)
            {
                command.SetComputeIntParam(_shader, "_ScanStep", stride);
                command.SetComputeBufferParam(_shader, stepKernel, "_ScanRead", read);
                command.SetComputeBufferParam(_shader, stepKernel, "_ScanWrite", write);
                Dispatch(command, stepKernel, count);
                (read, write) = (write, read);
            }

            command.SetComputeBufferParam(_shader, offsetsKernel, "_ScanFinal", read);
            command.SetComputeBufferParam(_shader, offsetsKernel, "_OffsetOutput", offsets);
            Dispatch(command, offsetsKernel, count + 1);
        }

        private GraphicsBuffer _scanScratchA;
        private GraphicsBuffer _scanScratchB;

        private void BindKernel(CommandBuffer command, int kernel, GeometryResource resource)
        {
            TopologyBuffers topology = resource.Topology;
            TopologyScratchBuffers scratch = topology.Scratch;
            _scanScratchA = scratch.ScanA;
            _scanScratchB = scratch.ScanB;
            AttributeSet attributes = resource.Attributes;

            if (kernel == _clear)
            {
                Set("_Degrees", scratch.Degrees);
                Set("_FaceDegrees", scratch.FaceDegrees);
                Set("_UniqueCounts", scratch.UniqueCounts);
                Set("_BoundaryWeights", attributes.Require(AttributeIds.BoundaryWeight).Buffer);
                Set("_NeighbourCounts", attributes.Require(AttributeIds.NeighbourCount).Buffer);
                Set("_Failure", scratch.Failure);
            }
            else if (kernel == _countPrimitives)
            {
                Set("_Indices", resource.Indices);
                Set("_Degrees", scratch.Degrees);
                Set("_FaceDegrees", scratch.FaceDegrees);
                Set("_Failure", scratch.Failure);
            }
            else if (kernel == _prepareScatter)
            {
                Set("_ProvisionalOffsets", scratch.ProvisionalOffsets);
                Set("_Cursors", scratch.Cursors);
                Set("_FaceCursors", scratch.FaceCursors);
                Set("_VertexFaceOffsets", topology.VertexFaceOffsets);
            }
            else if (kernel == _scatter)
            {
                Set("_Indices", resource.Indices);
                Set("_Cursors", scratch.Cursors);
                Set("_ProvisionalNeighbours", scratch.ProvisionalNeighbours);
                Set("_FaceCursors", scratch.FaceCursors);
                Set("_VertexFaces", topology.VertexFaces);
            }
            else if (kernel == _sortDedupe)
            {
                Set("_ProvisionalOffsets", scratch.ProvisionalOffsets);
                Set("_ProvisionalNeighbours", scratch.ProvisionalNeighbours);
                Set("_UniqueCounts", scratch.UniqueCounts);
                Set("_Failure", scratch.Failure);
            }
            else if (kernel == _compactNeighbours)
            {
                Set("_ProvisionalOffsets", scratch.ProvisionalOffsets);
                Set("_NeighbourOffsets", topology.NeighbourOffsets);
                Set("_ProvisionalNeighbours", scratch.ProvisionalNeighbours);
                Set("_UniqueCounts", scratch.UniqueCounts);
                Set("_Neighbours", topology.Neighbours);
                Set("_DirectedSources", scratch.DirectedSources);
                Set("_NeighbourCounts", attributes.Require(AttributeIds.NeighbourCount).Buffer);
            }
            else if (kernel == _markEdges)
            {
                Set("_NeighbourOffsets", topology.NeighbourOffsets);
                Set("_NeighboursRead", topology.Neighbours);
                Set("_DirectedSourcesRead", scratch.DirectedSources);
                Set("_DirectedMarks", scratch.DirectedMarks);
            }
            else if (kernel == _compactEdges)
            {
                Set("_NeighbourOffsets", topology.NeighbourOffsets);
                Set("_NeighboursRead", topology.Neighbours);
                Set("_NeighbourEdge", topology.NeighbourEdge);
                Set("_Edges", topology.Edges);
                Set("_EdgeTypes", topology.EdgeTypes);
                Set("_EdgeFaceCounts", topology.EdgeFaceCounts);
                Set("_DirectedMarksRead", scratch.DirectedMarks);
                Set("_DirectedSourcesRead", scratch.DirectedSources);
                Set("_EdgePositions", scratch.EdgePositions);
                Set("_Failure", scratch.Failure);
            }
            else if (kernel == _reverseEdges)
            {
                Set("_NeighbourOffsets", topology.NeighbourOffsets);
                Set("_NeighboursRead", topology.Neighbours);
                Set("_NeighbourEdge", topology.NeighbourEdge);
                Set("_DirectedSourcesRead", scratch.DirectedSources);
                Set("_Failure", scratch.Failure);
            }
            else if (kernel == _sortFaces)
            {
                Set("_VertexFaceOffsets", topology.VertexFaceOffsets);
                Set("_VertexFaces", topology.VertexFaces);
            }
            else if (kernel == _edgeData)
            {
                Set("_Indices", resource.Indices);
                Set("_RestPositions", attributes.Require(AttributeIds.RestPosition).Buffer);
                Set("_EdgesRead", topology.Edges);
                Set("_RestLengths", topology.EdgeRestLength);
                Set("_AttributeEdgeRestLengths", attributes.Require(AttributeIds.EdgeRestLength).Buffer);
                Set("_NeighbourOffsets", topology.NeighbourOffsets);
                Set("_NeighboursRead", topology.Neighbours);
                Set("_NeighbourEdge", topology.NeighbourEdge);
                Set("_EdgeFaceCounts", topology.EdgeFaceCounts);
                Set("_Failure", scratch.Failure);
            }
            else if (kernel == _boundary)
            {
                Set("_NeighbourOffsets", topology.NeighbourOffsets);
                Set("_NeighbourEdge", topology.NeighbourEdge);
                Set("_EdgeFaceCountsRead", topology.EdgeFaceCounts);
                Set("_BoundaryWeights", attributes.Require(AttributeIds.BoundaryWeight).Buffer);
            }

            void Set(string property, GraphicsBuffer buffer) =>
                command.SetComputeBufferParam(_shader, kernel, property, buffer);
        }

        private void Dispatch(CommandBuffer command, int kernel, int count)
        {
            command.DispatchCompute(_shader, kernel, (count + 63) / 64, 1, 1);
        }
    }
}
