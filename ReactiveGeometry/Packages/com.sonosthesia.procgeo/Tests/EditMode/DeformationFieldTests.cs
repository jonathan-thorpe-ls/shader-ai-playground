using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class NoiseDeformationFieldTests
    {
        [Test]
        public void NoiseField_IsDeterministicAndExactlyZeroOutsideSource()
        {
            using GeometryTestContext context = GeometryTestContext.Build(2);
            NoiseDeformationSettings settings = NoiseDeformationSettings.Default;
            settings.source.localCenter = new Vector3(100f, 0f, 0f);
            settings.source.innerRadius = 0f;
            settings.source.outerRadius = 0.25f;
            settings.output = NoiseOutputMode.Offset;
            using FieldAccumulator fields = new(
                context.Resource,
                new IGeometryField[] { new NoiseDeformationField(settings) });

            FieldParameters parameters = new() { simulationTime = 1.25f };
            fields.Record(parameters);
            Vector3[] first = context.Read<Vector3>(
                context.Resource.Live.Simulation.ExternalForces);
            fields.Record(parameters);
            Vector3[] second = context.Read<Vector3>(
                context.Resource.Live.Simulation.ExternalForces);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.All(value => value == Vector3.zero), Is.True);
        }
    }

    public sealed class DeformationEventTests
    {
        [Test]
        public void EventPool_FullCapacityReplacesOldestSlot()
        {
            LogAssert.Expect(
                LogType.Warning,
                "Deformation event pool is full; replacing event 1.");
            using DeformationEventPool pool = new(1);
            uint first = pool.AddWave(Vector3.zero, 0f, 1f, 1f, 1f, 10f);
            uint second = pool.AddWave(Vector3.zero, 0f, 1f, 1f, 1f, 10f);

            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(pool.IsActive(first), Is.False);
            Assert.That(pool.IsActive(second), Is.True);
            Assert.That(pool.LastReplacementSequence, Is.EqualTo(first));
        }

        [Test]
        public void ImpulseField_AppliesEachSequenceOnlyOnce()
        {
            using GeometryTestContext context = GeometryTestContext.Build(2);
            using DeformationEventPool pool = new(4);
            using FieldAccumulator fields = new(
                context.Resource,
                new IGeometryField[] { new ImpulseDeformationField(pool) });
            pool.AddImpulse(
                Vector3.zero,
                0f,
                3f,
                2f,
                DeformationDirectionMode.RestNormal);

            fields.Record(new FieldParameters { simulationTime = 0f });
            Vector3[] first = context.Read<Vector3>(
                context.Resource.Live.Simulation.ExternalForces);
            fields.Record(new FieldParameters { simulationTime = 0.01f });
            Vector3[] second = context.Read<Vector3>(
                context.Resource.Live.Simulation.ExternalForces);

            Assert.That(first.Max(value => value.magnitude), Is.GreaterThan(0f));
            Assert.That(second.All(value => value == Vector3.zero), Is.True);
        }

        [Test]
        public void WaveField_ExpiresAtDeterministicDuration()
        {
            using DeformationEventPool pool = new(2);
            uint sequence = pool.AddWave(
                Vector3.zero, 4f, 1f, 2f, 0.2f, 3f);

            pool.Prepare(6.9f);
            Assert.That(pool.IsActive(sequence), Is.True);
            pool.Prepare(7.1f);
            Assert.That(pool.IsActive(sequence), Is.False);
        }
    }
}
