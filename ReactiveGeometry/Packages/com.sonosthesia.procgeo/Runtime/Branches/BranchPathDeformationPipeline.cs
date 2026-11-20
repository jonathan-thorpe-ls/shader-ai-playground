using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class BranchPathDeformationPipeline : IDisposable
    {
        private readonly GeometryResource _geometry;
        private readonly BranchPathResource _paths;
        private readonly ComputeShader _shader;
        private readonly GeometryCommandScheduler _scheduler;
        private readonly int _clear;
        private readonly int _resolve;
        private readonly int _skin;
        private readonly int _normals;
        private readonly int _publish;
        private bool _disposed;

        public BranchPathDeformationPipeline(
            GeometryResource geometry,
            BranchPathGeometryData data,
            ComputeShader shader = null)
        {
            _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.VertexPathIndices.Length != geometry.Layout.VertexCount)
            {
                throw new ArgumentException(
                    "The branch path binding does not match the geometry.",
                    nameof(data));
            }
            _paths = new BranchPathResource(data.Plan);
            Buffers = new BranchPathDeformationBuffers(
                geometry.Layout, data, this);
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/BranchPathDeformation")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/BranchPathDeformation.compute was not found.");
            _clear = _shader.FindKernel("ClearBranchPath");
            _resolve = _shader.FindKernel("ResolveBranchDepth");
            _skin = _shader.FindKernel("SkinBranchGeometry");
            _normals = _shader.FindKernel("RecomputeBranchNormals");
            _publish = _shader.FindKernel("PublishBranchGeometry");
            _scheduler = new GeometryCommandScheduler(
                $"{geometry.Layout.Name}.BranchPathDeformation");
            Reset();
        }

        public BranchPathResource Paths => _paths;
        public BranchPathDeformationBuffers Buffers { get; }
        public ComputeShader Shader => _shader;

        public void Reset() =>
            Record(Array.Empty<IBranchPathDeformer>(), 0f, 0f);

        public void Record(
            IReadOnlyList<IBranchPathDeformer> deformers,
            float deltaTime,
            float time)
        {
            ThrowIfDisposed();
            if (deformers == null) throw new ArgumentNullException(nameof(deformers));
            _scheduler.Record(command =>
            {
                BindCommon(command);
                command.SetComputeBufferParam(
                    _shader, _clear, "_PathContributions",
                    Buffers.PathContributions);
                command.DispatchCompute(
                    _shader, _clear,
                    (_paths.SampleCount + 63) / 64, 1, 1);

                BranchPathDeformerContext context = new(
                    _paths, Buffers, _shader, deltaTime, time);
                foreach (IBranchPathDeformer deformer in deformers)
                    if (deformer != null && deformer.Enabled)
                        deformer.Record(command, context);

                BindResolve(command);
                for (int depth = 0;
                    depth <= _paths.MaximumGenerationDepth;
                    depth++)
                {
                    command.SetComputeIntParam(_shader, "_TargetDepth", depth);
                    command.DispatchCompute(
                        _shader, _resolve,
                        (_paths.BranchCount + 63) / 64, 1, 1);
                }

                BindSkin(command);
                command.DispatchCompute(
                    _shader, _skin,
                    (_geometry.Layout.VertexCount + 63) / 64, 1, 1);
                BindNormals(command);
                command.DispatchCompute(
                    _shader, _normals,
                    (_geometry.Layout.VertexCount + 63) / 64, 1, 1);
                BindPublish(command);
                command.DispatchCompute(
                    _shader, _publish,
                    (_geometry.Layout.VertexCount + 63) / 64, 1, 1);
            });
            _scheduler.ExecuteOnce();
        }

        public Vector3[] ReadLivePositionsOnce()
        {
            Vector3[] values = new Vector3[_paths.SampleCount];
            _paths.LivePositions.GetData(values);
            return values;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scheduler.Dispose();
            Buffers.Dispose();
            _paths.Dispose();
        }

        private void BindCommon(CommandBuffer command)
        {
            command.SetComputeIntParam(_shader, "_BranchCount", _paths.BranchCount);
            command.SetComputeIntParam(_shader, "_PathSampleCount", _paths.SampleCount);
            command.SetComputeIntParam(
                _shader, "_VertexCount", _geometry.Layout.VertexCount);
            command.SetComputeFloatParam(_shader, "_Epsilon", 1e-6f);
            command.SetComputeFloatParam(_shader, "_MaximumDisplacement", 1.5f);
            command.SetComputeIntParam(_shader, "_SourceEnabled", 0);
            command.SetComputeFloatParam(_shader, "_SourceWeight", 1f);
        }

        private void BindResolve(CommandBuffer command)
        {
            Set("_BranchHeaders", _paths.Headers);
            Set("_RestPathPositions", _paths.RestPositions);
            Set("_RestPathTangents", _paths.RestTangents);
            Set("_RestPathNormals", _paths.RestNormals);
            Set("_RestPathBinormals", _paths.RestBinormals);
            Set("_PathFlexibilities", _paths.Flexibilities);
            Set("_PathContributions", Buffers.PathContributions);
            Set("_LivePathPositions", _paths.LivePositions);
            Set("_LivePathTangents", _paths.LiveTangents);
            Set("_LivePathNormals", _paths.LiveNormals);
            Set("_LivePathBinormals", _paths.LiveBinormals);

            void Set(string name, GraphicsBuffer buffer) =>
                command.SetComputeBufferParam(_shader, _resolve, name, buffer);
        }

        private void BindSkin(CommandBuffer command)
        {
            Set("_VertexPathIndices", Buffers.VertexPathIndices);
            Set("_CrossSections", Buffers.CrossSections);
            Set("_LivePathPositions", _paths.LivePositions);
            Set("_LivePathTangents", _paths.LiveTangents);
            Set("_LivePathNormals", _paths.LiveNormals);
            Set("_LivePathBinormals", _paths.LiveBinormals);
            Set("_BasePositions", Buffers.BasePositions);
            Set("_BaseNormals", Buffers.BaseNormals);
            Set("_BaseTangents", Buffers.BaseTangents);

            void Set(string name, GraphicsBuffer buffer) =>
                command.SetComputeBufferParam(_shader, _skin, name, buffer);
        }

        private void BindNormals(CommandBuffer command)
        {
            command.SetComputeBufferParam(
                _shader, _normals, "_FinalPositions", Buffers.BasePositions);
            command.SetComputeBufferParam(
                _shader, _normals, "_FinalNormals", Buffers.FinalNormals);
            command.SetComputeBufferParam(
                _shader, _normals, "_BaseNormals", Buffers.BaseNormals);
            command.SetComputeBufferParam(
                _shader, _normals, "_Indices", _geometry.Indices);
            command.SetComputeBufferParam(
                _shader, _normals, "_VertexFaceOffsets",
                _geometry.Topology.VertexFaceOffsets);
            command.SetComputeBufferParam(
                _shader, _normals, "_VertexFaces",
                _geometry.Topology.VertexFaces);
        }

        private void BindPublish(CommandBuffer command)
        {
            command.SetComputeBufferParam(
                _shader, _publish, "_BasePositions", Buffers.BasePositions);
            command.SetComputeBufferParam(
                _shader, _publish, "_FinalNormals", Buffers.FinalNormals);
            command.SetComputeBufferParam(
                _shader, _publish, "_BaseTangents", Buffers.BaseTangents);
            command.SetComputeBufferParam(
                _shader, _publish, "_DynamicVertices",
                _geometry.Live.DynamicVertices);
            command.SetComputeBufferParam(
                _shader, _publish, "_StaticVertices",
                _geometry.StaticVertices);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(
                    nameof(BranchPathDeformationPipeline));
        }
    }
}
