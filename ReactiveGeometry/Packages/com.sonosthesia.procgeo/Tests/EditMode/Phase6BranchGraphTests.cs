using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class Phase6BranchGraphTests
    {
        [Test]
        public void TreePlanIsDeterministicAndParentFirst()
        {
            BranchGenerationSettings settings = SmallSettings();
            BranchGraphPlan first =
                new TreeBranchGenerator().Plan(settings);
            BranchGraphPlan second =
                new TreeBranchGenerator().Plan(settings);

            Assert.That(second.BranchCount, Is.EqualTo(first.BranchCount));
            Assert.That(second.SampleCount, Is.EqualTo(first.SampleCount));
            for (int i = 0; i < first.BranchCount; i++)
            {
                BranchRecord firstBranch = first.Branches[i];
                BranchRecord secondBranch = second.Branches[i];
                Assert.That(firstBranch.Id, Is.EqualTo((uint)i));
                Assert.That(secondBranch.Id, Is.EqualTo(firstBranch.Id));
                Assert.That(
                    secondBranch.SampleOffset,
                    Is.EqualTo(firstBranch.SampleOffset));
                Assert.That(
                    secondBranch.ParentId,
                    Is.EqualTo(firstBranch.ParentId));
                if (firstBranch.ParentId != uint.MaxValue)
                {
                    Assert.That(
                        firstBranch.ParentId,
                        Is.LessThan(firstBranch.Id));
                    Assert.That(
                        Vector3.Distance(
                            first.Samples[firstBranch.SampleOffset].Position,
                            first.Samples[firstBranch.ParentSample].Position),
                        Is.LessThan(0.0001f));
                }
            }
            for (int i = 0; i < first.SampleCount; i++)
            {
                Assert.That(
                    Vector3.Distance(
                        first.Samples[i].Position,
                        second.Samples[i].Position),
                    Is.LessThan(0.0001f));
            }
        }

        [Test]
        public void BranchMetadataMakesTipsMoreFlexibleThanLowerSegments()
        {
            BranchGraphPlan plan =
                new TreeBranchGenerator().Plan(SmallSettings());
            foreach (BranchRecord branch in plan.Branches)
            {
                int start = (int)branch.SampleOffset;
                int count = (int)branch.SampleCount;
                float previous = -1f;
                for (int i = 0; i < count; i++)
                {
                    BranchSample sample = plan.Samples[start + i];
                    Assert.That(sample.Flexibility, Is.GreaterThanOrEqualTo(previous - 0.0001f));
                    Assert.That(sample.Flexibility, Is.InRange(0f, 1f));
                    Assert.That(sample.Stiffness, Is.InRange(0f, 1f));
                    previous = sample.Flexibility;
                }
            }
            foreach (BranchRecord branch in plan.Branches)
            {
                if (branch.ParentId == uint.MaxValue)
                    continue;
                int parentTip = (int)(plan.Branches[branch.ParentId].SampleOffset +
                    plan.Branches[branch.ParentId].SampleCount - 1);
                int childTip = (int)(branch.SampleOffset + branch.SampleCount - 1);
                Assert.That(
                    plan.Samples[childTip].Flexibility,
                    Is.GreaterThanOrEqualTo(
                        plan.Samples[parentTip].Flexibility - 0.0001f),
                    $"Branch {branch.Id}");
            }
        }

        [Test]
        public void GeneratorBuildsValidBranchTubeTopology()
        {
            Assume.That(
                SystemInfo.supportsComputeShaders,
                "A graphics compute device is required.");
            using BranchTestContext context = BranchTestContext.Build();

            uint[] failure = new uint[1];
            context.Resource.Topology.Scratch.Failure.GetData(failure);
            Assert.That(failure[0], Is.Zero);
            Assert.That(
                Array.TrueForAll(
                    context.Generator.DebugTriangles,
                    triangle => triangle.x != triangle.y &&
                        triangle.y != triangle.z &&
                        triangle.z != triangle.x),
                Is.True);
            for (int i = 0; i < context.Generator.DebugTriangles.Length; i++)
            {
                Vector3Int triangle = context.Generator.DebugTriangles[i];
                Vector3 normal = Vector3.Cross(
                    context.Generator.DebugPositions[triangle.y] -
                    context.Generator.DebugPositions[triangle.x],
                    context.Generator.DebugPositions[triangle.z] -
                    context.Generator.DebugPositions[triangle.x]);
                Assert.That(
                    normal.magnitude,
                    Is.GreaterThan(0f),
                    $"Triangle {i}: {triangle}; " +
                    $"{context.Generator.DebugPositions[triangle.x]}, " +
                    $"{context.Generator.DebugPositions[triangle.y]}, " +
                    $"{context.Generator.DebugPositions[triangle.z]}");
            }
            ValidationResult[] validation = new ValidationResult[1];
            context.Resource.ValidationResult.GetData(validation);
            string firstDetail = "";
            uint first = validation[0].FirstElement;
            if (first >= (uint)context.Resource.Layout.VertexCount &&
                first < (uint)(context.Resource.Layout.VertexCount +
                    context.Resource.Layout.EdgeCapacity))
            {
                int edge = (int)first - context.Resource.Layout.VertexCount;
                Vector2Int[] edges = Read<Vector2Int>(
                    context.Resource.Topology.Edges);
                uint[] faces = Read<uint>(
                    context.Resource.Topology.EdgeFaceCounts);
                float[] lengths = Read<float>(
                    context.Resource.Topology.EdgeRestLength);
                firstDetail =
                    $"; edge {edge} {edges[edge]} faces {faces[edge]} " +
                    $"length {lengths[edge]}";
            }
            Assert.That(
                validation[0].ErrorCount,
                Is.Zero,
                $"First invalid element {validation[0].FirstElement}; " +
                $"vertices {context.Resource.Layout.VertexCount}, " +
                $"edges {context.Resource.Layout.EdgeCapacity}, " +
                $"triangles {context.Resource.Layout.PrimitiveCount}" +
                firstDetail);
        }

        [Test]
        public void UnifiedMeshIsClosedAndSupportsFourChildrenPerNode()
        {
            Assume.That(
                SystemInfo.supportsComputeShaders,
                "A graphics compute device is required.");
            BranchGenerationSettings settings = SmallSettings();
            settings.meshVariant = BranchMeshVariant.ImplicitUnion;
            settings.branchesPerNode = 4;
            settings.maximumBranches = 64;
            settings.maximumPathSamples = 256;
            settings.meshCellSize = 0.09f;
            using BranchTestContext context =
                BranchTestContext.Build(settings);

            IReadOnlyList<string> errors =
                TopologyValidator.Validate(context.Resource);
            Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors));
            uint[] faceCounts = Read<uint>(
                context.Resource.Topology.EdgeFaceCounts);
            int invalidFaceCount = Array.FindIndex(
                faceCounts, count => count != 2u);
            Assert.That(
                Array.TrueForAll(faceCounts, count => count == 2u),
                Is.True,
                invalidFaceCount >= 0
                    ? $"Edge {invalidFaceCount} has {faceCounts[invalidFaceCount]} triangles."
                    : string.Empty);
            float[] boundary = Read<float>(
                context.Resource.Attributes
                    .Require(AttributeIds.BoundaryWeight).Buffer);
            Assert.That(Array.TrueForAll(
                boundary, value => Mathf.Abs(value) < 1e-6f), Is.True);
            Dictionary<long, int> directions = new();
            foreach (Vector3Int triangle in context.Generator.DebugTriangles)
            {
                AssertEdgeDirection(triangle.x, triangle.y);
                AssertEdgeDirection(triangle.y, triangle.z);
                AssertEdgeDirection(triangle.z, triangle.x);
            }
            Assert.That(directions, Is.Empty);

            void AssertEdgeDirection(int a, int b)
            {
                long key = ((long)Mathf.Min(a, b) << 32) |
                    (uint)Mathf.Max(a, b);
                int direction = a < b ? 1 : -1;
                if (directions.TryGetValue(key, out int previous))
                {
                    Assert.That(direction, Is.EqualTo(-previous));
                    directions.Remove(key);
                }
                else
                {
                    directions.Add(key, direction);
                }
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void PortJunctionMeshIsClosedAndRegular(int childCount)
        {
            Assume.That(
                SystemInfo.supportsComputeShaders,
                "A graphics compute device is required.");
            BranchGenerationSettings settings = SmallSettings();
            settings.meshVariant = BranchMeshVariant.PortJunction;
            settings.branchesPerNode = childCount;
            settings.radialSegments = 8;
            settings.junctionSubdivisions = 0;
            settings.maximumBranches = 32;
            settings.maximumPathSamples = 128;
            using BranchTestContext context =
                BranchTestContext.Build(settings);

            Assert.That(
                context.Generator.VertexCount,
                Is.LessThan(
                    6 *
                    (context.Generator.GraphPlan.SampleCount *
                    settings.radialSegments +
                    context.Generator.GraphPlan.BranchCount *
                    settings.junctionRings *
                    settings.radialSegments)));
            uint[] degrees = Read<uint>(
                context.Resource.Attributes
                    .Require(AttributeIds.NeighbourCount).Buffer);
            Assert.That(
                Array.FindAll(degrees, degree => degree > 8).Length,
                Is.LessThanOrEqualTo(1),
                "Only the fork transition seam may exceed regular tube valence.");
            uint[] faceCounts = Read<uint>(
                context.Resource.Topology.EdgeFaceCounts);
            int invalidEdge = Array.FindIndex(
                faceCounts, count => count != 2u);
            Vector2Int invalidEndpoints = invalidEdge >= 0
                ? Read<Vector2Int>(
                    context.Resource.Topology.Edges)[invalidEdge]
                : new Vector2Int(-1, -1);
            Assert.That(
                Array.TrueForAll(faceCounts, count => count == 2u),
                Is.True,
                invalidEdge >= 0
                    ? $"Edge {invalidEdge} {invalidEndpoints} has {faceCounts[invalidEdge]} faces."
                    : string.Empty);
            float[] boundary = Read<float>(
                context.Resource.Attributes
                    .Require(AttributeIds.BoundaryWeight).Buffer);
            Assert.That(Array.TrueForAll(
                boundary, value => Mathf.Abs(value) < 1e-6f), Is.True);
            IReadOnlyList<string> errors =
                TopologyValidator.Validate(context.Resource);
            Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors));

            BranchGraphPlan plan = context.Generator.GraphPlan;
            int rings = settings.radialSegments;
            foreach (BranchRecord branch in plan.Branches)
            {
                if (branch.Terminal)
                    continue;
                int pathSample =
                    (int)(branch.SampleOffset + branch.SampleCount - 2);
                int firstVertex = pathSample * rings;
                int lastVertex = firstVertex + rings - 1;
                foreach (Vector3Int triangle in
                    context.Generator.DebugTriangles)
                {
                    bool allOnPort =
                        triangle.x >= firstVertex &&
                        triangle.x <= lastVertex &&
                        triangle.y >= firstVertex &&
                        triangle.y <= lastVertex &&
                        triangle.z >= firstVertex &&
                        triangle.z <= lastVertex;
                    Assert.That(
                        allOnPort,
                        Is.False,
                        $"Nonterminal branch {branch.Id} is capped.");
                }
            }
        }

        [Test]
        public void IdentitySkinningPreservesBranchCrossSections()
        {
            Assume.That(
                SystemInfo.supportsComputeShaders,
                "A graphics compute device is required.");
            using BranchTestContext context = BranchTestContext.Build();
            context.Pipeline.Reset();

            BranchPathGeometryData data =
                context.Generator.CreatePathGeometryData();
            Vector3[] path = Read<Vector3>(
                context.Pipeline.Paths.LivePositions);
            Vector3[] basePositions = Read<Vector3>(
                context.Pipeline.Buffers.BasePositions);
            for (int i = 0; i < basePositions.Length; i++)
            {
                int pathIndex = (int)data.VertexPathIndices[i];
                Assert.That(
                    Vector3.Distance(
                        basePositions[i],
                        context.Generator.DebugPositions[i]),
                    Is.LessThan(0.001f),
                    $"Vertex {i}");
            }
        }

        [Test]
        public void TurbulenceIsFiniteDeterministicAndKeepsJunctionsAttached()
        {
            Assume.That(
                SystemInfo.supportsComputeShaders,
                "A graphics compute device is required.");
            using BranchTestContext context = BranchTestContext.Build();
            TestTurbulence turbulence = new(Vector3.right, 0.2f);
            context.Pipeline.Record(
                new IBranchPathDeformer[] { turbulence },
                1f / 60f,
                0.75f);
            Vector3[] first = context.Pipeline.ReadLivePositionsOnce();
            context.Pipeline.Record(
                new IBranchPathDeformer[] { turbulence },
                1f / 60f,
                0.75f);
            Vector3[] second = context.Pipeline.ReadLivePositionsOnce();
            Assert.That(Array.TrueForAll(first, IsFinite), Is.True);
            for (int i = 0; i < first.Length; i++)
                Assert.That(
                    Vector3.Distance(first[i], second[i]),
                    Is.LessThan(0.0001f));
            AssertAttachments(context.Generator.GraphPlan, first);
        }

        [Test]
        public void AttractorBendsTipTowardsTargetWithoutDetachingChildren()
        {
            Assume.That(
                SystemInfo.supportsComputeShaders,
                "A graphics compute device is required.");
            using BranchTestContext context = BranchTestContext.Build();
            Vector3 target = new(4f, 3f, 0f);
            context.Pipeline.Record(
                new IBranchPathDeformer[] { new TestAttractor(target, 0.4f) },
                0f,
                0f);

            Vector3[] live = context.Pipeline.ReadLivePositionsOnce();
            BranchRecord terminal = Array.Find(
                context.Generator.GraphPlan.Branches,
                branch => branch.Terminal && branch.ParentId != uint.MaxValue);
            int tip = (int)(terminal.SampleOffset + terminal.SampleCount - 1);
            Assert.That(
                Vector3.Distance(live[tip], target),
                Is.LessThan(
                    Vector3.Distance(
                        context.Generator.GraphPlan.Samples[tip].Position,
                        target) - 0.001f));
            AssertAttachments(context.Generator.GraphPlan, live);
        }

        [Test]
        public void BranchPipelineLifecycleLeavesGpuRegistryUnchanged()
        {
            Assume.That(
                SystemInfo.supportsComputeShaders,
                "A graphics compute device is required.");
            int before = GpuBufferRegistry.Count;
            for (int i = 0; i < 2; i++)
            {
                using BranchTestContext context = BranchTestContext.Build();
                context.Pipeline.Reset();
            }
            Assert.That(GpuBufferRegistry.Count, Is.EqualTo(before));
        }

        private static BranchGenerationSettings SmallSettings()
        {
            BranchGenerationSettings settings =
                BranchGenerationSettings.Default;
            settings.maximumDepth = 2;
            settings.branchesPerNode = 2;
            settings.minimumSamples = 3;
            settings.samplesPerUnitLength = 3;
            settings.maximumSamplesPerBranch = 10;
            settings.maximumBranches = 32;
            settings.maximumPathSamples = 128;
            settings.seed = 90210;
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

        private static void AssertAttachments(
            BranchGraphPlan plan,
            Vector3[] live)
        {
            foreach (BranchRecord branch in plan.Branches)
            {
                if (branch.ParentId == uint.MaxValue)
                    continue;
                Assert.That(
                    Vector3.Distance(
                        live[branch.SampleOffset],
                        live[branch.ParentSample]),
                    Is.LessThan(0.0005f),
                    $"Branch {branch.Id}");
            }
        }

        private abstract class TestDeformer : IBranchPathDeformer
        {
            public bool Enabled => true;
            public abstract void Record(
                CommandBuffer command,
                BranchPathDeformerContext context);

            protected static void Bind(
                CommandBuffer command,
                int kernel,
                BranchPathDeformerContext context)
            {
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_BranchHeaders",
                    context.Paths.Headers);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_PathBranchIds",
                    context.Paths.BranchIds);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_RestPathPositions",
                    context.Paths.RestPositions);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_RestPathTangents",
                    context.Paths.RestTangents);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_RestPathNormals",
                    context.Paths.RestNormals);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_PathLocalPositions",
                    context.Paths.LocalPositions);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_PathFlexibilities",
                    context.Paths.Flexibilities);
                command.SetComputeBufferParam(
                    context.Shader, kernel, "_PathContributions",
                    context.Buffers.PathContributions);
                command.SetComputeIntParam(context.Shader, "_SourceEnabled", 0);
            }
        }

        private sealed class TestTurbulence : TestDeformer
        {
            private readonly Vector3 _direction;
            private readonly float _amplitude;

            public TestTurbulence(Vector3 direction, float amplitude)
            {
                _direction = direction;
                _amplitude = amplitude;
            }

            public override void Record(
                CommandBuffer command,
                BranchPathDeformerContext context)
            {
                int kernel = context.Shader.FindKernel("AddBranchTurbulence");
                Bind(command, kernel, context);
                command.SetComputeVectorParam(
                    context.Shader, "_WindDirection", _direction);
                command.SetComputeFloatParam(
                    context.Shader, "_TurbulenceAmplitude", _amplitude);
                command.SetComputeFloatParam(
                    context.Shader, "_TurbulenceFrequency", 2f);
                command.SetComputeFloatParam(
                    context.Shader, "_TurbulenceSpeed", 0.4f);
                command.SetComputeFloatParam(
                    context.Shader, "_TurbulencePhase", 0.2f);
                command.SetComputeIntParam(
                    context.Shader, "_TurbulenceSeed", 37);
                command.SetComputeIntParam(
                    context.Shader, "_TurbulenceOctaves", 3);
                command.SetComputeFloatParam(
                    context.Shader, "_TurbulenceLacunarity", 2f);
                command.SetComputeFloatParam(
                    context.Shader, "_TurbulenceGain", 0.5f);
                command.SetComputeFloatParam(
                    context.Shader, "_Time", context.Time);
                command.DispatchCompute(
                    context.Shader, kernel, context.SampleThreadGroups, 1, 1);
            }
        }

        private sealed class TestAttractor : TestDeformer
        {
            private readonly Vector3 _target;
            private readonly float _strength;

            public TestAttractor(Vector3 target, float strength)
            {
                _target = target;
                _strength = strength;
            }

            public override void Record(
                CommandBuffer command,
                BranchPathDeformerContext context)
            {
                int kernel = context.Shader.FindKernel("AddBranchAttractor");
                Bind(command, kernel, context);
                command.SetComputeVectorParam(
                    context.Shader, "_AttractorTarget", _target);
                command.SetComputeFloatParam(
                    context.Shader, "_AttractorStrength", _strength);
                command.SetComputeFloatParam(
                    context.Shader, "_AttractorPower", 1f);
                command.DispatchCompute(
                    context.Shader, kernel, context.SampleThreadGroups, 1, 1);
            }
        }

        private sealed class BranchTestContext : IDisposable
        {
            public BranchGeometryGenerator Generator { get; private set; }
            public GeometryResource Resource { get; private set; }
            public BranchPathDeformationPipeline Pipeline { get; private set; }
            private GeometryCommandScheduler _scheduler;

            public static BranchTestContext Build(
                BranchGenerationSettings? settingsOverride = null)
            {
                BranchTestContext result = new();
                BranchGenerationSettings settings =
                    settingsOverride ?? SmallSettings();
                BranchGraphPlan plan =
                    new TreeBranchGenerator().Plan(settings);
                result.Generator = new BranchGeometryGenerator(
                    plan,
                    settings.meshVariant,
                    settings.radialSegments,
                    settings.junctionRings,
                    settings.junctionSubdivisions,
                    settings.junctionThroatScale,
                    settings.meshCellSize,
                    settings.meshSmoothingRadius);
                result.Resource = new GeometryResource(result.Generator.Plan());
                result._scheduler = new GeometryCommandScheduler(
                    "Phase 6 Branch Test");
                result._scheduler.Record(command =>
                {
                    result.Generator.Record(command, result.Resource);
                    new TopologyBuilder().Record(command, result.Resource);
                    new AttributeProcessor().Record(command, result.Resource);
                    new GeometryValidator().Record(command, result.Resource);
                });
                result._scheduler.ExecuteOnce();
                result.Pipeline = new BranchPathDeformationPipeline(
                    result.Resource,
                    result.Generator.CreatePathGeometryData());
                return result;
            }

            public void Dispose()
            {
                Pipeline?.Dispose();
                _scheduler?.Dispose();
                Generator?.Dispose();
                Resource?.Dispose();
            }
        }
    }
}
