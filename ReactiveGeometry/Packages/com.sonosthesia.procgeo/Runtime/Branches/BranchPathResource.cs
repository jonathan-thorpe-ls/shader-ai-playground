using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public readonly struct BranchPathDeformerContext
    {
        public BranchPathDeformerContext(
            BranchPathResource paths,
            BranchPathDeformationBuffers buffers,
            ComputeShader shader,
            float deltaTime,
            float time)
        {
            Paths = paths;
            Buffers = buffers;
            Shader = shader;
            DeltaTime = deltaTime;
            Time = time;
            SampleThreadGroups = (paths.SampleCount + 63) / 64;
        }

        public BranchPathResource Paths { get; }
        public BranchPathDeformationBuffers Buffers { get; }
        public ComputeShader Shader { get; }
        public float DeltaTime { get; }
        public float Time { get; }
        public int SampleThreadGroups { get; }
    }

    public interface IBranchPathDeformer
    {
        bool Enabled { get; }
        void Record(CommandBuffer command, BranchPathDeformerContext context);
    }

    public sealed class BranchPathResource : IDisposable
    {
        private GraphicsBuffer _headers;
        private GraphicsBuffer _restPositions;
        private GraphicsBuffer _restDistances;
        private GraphicsBuffer _restTangents;
        private GraphicsBuffer _restNormals;
        private GraphicsBuffer _restBinormals;
        private GraphicsBuffer _flexibilities;
        private GraphicsBuffer _localPositions;
        private GraphicsBuffer _radii;
        private GraphicsBuffer _branchIds;
        private GraphicsBuffer _livePositions;
        private GraphicsBuffer _liveTangents;
        private GraphicsBuffer _liveNormals;
        private GraphicsBuffer _liveBinormals;
        private bool _disposed;

        public BranchPathResource(BranchGraphPlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (plan.BranchCount == 0 || plan.SampleCount < 2)
                throw new ArgumentException("A branch graph requires branches and samples.");
            _headers = Create(plan.PathHeaders, "BranchPaths.Headers");
            Vector3[] positions = new Vector3[plan.SampleCount];
            float[] distances = new float[plan.SampleCount];
            Vector3[] tangents = new Vector3[plan.SampleCount];
            Vector3[] normals = new Vector3[plan.SampleCount];
            Vector3[] binormals = new Vector3[plan.SampleCount];
            float[] flexibilities = new float[plan.SampleCount];
            float[] localPositions = new float[plan.SampleCount];
            float[] radii = new float[plan.SampleCount];
            uint[] branchIds = new uint[plan.SampleCount];
            for (int i = 0; i < plan.SampleCount; i++)
            {
                BranchSample sample = plan.Samples[i];
                positions[i] = sample.Position;
                distances[i] = sample.LocalDistance;
                tangents[i] = sample.Tangent;
                normals[i] = sample.Normal;
                binormals[i] = sample.Binormal;
                flexibilities[i] = sample.Flexibility;
                localPositions[i] = sample.LocalPosition;
                radii[i] = sample.Radius;
                branchIds[i] = sample.BranchId;
            }
            _restPositions = Create(positions, "BranchPaths.RestPositions");
            _restDistances = Create(distances, "BranchPaths.RestDistances");
            _restTangents = Create(tangents, "BranchPaths.RestTangents");
            _restNormals = Create(normals, "BranchPaths.RestNormals");
            _restBinormals = Create(binormals, "BranchPaths.RestBinormals");
            _flexibilities = Create(flexibilities, "BranchPaths.Flexibilities");
            _localPositions = Create(localPositions, "BranchPaths.LocalPositions");
            _radii = Create(radii, "BranchPaths.Radii");
            _branchIds = Create(branchIds, "BranchPaths.BranchIds");
            _livePositions = Empty<Vector3>(SampleCount, "BranchPaths.LivePositions");
            _liveTangents = Empty<Vector3>(SampleCount, "BranchPaths.LiveTangents");
            _liveNormals = Empty<Vector3>(SampleCount, "BranchPaths.LiveNormals");
            _liveBinormals = Empty<Vector3>(SampleCount, "BranchPaths.LiveBinormals");
        }

        public BranchGraphPlan Plan { get; }
        public int BranchCount => Plan.BranchCount;
        public int SampleCount => Plan.SampleCount;
        public int MaximumGenerationDepth => Plan.MaximumGenerationDepth;
        public GraphicsBuffer Headers => _headers;
        public GraphicsBuffer RestPositions => _restPositions;
        public GraphicsBuffer RestDistances => _restDistances;
        public GraphicsBuffer RestTangents => _restTangents;
        public GraphicsBuffer RestNormals => _restNormals;
        public GraphicsBuffer RestBinormals => _restBinormals;
        public GraphicsBuffer Flexibilities => _flexibilities;
        public GraphicsBuffer LocalPositions => _localPositions;
        public GraphicsBuffer Radii => _radii;
        public GraphicsBuffer BranchIds => _branchIds;
        public GraphicsBuffer LivePositions => _livePositions;
        public GraphicsBuffer LiveTangents => _liveTangents;
        public GraphicsBuffer LiveNormals => _liveNormals;
        public GraphicsBuffer LiveBinormals => _liveBinormals;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Release(_headers);
            Release(_restPositions);
            Release(_restDistances);
            Release(_restTangents);
            Release(_restNormals);
            Release(_restBinormals);
            Release(_flexibilities);
            Release(_localPositions);
            Release(_radii);
            Release(_branchIds);
            Release(_livePositions);
            Release(_liveTangents);
            Release(_liveNormals);
            Release(_liveBinormals);
        }

        private GraphicsBuffer Create<T>(T[] values, string name)
            where T : struct
        {
            GraphicsBuffer buffer = new(
                GraphicsBuffer.Target.Structured,
                values.Length,
                System.Runtime.InteropServices.Marshal.SizeOf<T>());
            buffer.SetData(values);
            GpuBufferRegistry.Register(buffer, name, this);
            return buffer;
        }

        private GraphicsBuffer Empty<T>(int count, string name)
            where T : struct
        {
            GraphicsBuffer buffer = new(
                GraphicsBuffer.Target.Structured,
                count,
                System.Runtime.InteropServices.Marshal.SizeOf<T>());
            GpuBufferRegistry.Register(buffer, name, this);
            return buffer;
        }

        internal static void Release(GraphicsBuffer buffer) =>
            GpuBufferRegistry.Release(ref buffer);
    }

    public sealed class BranchPathDeformationBuffers : IDisposable
    {
        private GraphicsBuffer _pathContributions;
        private GraphicsBuffer _pathIndices;
        private GraphicsBuffer _crossSections;
        private GraphicsBuffer _basePositions;
        private GraphicsBuffer _baseNormals;
        private GraphicsBuffer _baseTangents;
        private GraphicsBuffer _finalNormals;
        private bool _disposed;

        internal BranchPathDeformationBuffers(
            GeometryLayout layout,
            BranchPathGeometryData pathData,
            object owner)
        {
            _pathContributions = Create<Vector3>(
                pathData.Plan.SampleCount, "BranchPath.Contributions", owner);
            _pathIndices = SetData(
                pathData.VertexPathIndices, "BranchPath.VertexIndices", owner);
            _crossSections = SetData(
                pathData.CrossSectionPositions, "BranchPath.CrossSections", owner);
            _basePositions = Create<Vector3>(
                layout.VertexCount, "BranchPath.BasePositions", owner);
            _baseNormals = Create<Vector3>(
                layout.VertexCount, "BranchPath.BaseNormals", owner);
            _baseTangents = Create<Vector3>(
                layout.VertexCount, "BranchPath.BaseTangents", owner);
            _finalNormals = Create<Vector3>(
                layout.VertexCount, "BranchPath.FinalNormals", owner);
        }

        public GraphicsBuffer PathContributions => _pathContributions;
        public GraphicsBuffer VertexPathIndices => _pathIndices;
        public GraphicsBuffer CrossSections => _crossSections;
        public GraphicsBuffer BasePositions => _basePositions;
        public GraphicsBuffer BaseNormals => _baseNormals;
        public GraphicsBuffer BaseTangents => _baseTangents;
        public GraphicsBuffer FinalNormals => _finalNormals;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BranchPathResource.Release(_pathContributions);
            BranchPathResource.Release(_pathIndices);
            BranchPathResource.Release(_crossSections);
            BranchPathResource.Release(_basePositions);
            BranchPathResource.Release(_baseNormals);
            BranchPathResource.Release(_baseTangents);
            BranchPathResource.Release(_finalNormals);
        }

        private static GraphicsBuffer Create<T>(int count, string name, object owner)
            where T : struct
        {
            GraphicsBuffer buffer = new(
                GraphicsBuffer.Target.Structured,
                count,
                System.Runtime.InteropServices.Marshal.SizeOf<T>());
            GpuBufferRegistry.Register(buffer, name, owner);
            return buffer;
        }

        private static GraphicsBuffer SetData<T>(
            T[] values,
            string name,
            object owner)
            where T : struct
        {
            GraphicsBuffer buffer = Create<T>(values.Length, name, owner);
            buffer.SetData(values);
            return buffer;
        }
    }
}
