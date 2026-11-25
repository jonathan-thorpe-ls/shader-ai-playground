using System;
using UnityEngine;

namespace Sonosthesia.Signals
{
    public sealed class ControlGraphExecutor
    {
        private readonly CompiledControlGraph _graph;
        private readonly ControlValue[] _values;
        private readonly byte[] _pulseStarted;
        private readonly float[] _states;
        private readonly int[] _integerStates;
        private bool _stateValid;

        public ControlGraphExecutor(CompiledControlGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _values = new ControlValue[Math.Max(1, graph.OperationCount)];
            _pulseStarted = new byte[Math.Max(1, graph.OperationCount)];
            _states = new float[Math.Max(1, graph.TotalStateComponentCount)];
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
            pulse = default;
            if (!TryGetOutputValue(outputIndex, ControlSignalType.Continuous, out ControlValue result)
                && !TryGetOutputValue(outputIndex, ControlSignalType.Pulse, out result))
            {
                value = 0f;
                return false;
            }
            value = result.X;
            ControlGraphOutput output = _graph.GetOutput(outputIndex);
            pulse = new ControlPulse(result.X, result.Y, _pulseStarted[output.OperationIndex] != 0);
            return true;
        }

        public bool TryGetOutput(int outputIndex, out Vector2 value)
        {
            if (!TryGetOutputValue(outputIndex, ControlSignalType.Vector2, out ControlValue result))
            {
                value = default;
                return false;
            }
            value = ControlValue.ToVector2(result);
            return true;
        }

        public bool TryGetOutput(int outputIndex, out Vector3 value)
        {
            if (!TryGetOutputValue(outputIndex, ControlSignalType.Vector3, out ControlValue result))
            {
                value = default;
                return false;
            }
            value = ControlValue.ToVector3(result);
            return true;
        }

        public bool TryGetOutput(int outputIndex, out Color value)
        {
            if (!TryGetOutputValue(outputIndex, ControlSignalType.Color, out ControlValue result))
            {
                value = default;
                return false;
            }
            value = ControlValue.ToColor(result);
            return true;
        }

        private bool TryGetOutputValue(
            int outputIndex,
            ControlSignalType type,
            out ControlValue value)
        {
            value = default;
            if ((uint)outputIndex >= (uint)_graph.OutputCount)
                return false;
            ControlGraphOutput output = _graph.GetOutput(outputIndex);
            if (output.Type != type || (uint)output.OperationIndex >= (uint)_values.Length)
                return false;
            value = _values[output.OperationIndex];
            return true;
        }

        private ControlValue EvaluateOperation(
            int outputIndex,
            ControlOperation operation,
            in ControlSignalFrame frame,
            bool resetState)
        {
            ControlValue a = InputValue(operation.InputA);
            ControlValue b = InputValue(operation.InputB);
            ControlValue c = InputValue(operation.InputC);
            ControlValue d = InputValue(operation.InputD);
            _pulseStarted[outputIndex] = 0;

            switch (operation.Kind)
            {
                case ControlOperationKind.ContinuousSource:
                    return Read(frame.ContinuousValues, frame.ContinuousAvailable, operation.SourceIndex);
                case ControlOperationKind.Constant:
                    return operation.ParameterA;
                case ControlOperationKind.Add:
                    return a.X + b.X;
                case ControlOperationKind.Multiply:
                    return a.X * b.X;
                case ControlOperationKind.Clamp:
                    return Mathf.Clamp(a.X, operation.ParameterA, operation.ParameterB);
                case ControlOperationKind.Remap:
                    return Remap(a.X, operation.ParameterA, operation.ParameterB,
                        operation.ParameterC, operation.ParameterD);
                case ControlOperationKind.Smoothstep:
                    return Mathf.SmoothStep(operation.ParameterA, operation.ParameterB, a.X);
                case ControlOperationKind.LowPass:
                    return EvaluateLowPass(operation, a.X, frame.DeltaTime, resetState);
                case ControlOperationKind.AttackRelease:
                    return EvaluateAttackRelease(operation, a.X, frame.DeltaTime, resetState);
                case ControlOperationKind.Slew:
                    return EvaluateSlew(operation, a.X, frame.DeltaTime, resetState);
                case ControlOperationKind.Delay:
                    return EvaluateDelay(operation, a.X, frame.DeltaTime, resetState);
                case ControlOperationKind.Subtract:
                    return a.X - b.X;
                case ControlOperationKind.Divide:
                    return b.X == 0f ? operation.ParameterA : a.X / b.X;
                case ControlOperationKind.Invert:
                    return 1f - a.X;
                case ControlOperationKind.Minimum:
                    return Mathf.Min(a.X, b.X);
                case ControlOperationKind.Maximum:
                    return Mathf.Max(a.X, b.X);
                case ControlOperationKind.Mix:
                    return Mathf.Lerp(a.X, b.X, Mathf.Clamp01(operation.ParameterA));
                case ControlOperationKind.Threshold:
                    return EvaluateThreshold(operation, a.X, resetState);
                case ControlOperationKind.SampleAndHold:
                    return EvaluateSampleAndHold(operation, a.X, PulseStarted(operation.InputB), resetState);
                case ControlOperationKind.PulseStrengthSource:
                    _pulseStarted[outputIndex] = Read(frame.PulseStarted, operation.SourceIndex);
                    return new ControlValue(
                        ReadFloat(frame.PulseStrengths, operation.SourceIndex),
                        ReadFloat(frame.PulseMagnitudes, operation.SourceIndex));
                case ControlOperationKind.PulseMagnitudeSource:
                    _pulseStarted[outputIndex] = Read(frame.PulseStarted, operation.SourceIndex);
                    return new ControlValue(
                        ReadFloat(frame.PulseStrengths, operation.SourceIndex),
                        ReadFloat(frame.PulseMagnitudes, operation.SourceIndex));
                case ControlOperationKind.PulseStartedSource:
                    _pulseStarted[outputIndex] = Read(frame.PulseStarted, operation.SourceIndex);
                    return new ControlValue(
                        ReadFloat(frame.PulseStrengths, operation.SourceIndex),
                        ReadFloat(frame.PulseMagnitudes, operation.SourceIndex));
                case ControlOperationKind.PulseDecay:
                    return EvaluatePulseDecay(operation, a.X, PulseStarted(operation.InputB),
                        frame.DeltaTime, resetState, outputIndex);
                case ControlOperationKind.PulseMerge:
                    _pulseStarted[outputIndex] =
                        (byte)(PulseStarted(operation.InputA) | PulseStarted(operation.InputB));
                    return new ControlValue(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));

                case ControlOperationKind.Vector2Constant:
                    return new ControlValue(operation.ParameterA, operation.ParameterB);
                case ControlOperationKind.Vector2Add:
                    return new ControlValue(a.X + b.X, a.Y + b.Y);
                case ControlOperationKind.Vector2Subtract:
                    return new ControlValue(a.X - b.X, a.Y - b.Y);
                case ControlOperationKind.Vector2Scale:
                    return new ControlValue(a.X * b.X, a.Y * b.X);
                case ControlOperationKind.Vector2Normalize:
                    return ControlValue.From(NormalizeSafe(ControlValue.ToVector2(a)));
                case ControlOperationKind.Vector2Lerp:
                    return ControlValue.From(Vector2.Lerp(
                        ControlValue.ToVector2(a), ControlValue.ToVector2(b),
                        Mathf.Clamp01(c.X)));
                case ControlOperationKind.Vector2Length:
                    return ControlValue.ToVector2(a).magnitude;
                case ControlOperationKind.Vector2FromScalars:
                    return new ControlValue(a.X, b.X);
                case ControlOperationKind.Polar:
                    {
                        float angle = a.X * (operation.ParameterA != 0f ? Mathf.Deg2Rad : 1f);
                        return new ControlValue(
                            Mathf.Cos(angle) * b.X,
                            Mathf.Sin(angle) * b.X);
                    }

                case ControlOperationKind.Vector3Constant:
                    return new ControlValue(
                        operation.ParameterA, operation.ParameterB, operation.ParameterC);
                case ControlOperationKind.Vector3Add:
                    return ControlValue.From(ControlValue.ToVector3(a) + ControlValue.ToVector3(b));
                case ControlOperationKind.Vector3Subtract:
                    return ControlValue.From(ControlValue.ToVector3(a) - ControlValue.ToVector3(b));
                case ControlOperationKind.Vector3Scale:
                    return ControlValue.From(ControlValue.ToVector3(a) * b.X);
                case ControlOperationKind.Vector3Normalize:
                    return ControlValue.From(NormalizeSafe(ControlValue.ToVector3(a)));
                case ControlOperationKind.Vector3Lerp:
                    return ControlValue.From(Vector3.Lerp(
                        ControlValue.ToVector3(a), ControlValue.ToVector3(b),
                        Mathf.Clamp01(c.X)));
                case ControlOperationKind.Vector3Length:
                    return ControlValue.ToVector3(a).magnitude;
                case ControlOperationKind.Vector3FromScalars:
                    return new ControlValue(a.X, b.X, c.X);
                case ControlOperationKind.Cylindrical:
                    {
                        float angle = a.X * (operation.ParameterA != 0f ? Mathf.Deg2Rad : 1f);
                        return new ControlValue(
                            Mathf.Cos(angle) * b.X,
                            c.X,
                            Mathf.Sin(angle) * b.X);
                    }
                case ControlOperationKind.Spherical:
                    {
                        float azimuth = a.X * (operation.ParameterA != 0f ? Mathf.Deg2Rad : 1f);
                        float elevation = b.X * (operation.ParameterA != 0f ? Mathf.Deg2Rad : 1f);
                        return new ControlValue(
                            Mathf.Cos(elevation) * Mathf.Cos(azimuth) * c.X,
                            Mathf.Sin(elevation) * c.X,
                            Mathf.Cos(elevation) * Mathf.Sin(azimuth) * c.X);
                    }
                case ControlOperationKind.DirectionWithLength:
                    return ControlValue.From(NormalizeSafe(ControlValue.ToVector3(a)) * b.X);
                case ControlOperationKind.Vector2Oscillator:
                    return Oscillator2(frame.TimelineTime, a.X, b.X, c.X, d.X, operation);
                case ControlOperationKind.Vector3Oscillator:
                    return Oscillator3(frame.TimelineTime, a.X, b.X, c.X, d.X, operation);
                case ControlOperationKind.Vector2Brownian:
                    return Brownian2(frame.TimelineTime, a.X, b.X, c.X, operation);
                case ControlOperationKind.Vector3Brownian:
                    return Brownian3(frame.TimelineTime, a.X, b.X, c.X, operation);

                case ControlOperationKind.ColorConstant:
                    return ControlValue.From(operation.ParameterColorA);
                case ControlOperationKind.ColorLerp:
                    return ControlValue.From(Color.Lerp(
                        ControlValue.ToColor(a), ControlValue.ToColor(b),
                        Mathf.Clamp01(c.X)));
                case ControlOperationKind.ColorMultiplyScalar:
                    return ScaleColor(a, b.X);
                case ControlOperationKind.ColorMultiply:
                    return new ControlValue(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
                case ControlOperationKind.HsvToColor:
                    return ControlValue.From(Color.HSVToRGB(
                        Mathf.Repeat(a.X, 1f), Mathf.Clamp01(b.X), Mathf.Clamp01(c.X), true));
                case ControlOperationKind.ColorToHsv:
                    {
                        Color color = ControlValue.ToColor(a);
                        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
                        return new ControlValue(hue, saturation, value, color.a);
                    }
                case ControlOperationKind.Gradient:
                    return ControlValue.From(EvaluateGradient(operation.Gradient, a.X));
                case ControlOperationKind.Palette:
                    return ControlValue.From(EvaluatePalette(operation.Palette, a.X));
                case ControlOperationKind.ColorTint:
                    return ColorTint(a, operation.ParameterColorA, b.X);
                default:
                    throw new InvalidOperationException($"Unsupported control operation {operation.Kind}.");
            }
        }

        private float EvaluateLowPass(ControlOperation operation, float input, double deltaTime, bool resetState)
        {
            if (resetState)
            {
                _states[operation.StateOffset] = input;
                return input;
            }
            float alpha = 1f - Mathf.Exp(-(float)deltaTime / Mathf.Max(operation.ParameterA, 0.0001f));
            float value = Mathf.Lerp(_states[operation.StateOffset], input, alpha);
            _states[operation.StateOffset] = value;
            return value;
        }

        private float EvaluateAttackRelease(ControlOperation operation, float input, double deltaTime, bool resetState)
        {
            if (resetState)
            {
                _states[operation.StateOffset] = input;
                return input;
            }
            float previous = _states[operation.StateOffset];
            float time = input >= previous ? operation.ParameterA : operation.ParameterB;
            float value = Mathf.Lerp(previous, input,
                1f - Mathf.Exp(-(float)deltaTime / Mathf.Max(time, 0.0001f)));
            _states[operation.StateOffset] = value;
            return value;
        }

        private float EvaluateSlew(ControlOperation operation, float input, double deltaTime, bool resetState)
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

        private float EvaluateDelay(ControlOperation operation, float input, double deltaTime, bool resetState)
        {
            int capacity = Mathf.Max(2, operation.DelayCapacity);
            int stateIndex = operation.StateOffset;
            int integerIndex = Mathf.Max(operation.InputB, 0);
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
            float result = _states[stateIndex + (write - delaySamples + capacity) % capacity];
            _integerStates[integerIndex] = (write + 1) % capacity;
            return result;
        }

        private float EvaluateThreshold(ControlOperation operation, float input, bool resetState)
        {
            if (resetState)
                _states[operation.StateOffset] = input >= operation.ParameterA ? 1f : 0f;
            else if (_states[operation.StateOffset] > 0.5f)
            {
                if (input <= operation.ParameterA - operation.ParameterB)
                    _states[operation.StateOffset] = 0f;
            }
            else if (input >= operation.ParameterA + operation.ParameterB)
                _states[operation.StateOffset] = 1f;
            return _states[operation.StateOffset];
        }

        private float EvaluateSampleAndHold(ControlOperation operation, float input, byte started, bool resetState)
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
                _states[operation.StateOffset] = Mathf.Max(
                    0f, _states[operation.StateOffset] -
                    Mathf.Max(0f, operation.ParameterA) * (float)deltaTime);
            _pulseStarted[outputIndex] = started;
            return _states[operation.StateOffset];
        }

        private ControlValue InputValue(int index)
        {
            return index >= 0 && index < _values.Length ? _values[index] : default;
        }

        private byte PulseStarted(int operationIndex)
        {
            return operationIndex >= 0 && operationIndex < _pulseStarted.Length
                ? _pulseStarted[operationIndex]
                : (byte)0;
        }

        private static ControlValue ScaleColor(ControlValue color, float scale)
        {
            return new ControlValue(color.X * scale, color.Y * scale, color.Z * scale, color.W);
        }

        private static ControlValue ColorTint(ControlValue color, Color tint, float strength)
        {
            float amount = Mathf.Clamp01(strength);
            return ControlValue.From(Color.Lerp(
                ControlValue.ToColor(color),
                tint,
                amount));
        }

        private static Color EvaluatePalette(Color[] palette, float time)
        {
            if (palette == null || palette.Length == 0)
                return Color.clear;
            float scaled = Mathf.Repeat(time, 1f) * (palette.Length - 1);
            int index = Mathf.FloorToInt(scaled);
            int next = Mathf.Min(index + 1, palette.Length - 1);
            return Color.Lerp(palette[index], palette[next], scaled - index);
        }

        private static Color EvaluateGradient(GradientRuntimeData gradient, float time)
        {
            if (gradient?.Keys == null || gradient.Keys.Length == 0)
                return Color.clear;
            if (gradient.Keys.Length == 1)
                return gradient.Keys[0].Color;

            time = Wrap(time, gradient.WrapMode);
            for (int index = 0; index < gradient.Keys.Length - 1; index++)
            {
                GradientRuntimeKey left = gradient.Keys[index];
                GradientRuntimeKey right = gradient.Keys[index + 1];
                if (time > right.Time)
                    continue;
                float range = Mathf.Max(right.Time - left.Time, 0.000001f);
                return Color.Lerp(left.Color, right.Color, Mathf.Clamp01((time - left.Time) / range));
            }
            return gradient.Keys[gradient.Keys.Length - 1].Color;
        }

        private static float Wrap(float value, int mode)
        {
            return mode == 1
                ? Mathf.Repeat(value, 1f)
                : mode == 2
                    ? Mathf.PingPong(value, 1f)
                    : Mathf.Clamp01(value);
        }

        private static ControlValue Oscillator2(
            double time, float frequency, float amplitude, float excitation, float phase,
            ControlOperation operation)
        {
            float t = (float)time * Mathf.Max(frequency, 0f) * Mathf.PI * 2f + phase;
            float scale = Mathf.Max(amplitude, 0f) * Mathf.Max(excitation, 0f);
            return new ControlValue(
                Mathf.Sin(t) * scale,
                Mathf.Cos(t + operation.ParameterA) * scale);
        }

        private static ControlValue Oscillator3(
            double time, float frequency, float amplitude, float excitation, float phase,
            ControlOperation operation)
        {
            float t = (float)time * Mathf.Max(frequency, 0f) * Mathf.PI * 2f + phase;
            float scale = Mathf.Max(amplitude, 0f) * Mathf.Max(excitation, 0f);
            return new ControlValue(
                Mathf.Sin(t) * scale,
                Mathf.Sin(t * 2f + operation.ParameterA) * scale,
                Mathf.Cos(t + operation.ParameterB) * scale);
        }

        private static ControlValue Brownian2(
            double time, float amplitude, float frequency, float excitation,
            ControlOperation operation)
        {
            float strength = Mathf.Max(amplitude, 0f) * Mathf.Max(excitation, 0f);
            return new ControlValue(
                FractalNoise1D((float)time * frequency, operation) * strength,
                FractalNoise1D(
                    (float)time * frequency,
                    operation,
                    operation.ParameterA + 17.13f) * strength);
        }

        private static ControlValue Brownian3(
            double time, float amplitude, float frequency, float excitation,
            ControlOperation operation)
        {
            float strength = Mathf.Max(amplitude, 0f) * Mathf.Max(excitation, 0f);
            float t = (float)time * frequency;
            return new ControlValue(
                FractalNoise1D(t, operation) * strength,
                FractalNoise1D(t, operation, operation.ParameterA + 17.13f) * strength,
                FractalNoise1D(t, operation, operation.ParameterA + 41.71f) * strength);
        }

        private static float FractalNoise1D(
            float x,
            ControlOperation operation,
            float seedOverride = float.NaN)
        {
            int octaves = Mathf.Clamp(Mathf.RoundToInt(operation.ParameterB), 1, 8);
            float amplitude = 1f;
            float frequency = 1f;
            float sum = 0f;
            float normalization = 0f;
            float seed = float.IsNaN(seedOverride)
                ? operation.ParameterA
                : seedOverride;
            for (int octave = 0; octave < octaves; octave++)
            {
                sum += Noise1D(x * frequency, seed + octave * 9.37f) * amplitude;
                normalization += amplitude;
                amplitude *= Mathf.Clamp01(operation.ParameterC);
                frequency *= Mathf.Max(operation.ParameterD, 1f);
            }
            return normalization > 0f ? sum / normalization : 0f;
        }

        private static float Noise1D(float x, float seed)
        {
            float left = Mathf.Floor(x);
            float amount = Mathf.SmoothStep(0f, 1f, x - left);
            return Mathf.Lerp(Hash(left + seed), Hash(left + 1f + seed), amount);
        }

        private static float Hash(float value)
        {
            float result = Mathf.Sin(value * 12.9898f + 78.233f) * 43758.5453f;
            return (result - Mathf.Floor(result)) * 2f - 1f;
        }

        private static Vector2 NormalizeSafe(Vector2 value)
        {
            return value.sqrMagnitude > 0.0000001f ? value.normalized : Vector2.right;
        }

        private static Vector3 NormalizeSafe(Vector3 value)
        {
            return value.sqrMagnitude > 0.0000001f ? value.normalized : Vector3.forward;
        }

        private static ControlValue Read(float[] values, int index)
        {
            return (uint)index < (uint)(values?.Length ?? 0) ? values[index] : default;
        }

        private static ControlValue Read(float[] values, byte[] availability, int index)
        {
            if ((uint)index >= (uint)(values?.Length ?? 0))
                return default;
            return availability != null
                   && (uint)index < (uint)availability.Length
                   && availability[index] == 0
                ? default
                : values[index];
        }

        private static float ReadFloat(float[] values, int index)
        {
            return (uint)index < (uint)(values?.Length ?? 0) ? values[index] : 0f;
        }

        private static byte Read(byte[] values, int index)
        {
            return (uint)index < (uint)(values?.Length ?? 0) ? values[index] : (byte)0;
        }

        private static float Remap(float value, float inputMinimum, float inputMaximum,
            float outputMinimum, float outputMaximum)
        {
            float range = inputMaximum - inputMinimum;
            float amount = Mathf.Abs(range) < 0.000001f ? 0f : (value - inputMinimum) / range;
            return Mathf.Lerp(outputMinimum, outputMaximum, amount);
        }
    }
}
