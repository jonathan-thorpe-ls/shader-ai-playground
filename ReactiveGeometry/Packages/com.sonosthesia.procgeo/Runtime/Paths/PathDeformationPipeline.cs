using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class PathDeformationPipeline : IDisposable
    {
        private readonly GeometryResource _geometry;
        private readonly PathResource _path;
        private readonly PathBinding _binding;
        private readonly ComputeShader _shader;
        private readonly GeometryCommandScheduler _scheduler;
        private readonly int _clearPathKernel;
        private readonly int _buildFramesKernel;
        private readonly int _skinKernel;
        private readonly int _clearVertexKernel;
        private readonly int _composeKernel;
        private readonly int _finalNormalsKernel;
        private readonly int _publishKernel;
        private bool _disposed;

        public PathDeformationPipeline(
            GeometryResource geometry,
            PathGeometryData pathData,
            ComputeShader shader = null)
        {
            _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            if (pathData == null) throw new ArgumentNullException(nameof(pathData));
            if (pathData.VertexPathIndices.Length != geometry.Layout.VertexCount)
                throw new ArgumentException("The path binding does not match the geometry.", nameof(pathData));

            _path = new PathResource($"{geometry.Layout.Name}.Path", pathData);
            _binding = new PathBinding(pathData);
            Buffers = new PathDeformationBuffers(
                geometry.Layout, _path.SampleCount, geometry.Layout.Name, this);
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/PathDeformation")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/PathDeformation.compute was not found.");
            _clearPathKernel = _shader.FindKernel("ClearPath");
            _buildFramesKernel = _shader.FindKernel("BuildPathFrames");
            _skinKernel = _shader.FindKernel("SkinPathGeometry");
            _clearVertexKernel = _shader.FindKernel("ClearVertexOffsets");
            _composeKernel = _shader.FindKernel("ComposeVertexPositions");
            _finalNormalsKernel = _shader.FindKernel("RecomputeFinalNormals");
            _publishKernel = _shader.FindKernel("PublishPathGeometry");
            _scheduler = new GeometryCommandScheduler(
                $"{geometry.Layout.Name}.PathDeformation");
            Reset();
        }

        public PathResource Path => _path;
        public PathDeformationBuffers Buffers { get; }
        public ComputeShader Shader => _shader;

        public void Reset() => Record(
            Array.Empty<IPathDeformer>(),
            Array.Empty<IVertexDeformer>(),
            0f,
            0f);

        public void Record(
            IReadOnlyList<IPathDeformer> pathDeformers,
            IReadOnlyList<IVertexDeformer> vertexDeformers,
            float deltaTime,
            float time)
        {
            ThrowIfDisposed();
            if (pathDeformers == null) throw new ArgumentNullException(nameof(pathDeformers));
            if (vertexDeformers == null) throw new ArgumentNullException(nameof(vertexDeformers));
            _scheduler.Record(command =>
            {
                bool dynamicPath = false;
                foreach (IPathDeformer deformer in pathDeformers)
                    dynamicPath |= deformer != null &&
                        deformer.Enabled &&
                        deformer is IDynamicPathDeformer;
                BindCounts(command);
                command.SetComputeIntParam(
                    _shader, "_PathDynamic", dynamicPath ? 1 : 0);
                command.SetComputeFloatParam(
                    _shader, "_PathDeltaTime", Mathf.Clamp(deltaTime, 0f, 1f / 30f));
                command.SetComputeFloatParam(_shader, "_PathRestoring", 24f);
                command.SetComputeFloatParam(_shader, "_PathDamping", 6f);
                command.SetComputeFloatParam(_shader, "_PathMaximumVelocity", 12f);
                command.SetComputeFloatParam(_shader, "_PathMaximumDisplacement", 2f);
                BindClearPath(command);
                command.DispatchCompute(_shader, _clearPathKernel, 1 + (_path.SampleCount - 1) / 64, 1, 1);

                PathDeformerContext pathContext = new(
                    _path, Buffers, _shader, deltaTime, time);
                foreach (IPathDeformer deformer in pathDeformers)
                    if (deformer != null && deformer.Enabled)
                        deformer.Record(command, pathContext);

                BindBuildFrames(command);
                command.DispatchCompute(_shader, _buildFramesKernel, 1, 1, 1);

                BindSkin(command);
                command.DispatchCompute(
                    _shader, _skinKernel,
                    (_geometry.Layout.VertexCount + 63) / 64, 1, 1);

                BindClearVertex(command);
                command.DispatchCompute(
                    _shader, _clearVertexKernel,
                    (_geometry.Layout.VertexCount + 63) / 64, 1, 1);

                VertexDeformerContext vertexContext = new(
                    _geometry, _path, Buffers, _shader, deltaTime, time);
                foreach (IVertexDeformer deformer in vertexDeformers)
                    if (deformer != null && deformer.Enabled)
                        deformer.Record(command, vertexContext);

                BindCompose(command);
                command.DispatchCompute(
                    _shader, _composeKernel,
                    (_geometry.Layout.VertexCount + 63) / 64, 1, 1);
                BindFinalNormals(command);
                command.DispatchCompute(
                    _shader, _finalNormalsKernel,
                    (_geometry.Layout.VertexCount + 63) / 64, 1, 1);
                BindPublish(command);
                command.DispatchCompute(
                    _shader, _publishKernel,
                    (_geometry.Layout.VertexCount + 63) / 64, 1, 1);
            });
            _scheduler.ExecuteOnce();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scheduler.Dispose();
            Buffers.Dispose();
            _binding.Dispose();
            _path.Dispose();
        }

        private void BindCounts(CommandBuffer command)
        {
            command.SetComputeIntParam(_shader, "_PathSampleCount", _path.SampleCount);
            command.SetComputeIntParam(_shader, "_PathClosed", _path.Closed ? 1 : 0);
            command.SetComputeIntParam(
                _shader, "_VertexCount", _geometry.Layout.VertexCount);
            command.SetComputeFloatParam(_shader, "_Epsilon", 1e-6f);
            command.SetComputeIntParam(_shader, "_SourceEnabled", 0);
            command.SetComputeFloatParam(_shader, "_SourceWeight", 1f);
        }

        private void BindClearPath(CommandBuffer command)
        {
            Set(command, _clearPathKernel, "_PathContributions", Buffers.PathContributions);
            Set(command, _clearPathKernel, "_PathTwists", Buffers.PathTwists);
            Set(command, _clearPathKernel, "_PathForces", Buffers.PathForces);
        }

        private void BindBuildFrames(CommandBuffer command)
        {
            Set(command, _buildFramesKernel, "_RestPathPositions", _path.RestPositions);
            Set(command, _buildFramesKernel, "_RestPathTangents", _path.RestTangents);
            Set(command, _buildFramesKernel, "_RestPathNormals", _path.RestNormals);
            Set(command, _buildFramesKernel, "_RestPathBinormals", _path.RestBinormals);
            Set(command, _buildFramesKernel, "_PathContributions", Buffers.PathContributions);
            Set(command, _buildFramesKernel, "_PathTwists", Buffers.PathTwists);
            Set(command, _buildFramesKernel, "_LivePathPositions", _path.LivePositions);
            Set(command, _buildFramesKernel, "_LivePathTangents", _path.LiveTangents);
            Set(command, _buildFramesKernel, "_LivePathNormals", _path.LiveNormals);
            Set(command, _buildFramesKernel, "_LivePathBinormals", _path.LiveBinormals);
            Set(command, _buildFramesKernel, "_LivePathVelocities", _path.LiveVelocities);
            Set(command, _buildFramesKernel, "_PathForces", Buffers.PathForces);
        }

        private void BindSkin(CommandBuffer command)
        {
            Set(command, _skinKernel, "_VertexPathIndices", _binding.PathIndices);
            Set(command, _skinKernel, "_VertexPathBlends", _binding.PathBlends);
            Set(command, _skinKernel, "_CrossSections", _binding.CrossSections);
            Set(command, _skinKernel, "_LivePathPositions", _path.LivePositions);
            Set(command, _skinKernel, "_LivePathTangents", _path.LiveTangents);
            Set(command, _skinKernel, "_LivePathNormals", _path.LiveNormals);
            Set(command, _skinKernel, "_LivePathBinormals", _path.LiveBinormals);
            Set(command, _skinKernel, "_BasePositions", Buffers.BasePositions);
            Set(command, _skinKernel, "_BaseNormals", Buffers.BaseNormals);
            Set(command, _skinKernel, "_BaseTangents", Buffers.BaseTangents);
        }

        private void BindClearVertex(CommandBuffer command) =>
            Set(command, _clearVertexKernel, "_VertexOffsets", Buffers.VertexOffsets);

        private void BindCompose(CommandBuffer command)
        {
            Set(command, _composeKernel, "_BasePositions", Buffers.BasePositions);
            Set(command, _composeKernel, "_VertexOffsets", Buffers.VertexOffsets);
            Set(command, _composeKernel, "_FinalPositions", Buffers.FinalPositions);
        }

        private void BindFinalNormals(CommandBuffer command)
        {
            Set(command, _finalNormalsKernel, "_FinalPositions", Buffers.FinalPositions);
            Set(command, _finalNormalsKernel, "_FinalNormals", Buffers.FinalNormals);
            Set(command, _finalNormalsKernel, "_Indices", _geometry.Indices);
            Set(command, _finalNormalsKernel, "_VertexFaceOffsets", _geometry.Topology.VertexFaceOffsets);
            Set(command, _finalNormalsKernel, "_VertexFaces", _geometry.Topology.VertexFaces);
            Set(command, _finalNormalsKernel, "_BaseNormals", Buffers.BaseNormals);
        }

        private void BindPublish(CommandBuffer command)
        {
            Set(command, _publishKernel, "_FinalPositions", Buffers.FinalPositions);
            Set(command, _publishKernel, "_FinalNormals", Buffers.FinalNormals);
            Set(command, _publishKernel, "_BaseTangents", Buffers.BaseTangents);
            Set(command, _publishKernel, "_DynamicVertices", _geometry.Live.DynamicVertices);
            Set(command, _publishKernel, "_StaticVertices", _geometry.StaticVertices);
        }

        private void Set(
            CommandBuffer command,
            int kernel,
            string name,
            GraphicsBuffer buffer) =>
            command.SetComputeBufferParam(_shader, kernel, name, buffer);

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PathDeformationPipeline));
        }
    }

    public sealed class PathDeformationBuffers : IDisposable
    {
        private GraphicsBuffer _pathContributions;
        private GraphicsBuffer _pathTwists;
        private GraphicsBuffer _pathForces;
        private GraphicsBuffer _basePositions;
        private GraphicsBuffer _baseNormals;
        private GraphicsBuffer _baseTangents;
        private GraphicsBuffer _vertexOffsets;
        private GraphicsBuffer _finalPositions;
        private GraphicsBuffer _finalNormals;
        private bool _disposed;

        internal PathDeformationBuffers(
            GeometryLayout layout,
            int pathSampleCount,
            string name,
            object owner)
        {
            _pathContributions = PathResource.Empty<Vector3>(
                pathSampleCount, $"{name}.PathContributions", owner);
            _pathTwists = PathResource.Empty<float>(
                pathSampleCount, $"{name}.PathTwists", owner);
            _pathForces = PathResource.Empty<Vector3>(
                pathSampleCount, $"{name}.PathForces", owner);
            _basePositions = PathResource.Empty<Vector3>(
                layout.VertexCount, $"{name}.BasePositions", owner);
            _baseNormals = PathResource.Empty<Vector3>(
                layout.VertexCount, $"{name}.BaseNormals", owner);
            _baseTangents = PathResource.Empty<Vector3>(
                layout.VertexCount, $"{name}.BaseTangents", owner);
            _vertexOffsets = PathResource.Empty<Vector3>(
                layout.VertexCount, $"{name}.VertexOffsets", owner);
            _finalPositions = PathResource.Empty<Vector3>(
                layout.VertexCount, $"{name}.FinalPositions", owner);
            _finalNormals = PathResource.Empty<Vector3>(
                layout.VertexCount, $"{name}.FinalNormals", owner);
        }

        public GraphicsBuffer PathContributions => _pathContributions;
        public GraphicsBuffer PathTwists => _pathTwists;
        public GraphicsBuffer PathForces => _pathForces;
        public GraphicsBuffer BasePositions => _basePositions;
        public GraphicsBuffer BaseNormals => _baseNormals;
        public GraphicsBuffer BaseTangents => _baseTangents;
        public GraphicsBuffer VertexOffsets => _vertexOffsets;
        public GraphicsBuffer FinalPositions => _finalPositions;
        public GraphicsBuffer FinalNormals => _finalNormals;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GpuBufferRegistry.Release(ref _pathContributions);
            GpuBufferRegistry.Release(ref _pathTwists);
            GpuBufferRegistry.Release(ref _pathForces);
            GpuBufferRegistry.Release(ref _basePositions);
            GpuBufferRegistry.Release(ref _baseNormals);
            GpuBufferRegistry.Release(ref _baseTangents);
            GpuBufferRegistry.Release(ref _vertexOffsets);
            GpuBufferRegistry.Release(ref _finalPositions);
            GpuBufferRegistry.Release(ref _finalNormals);
        }
    }
}
