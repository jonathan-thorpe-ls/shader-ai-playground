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
    }
}
