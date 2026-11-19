using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class Phase3FieldTests
    {
        [Test]
        public void GraphPulse_FieldProducesFiniteNormalAlignedForces()
        {
            using GeometryTestContext context = GeometryTestContext.Build(2);
            using GraphDistanceOperator graph = new(new[] { 0 });
            graph.Execute(context.Resource);
            using FieldAccumulator fields = new(
                context.Resource,
                new IGeometryField[] { new GraphPulseField() });

            fields.Record(new FieldParameters
            {
                phase = 0f,
                pulseAmplitude = 8f,
                pulseWidth = 0.2f,
                pulseWeight = 1f
            });

            Vector3[] forces = Read<Vector3>(
                context.Resource.Live.Simulation.ExternalForces);
            Vector3[] normals = Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestNormal).Buffer);
            for (int index = 0; index < forces.Length; index++)
            {
                Assert.That(IsFinite(forces[index]), Is.True);
                if (forces[index].sqrMagnitude > 1e-6f)
                    Assert.That(
                        Vector3.Dot(forces[index].normalized, normals[index]),
                        Is.GreaterThan(0.999f));
            }
            Assert.That(forces[0].sqrMagnitude, Is.GreaterThan(0f));
        }

        [Test]
        public void Fields_ComposeInDeclaredOrderWithoutInvalidValues()
        {
            using GeometryTestContext context = GeometryTestContext.Build(1);
            using GraphDistanceOperator graph = new(new[] { 0 });
            graph.Execute(context.Resource);
            using FieldAccumulator fields = new(
                context.Resource,
                new IGeometryField[]
                {
                    new GraphPulseField(),
                    new DirectionalField()
                });

            fields.Record(new FieldParameters
            {
                phase = 0.5f,
                pulseAmplitude = 4f,
                pulseWidth = 0.15f,
                directionalStrength = 3f,
                direction = Vector3.up,
                pulseWeight = 0.5f,
                directionalWeight = 0.75f
            });

            foreach (Vector3 force in Read<Vector3>(
                         context.Resource.Live.Simulation.ExternalForces))
                Assert.That(IsFinite(force), Is.True);
        }

        [Test]
        public void Simulation_WithoutFieldsPreservesPhase2RestState()
        {
            using GeometryTestContext context = GeometryTestContext.Build(2);
            using NeighbourSimulation simulation = new(context.Resource);
            simulation.RecordFixedSteps(
                12, SimulationParameters.Nominal, publish: false);

            Vector3[] positions = Read<Vector3>(
                context.Resource.Live.Simulation.Position(simulation.ReadIndex));
            Vector3[] rest = Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            for (int index = 0; index < positions.Length; index++)
                Assert.That(
                    Vector3.Distance(positions[index], rest[index]),
                    Is.LessThan(1e-4f));
        }

        private static T[] Read<T>(GraphicsBuffer buffer)
            where T : struct
        {
            T[] result = new T[buffer.count];
            buffer.GetData(result);
            return result;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
