using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class Phase5PathDeformationTests
    {
        [Test]
        public void IdentityPathSkinningReproducesRestGeometry()
        {
            Assume.That(SystemInfo.supportsComputeShaders, "A graphics compute device is required.");
            using PathTestContext context = PathTestContext.Build();
            context.Pipeline.Reset();

            Vector3[] positions = Read<Vector3>(context.Pipeline.Buffers.BasePositions);
            for (int i = 0; i < positions.Length; i++)
            {
                Assert.That(
                    Vector3.Distance(positions[i], context.Generator.DebugPositions[i]),
                    Is.LessThan(0.0005f),
                    $"Vertex {i}");
            }
        }

        [Test]
        public void PathOffsetPreservesEveryBaseRingRadius()
        {
            Assume.That(SystemInfo.supportsComputeShaders, "A graphics compute device is required.");
            using PathTestContext context = PathTestContext.Build();
            context.Pipeline.Record(
                new IPathDeformer[] { new TestPathOffset(Vector3.up * 0.4f) },
                Array.Empty<IVertexDeformer>(),
                0f,
                0f);

            Vector3[] path = Read<Vector3>(context.Pipeline.Path.LivePositions);
            Vector3[] positions = Read<Vector3>(context.Pipeline.Buffers.BasePositions);
            PathGeometryData data = context.Generator.CreatePathGeometryData();
            for (int i = 0; i < positions.Length; i++)
            {
                int pathIndex = (int)data.VertexPathIndices[i];
                float radius = Vector3.Distance(positions[i], path[pathIndex]);
                Assert.That(
                    radius,
                    Is.EqualTo(data.CrossSectionPositions[i].magnitude).Within(0.001f),
                    $"Vertex {i}");
            }
            Assert.That(
                Vector3.Distance(
                    path[0], data.Positions[0] + Vector3.up * 0.4f),
                Is.LessThan(0.001f));
        }

        [Test]
        public void VertexDeformerFollowsPathSkinnedNormal()
        {
            Assume.That(SystemInfo.supportsComputeShaders, "A graphics compute device is required.");
            using PathTestContext context = PathTestContext.Build();
            context.Pipeline.Record(
                new IPathDeformer[] { new TestPathOffset(Vector3.one * 0.3f) },
                new IVertexDeformer[] { new TestVertexNormalOffset(0.08f) },
                0f,
                0f);

            Vector3[] basePositions = Read<Vector3>(
                context.Pipeline.Buffers.BasePositions);
            Vector3[] baseNormals = Read<Vector3>(
                context.Pipeline.Buffers.BaseNormals);
            Vector3[] finalPositions = Read<Vector3>(
                context.Pipeline.Buffers.FinalPositions);
            for (int i = 0; i < basePositions.Length; i++)
            {
                Vector3 expected = baseNormals[i] * 0.08f;
                Assert.That(
                    Vector3.Distance(finalPositions[i] - basePositions[i], expected),
                    Is.LessThan(0.001f),
                    $"Vertex {i}");
            }
        }

        [Test]
        public void PathSpatialFalloffLimitsContributionToSourceRadius()
        {
            Assume.That(SystemInfo.supportsComputeShaders, "A graphics compute device is required.");
            using PathTestContext context = PathTestContext.Build();
            PathGeometryData data = context.Generator.CreatePathGeometryData();
            context.Pipeline.Record(
                new IPathDeformer[]
                {
                    new TestSpatialPathOffset(
                        Vector3.up * 0.2f, data.Positions[0], 0.01f)
                },
                Array.Empty<IVertexDeformer>(),
                0f,
                0f);

            Vector3[] path = Read<Vector3>(context.Pipeline.Path.LivePositions);
            Assert.That(
                Vector3.Distance(
                    path[0], data.Positions[0] + Vector3.up * 0.2f),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Distance(path[path.Length / 2],
                    data.Positions[path.Length / 2]),
                Is.LessThan(0.001f));
        }

        [Test]
        public void DynamicPathModeRemainsFiniteAndReturnsBaseRingRadius()
        {
            Assume.That(SystemInfo.supportsComputeShaders, "A graphics compute device is required.");
            using PathTestContext context = PathTestContext.Build();
            TestDynamicPathForce force = new(80f);
            for (int step = 0; step < 8; step++)
                context.Pipeline.Record(
                    new IPathDeformer[] { force },
                    Array.Empty<IVertexDeformer>(),
                    1f / 60f,
                    step / 60f);

            Vector3[] path = Read<Vector3>(context.Pipeline.Path.LivePositions);
            Vector3[] positions = Read<Vector3>(
                context.Pipeline.Buffers.BasePositions);
            PathGeometryData data = context.Generator.CreatePathGeometryData();
            Assert.That(
                Array.TrueForAll(
                    path,
                    value => IsFinite(value)),
                Is.True);
            for (int i = 0; i < positions.Length; i++)
            {
                int pathIndex = (int)data.VertexPathIndices[i];
                Assert.That(
                    Vector3.Distance(positions[i], path[pathIndex]),
                    Is.EqualTo(data.CrossSectionPositions[i].magnitude).Within(0.0015f));
            }
        }

        [Test]
        public void PipelineLifecycleLeavesGpuRegistryUnchanged()
        {
            Assume.That(SystemInfo.supportsComputeShaders, "A graphics compute device is required.");
            int before = GpuBufferRegistry.Count;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                using PathTestContext context = PathTestContext.Build();
                context.Pipeline.Reset();
            }
            Assert.That(GpuBufferRegistry.Count, Is.EqualTo(before));
        }

        [Test]
        public void NonTubeGeneratorCannotCreatePathBinding()
        {
            using ParametricGeometryGenerator generator = new(
                ParametricGeometryType.Plane,
                SmallSettings());
            Assert.Throws<InvalidOperationException>(
                () => generator.CreatePathGeometryData());
        }

        private static ParametricGeometrySettings SmallSettings()
        {
            ParametricGeometrySettings settings = ParametricGeometrySettings.Default;
            settings.uSegments = 16;
            settings.vSegments = 8;
            return settings;
        }

        private static T[] Read<T>(GraphicsBuffer buffer)
            where T : struct
        {
            T[] values = new T[buffer.count];
            buffer.GetData(values);
            return values;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) &&
            float.IsFinite(value.y) &&
            float.IsFinite(value.z);

        private sealed class TestPathOffset : IPathDeformer
        {
            private readonly Vector3 _offset;

            public TestPathOffset(Vector3 offset)
            {
                _offset = offset;
            }

            public bool Enabled => true;

            public void Record(CommandBuffer command, PathDeformerContext context)
            {
                int kernel = context.Shader.FindKernel("AddPathOffset");
                command.SetComputeVectorParam(context.Shader, "_PathOffset", _offset);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_RestPathPositions",
                    context.Path.RestPositions);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_PathContributions",
                    context.Buffers.PathContributions);
                command.DispatchCompute(
                    context.Shader, kernel, context.ThreadGroups, 1, 1);
            }
        }

        private sealed class TestSpatialPathOffset : IPathDeformer
        {
            private readonly Vector3 _offset;
            private readonly Vector3 _center;
            private readonly float _radius;

            public TestSpatialPathOffset(
                Vector3 offset, Vector3 center, float radius)
            {
                _offset = offset;
                _center = center;
                _radius = radius;
            }

            public bool Enabled => true;

            public void Record(CommandBuffer command, PathDeformerContext context)
            {
                int kernel = context.Shader.FindKernel("AddPathOffset");
                command.SetComputeIntParam(context.Shader, "_SourceEnabled", 1);
                command.SetComputeVectorParam(
                    context.Shader, "_SourceCenter", _center);
                command.SetComputeFloatParam(context.Shader, "_InnerRadius", 0f);
                command.SetComputeFloatParam(
                    context.Shader, "_OuterRadius", _radius);
                command.SetComputeFloatParam(
                    context.Shader, "_SourceWeight", 1f);
                command.SetComputeIntParam(context.Shader, "_FalloffMode", 1);
                command.SetComputeVectorParam(context.Shader, "_PathOffset", _offset);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_RestPathPositions",
                    context.Path.RestPositions);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_PathContributions",
                    context.Buffers.PathContributions);
                command.DispatchCompute(
                    context.Shader, kernel, context.ThreadGroups, 1, 1);
            }
        }

        private sealed class TestVertexNormalOffset : IVertexDeformer
        {
            private readonly float _amplitude;

            public TestVertexNormalOffset(float amplitude)
            {
                _amplitude = amplitude;
            }

            public bool Enabled => true;

            public void Record(CommandBuffer command, VertexDeformerContext context)
            {
                int kernel = context.Shader.FindKernel("AddVertexNormalOffset");
                command.SetComputeFloatParam(
                    context.Shader, "_VertexNormalAmplitude", _amplitude);
                command.SetComputeFloatParam(
                    context.Shader, "_VertexWaveFrequency", 0f);
                command.SetComputeFloatParam(
                    context.Shader, "_VertexWaveSpeed", 0f);
                command.SetComputeFloatParam(
                    context.Shader, "_VertexWavePhase", 0.5f * Mathf.PI);
                command.SetComputeFloatParam(context.Shader, "_Time", 0f);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_BaseNormals",
                    context.Buffers.BaseNormals);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_BasePositions",
                    context.Buffers.BasePositions);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_VertexOffsets",
                    context.Buffers.VertexOffsets);
                command.DispatchCompute(
                    context.Shader, kernel, context.ThreadGroups, 1, 1);
            }
        }

        private sealed class TestDynamicPathForce : IDynamicPathDeformer
        {
            private readonly float _amplitude;

            public TestDynamicPathForce(float amplitude)
            {
                _amplitude = amplitude;
            }

            public bool Enabled => true;

            public void Record(CommandBuffer command, PathDeformerContext context)
            {
                int kernel = context.Shader.FindKernel("AddDynamicPathWave");
                command.SetComputeVectorParam(
                    context.Shader, "_WaveDirection", Vector3.up);
                command.SetComputeFloatParam(
                    context.Shader, "_WaveAmplitude", _amplitude);
                command.SetComputeFloatParam(
                    context.Shader, "_WaveFrequency", 0f);
                command.SetComputeFloatParam(
                    context.Shader, "_WaveSpeed", 0f);
                command.SetComputeFloatParam(
                    context.Shader, "_WavePhase", Mathf.PI * 0.5f);
                command.SetComputeFloatParam(
                    context.Shader, "_Time", 0f);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_RestPathDistances",
                    context.Path.RestDistances);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_RestPathPositions",
                    context.Path.RestPositions);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_PathForces",
                    context.Buffers.PathForces);
                command.DispatchCompute(
                    context.Shader, kernel, context.ThreadGroups, 1, 1);
            }
        }

        private sealed class PathTestContext : IDisposable
        {
            private PathTestContext(
                ParametricGeometryGenerator generator,
                GeometryResource resource,
                PathDeformationPipeline pipeline)
            {
                Generator = generator;
                Resource = resource;
                Pipeline = pipeline;
            }

            public ParametricGeometryGenerator Generator { get; }
            public GeometryResource Resource { get; }
            public PathDeformationPipeline Pipeline { get; }

            public static PathTestContext Build()
            {
                ParametricGeometryGenerator generator = null;
                GeometryResource resource = null;
                PathDeformationPipeline pipeline = null;
                try
                {
                    generator = new ParametricGeometryGenerator(
                        ParametricGeometryType.TorusKnotTube, SmallSettings());
                    resource = new GeometryResource(generator.Plan());
                    TopologyBuilder topology = new();
                    AttributeProcessor attributes = new();
                    using GeometryCommandScheduler scheduler = new(
                        "Phase5 Path Test Generation");
                    scheduler.Record(command =>
                    {
                        generator.Record(command, resource);
                        topology.Record(command, resource);
                        attributes.Record(command, resource);
                    });
                    scheduler.ExecuteOnce();
                    pipeline = new PathDeformationPipeline(
                        resource, generator.CreatePathGeometryData());
                    PathTestContext result = new(
                        generator, resource, pipeline);
                    generator = null;
                    resource = null;
                    pipeline = null;
                    return result;
                }
                finally
                {
                    pipeline?.Dispose();
                    resource?.Dispose();
                    generator?.Dispose();
                }
            }

            public void Dispose()
            {
                Pipeline.Dispose();
                Resource.Dispose();
                Generator.Dispose();
            }
        }
    }
}
