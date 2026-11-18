using System;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public enum PrimitiveTopology
    {
        Triangles,
        Lines,
        Points
    }

    public enum AttributeDomain
    {
        Vertex,
        Corner,
        Edge,
        Primitive,
        Region,
        Geometry
    }

    public enum AttributeFormat
    {
        Float,
        Float2,
        Float3,
        Float4,
        UInt,
        UInt2
    }

    public enum AttributeInterpolation
    {
        Smooth,
        Flat
    }

    public enum AttributeLifetime
    {
        Static,
        Dynamic
    }

    [Flags]
    public enum GeometryFlags : uint
    {
        None = 0,
        Manifold = 1 << 0,
        Closed = 1 << 1,
        OrderedOneRing = 1 << 2,
        HasBoundary = 1 << 3,
        ValidationFailed = 1 << 4
    }

    [Serializable]
    public readonly struct AttributeId : IEquatable<AttributeId>
    {
        public AttributeId(string value)
        {
            Value = string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Attribute id cannot be empty.", nameof(value))
                : value;
        }

        public string Value { get; }

        public bool Equals(AttributeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AttributeId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    public static class AttributeIds
    {
        public static readonly AttributeId RestPosition = new("RestPosition");
        public static readonly AttributeId RestNormal = new("RestNormal");
        public static readonly AttributeId RestTangent = new("RestTangent");
        public static readonly AttributeId LocalCoord = new("LocalCoord");
        public static readonly AttributeId RegionId = new("RegionId");
        public static readonly AttributeId BoundaryWeight = new("BoundaryWeight");
        public static readonly AttributeId NeighbourCount = new("NeighbourCount");
        public static readonly AttributeId Curvature = new("Curvature");
        public static readonly AttributeId EdgeRestLength = new("EdgeRestLength");
    }

    public sealed class AttributeDescriptor
    {
        public AttributeDescriptor(
            AttributeId id,
            AttributeDomain domain,
            AttributeFormat format,
            AttributeInterpolation interpolation = AttributeInterpolation.Smooth,
            AttributeLifetime lifetime = AttributeLifetime.Static)
        {
            Id = id;
            Domain = domain;
            Format = format;
            Interpolation = interpolation;
            Lifetime = lifetime;
        }

        public AttributeId Id { get; }
        public AttributeDomain Domain { get; }
        public AttributeFormat Format { get; }
        public AttributeInterpolation Interpolation { get; }
        public AttributeLifetime Lifetime { get; }
        public int Stride => Format switch
        {
            AttributeFormat.Float => sizeof(float),
            AttributeFormat.Float2 => 2 * sizeof(float),
            AttributeFormat.Float3 => 3 * sizeof(float),
            AttributeFormat.Float4 => 4 * sizeof(float),
            AttributeFormat.UInt => sizeof(uint),
            AttributeFormat.UInt2 => 2 * sizeof(uint),
            _ => throw new ArgumentOutOfRangeException(nameof(Format), Format, null)
        };
    }

    public readonly struct GeometryHeader
    {
        public readonly uint VertexCount;
        public readonly uint PrimitiveCount;
        public readonly uint EdgeCount;
        public readonly uint RegionCount;
        public readonly Vector3 BoundsMin;
        public readonly uint Flags;
        public readonly Vector3 BoundsMax;
        public readonly uint MaxDegree;

        public GeometryHeader(
            uint vertexCount,
            uint primitiveCount,
            uint edgeCount,
            uint regionCount,
            Vector3 boundsMin,
            uint flags,
            Vector3 boundsMax,
            uint maxDegree)
        {
            VertexCount = vertexCount;
            PrimitiveCount = primitiveCount;
            EdgeCount = edgeCount;
            RegionCount = regionCount;
            BoundsMin = boundsMin;
            Flags = flags;
            BoundsMax = boundsMax;
            MaxDegree = maxDegree;
        }
    }
}
