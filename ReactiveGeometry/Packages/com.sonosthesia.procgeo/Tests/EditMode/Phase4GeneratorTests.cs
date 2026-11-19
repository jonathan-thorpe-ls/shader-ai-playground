using System;
using NUnit.Framework;
using UnityEngine;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class Phase4GeneratorTests
    {
        [TestCase(ParametricGeometryType.Plane)]
        [TestCase(ParametricGeometryType.Sine)]
        [TestCase(ParametricGeometryType.Saddle)]
        [TestCase(ParametricGeometryType.Twist)]
        [TestCase(ParametricGeometryType.Cylinder)]
        [TestCase(ParametricGeometryType.Torus)]
        [TestCase(ParametricGeometryType.Mobius)]
        [TestCase(ParametricGeometryType.LineTube)]
        [TestCase(ParametricGeometryType.CircleTube)]
        [TestCase(ParametricGeometryType.HelixTube)]
        [TestCase(ParametricGeometryType.LissajousTube)]
        [TestCase(ParametricGeometryType.TorusKnotTube)]
        public void GeneratorBuildsFiniteValidatedGeometry(ParametricGeometryType type)
        {
            Assume.That(
                SystemInfo.supportsComputeShaders,
                "A graphics compute device is required.");
            ParametricGeometrySettings settings = ParametricGeometrySettings.Default;
            settings.uSegments = 12;
            settings.vSegments = 6;

            using ParametricGeometryGenerator generator =
                new(type, settings);
            using GeometryResource resource = new(generator.Plan());
            TopologyBuilder topology = new();
            AttributeProcessor attributes = new();
            GeometryValidator validator = new();
            using GeometryCommandScheduler scheduler =
                new($"Phase4 Test {type}");
            scheduler.Record(command =>
            {
                generator.Record(command, resource);
                topology.Record(command, resource);
                attributes.Record(command, resource);
                validator.Record(command, resource);
            });
            scheduler.ExecuteOnce();

            uint[] failure = new uint[1];
            resource.Topology.Scratch.Failure.GetData(failure);
            Assert.That(failure[0], Is.Zero);
            ValidationResult[] validation = new ValidationResult[1];
            resource.ValidationResult.GetData(validation);
            Assert.That(validation[0].ErrorCount, Is.Zero);
            foreach (Vector3 position in generator.DebugPositions)
                Assert.That(
                    float.IsFinite(position.x) &&
                    float.IsFinite(position.y) &&
                    float.IsFinite(position.z),
                    Is.True);
        }

        [Test]
        public void TorusKnotIsClosedWithoutDegenerateTriangles()
        {
            ParametricGeometrySettings settings = ParametricGeometrySettings.Default;
            settings.uSegments = 24;
            settings.vSegments = 8;
            settings.knotP = 2;
            settings.knotQ = 3;
            using ParametricGeometryGenerator generator = new(
                ParametricGeometryType.TorusKnotTube, settings);

            Assert.That(generator.VertexCount, Is.EqualTo(24 * 8));
            Assert.That(generator.PrimitiveCount, Is.EqualTo(24 * 8 * 2));
            Assert.That(
                Array.TrueForAll(
                    generator.DebugTriangles,
                    triangle => triangle.x != triangle.y &&
                        triangle.y != triangle.z &&
                        triangle.z != triangle.x),
                Is.True);
        }

        [Test]
        public void PlaneNormalsAndWindingFaceUp()
        {
            using ParametricGeometryGenerator generator = new(
                ParametricGeometryType.Plane,
                SmallSettings());

            Assert.That(
                Array.TrueForAll(
                    generator.DebugNormals,
                    normal => Vector3.Dot(normal, Vector3.up) > 0.999f),
                Is.True);
            AssertAllTriangleNormalsTowardExpected(generator, Vector3.up);
        }

        [Test]
        public void CylinderNormalsAndWindingFaceOutward()
        {
            using ParametricGeometryGenerator generator = new(
                ParametricGeometryType.Cylinder,
                SmallSettings());

            for (int i = 0; i < generator.DebugPositions.Length; i++)
            {
                Vector3 expected = generator.DebugPositions[i];
                expected.y = 0f;
                expected.Normalize();
                Assert.That(Vector3.Dot(generator.DebugNormals[i], expected), Is.GreaterThan(0.999f));
            }
        }

        [Test]
        public void TorusNormalsAndWindingFaceOutward()
        {
            ParametricGeometrySettings settings = SmallSettings();
            using ParametricGeometryGenerator generator = new(
                ParametricGeometryType.Torus,
                settings);

            for (int i = 0; i < generator.DebugPositions.Length; i++)
            {
                Vector3 p = generator.DebugPositions[i];
                Vector3 centreline =
                    new Vector3(p.x, 0f, p.z).normalized * settings.radius;
                Vector3 expected = (p - centreline).normalized;
                Assert.That(Vector3.Dot(generator.DebugNormals[i], expected), Is.GreaterThan(0.999f));
            }
        }

        private static ParametricGeometrySettings SmallSettings()
        {
            ParametricGeometrySettings settings = ParametricGeometrySettings.Default;
            settings.uSegments = 16;
            settings.vSegments = 8;
            return settings;
        }

        private static void AssertAllTriangleNormalsTowardExpected(
            ParametricGeometryGenerator generator,
            Vector3 expected)
        {
            foreach (Vector3Int triangle in generator.DebugTriangles)
            {
                Vector3 a = generator.DebugPositions[triangle.x];
                Vector3 b = generator.DebugPositions[triangle.y];
                Vector3 c = generator.DebugPositions[triangle.z];
                Assert.That(
                    Vector3.Dot(Vector3.Cross(b - a, c - a).normalized, expected),
                    Is.GreaterThan(0.999f));
            }
        }
    }
}
