using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class IcosphereGeneratorTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(64)]
        public void Generator_ProducesExpectedTopologyAndSphere(int frequency)
        {
            using GeometryTestContext context = GeometryTestContext.Build(frequency);
            GeometryLayout layout = context.Resource.Layout;
            int expectedVertices = 10 * frequency * frequency + 2;
            int expectedEdges = 30 * frequency * frequency;
            int expectedFaces = 20 * frequency * frequency;

            Assert.That(layout.VertexCount, Is.EqualTo(expectedVertices));
            Assert.That(layout.EdgeCapacity, Is.EqualTo(expectedEdges));
            Assert.That(layout.PrimitiveCount, Is.EqualTo(expectedFaces));
            Assert.That(expectedVertices - expectedEdges + expectedFaces, Is.EqualTo(2));
            Assert.That(context.Validation.ErrorCount, Is.Zero);
            Assert.That(TopologyValidator.Validate(context.Resource), Is.Empty);

            Vector3[] positions = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            Vector3[] normals = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestNormal).Buffer);
            Assert.That(positions.All(position =>
                Mathf.Abs(position.magnitude - 1f) <= 1e-5f), Is.True);
            Assert.That(normals.All(normal =>
                Mathf.Abs(normal.magnitude - 1f) <= 1e-3f), Is.True);
        }

        [Test]
        public void Generator_DegreeHistogramIsTwelveDegreeFiveAndRestDegreeSix()
        {
            const int frequency = 8;
            using GeometryTestContext context = GeometryTestContext.Build(frequency);
            uint[] degrees = context.Read<uint>(
                context.Resource.Attributes.Require(AttributeIds.NeighbourCount).Buffer);

            Assert.That(degrees.Count(value => value == 5), Is.EqualTo(12));
            Assert.That(degrees.Count(value => value == 6), Is.EqualTo(degrees.Length - 12));
            Assert.That(degrees.All(value => value == 5 || value == 6), Is.True);
        }

        [Test]
        public void Generator_AttributeDomainsHaveCorrectCardinality()
        {
            using GeometryTestContext context = GeometryTestContext.Build(3);
            GeometryResource resource = context.Resource;
            Assert.That(
                resource.Attributes.Require(AttributeIds.LocalCoord).Buffer.count,
                Is.EqualTo(resource.Layout.CornerCount));
            Assert.That(
                resource.Attributes.Require(AttributeIds.RegionId).Buffer.count,
                Is.EqualTo(resource.Layout.PrimitiveCount));
            Assert.That(
                resource.Attributes.Require(AttributeIds.RestPosition).Buffer.count,
                Is.EqualTo(resource.Layout.VertexCount));

            uint[] regions = context.Read<uint>(
                resource.Attributes.Require(AttributeIds.RegionId).Buffer);
            Assert.That(regions.All(region => region < 20), Is.True);
        }
    }

    public sealed class ReferenceIcosphereTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        public void Plan_MatchesClosedIcosahedralInvariants(int frequency)
        {
            using IcosphereGenerator generator = new(frequency);
            GeometryLayout layout = generator.Plan();
            int vertices = 10 * frequency * frequency + 2;
            int edges = 30 * frequency * frequency;
            int faces = 20 * frequency * frequency;

            Assert.That(layout.VertexCount, Is.EqualTo(vertices));
            Assert.That(layout.EdgeCapacity, Is.EqualTo(edges));
            Assert.That(layout.PrimitiveCount, Is.EqualTo(faces));
            Assert.That(layout.IndexCount, Is.EqualTo(faces * 3));
            Assert.That(vertices - edges + faces, Is.EqualTo(2));
        }

        [TestCase(2)]
        [TestCase(4)]
        public void Generator_PowerOfTwoPositionsMatchRecursiveReference(int frequency)
        {
            Vector3[] reference;
            Vector3Int[] referenceTriangles;
            using (GeometryTestContext baseMesh = GeometryTestContext.Build(1))
            {
                reference = baseMesh.Read<Vector3>(
                    baseMesh.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
                referenceTriangles = baseMesh.Generator.DebugTriangles;
            }

            int subdivisions = (int)Mathf.Log(frequency, 2);
            (reference, referenceTriangles) = Subdivide(reference, referenceTriangles, subdivisions);
            using GeometryTestContext generated =
                GeometryTestContext.Build(frequency, recursiveProjection: true);
            Vector3[] positions = generated.Read<Vector3>(
                generated.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);

            string[] expected = reference
                .Select(RoundedPosition)
                .OrderBy(value => value, System.StringComparer.Ordinal)
                .ToArray();
            string[] actual = positions
                .Select(RoundedPosition)
                .OrderBy(value => value, System.StringComparer.Ordinal)
                .ToArray();
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static (Vector3[] positions, Vector3Int[] triangles) Subdivide(
            Vector3[] sourcePositions,
            Vector3Int[] sourceTriangles,
            int count)
        {
            List<Vector3> positions = sourcePositions.ToList();
            Vector3Int[] triangles = sourceTriangles;
            for (int iteration = 0; iteration < count; iteration++)
            {
                Dictionary<(int low, int high), int> midpoints = new();
                List<Vector3Int> nextTriangles = new();
                foreach (Vector3Int face in triangles)
                {
                    int ab = Midpoint(face.x, face.y);
                    int bc = Midpoint(face.y, face.z);
                    int ca = Midpoint(face.z, face.x);
                    nextTriangles.Add(new Vector3Int(face.x, ab, ca));
                    nextTriangles.Add(new Vector3Int(ab, face.y, bc));
                    nextTriangles.Add(new Vector3Int(ca, bc, face.z));
                    nextTriangles.Add(new Vector3Int(ab, bc, ca));
                }
                triangles = nextTriangles.ToArray();

                int Midpoint(int first, int second)
                {
                    (int low, int high) key = first < second
                        ? (first, second)
                        : (second, first);
                    if (midpoints.TryGetValue(key, out int id))
                        return id;
                    id = positions.Count;
                    positions.Add(((positions[first] + positions[second]) * 0.5f).normalized);
                    midpoints.Add(key, id);
                    return id;
                }
            }
            return (positions.ToArray(), triangles);
        }

        private static string RoundedPosition(Vector3 position) =>
            $"{position.x:F5},{position.y:F5},{position.z:F5}";
    }
}
