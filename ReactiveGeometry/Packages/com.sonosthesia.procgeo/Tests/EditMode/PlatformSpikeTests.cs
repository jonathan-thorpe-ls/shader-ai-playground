using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo.Tests
{
    [Category("Spike")]
    public sealed class PlatformSpikeTests
    {
        [Test]
        public void S0_ActivePipelineIsUrp()
        {
            Assert.That(GraphicsSettings.currentRenderPipeline, Is.Not.Null);
            Assert.That(
                GraphicsSettings.currentRenderPipeline.GetType().Namespace,
                Is.EqualTo("UnityEngine.Rendering.Universal"));
        }

        [Test]
        public void S1_ComputeWrittenMeshBuffersAreReadable()
        {
            using GeometryTestContext context = GeometryTestContext.Build(1);
            Assert.That(context.Resource.Mesh.vertexCount, Is.EqualTo(12));
            uint[] indices = context.Read<uint>(context.Resource.Indices);
            Assert.That(indices.Length, Is.EqualTo(60));
            Assert.That(indices, Has.All.LessThan(12));
        }

        [Test]
        public void S4_StructuredAndRawBufferCombinationIsSupported()
        {
            Assume.That(SystemInfo.supportsComputeShaders, "A graphics compute device is required.");
            using var buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Raw,
                16,
                sizeof(uint));
            uint[] input = new uint[16];
            uint[] output = new uint[16];
            for (uint i = 0; i < input.Length; i++)
                input[i] = i;
            buffer.SetData(input);
            buffer.GetData(output);
            Assert.That(output, Is.EqualTo(input));
        }

        [Test]
        public void S6_ComputeGenerationAndReadbackWork()
        {
            using GeometryTestContext context = GeometryTestContext.Build(2);
            GeometryHeader[] header = context.Read<GeometryHeader>(context.Resource.Header);
            Assert.That(header[0].VertexCount, Is.EqualTo(42));
            Assert.That(header[0].PrimitiveCount, Is.EqualTo(80));
            Assert.That(
                header[0].Flags & (uint)(GeometryFlags.Manifold | GeometryFlags.Closed),
                Is.EqualTo((uint)(GeometryFlags.Manifold | GeometryFlags.Closed)));
            Assert.That(context.Validation.ErrorCount, Is.Zero);
        }
    }
}
