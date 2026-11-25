using System;
using System.Collections.Generic;
using Sonosthesia.Signals;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sonosthesia.Signals.Timeline
{
    [TrackColor(0.82f, 0.54f, 0.22f)]
    [TrackClipType(typeof(ManualControlPlayableAsset))]
    [TrackBindingType(typeof(ControlSignalTarget))]
    public sealed class ManualControlTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(
            PlayableGraph graph,
            GameObject go,
            int inputCount)
        {
            ScriptPlayable<ManualControlMixerBehaviour> mixer =
                ScriptPlayable<ManualControlMixerBehaviour>.Create(graph, inputCount);
            mixer.GetBehaviour().Initialize(GetClips(), inputCount);
            return mixer;
        }
    }

    public sealed class ManualControlMixerBehaviour : PlayableBehaviour
    {
        private ControlSignalProfile _profile;
        private int[] _clipOutputIndices;
        private float[] _values;
        private float[] _weights;
        private byte[] _available;
        private float[] _emptyFloats;
        private byte[] _emptyBytes;
        private ControlSignalTarget _target;
        private double _previousTime = double.NaN;

        public ControlSignalProfile Profile => _profile;

        public void Initialize(IEnumerable<TimelineClip> clips, int inputCount)
        {
            var descriptors = new List<ControlSignalDescriptor>();
            var descriptorIndex = new Dictionary<ControlSignalId, int>();
            _clipOutputIndices = new int[inputCount];
            Array.Fill(_clipOutputIndices, -1);
            int clipIndex = 0;
            foreach (TimelineClip timelineClip in clips)
            {
                var asset = timelineClip.asset as ManualControlPlayableAsset;
                if (asset == null || clipIndex >= _clipOutputIndices.Length)
                {
                    clipIndex++;
                    continue;
                }

                ControlSignalId id = ControlSignalId.FromString(asset.SignalName);
                if (!descriptorIndex.TryGetValue(id, out int outputIndex))
                {
                    outputIndex = descriptors.Count;
                    descriptorIndex.Add(id, outputIndex);
                    descriptors.Add(new ControlSignalDescriptor
                    {
                        Name = asset.SignalName,
                        Type = ControlSignalType.Continuous
                    });
                }
                _clipOutputIndices[clipIndex] = outputIndex;
                clipIndex++;
            }

            _profile = new ControlSignalProfile(
                "Manual Control Curves",
                descriptors.ToArray(),
                Array.Empty<ControlSignalDescriptor>());
            _values = new float[descriptors.Count];
            _weights = new float[descriptors.Count];
            _available = new byte[descriptors.Count];
            _emptyFloats = Array.Empty<float>();
            _emptyBytes = Array.Empty<byte>();
        }

        public override void ProcessFrame(
            Playable playable,
            FrameData info,
            object playerData)
        {
            _target ??= playerData as ControlSignalTarget;
            if (_target == null || _profile == null)
                return;

            Array.Clear(_values, 0, _values.Length);
            Array.Clear(_weights, 0, _weights.Length);
            Array.Clear(_available, 0, _available.Length);

            int inputCount = Math.Min(playable.GetInputCount(), _clipOutputIndices.Length);
            for (int inputIndex = 0; inputIndex < inputCount; inputIndex++)
            {
                float weight = playable.GetInputWeight(inputIndex);
                int outputIndex = _clipOutputIndices[inputIndex];
                if (weight <= 0f || outputIndex < 0)
                    continue;
                var input = (ScriptPlayable<ManualControlBehaviour>)playable.GetInput(inputIndex);
                _values[outputIndex] += input.GetBehaviour().value * weight;
                _weights[outputIndex] += weight;
            }

            for (int index = 0; index < _values.Length; index++)
            {
                if (_weights[index] <= 0f)
                    continue;
                _values[index] /= _weights[index];
                _available[index] = 1;
            }

            double time = playable.GetTime();
            double delta = double.IsNaN(_previousTime) ? info.deltaTime : time - _previousTime;
            ControlPlaybackDirection direction = delta > 0
                ? ControlPlaybackDirection.Forward
                : delta < 0
                    ? ControlPlaybackDirection.Reverse
                    : ControlPlaybackDirection.Stationary;
            bool discontinuous = double.IsNaN(_previousTime)
                                 || Math.Abs(delta) > Math.Max(Math.Abs(info.deltaTime) * 4, 0.05);
            var frame = new ControlSignalFrame(
                _profile,
                time,
                delta,
                discontinuous ? ControlPlaybackDirection.Discontinuous : direction,
                discontinuous,
                _values,
                _available,
                _emptyFloats,
                _emptyFloats,
                _emptyBytes);
            _target.Process(frame);
            _previousTime = time;
        }

        public override void OnGraphStop(Playable playable)
        {
            _target?.ResetEvaluationState();
            _previousTime = double.NaN;
        }
    }
}
