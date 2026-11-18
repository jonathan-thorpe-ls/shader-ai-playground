using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class TopologyBuilderTests
    {
        [Test]
        public void Builder_HandlesTriangleFixtures()
        {
            AssertFixture(
                new[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)
                },
                new uint[] { 0, 1, 2 },
                3);
            AssertFixture(
                new[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0),
                    new Vector3(1, 1, 0), new Vector3(0, 1, 0)
                },
                new uint[] { 0, 1, 2, 0, 2, 3 },
                5);
            AssertFixture(
                new[]
                {
                    new Vector3(1, 1, 1), new Vector3(-1, -1, 1),
                    new Vector3(-1, 1, -1), new Vector3(1, -1, -1)
                },
                new uint[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 },
                6);
            AssertFixture(
                new[]
                {
                    new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 0, 0),
                    new Vector3(-1, -1, 0), new Vector3(1, -1, 0)
                },
                new uint[] { 0, 1, 2, 0, 3, 4 },
                6);
            AssertFixture(
                new[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
                    new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(3, 1, 0)
                },
                new uint[] { 0, 1, 2, 3, 4, 5 },
                6);
        }

        [Test]
        public void Builder_ProducesDeterministicCommittedBuffers()
        {
            byte[] firstEdges;
            byte[] firstNeighbours;
            byte[] secondEdges;
            byte[] secondNeighbours;

            using (GeometryTestContext first = GeometryTestContext.Build(4))
            {
                firstEdges = first.Read<Vector2Int>(first.Resource.Topology.Edges)
                    .SelectMany(value => BitConverter.GetBytes(value.x).Concat(BitConverter.GetBytes(value.y)))
                    .ToArray();
                firstNeighbours = first.Read<uint>(first.Resource.Topology.Neighbours)
                    .SelectMany(BitConverter.GetBytes)
                    .ToArray();
            }

            using (GeometryTestContext second = GeometryTestContext.Build(4))
            {
                secondEdges = second.Read<Vector2Int>(second.Resource.Topology.Edges)
                    .SelectMany(value => BitConverter.GetBytes(value.x).Concat(BitConverter.GetBytes(value.y)))
                    .ToArray();
                secondNeighbours = second.Read<uint>(second.Resource.Topology.Neighbours)
                    .SelectMany(BitConverter.GetBytes)
                    .ToArray();
            }

            Assert.That(secondEdges, Is.EqualTo(firstEdges));
            Assert.That(secondNeighbours, Is.EqualTo(firstNeighbours));
        }

        [Test]
        public void Builder_ProducesOnlyPositiveFiniteRestLengths()
        {
            using GeometryTestContext context = GeometryTestContext.Build(4);
            float[] lengths = context.Read<float>(context.Resource.Topology.EdgeRestLength);
            Assert.That(lengths.All(value =>
                value > 0f && !float.IsNaN(value) && !float.IsInfinity(value)), Is.True);
        }

        [Test]
        public void Lifecycle_LeavesNoRegisteredGpuBuffers()
        {
            int baseline = GpuBufferRegistry.Count;
            using (GeometryTestContext.Build(4))
            {
                Assert.That(GpuBufferRegistry.Count, Is.GreaterThan(baseline));
            }
            Assert.That(GpuBufferRegistry.Count, Is.EqualTo(baseline));
        }

        private static void AssertFixture(Vector3[] positions, uint[] indices, int edgeCount)
        {
            AttributeDescriptor[] descriptors =
            {
                new(AttributeIds.RestPosition, AttributeDomain.Vertex, AttributeFormat.Float3),
                new(AttributeIds.BoundaryWeight, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.NeighbourCount, AttributeDomain.Vertex, AttributeFormat.UInt),
                new(AttributeIds.EdgeRestLength, AttributeDomain.Edge, AttributeFormat.Float)
            };
            GeometryLayout layout = new(
                "TopologyFixture",
                PrimitiveTopology.Triangles,
                positions.Length,
                indices.Length / 3,
                edgeCount,
                1,
                32,
                descriptors);
            using GeometryResource resource = new(layout);
            resource.Indices.SetData(indices);
            resource.Attributes.Require(AttributeIds.RestPosition).Buffer.SetData(positions);
            using GeometryCommandScheduler scheduler = new("Topology Fixture");
            TopologyBuilder builder = new();
            scheduler.Record(command => builder.Record(command, resource));
            scheduler.ExecuteOnce();

            uint[] failure = new uint[1];
            resource.Topology.Scratch.Failure.GetData(failure);
            Assert.That(failure[0], Is.Zero);
            uint[] offsets = Read<uint>(resource.Topology.NeighbourOffsets);
            Assert.That(offsets[offsets.Length - 1], Is.EqualTo(edgeCount * 2));
            Vector2Int[] edges = Read<Vector2Int>(resource.Topology.Edges);
            Assert.That(edges.Distinct().Count(), Is.EqualTo(edgeCount));
        }

        private static T[] Read<T>(GraphicsBuffer buffer)
            where T : struct
        {
            T[] result = new T[buffer.count];
            buffer.GetData(result);
            return result;
        }
    }
}
