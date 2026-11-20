using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class NeighbourSimulationTests
    {
        [Test]
        public void Simulation_InitializesBothBuffersFromRest()
        {
            using GeometryTestContext context = GeometryTestContext.Build(4);
            using NeighbourSimulation simulation = new(context.Resource);
            Vector3[] rest = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);

            for (int index = 0; index < 2; index++)
            {
                Assert.That(
                    context.Read<Vector3>(context.Resource.Live.Simulation.Position(index)),
                    Is.EqualTo(rest).Within(1e-6f));
                Assert.That(
                    context.Read<Vector3>(context.Resource.Live.Simulation.Velocity(index))
                        .All(value => value == Vector3.zero),
                    Is.True);
            }
            Assert.That(SimulationDiagnostics.ReadStatus(context.Resource).StepSequence, Is.Zero);
        }

        [Test]
        public void Simulation_RemainsAtRestWithoutImpulse()
        {
            using GeometryTestContext context = GeometryTestContext.Build(4);
            using NeighbourSimulation simulation = new(context.Resource);
            Vector3[] rest = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);

            simulation.RecordFixedSteps(12, SimulationParameters.Nominal);

            Assert.That(
                context.Read<Vector3>(
                    context.Resource.Live.Simulation.Position(simulation.ReadIndex)),
                Is.EqualTo(rest).Within(2e-5f));
            SimulationStatus status = SimulationDiagnostics.ReadStatus(context.Resource);
            Assert.That(status.InvalidValueCount, Is.Zero);
            Assert.That(status.StepSequence, Is.EqualTo(12));
        }

        [Test]
        public void Simulation_ImpulsePropagatesFromFirstHopOnSecondStep()
        {
            using GeometryTestContext context = GeometryTestContext.Build(4);
            using NeighbourSimulation simulation = new(context.Resource);
            uint target = 0;
            uint[] offsets = context.Read<uint>(context.Resource.Topology.NeighbourOffsets);
            uint[] neighbours = context.Read<uint>(context.Resource.Topology.Neighbours);
            int firstHop = (int)neighbours[offsets[target]];

            simulation.ApplyImpulse((int)target, Vector3.up, 1.5f);
            simulation.Step(
                SimulationParameters.Nominal.fixedDeltaTime,
                SimulationParameters.Nominal);
            Vector3[] afterOne = context.Read<Vector3>(
                context.Resource.Live.Simulation.Velocity(simulation.ReadIndex));
            Assert.That(afterOne[target].magnitude, Is.GreaterThan(0f));
            Assert.That(afterOne[firstHop].magnitude, Is.LessThan(1e-5f));

            simulation.Step(
                SimulationParameters.Nominal.fixedDeltaTime,
                SimulationParameters.Nominal);
            Vector3[] afterTwo = context.Read<Vector3>(
                context.Resource.Live.Simulation.Velocity(simulation.ReadIndex));
            Assert.That(afterTwo[firstHop].magnitude, Is.GreaterThan(1e-6f));
        }

        [Test]
        public void Simulation_PingPongAlternatesWithoutAliasing()
        {
            using GeometryTestContext context = GeometryTestContext.Build(2);
            using NeighbourSimulation simulation = new(context.Resource);

            Assert.That(simulation.ReadIndex, Is.Zero);
            simulation.Step(
                SimulationParameters.Gentle.fixedDeltaTime,
                SimulationParameters.Gentle);
            Assert.That(simulation.ReadIndex, Is.EqualTo(1));
            simulation.Step(
                SimulationParameters.Gentle.fixedDeltaTime,
                SimulationParameters.Gentle);
            Assert.That(simulation.ReadIndex, Is.Zero);
        }

        [Test]
        public void Simulation_ClampsVelocityAndDisplacementToFiniteState()
        {
            using GeometryTestContext context = GeometryTestContext.Build(4);
            using NeighbourSimulation simulation = new(context.Resource);
            SimulationParameters parameters = SimulationParameters.Gentle;
            parameters.stiffness = 0f;
            parameters.restoringStiffness = 0f;
            parameters.damping = 0f;
            parameters.maximumVelocity = 0.05f;
            parameters.maximumDisplacement = 0.01f;

            simulation.ApplyImpulse(0, Vector3.one, 100f);
            simulation.RecordFixedSteps(40, parameters);

            Vector3[] rest = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            Vector3[] positions = context.Read<Vector3>(
                context.Resource.Live.Simulation.Position(simulation.ReadIndex));
            Vector3[] velocities = context.Read<Vector3>(
                context.Resource.Live.Simulation.Velocity(simulation.ReadIndex));
            Assert.That(positions.All(IsFinite), Is.True);
            Assert.That(velocities.All(IsFinite), Is.True);
            Assert.That(positions.Select((value, i) => (value - rest[i]).magnitude).Max(),
                Is.LessThanOrEqualTo(parameters.maximumDisplacement + 1e-5f));
            Assert.That(velocities.Max(value => value.magnitude),
                Is.LessThanOrEqualTo(parameters.maximumVelocity + 1e-5f));

            SimulationStatus status = SimulationDiagnostics.ReadStatus(context.Resource);
            Assert.That(status.ClampedVelocityCount, Is.GreaterThan(0u));
            Assert.That(status.ClampedDisplacementCount, Is.GreaterThan(0u));
            Assert.That(status.InvalidValueCount, Is.Zero);
        }

        [Test]
        public void Simulation_LimitsInwardDisplacementSmoothly()
        {
            const float maximum = 0.05f;
            float inward = MeasureInwardDisplacement(maximum);

            Assert.That(inward, Is.GreaterThan(0f));
            Assert.That(inward, Is.LessThanOrEqualTo(maximum + 1e-5f));
        }

        [Test]
        public void Simulation_PublishesFiniteUnitNormalsAndResetRestoresState()
        {
            using GeometryTestContext context = GeometryTestContext.Build(4);
            using NeighbourSimulation simulation = new(context.Resource);
            Vector3[] rest = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);

            DynamicVertexData[] initialVertices = ReadDynamicVertices(context);
            Vector3[] initialNormals = initialVertices.Select(value => value.Normal).ToArray();
            Assert.That(initialNormals.All(IsFinite), Is.True);
            Assert.That(initialNormals.All(normal => Mathf.Abs(normal.magnitude - 1f) < 1e-4f), Is.True);

            simulation.ApplyImpulse(0, Vector3.up, 2f);
            simulation.RecordFixedSteps(8, SimulationParameters.Nominal);
            Assert.That(
                ReadDynamicVertices(context).Select(value => value.Normal).All(IsFinite),
                Is.True);

            simulation.Reset();
            Assert.That(
                context.Read<Vector3>(context.Resource.Live.Simulation.Position(0)),
                Is.EqualTo(rest).Within(1e-5f));
            Assert.That(SimulationDiagnostics.ReadStatus(context.Resource).StepSequence, Is.Zero);
        }

        [Test]
        public void Simulation_DampingReducesEnergyAndRecoversTowardRest()
        {
            using GeometryTestContext context = GeometryTestContext.Build(4);
            using NeighbourSimulation simulation = new(context.Resource);
            Vector3[] rest = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            simulation.ApplyImpulse(0, Vector3.up, 2f);

            simulation.RecordFixedSteps(60, SimulationParameters.Nominal, false);
            float earlyEnergy = MeasureEnergy(context, simulation, rest);
            simulation.RecordFixedSteps(240, SimulationParameters.Nominal, false);
            float finalEnergy = MeasureEnergy(context, simulation, rest);

            Assert.That(finalEnergy, Is.LessThan(earlyEnergy));
            Assert.That(finalEnergy, Is.LessThan(0.01f));
        }

        [Test]
        public void Simulation_MatchesCpuReferenceForOneStep()
        {
            const int target = 7;
            using GeometryTestContext context = GeometryTestContext.Build(2);
            SimulationParameters parameters = SimulationParameters.Gentle;
            Vector3[] rest = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            uint[] offsets = context.Read<uint>(context.Resource.Topology.NeighbourOffsets);
            uint[] neighbours = context.Read<uint>(context.Resource.Topology.Neighbours);
            uint[] neighbourEdges = context.Read<uint>(context.Resource.Topology.NeighbourEdge);
            float[] restLengths = context.Read<float>(context.Resource.Topology.EdgeRestLength);
            Vector3[] expectedVelocity = CpuStep(
                target,
                Vector3.up,
                1.25f,
                rest,
                offsets,
                neighbours,
                neighbourEdges,
                restLengths,
                parameters);

            using NeighbourSimulation simulation = new(context.Resource);
            simulation.ApplyImpulse(target, Vector3.up, 1.25f);
            simulation.Step(parameters.fixedDeltaTime, parameters);
            Vector3[] actualVelocity = context.Read<Vector3>(
                context.Resource.Live.Simulation.Velocity(simulation.ReadIndex));

            float maximumError = actualVelocity
                .Select((velocity, index) => (velocity - expectedVelocity[index]).magnitude)
                .Max();
            Assert.That(maximumError, Is.LessThan(2e-4f));
        }

        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        public void Simulation_MaximumPresetRemainsFiniteAndBounded(int frequency)
        {
            using GeometryTestContext context = GeometryTestContext.Build(frequency);
            using NeighbourSimulation simulation = new(context.Resource);
            SimulationParameters parameters = SimulationParameters.Maximum;
            Vector3[] rest = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);

            simulation.ApplyImpulse(0, Vector3.one, 8f);
            simulation.RecordFixedSteps(20, parameters, false);

            Vector3[] positions = context.Read<Vector3>(
                context.Resource.Live.Simulation.Position(simulation.ReadIndex));
            Vector3[] velocities = context.Read<Vector3>(
                context.Resource.Live.Simulation.Velocity(simulation.ReadIndex));
            Assert.That(positions.All(IsFinite), Is.True);
            Assert.That(velocities.All(IsFinite), Is.True);
            Assert.That(
                positions.Select((position, index) => (position - rest[index]).magnitude).Max(),
                Is.LessThanOrEqualTo(parameters.maximumDisplacement + 2e-4f));
            Assert.That(
                velocities.Max(velocity => velocity.magnitude),
                Is.LessThanOrEqualTo(parameters.maximumVelocity + 2e-4f));
            Assert.That(
                SimulationDiagnostics.ReadStatus(context.Resource).InvalidValueCount,
                Is.Zero);
        }

        [Test]
        public void Parameters_ValidateClampsInvalidValuesAndReportsEveryChange()
        {
            SimulationParameters input = SimulationParameters.Nominal;
            input.fixedDeltaTime = float.NaN;
            input.maximumSubsteps = 100;
            input.maximumVelocity = -1f;

            SimulationParameters output = SimulationParameters.Validate(input, out var issues);

            Assert.That(output.fixedDeltaTime, Is.EqualTo(1f / 120f));
            Assert.That(output.maximumSubsteps, Is.EqualTo(8));
            Assert.That(output.maximumVelocity, Is.EqualTo(0.001f));
            Assert.That(issues.Count, Is.EqualTo(3));
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static float MeasureEnergy(
            GeometryTestContext context,
            NeighbourSimulation simulation,
            Vector3[] rest)
        {
            Vector3[] positions = context.Read<Vector3>(
                context.Resource.Live.Simulation.Position(simulation.ReadIndex));
            Vector3[] velocities = context.Read<Vector3>(
                context.Resource.Live.Simulation.Velocity(simulation.ReadIndex));
            float energy = 0f;
            for (int index = 0; index < positions.Length; index++)
                energy += velocities[index].sqrMagnitude + (positions[index] - rest[index]).sqrMagnitude;
            return energy;
        }

        private static Vector3[] CpuStep(
            int target,
            Vector3 direction,
            float magnitude,
            Vector3[] positions,
            uint[] offsets,
            uint[] neighbours,
            uint[] neighbourEdges,
            float[] restLengths,
            SimulationParameters parameters)
        {
            Vector3[] velocities = new Vector3[positions.Length];
            velocities[target] = direction.normalized * magnitude;
            Vector3[] nextVelocities = new Vector3[positions.Length];
            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                Vector3 force = Vector3.zero;
                for (uint slot = offsets[vertex]; slot < offsets[vertex + 1]; slot++)
                {
                    Vector3 delta = positions[neighbours[slot]] - positions[vertex];
                    float distance = delta.magnitude;
                    Vector3 edgeDirection = distance > 1e-7f
                        ? delta / distance
                        : Vector3.right;
                    force += parameters.stiffness *
                        (distance - restLengths[neighbourEdges[slot]]) *
                        edgeDirection;
                }

                force -= parameters.damping * velocities[vertex];
                nextVelocities[vertex] =
                    velocities[vertex] + force * parameters.fixedDeltaTime;
                float speed = nextVelocities[vertex].magnitude;
                if (speed > parameters.maximumVelocity)
                    nextVelocities[vertex] *= parameters.maximumVelocity / speed;
            }
            return nextVelocities;
        }

        private static float MeasureInwardDisplacement(float maximum)
        {
            using GeometryTestContext context = GeometryTestContext.Build(4);
            using NeighbourSimulation simulation = new(context.Resource);
            Vector3[] rest = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            Vector3[] normals = context.Read<Vector3>(
                context.Resource.Attributes.Require(AttributeIds.RestNormal).Buffer);
            SimulationParameters parameters = SimulationParameters.Gentle;
            parameters.stiffness = 0f;
            parameters.restoringStiffness = 0f;
            parameters.damping = 0f;
            parameters.maximumInwardDisplacement = maximum;

            const int vertex = 0;
            simulation.ApplyImpulse(vertex, -normals[vertex], 10f);
            simulation.Step(parameters.fixedDeltaTime, parameters);
            Vector3 position = context.Read<Vector3>(
                context.Resource.Live.Simulation.Position(simulation.ReadIndex))[vertex];
            return -Vector3.Dot(position - rest[vertex], normals[vertex]);
        }

        private static DynamicVertexData[] ReadDynamicVertices(GeometryTestContext context)
        {
            DynamicVertexData[] values = new DynamicVertexData[context.Resource.Layout.VertexCount];
            context.Resource.Live.DynamicVertices.GetData(values);
            return values;
        }

        public sealed class SimulationSchedulerTests
        {
            [Test]
            public void Scheduler_CapsCatchUpAndReportsDroppedTime()
            {
                SimulationScheduler scheduler = new();
                SimulationParameters parameters = SimulationParameters.Nominal;

                int steps = scheduler.Accumulate(10f, parameters);

                Assert.That(steps, Is.EqualTo(parameters.maximumSubsteps));
                Assert.That(scheduler.LastDroppedTime, Is.GreaterThan(0f));
                Assert.That(
                    scheduler.Accumulator,
                    Is.LessThan(parameters.fixedDeltaTime));
                Assert.That(
                    steps * parameters.fixedDeltaTime + scheduler.Accumulator,
                    Is.LessThanOrEqualTo(parameters.maximumCatchUpTime));
            }

            [Test]
            public void Scheduler_PausedStateDoesNotAdvance()
            {
                SimulationScheduler scheduler = new();
                SimulationParameters parameters = SimulationParameters.Nominal;
                parameters.paused = true;

                Assert.That(scheduler.Accumulate(1f, parameters), Is.Zero);
                Assert.That(scheduler.Accumulator, Is.Zero);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DynamicVertexData
        {
            public Vector3 Position;
            public Vector3 Normal;
        }
    }

    public sealed class SimulationLifecycleTests
    {
        [Test]
        public void Simulation_RepeatedResourceDisposeLeavesRegistryUnchanged()
        {
            int baseline = GpuBufferRegistry.Count;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                GeometryTestContext context = GeometryTestContext.Build(iteration % 2 == 0 ? 2 : 8);
                try
                {
                    using NeighbourSimulation simulation = new(context.Resource);
                    simulation.RecordFixedSteps(2, SimulationParameters.Gentle);
                }
                finally
                {
                    context.Dispose();
                }
            }

            Assert.That(GpuBufferRegistry.Count, Is.EqualTo(baseline));
        }
    }
}
