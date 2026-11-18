using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public static class TopologyValidator
    {
        public static IReadOnlyList<string> Validate(GeometryResource resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            List<string> errors = new();
            GeometryLayout layout = resource.Layout;

            uint[] indices = Read<uint>(resource.Indices);
            uint[] offsets = Read<uint>(resource.Topology.NeighbourOffsets);
            uint[] neighbours = Read<uint>(resource.Topology.Neighbours);
            uint[] neighbourEdges = Read<uint>(resource.Topology.NeighbourEdge);
            Vector2Int[] edges = Read<Vector2Int>(resource.Topology.Edges);
            float[] restLengths = Read<float>(resource.Topology.EdgeRestLength);
            uint[] edgeFaceCounts = Read<uint>(resource.Topology.EdgeFaceCounts);
            uint[] faceOffsets = Read<uint>(resource.Topology.VertexFaceOffsets);
            uint[] faces = Read<uint>(resource.Topology.VertexFaces);
            Vector3[] positions = Read<Vector3>(
                resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            float[] boundary = Read<float>(
                resource.Attributes.Require(AttributeIds.BoundaryWeight).Buffer);
            uint[] degrees = Read<uint>(
                resource.Attributes.Require(AttributeIds.NeighbourCount).Buffer);

            if (indices.Length != layout.IndexCount)
                errors.Add($"Index count is {indices.Length}, expected {layout.IndexCount}.");
            if (offsets.Length != layout.VertexCount + 1)
                errors.Add("Neighbour offset cardinality does not match the vertex count.");
            if (offsets[layout.VertexCount] != layout.EdgeCapacity * 2)
                errors.Add(
                    $"Directed degree total is {offsets[layout.VertexCount]}, expected {layout.EdgeCapacity * 2}.");
            if (faceOffsets[layout.VertexCount] != layout.CornerCount)
                errors.Add("Vertex-face incidence count does not match the corner count.");

            for (int primitive = 0; primitive < layout.PrimitiveCount; primitive++)
            {
                uint a = indices[primitive * 3];
                uint b = indices[primitive * 3 + 1];
                uint c = indices[primitive * 3 + 2];
                if (a >= layout.VertexCount || b >= layout.VertexCount || c >= layout.VertexCount ||
                    a == b || b == c || c == a)
                {
                    errors.Add($"Primitive {primitive} is invalid or degenerate.");
                    continue;
                }

                float area = Vector3.Cross(
                    positions[b] - positions[a],
                    positions[c] - positions[a]).magnitude;
                if (!IsFinite(area) || area <= 1e-7f)
                    errors.Add($"Primitive {primitive} has zero area.");
            }

            for (int vertex = 0; vertex < layout.VertexCount; vertex++)
            {
                uint begin = offsets[vertex];
                uint end = offsets[vertex + 1];
                if (end < begin || end > neighbours.Length)
                {
                    errors.Add($"Vertex {vertex} has invalid CSR offsets.");
                    continue;
                }
                if (degrees[vertex] != end - begin)
                    errors.Add($"Vertex {vertex} degree metadata is inconsistent with CSR.");

                for (uint slot = begin; slot < end; slot++)
                {
                    uint neighbour = neighbours[slot];
                    if (neighbour >= layout.VertexCount || neighbour == vertex)
                    {
                        errors.Add($"Vertex {vertex} has invalid neighbour {neighbour}.");
                        continue;
                    }
                    if (slot > begin && neighbours[slot - 1] >= neighbour)
                        errors.Add($"Vertex {vertex} neighbours are not strictly sorted.");
                    if (!Contains(neighbours, offsets[neighbour], offsets[neighbour + 1], (uint)vertex))
                        errors.Add($"Adjacency {vertex}->{neighbour} is not symmetric.");
                    if (neighbourEdges[slot] >= layout.EdgeCapacity)
                        errors.Add($"Vertex {vertex} has an invalid edge id.");
                }

                if (boundary[vertex] != 0f)
                    errors.Add($"Closed icosphere vertex {vertex} is marked as a boundary.");
            }

            for (int edge = 0; edge < layout.EdgeCapacity; edge++)
            {
                Vector2Int endpoints = edges[edge];
                if (endpoints.x >= endpoints.y || endpoints.y >= layout.VertexCount)
                    errors.Add($"Edge {edge} has invalid endpoints.");
                if (!IsFinite(restLengths[edge]) || restLengths[edge] <= 1e-7f)
                    errors.Add($"Edge {edge} has invalid rest length.");
                if (edgeFaceCounts[edge] != 2)
                    errors.Add($"Edge {edge} belongs to {edgeFaceCounts[edge]} triangles.");
                if (edge > 0 &&
                    (edges[edge - 1].x > endpoints.x ||
                     (edges[edge - 1].x == endpoints.x && edges[edge - 1].y >= endpoints.y)))
                    errors.Add("Edges are not strictly sorted by endpoint.");
            }

            for (int vertex = 0; vertex < layout.VertexCount; vertex++)
            {
                uint previous = uint.MaxValue;
                for (uint slot = faceOffsets[vertex]; slot < faceOffsets[vertex + 1]; slot++)
                {
                    if (faces[slot] >= layout.PrimitiveCount)
                        errors.Add($"Vertex {vertex} references an invalid face.");
                    if (faces[slot] <= previous && previous != uint.MaxValue)
                        errors.Add($"Vertex {vertex} face incidence is not sorted.");
                    previous = faces[slot];
                }
            }

            return errors;
        }

        private static bool Contains(uint[] values, uint begin, uint end, uint expected)
        {
            for (uint i = begin; i < end; i++)
            {
                if (values[i] == expected) return true;
            }
            return false;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static T[] Read<T>(GraphicsBuffer buffer)
            where T : struct
        {
            T[] values = new T[buffer.count];
            buffer.GetData(values);
            return values;
        }
    }
}
