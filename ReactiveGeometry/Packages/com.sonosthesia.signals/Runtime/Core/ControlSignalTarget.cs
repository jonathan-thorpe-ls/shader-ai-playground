using System;
using UnityEngine;

namespace Sonosthesia.Signals
{
    [DisallowMultipleComponent]
    public sealed class ControlSignalTarget : MonoBehaviour
    {
        [SerializeField] private CompiledControlGraph graph;
        [SerializeField] private ControlRouteBinding[] routes = Array.Empty<ControlRouteBinding>();

        private ControlGraphExecutor _executor;
        private ResolvedRoute[] _resolvedRoutes;
        private float[] _continuousInput;
        private byte[] _continuousAvailable;
        private float[] _pulseStrengths;
        private float[] _pulseMagnitudes;
        private byte[] _pulseStarted;
        private ControlSignalFrame _lastFrame;
        private bool _hasFrame;
        private bool _bindingsValid;

        public CompiledControlGraph Graph => graph;
        public int ResolvedRouteCount => _resolvedRoutes?.Length ?? 0;
        public int EvaluationCount { get; private set; }
        public bool HasFrame => _hasFrame;

        public void BindGraph(CompiledControlGraph compiledGraph)
        {
            graph = compiledGraph;
            Rebuild();
        }

        public void SetRoutes(ControlRouteBinding[] value)
        {
            routes = value ?? Array.Empty<ControlRouteBinding>();
            Rebuild();
        }

        public void Process(in ControlSignalFrame frame)
        {
            if (graph == null)
                throw new InvalidOperationException("ControlSignalTarget has no compiled graph.");
            if (_executor == null || !_bindingsValid)
                Rebuild();

            BuildExecutorFrame(frame, out ControlSignalFrame executorFrame);
            _lastFrame = executorFrame;
            _hasFrame = true;
            _executor.Evaluate(executorFrame);
            EvaluationCount++;
            WriteRoutes();
        }

        public void ResetEvaluationState()
        {
            _executor?.Reset();
            _hasFrame = false;
        }

        public bool TryGetRawContinuous(ControlSignalId id, out float value)
        {
            value = 0f;
            if (!_hasFrame
                || graph?.Profile == null
                || !graph.Profile.TryGetContinuousIndex(id, out int index)
                || (uint)index >= (uint)_lastFrame.ContinuousValues.Length)
            {
                return false;
            }

            if ((uint)index < (uint)_lastFrame.ContinuousAvailable.Length
                && _lastFrame.ContinuousAvailable[index] == 0)
            {
                return false;
            }

            value = _lastFrame.ContinuousValues[index];
            return true;
        }

        public bool TryGetRawPulse(ControlSignalId id, out ControlPulse pulse)
        {
            pulse = default;
            if (!_hasFrame
                || graph?.Profile == null
                || !graph.Profile.TryGetPulseIndex(id, out int index)
                || (uint)index >= (uint)_lastFrame.PulseStrengths.Length)
            {
                return false;
            }

            pulse = new ControlPulse(
                _lastFrame.PulseStrengths[index],
                (uint)index < (uint)_lastFrame.PulseMagnitudes.Length
                    ? _lastFrame.PulseMagnitudes[index]
                    : 0f,
                (uint)index < (uint)_lastFrame.PulseStarted.Length
                && _lastFrame.PulseStarted[index] != 0);
            return true;
        }

        public bool TryGetOutput(int outputIndex, out float value, out ControlPulse pulse)
        {
            if (_executor == null)
            {
                value = 0f;
                pulse = default;
                return false;
            }

            return _executor.TryGetOutput(outputIndex, out value, out pulse);
        }

        public bool TryGetOutput(ControlSignalId id, out float value, out ControlPulse pulse)
        {
            if (graph == null
                || !graph.TryGetOutputIndex(id, out int outputIndex))
            {
                value = 0f;
                pulse = default;
                return false;
            }

            return TryGetOutput(outputIndex, out value, out pulse);
        }

        public bool TryGetOutput(int outputIndex, out Vector2 value)
        {
            if (_executor == null)
            {
                value = default;
                return false;
            }
            return _executor.TryGetOutput(outputIndex, out value);
        }

        public bool TryGetOutput(int outputIndex, out Vector3 value)
        {
            if (_executor == null)
            {
                value = default;
                return false;
            }
            return _executor.TryGetOutput(outputIndex, out value);
        }

        public bool TryGetOutput(int outputIndex, out Color value)
        {
            if (_executor == null)
            {
                value = default;
                return false;
            }
            return _executor.TryGetOutput(outputIndex, out value);
        }

        public bool TryGetOutput(ControlSignalId id, out Vector2 value) =>
            TryGetTypedOutput(id, out value);

        public bool TryGetOutput(ControlSignalId id, out Vector3 value) =>
            TryGetTypedOutput(id, out value);

        public bool TryGetOutput(ControlSignalId id, out Color value) =>
            TryGetTypedOutput(id, out value);

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            _executor = null;
            _resolvedRoutes = null;
            _bindingsValid = false;
            _hasFrame = false;
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled)
                Rebuild();
        }

        private void Rebuild()
        {
            _bindingsValid = false;
            _resolvedRoutes = Array.Empty<ResolvedRoute>();
            if (graph == null)
            {
                _executor = null;
                return;
            }

            _executor = new ControlGraphExecutor(graph);
            int continuousCount = graph.Profile?.ContinuousCount ?? 0;
            int pulseCount = graph.Profile?.PulseCount ?? 0;
            _continuousInput = new float[continuousCount];
            _continuousAvailable = new byte[continuousCount];
            _pulseStrengths = new float[pulseCount];
            _pulseMagnitudes = new float[pulseCount];
            _pulseStarted = new byte[pulseCount];
            Array.Clear(_continuousInput, 0, _continuousInput.Length);
            Array.Clear(_continuousAvailable, 0, _continuousAvailable.Length);
            Array.Clear(_pulseStrengths, 0, _pulseStrengths.Length);
            Array.Clear(_pulseMagnitudes, 0, _pulseMagnitudes.Length);
            Array.Clear(_pulseStarted, 0, _pulseStarted.Length);
            int routeCapacity = routes?.Length ?? 0;
            if (routeCapacity == 0)
            {
                _bindingsValid = true;
                return;
            }

            var resolved = new ResolvedRoute[routeCapacity];
            int count = 0;
            for (int index = 0; index < routeCapacity; index++)
            {
                ControlRouteBinding binding = routes[index];
                if (binding == null || !binding.Enabled)
                    continue;

                ControlSignalId outputId = ControlSignalId.FromString(binding.OutputName);
                if (!graph.TryGetOutputIndex(outputId, out int outputIndex))
                {
                    Debug.LogError(
                        $"Control graph has no output named '{binding.OutputName}'.",
                        this);
                    continue;
                }

                var target = binding.Target as IControlSlotTarget;
                if (target == null
                    || !target.TryGetControlSlot(
                        ControlSignalId.FromString(binding.SlotName),
                        out int slotHandle))
                {
                    Debug.LogError(
                        $"Control slot '{binding.SlotName}' is unavailable on " +
                        $"{binding.Target?.name ?? "the route target"}.",
                        this);
                    continue;
                }

                ControlGraphOutput output = graph.GetOutput(outputIndex);
                ControlSignalType targetType = target.GetControlSlotType(slotHandle);
                if (output.Type != targetType)
                {
                    Debug.LogError(
                        $"Control route type mismatch from '{binding.OutputName}' to " +
                        $"'{binding.SlotName}'.",
                        this);
                    continue;
                }
                if ((output.Type == ControlSignalType.Vector2
                     && target is not IControlVector2SlotTarget)
                    || (output.Type == ControlSignalType.Vector3
                        && target is not IControlVector3SlotTarget)
                    || (output.Type == ControlSignalType.Color
                        && target is not IControlColorSlotTarget))
                {
                    Debug.LogError(
                        $"Control target for '{binding.SlotName}' does not implement " +
                        $"the required {output.Type} writer.",
                        this);
                    continue;
                }

                resolved[count++] = new ResolvedRoute(
                    target,
                    slotHandle,
                    outputIndex,
                    output.Type,
                    binding.Gain,
                    binding.Bias);
            }

            _resolvedRoutes = count == resolved.Length
                ? resolved
                : TrimRoutes(resolved, count);
            _bindingsValid = true;
        }

        private void WriteRoutes()
        {
            for (int index = 0; index < _resolvedRoutes.Length; index++)
            {
                ResolvedRoute route = _resolvedRoutes[index];
                if (route.Type == ControlSignalType.Continuous
                    || route.Type == ControlSignalType.Pulse)
                {
                    if (!_executor.TryGetOutput(route.OutputIndex, out float value, out ControlPulse pulse))
                        continue;
                    if (route.Type == ControlSignalType.Continuous)
                    {
                        route.Target.WriteFloat(
                            route.SlotHandle,
                            value * route.Gain + route.Bias);
                    }
                    else
                    {
                        var routedPulse = new ControlPulse(
                            pulse.Strength * route.Gain + route.Bias,
                            pulse.Magnitude * route.Gain + route.Bias,
                            pulse.Started);
                        route.Target.WritePulse(route.SlotHandle, routedPulse);
                    }
                }
                else if (route.Type == ControlSignalType.Vector2)
                {
                    if (_executor.TryGetOutput(route.OutputIndex, out Vector2 value)
                        && route.Target is IControlVector2SlotTarget target)
                    {
                        target.WriteVector2(
                            route.SlotHandle,
                            value * route.Gain + Vector2.one * route.Bias);
                    }
                }
                else if (route.Type == ControlSignalType.Vector3)
                {
                    if (_executor.TryGetOutput(route.OutputIndex, out Vector3 value)
                        && route.Target is IControlVector3SlotTarget target)
                    {
                        target.WriteVector3(
                            route.SlotHandle,
                            value * route.Gain + Vector3.one * route.Bias);
                    }
                }
                else if (route.Type == ControlSignalType.Color)
                {
                    if (_executor.TryGetOutput(route.OutputIndex, out Color value)
                        && route.Target is IControlColorSlotTarget target)
                    {
                        value = new Color(
                            value.r * route.Gain + route.Bias,
                            value.g * route.Gain + route.Bias,
                            value.b * route.Gain + route.Bias,
                            value.a);
                        target.WriteColor(route.SlotHandle, value);
                    }
                }
            }
        }

        private bool TryGetTypedOutput(ControlSignalId id, out Vector2 value)
        {
            value = default;
            return graph != null
                   && graph.TryGetOutputIndex(id, out int index)
                   && TryGetOutput(index, out value);
        }

        private bool TryGetTypedOutput(ControlSignalId id, out Vector3 value)
        {
            value = default;
            return graph != null
                   && graph.TryGetOutputIndex(id, out int index)
                   && TryGetOutput(index, out value);
        }

        private bool TryGetTypedOutput(ControlSignalId id, out Color value)
        {
            value = default;
            return graph != null
                   && graph.TryGetOutputIndex(id, out int index)
                   && TryGetOutput(index, out value);
        }

        private void BuildExecutorFrame(
            in ControlSignalFrame source,
            out ControlSignalFrame executorFrame)
        {
            if (source.Profile != null)
            {
                for (int index = 0; index < graph.Profile.ContinuousCount; index++)
                {
                    ControlSignalDescriptor descriptor = graph.Profile.GetContinuous(index);
                    if (!source.Profile.TryGetContinuousIndex(descriptor.Id, out int sourceIndex)
                        || sourceIndex < 0
                        || (uint)sourceIndex >= (uint)(source.ContinuousValues?.Length ?? 0))
                    {
                        continue;
                    }

                    _continuousInput[index] = source.ContinuousValues[sourceIndex];
                    _continuousAvailable[index] =
                        sourceIndex < (source.ContinuousAvailable?.Length ?? 0)
                            ? source.ContinuousAvailable[sourceIndex]
                            : (byte)1;
                }

                for (int index = 0; index < graph.Profile.PulseCount; index++)
                {
                    ControlSignalDescriptor descriptor = graph.Profile.GetPulse(index);
                    if (!source.Profile.TryGetPulseIndex(descriptor.Id, out int sourceIndex)
                        || sourceIndex < 0)
                        continue;
                    if (sourceIndex < (source.PulseStrengths?.Length ?? 0))
                        _pulseStrengths[index] = source.PulseStrengths[sourceIndex];
                    if (sourceIndex < (source.PulseMagnitudes?.Length ?? 0))
                        _pulseMagnitudes[index] = source.PulseMagnitudes[sourceIndex];
                    if (sourceIndex < (source.PulseStarted?.Length ?? 0))
                        _pulseStarted[index] = source.PulseStarted[sourceIndex];
                }
            }

            executorFrame = new ControlSignalFrame(
                graph.Profile,
                source.TimelineTime,
                source.DeltaTime,
                source.Direction,
                source.IsDiscontinuous,
                _continuousInput,
                _continuousAvailable,
                _pulseStrengths,
                _pulseMagnitudes,
                _pulseStarted);
        }

        private static ResolvedRoute[] TrimRoutes(ResolvedRoute[] routes, int count)
        {
            var result = new ResolvedRoute[count];
            Array.Copy(routes, result, count);
            return result;
        }

        private readonly struct ResolvedRoute
        {
            public readonly IControlSlotTarget Target;
            public readonly int SlotHandle;
            public readonly int OutputIndex;
            public readonly ControlSignalType Type;
            public readonly float Gain;
            public readonly float Bias;

            public ResolvedRoute(
                IControlSlotTarget target,
                int slotHandle,
                int outputIndex,
                ControlSignalType type,
                float gain,
                float bias)
            {
                Target = target;
                SlotHandle = slotHandle;
                OutputIndex = outputIndex;
                Type = type;
                Gain = gain;
                Bias = bias;
            }
        }
    }
}
