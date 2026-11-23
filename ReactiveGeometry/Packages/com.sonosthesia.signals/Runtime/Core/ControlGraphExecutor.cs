using System;
using UnityEngine;

namespace Sonosthesia.Signals
{
    public sealed class ControlGraphExecutor
    {
        private readonly CompiledControlGraph _graph;
        private readonly float[] _values;
        private readonly byte[] _pulseStarted;
        private readonly float[] _states;
        private readonly int[] _integerStates;
        private bool _stateValid;

        public ControlGraphExecutor(CompiledControlGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _values = new float[Math.Max(1, graph.OperationCount)];
            _pulseStarted = new byte[Math.Max(1, graph.OperationCount)];
            _states = new float[Math.Max(1, graph.FloatStateCount)];
            _integerStates = new int[Math.Max(1, graph.IntegerStateCount)];
        }

        public int OperationCount => _graph.OperationCount;

        public void Reset()
        {
            Array.Clear(_states, 0, _states.Length);
            Array.Clear(_integerStates, 0, _integerStates.Length);
            Array.Clear(_pulseStarted, 0, _pulseStarted.Length);
            _stateValid = false;
        }

        public void Evaluate(in ControlSignalFrame frame)
        {
            bool resetState = !_stateValid || frame.IsDiscontinuous;
            if (resetState)
            {
                Reset();
                _stateValid = true;
            }

            for (int index = 0; index < _graph.OperationCount; index++)
            {
                ControlOperation operation = _graph.GetOperation(index);
                _values[index] = EvaluateOperation(index, operation, frame, resetState);
            }
        }

        public bool TryGetOutput(int outputIndex, out float value, out ControlPulse pulse)
        {
            value = 0f;
            pulse = default;
            if ((uint)outputIndex >= (uint)_graph.OutputCount)
                return false;

            ControlGraphOutput output = _graph.GetOutput(outputIndex);
            if ((uint)output.OperationIndex >= (uint)_values.Length)
                return false;

            value = _values[output.OperationIndex];
            pulse = new ControlPulse(
                value,
                _values[output.OperationIndex],
                _pulseStarted[output.OperationIndex] != 0);
            if (output.Type == ControlSignalType.Pulse)
                value = pulse.Strength;
            return true;
        }

        private float EvaluateOperation(
            int outputIndex,
            ControlOperation operation,
            in ControlSignalFrame frame,
            bool resetState)
        {
            float first = InputValue(operation.InputA);
            float second = InputValue(operation.InputB);
            _pulseStarted[outputIndex] = 0;

            switch (operation.Kind)
            {
                case ControlOperationKind.ContinuousSource:
                    return Read(
                        frame.ContinuousValues,
                        frame.ContinuousAvailable,
                        operation.SourceIndex);
                case ControlOperationKind.Constant:
                    return operation.ParameterA;
                case ControlOperationKind.Add:
                    return first + second;
                case ControlOperationKind.Multiply:
                    return first * second;
                case ControlOperationKind.Clamp:
                    return Mathf.Clamp(first, operation.ParameterA, operation.ParameterB);
                case ControlOperationKind.Remap:
                    return Remap(first, operation.ParameterA, operation.ParameterB,
                        operation.ParameterC, operation.ParameterD);
                case ControlOperationKind.Smoothstep:
                    return Mathf.SmoothStep(
                        operation.ParameterA,
                        operation.ParameterB,
                        first);
                case ControlOperationKind.LowPass:
                    return EvaluateLowPass(operation, first, frame.DeltaTime, resetState);
                case ControlOperationKind.AttackRelease:
                    return EvaluateAttackRelease(operation, first, frame.DeltaTime, resetState);
                case ControlOperationKind.Slew:
                    return EvaluateSlew(operation, first, frame.DeltaTime, resetState);
                case ControlOperationKind.Delay:
                    return EvaluateDelay(operation, first, frame.DeltaTime, resetState);
                case ControlOperationKind.Subtract:
                    return first - second;
                case ControlOperationKind.Divide:
                    return second == 0f
                        ? operation.ParameterA
                        : first / second;
                case ControlOperationKind.Invert:
                    return 1f - first;
                case ControlOperationKind.Minimum:
                    return Mathf.Min(first, second);
                case ControlOperationKind.Maximum:
                    return Mathf.Max(first, second);
                case ControlOperationKind.Mix:
                    return Mathf.Lerp(
                        first,
                        second,
                        Mathf.Clamp01(operation.ParameterA));
                case ControlOperationKind.Threshold:
                    return EvaluateThreshold(operation, first, resetState);
                case ControlOperationKind.SampleAndHold:
                    return EvaluateSampleAndHold(
                        operation, first, PulseStarted(operation.InputB), resetState);
                case ControlOperationKind.PulseStrengthSource:
                    _pulseStarted[outputIndex] = Read(
                        frame.PulseStarted, operation.SourceIndex);
                    return Read(frame.PulseStrengths, operation.SourceIndex);
                case ControlOperationKind.PulseMagnitudeSource:
                    _pulseStarted[outputIndex] = Read(
                        frame.PulseStarted, operation.SourceIndex);
                    return Read(frame.PulseMagnitudes, operation.SourceIndex);
                case ControlOperationKind.PulseStartedSource:
                    _pulseStarted[outputIndex] = Read(
                        frame.PulseStarted, operation.SourceIndex);
                    return Read(frame.PulseStrengths, operation.SourceIndex);
                case ControlOperationKind.PulseDecay:
                    return EvaluatePulseDecay(
                        operation, first, PulseStarted(operation.InputB),
                        frame.DeltaTime, resetState, outputIndex);
                case ControlOperationKind.PulseMerge:
                    _pulseStarted[outputIndex] =
                        (byte)(PulseStarted(operation.InputA) | PulseStarted(operation.InputB));
                    return Mathf.Max(first, second);
                default:
                    throw new InvalidOperationException($"Unsupported control operation {operation.Kind}.");
            }
        }

        private float EvaluateLowPass(
            ControlOperation operation,
            float input,
            double deltaTime,
            bool resetState)
        {
            if (resetState)
            {
                _states[operation.StateOffset] = input;
                return input;
            }

            float tau = Mathf.Max(operation.ParameterA, 0.0001f);
            float alpha = 1f - Mathf.Exp(-(float)deltaTime / tau);
            float value = Mathf.Lerp(_states[operation.StateOffset], input, alpha);
            _states[operation.StateOffset] = value;
            return value;
        }

        private float EvaluateAttackRelease(
            ControlOperation operation,
            float input,
            double deltaTime,
            bool resetState)
        {
            if (resetState)
            {
                _states[operation.StateOffset] = input;
                return input;
            }

            float previous = _states[operation.StateOffset];
            float time = input >= previous ? operation.ParameterA : operation.ParameterB;
            float coefficient = 1f - Mathf.Exp(
                -(float)deltaTime / Mathf.Max(time, 0.0001f));
            float value = Mathf.Lerp(previous, input, coefficient);
            _states[operation.StateOffset] = value;
            return value;
        }

        private float EvaluateSlew(
            ControlOperation operation,
            float input,
            double deltaTime,
            bool resetState)
        {
            if (resetState)
            {
                _states[operation.StateOffset] = input;
                return input;
            }

            float previous = _states[operation.StateOffset];
            float maxDelta = Mathf.Max(operation.ParameterA, 0f) * (float)deltaTime;
            float value = previous + Mathf.Clamp(input - previous, -maxDelta, maxDelta);
            _states[operation.StateOffset] = value;
            return value;
        }

        private float EvaluateDelay(
            ControlOperation operation,
            float input,
            double deltaTime,
            bool resetState)
        {
            int capacity = Mathf.Max(2, operation.DelayCapacity);
            int stateIndex = operation.StateOffset;
            int integerIndex = 0;
            if (operation.InputB >= 0)
                integerIndex = operation.InputB;

            if (resetState)
            {
                for (int index = 0; index < capacity; index++)
                    _states[stateIndex + index] = input;
                _integerStates[integerIndex] = 0;
                return input;
            }

            int delaySamples = Mathf.Clamp(
                Mathf.RoundToInt(operation.ParameterA / Mathf.Max((float)deltaTime, 0.0001f)),
                0,
                capacity - 1);
            int write = _integerStates[integerIndex] % capacity;
            _states[stateIndex + write] = input;
            int read = (write - delaySamples + capacity) % capacity;
            float result = _states[stateIndex + read];
            _integerStates[integerIndex] = (write + 1) % capacity;
            return result;
        }

        private float EvaluateThreshold(
            ControlOperation operation,
            float input,
            bool resetState)
        {
            if (resetState)
                _states[operation.StateOffset] = input >= operation.ParameterA ? 1f : 0f;
            else if (_states[operation.StateOffset] > 0.5f)
            {
                if (input <= operation.ParameterA - operation.ParameterB)
                    _states[operation.StateOffset] = 0f;
            }
            else if (input >= operation.ParameterA + operation.ParameterB)
            {
                _states[operation.StateOffset] = 1f;
            }

            return _states[operation.StateOffset];
        }

        private float EvaluateSampleAndHold(
            ControlOperation operation,
            float input,
            byte started,
            bool resetState)
        {
            if (resetState || started != 0)
                _states[operation.StateOffset] = input;
            return _states[operation.StateOffset];
        }

        private float EvaluatePulseDecay(
            ControlOperation operation,
            float input,
            byte started,
            double deltaTime,
            bool resetState,
            int outputIndex)
        {
            if (started != 0 || resetState)
                _states[operation.StateOffset] = input;
            else
            {
                float decay = Mathf.Max(0f, operation.ParameterA);
                _states[operation.StateOffset] = Mathf.Max(
                    0f,
                    _states[operation.StateOffset] - decay * (float)deltaTime);
            }
            _pulseStarted[outputIndex] = started;
            return _states[operation.StateOffset];
        }

        private byte PulseStarted(int operationIndex)
        {
            return operationIndex >= 0 && operationIndex < _pulseStarted.Length
                ? _pulseStarted[operationIndex]
                : (byte)0;
        }

        private float InputValue(int index)
        {
            return index >= 0 && index < _values.Length ? _values[index] : 0f;
        }

        private static float Read(float[] values, int index)
        {
            return (uint)index < (uint)(values?.Length ?? 0) ? values[index] : 0f;
        }

        private static float Read(float[] values, byte[] availability, int index)
        {
            if ((uint)index >= (uint)(values?.Length ?? 0))
                return 0f;
            return availability != null
                   && (uint)index < (uint)availability.Length
                   && availability[index] == 0
                ? 0f
                : values[index];
        }

        private static byte Read(byte[] values, int index)
        {
            return (uint)index < (uint)(values?.Length ?? 0) ? values[index] : (byte)0;
        }

        private static float Remap(
            float value,
            float inputMinimum,
            float inputMaximum,
            float outputMinimum,
            float outputMaximum)
        {
            float range = inputMaximum - inputMinimum;
            float amount = Mathf.Abs(range) < 0.000001f
                ? 0f
                : (value - inputMinimum) / range;
            return Mathf.Lerp(outputMinimum, outputMaximum, amount);
        }
    }
}
