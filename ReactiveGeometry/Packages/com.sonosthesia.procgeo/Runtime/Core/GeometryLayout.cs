using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public sealed class GeometryLayout
    {
        private readonly IReadOnlyList<AttributeDescriptor> _attributes;

        public GeometryLayout(
            string name,
            PrimitiveTopology topology,
            int vertexCount,
            int primitiveCount,
            int edgeCapacity,
            int regionCount,
            int maxSortDegree,
            IReadOnlyList<AttributeDescriptor> attributes,
            bool useMeshBacking = true)
        {
            if (vertexCount <= 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
            if (primitiveCount < 0) throw new ArgumentOutOfRangeException(nameof(primitiveCount));
            if (edgeCapacity < 0) throw new ArgumentOutOfRangeException(nameof(edgeCapacity));
            if (regionCount < 0) throw new ArgumentOutOfRangeException(nameof(regionCount));
            if (maxSortDegree <= 0) throw new ArgumentOutOfRangeException(nameof(maxSortDegree));

            Name = name;
            Topology = topology;
            VertexCount = vertexCount;
            PrimitiveCount = primitiveCount;
            IndexCount = topology == PrimitiveTopology.Triangles ? primitiveCount * 3 : primitiveCount * 2;
            EdgeCapacity = edgeCapacity;
            RegionCount = regionCount;
            CornerCount = topology == PrimitiveTopology.Triangles
                ? primitiveCount * 3
                : primitiveCount * 2;
            MaxSortDegree = maxSortDegree;
            UseMeshBacking = useMeshBacking;
            _attributes = attributes ?? Array.Empty<AttributeDescriptor>();
        }

        public string Name { get; }
        public PrimitiveTopology Topology { get; }
        public int VertexCount { get; }
        public int PrimitiveCount { get; }
        public int IndexCount { get; }
        public int EdgeCapacity { get; }
        public int RegionCount { get; }
        public int CornerCount { get; }
        public int MaxSortDegree { get; }
        public bool UseMeshBacking { get; }
        public IReadOnlyList<AttributeDescriptor> Attributes => _attributes;

        public int AttributeCount(AttributeDomain domain) => domain switch
        {
            AttributeDomain.Vertex => VertexCount,
            AttributeDomain.Corner => CornerCount,
            AttributeDomain.Edge => EdgeCapacity,
            AttributeDomain.Primitive => PrimitiveCount,
            AttributeDomain.Region => RegionCount,
            AttributeDomain.Geometry => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, null)
        };
    }
}
