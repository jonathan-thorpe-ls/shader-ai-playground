using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class AttributeProcessor
    {
        private readonly ComputeShader _shader;
        private readonly int _normalsKernel;

        public AttributeProcessor(ComputeShader shader = null)
        {
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/Normals")
                    ?? throw new InvalidOperationException("ProcGeo/Normals.compute was not found.");
            _normalsKernel = _shader.FindKernel("ComputeNormalsAndCurvature");
        }

        public void Record(CommandBuffer command, GeometryResource resource)
        {
            command.SetComputeIntParam(_shader, "_VertexCount", resource.Layout.VertexCount);
            command.SetComputeFloatParam(_shader, "_Epsilon", 1e-6f);
            command.SetComputeBufferParam(_shader, _normalsKernel, "_Indices", resource.Indices);
            BindAttribute("_RestPositions", AttributeIds.RestPosition);
            BindAttribute("_RestNormals", AttributeIds.RestNormal);
            BindBuffer("_NeighbourOffsets", resource.Topology.NeighbourOffsets);
            BindBuffer("_Neighbours", resource.Topology.Neighbours);
            BindBuffer("_VertexFaceOffsets", resource.Topology.VertexFaceOffsets);
            BindBuffer("_VertexFaces", resource.Topology.VertexFaces);
            BindAttribute("_Curvature", AttributeIds.Curvature);
            command.SetComputeBufferParam(
                _shader, _normalsKernel, "_DynamicVertices", resource.Live.DynamicVertices);
            command.DispatchCompute(
                _shader,
                _normalsKernel,
                (resource.Layout.VertexCount + 63) / 64,
                1,
                1);

            void BindAttribute(string property, AttributeId id) =>
                command.SetComputeBufferParam(
                    _shader, _normalsKernel, property, resource.Attributes.Require(id).Buffer);

            void BindBuffer(string property, GraphicsBuffer buffer) =>
                command.SetComputeBufferParam(_shader, _normalsKernel, property, buffer);
        }
    }

    public sealed class GeometryValidator
    {
        private readonly ComputeShader _shader;
        private readonly int _clearKernel;
        private readonly int _verticesKernel;
        private readonly int _edgesKernel;
        private readonly int _primitivesKernel;
        private readonly int _finalizeKernel;

        public GeometryValidator(ComputeShader shader = null)
        {
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/Validate")
                    ?? throw new InvalidOperationException("ProcGeo/Validate.compute was not found.");
            _clearKernel = _shader.FindKernel("ClearValidation");
            _verticesKernel = _shader.FindKernel("ValidateVertices");
            _edgesKernel = _shader.FindKernel("ValidateEdges");
            _primitivesKernel = _shader.FindKernel("ValidatePrimitives");
            _finalizeKernel = _shader.FindKernel("FinalizeValidation");
        }

        public void Record(CommandBuffer command, GeometryResource resource)
        {
            command.SetComputeIntParam(_shader, "_VertexCount", resource.Layout.VertexCount);
            command.SetComputeIntParam(_shader, "_PrimitiveCount", resource.Layout.PrimitiveCount);
            command.SetComputeIntParam(_shader, "_EdgeCount", resource.Layout.EdgeCapacity);
            command.SetComputeFloatParam(_shader, "_Epsilon", 1e-7f);

            Bind(_clearKernel);
            command.DispatchCompute(_shader, _clearKernel, 1, 1, 1);
            Bind(_verticesKernel);
            Dispatch(command, _verticesKernel, resource.Layout.VertexCount);
            Bind(_edgesKernel);
            Dispatch(command, _edgesKernel, resource.Layout.EdgeCapacity);
            Bind(_primitivesKernel);
            Dispatch(command, _primitivesKernel, resource.Layout.PrimitiveCount);
            command.SetComputeBufferParam(
                _shader, _finalizeKernel, "_ValidationResult", resource.ValidationResult);
            command.SetComputeBufferParam(
                _shader, _finalizeKernel, "_GeometryHeader", resource.Header);
            command.DispatchCompute(_shader, _finalizeKernel, 1, 1, 1);

            void Bind(int kernel)
            {
                command.SetComputeBufferParam(
                    _shader, kernel, "_ValidationResult", resource.ValidationResult);
                if (kernel == _verticesKernel)
                {
                    Set("_RestPositions", AttributeIds.RestPosition);
                    Set("_RestNormals", AttributeIds.RestNormal);
                    SetBuffer("_NeighbourOffsets", resource.Topology.NeighbourOffsets);
                    SetBuffer("_Neighbours", resource.Topology.Neighbours);
                }
                else if (kernel == _edgesKernel)
                {
                    SetBuffer("_Edges", resource.Topology.Edges);
                    SetBuffer("_RestLengths", resource.Topology.EdgeRestLength);
                    SetBuffer("_EdgeFaceCounts", resource.Topology.EdgeFaceCounts);
                }
                else if (kernel == _primitivesKernel)
                {
                    SetBuffer("_Indices", resource.Indices);
                    Set("_RestPositions", AttributeIds.RestPosition);
                }

                void Set(string property, AttributeId id) =>
                    SetBuffer(property, resource.Attributes.Require(id).Buffer);

                void SetBuffer(string property, GraphicsBuffer buffer) =>
                    command.SetComputeBufferParam(_shader, kernel, property, buffer);
            }
        }

        private void Dispatch(CommandBuffer command, int kernel, int count) =>
            command.DispatchCompute(_shader, kernel, (count + 63) / 64, 1, 1);
    }

    public readonly struct ValidationResult
    {
        public readonly uint ErrorCount;
        public readonly uint FirstElement;

        public ValidationResult(uint errorCount, uint firstElement)
        {
            ErrorCount = errorCount;
            FirstElement = firstElement;
        }
    }
}
