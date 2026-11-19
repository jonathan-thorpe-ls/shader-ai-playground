using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public enum ScalarOperation
    {
        Fill,
        Remap,
        CombineAdd,
        CombineMultiply,
        CombineMin,
        CombineMax
    }

    public sealed class ScalarAttributeOperator : IAttributeOperator
    {
        private static readonly IReadOnlyList<AttributeDependency> NoInputs =
            Array.Empty<AttributeDependency>();
        private readonly AttributeId _output;
        private readonly AttributeId _firstInput;
        private readonly AttributeId _secondInput;
        private readonly float _minimum;
        private readonly float _maximum;
        private readonly float _outputMinimum;
        private readonly float _outputMaximum;
        private readonly float _firstWeight;
        private readonly float _secondWeight;
        private readonly ComputeShader _shader;
        private readonly int _kernel;

        private ScalarAttributeOperator(
            ScalarOperation operation,
            AttributeId output,
            AttributeId firstInput,
            AttributeId secondInput,
            float minimum,
            float maximum,
            float outputMinimum,
            float outputMaximum,
            float firstWeight,
            float secondWeight,
            ComputeShader shader)
        {
            Operation = operation;
            _output = output;
            _firstInput = firstInput;
            _secondInput = secondInput;
            _minimum = minimum;
            _maximum = maximum;
            _outputMinimum = outputMinimum;
            _outputMaximum = outputMaximum;
            _firstWeight = firstWeight;
            _secondWeight = secondWeight;
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/AttributeOps")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/AttributeOps.compute was not found.");
            _kernel = _shader.FindKernel(Operation.ToString());

            AttributeDescriptor descriptor = new(
                output,
                AttributeDomain.Vertex,
                AttributeFormat.Float,
                lifetime: AttributeLifetime.Dynamic);
            Outputs = new[] { descriptor };
            Inputs = BuildInputs(operation, firstInput, secondInput);
        }

        public string Name => Operation.ToString();
        public ScalarOperation Operation { get; }
        public IReadOnlyList<AttributeDependency> Inputs { get; }
        public IReadOnlyList<AttributeDescriptor> Outputs { get; }

        public static ScalarAttributeOperator Fill(
            AttributeId output,
            float value,
            ComputeShader shader = null) =>
            new(
                ScalarOperation.Fill, output, default, default,
                value, value, value, value, 1f, 0f, shader);

        public static ScalarAttributeOperator Remap(
            AttributeId input,
            AttributeId output,
            Vector2 sourceRange,
            Vector2 outputRange,
            ComputeShader shader = null) =>
            new(
                ScalarOperation.Remap, output, input, default,
                sourceRange.x, sourceRange.y, outputRange.x, outputRange.y,
                1f, 0f, shader);

        public static ScalarAttributeOperator Combine(
            ScalarOperation operation,
            AttributeId first,
            AttributeId second,
            AttributeId output,
            float firstWeight = 1f,
            float secondWeight = 1f,
            ComputeShader shader = null)
        {
            if (operation != ScalarOperation.CombineAdd &&
                operation != ScalarOperation.CombineMultiply &&
                operation != ScalarOperation.CombineMin &&
                operation != ScalarOperation.CombineMax)
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
            return new(
                operation, output, first, second,
                0f, 1f, 0f, 1f, firstWeight, secondWeight, shader);
        }

        public void Validate(AttributeSet attributes)
        {
            foreach (AttributeDependency input in Inputs)
            {
                attributes.Require(input.Id);
            }
            if (Operation == ScalarOperation.Remap &&
                Mathf.Approximately(_minimum, _maximum))
            {
                throw new InvalidOperationException(
                    $"Operation '{Name}' requires a non-zero source range.");
            }
        }

        public void Record(CommandBuffer command, GeometryResource resource)
        {
            command.SetComputeIntParam(
                _shader, "_ElementCount", resource.Layout.VertexCount);
            command.SetComputeFloatParam(_shader, "_Value", _minimum);
            command.SetComputeFloatParam(_shader, "_InputMinimum", _minimum);
            command.SetComputeFloatParam(_shader, "_InputMaximum", _maximum);
            command.SetComputeFloatParam(_shader, "_OutputMinimum", _outputMinimum);
            command.SetComputeFloatParam(_shader, "_OutputMaximum", _outputMaximum);
            command.SetComputeFloatParam(_shader, "_FirstWeight", _firstWeight);
            command.SetComputeFloatParam(_shader, "_SecondWeight", _secondWeight);
            if (Inputs.Count > 0)
            {
                command.SetComputeBufferParam(
                    _shader, _kernel, "_InputA",
                    resource.Attributes.Require(_firstInput).Buffer);
            }
            if (Inputs.Count > 1)
            {
                command.SetComputeBufferParam(
                    _shader, _kernel, "_InputB",
                    resource.Attributes.Require(_secondInput).Buffer);
            }
            command.SetComputeBufferParam(
                _shader, _kernel, "_Output",
                resource.Attributes.Require(_output).Buffer);
            command.DispatchCompute(
                _shader, _kernel,
                (resource.Layout.VertexCount + 63) / 64, 1, 1);
        }

        private static IReadOnlyList<AttributeDependency> BuildInputs(
            ScalarOperation operation,
            AttributeId first,
            AttributeId second)
        {
            if (operation == ScalarOperation.Fill)
                return NoInputs;
            var dependency = new AttributeDependency(
                first, AttributeDomain.Vertex, AttributeFormat.Float);
            if (operation == ScalarOperation.Remap)
                return new[] { dependency };
            return new[]
            {
                dependency,
                new AttributeDependency(
                    second, AttributeDomain.Vertex, AttributeFormat.Float)
            };
        }
    }
}
