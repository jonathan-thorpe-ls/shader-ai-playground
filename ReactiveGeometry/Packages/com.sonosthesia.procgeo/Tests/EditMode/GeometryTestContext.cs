using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo.Tests
{
    internal sealed class GeometryTestContext : IDisposable
    {
        private GeometryTestContext(
            IcosphereGenerator generator,
            GeometryResource resource)
        {
            Generator = generator;
            Resource = resource;
        }

        public IcosphereGenerator Generator { get; }
        public GeometryResource Resource { get; }
        public ValidationResult Validation { get; private set; }

        public static GeometryTestContext Build(
            int frequency,
            bool recursiveProjection = false)
        {
            Assume.That(SystemInfo.supportsComputeShaders, "A graphics compute device is required.");
            IcosphereGenerator generator = null;
            GeometryResource resource = null;
            try
            {
                generator = new IcosphereGenerator(
                    frequency,
                    recursiveProjection: recursiveProjection);
                resource = new GeometryResource(generator.Plan());
                TopologyBuilder topology = new();
                AttributeProcessor attributes = new();
                GeometryValidator validator = new();
                using GeometryCommandScheduler scheduler =
                    new($"ProcGeo Test Icosphere {frequency}");
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
                Assert.That(failure[0], Is.Zero, "Topology builder failure flag.");

                ValidationResult[] validation = new ValidationResult[1];
                resource.ValidationResult.GetData(validation);
                GeometryTestContext context = new(generator, resource)
                {
                    Validation = validation[0]
                };
                generator = null;
                resource = null;
                return context;
            }
            finally
            {
                generator?.Dispose();
                resource?.Dispose();
            }
        }

        public T[] Read<T>(GraphicsBuffer buffer)
            where T : struct
        {
            T[] result = new T[buffer.count];
            buffer.GetData(result);
            return result;
        }

        public void Dispose()
        {
            Generator.Dispose();
            Resource.Dispose();
        }
    }
}
