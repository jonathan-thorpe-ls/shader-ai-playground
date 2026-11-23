using System;
using Sonosthesia.Signals;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sonosthesia.Signals.Xas
{
    [TrackColor(0.18f, 0.62f, 0.88f)]
    [TrackClipType(typeof(XasPlayableAsset))]
    [TrackBindingType(typeof(ControlSignalTarget))]
    public sealed class XasTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(
            PlayableGraph graph,
            GameObject go,
            int inputCount)
        {
            ScriptPlayable<XasTrackMixerBehaviour> mixer =
                ScriptPlayable<XasTrackMixerBehaviour>.Create(graph, inputCount);
            return mixer;
        }
    }

    public sealed class XasTrackMixerBehaviour : PlayableBehaviour
    {
        private readonly float[] _continuous =
            new float[XasControlSignalProfile.ContinuousCount];
        private readonly byte[] _available =
            new byte[XasControlSignalProfile.ContinuousCount];
        private readonly float[] _continuousWeights =
            new float[XasControlSignalProfile.ContinuousCount];
        private readonly float[] _pulseStrengths =
            new float[XasControlSignalProfile.PulseCount];
        private readonly float[] _pulseMagnitudes =
            new float[XasControlSignalProfile.PulseCount];
        private readonly byte[] _pulseStarted =
            new byte[XasControlSignalProfile.PulseCount];

        private double _previousTime = double.NaN;
        private ControlPlaybackDirection _previousDirection =
            ControlPlaybackDirection.Stationary;
        private ControlSignalTarget _target;

        public override void ProcessFrame(
            Playable playable,
            FrameData info,
            object playerData)
        {
            _target ??= playerData as ControlSignalTarget;
            if (_target == null)
                return;

            XasSignalBlend.ResetContinuous(
                _continuous,
                _continuousWeights,
                _available);
            Array.Clear(_pulseStrengths, 0, _pulseStrengths.Length);
            Array.Clear(_pulseMagnitudes, 0, _pulseMagnitudes.Length);
            Array.Clear(_pulseStarted, 0, _pulseStarted.Length);

            double time = playable.GetTime();
            double delta = double.IsNaN(_previousTime) ? info.deltaTime : time - _previousTime;
            ControlPlaybackDirection direction = delta > 0
                ? ControlPlaybackDirection.Forward
                : delta < 0
                    ? ControlPlaybackDirection.Reverse
                    : ControlPlaybackDirection.Stationary;
            bool discontinuous = double.IsNaN(_previousTime)
                                 || Math.Abs(delta) > Math.Max(
                                     Math.Abs(info.deltaTime) * 4,
                                     0.05)
                                 || (_previousDirection == ControlPlaybackDirection.Forward
                                     && direction == ControlPlaybackDirection.Reverse)
                                 || (_previousDirection == ControlPlaybackDirection.Reverse
                                     && direction == ControlPlaybackDirection.Forward);

            for (int inputIndex = 0; inputIndex < playable.GetInputCount(); inputIndex++)
            {
                float weight = playable.GetInputWeight(inputIndex);
                if (weight <= 0f)
                    continue;

                Playable input = playable.GetInput(inputIndex);
                var scriptPlayable = (ScriptPlayable<XasPlayableBehaviour>)input;
                XasPlayableBehaviour behaviour = scriptPlayable.GetBehaviour();
                if (behaviour == null
                    || !behaviour.Sample(
                        input,
                        direction == ControlPlaybackDirection.Forward && !discontinuous))
                {
                    continue;
                }

                float effectiveWeight = weight * behaviour.Gain;
                XasSignalBlend.AddContinuous(
                    behaviour.Continuous,
                    behaviour.ContinuousAvailable,
                    effectiveWeight,
                    _continuous,
                    _continuousWeights,
                    _continuous.Length);
                XasSignalBlend.AddPulses(
                    behaviour.PulseStrengths,
                    behaviour.PulseMagnitudes,
                    behaviour.PulseStarted,
                    effectiveWeight,
                    _pulseStrengths,
                    _pulseMagnitudes,
                    _pulseStarted,
                    _pulseStrengths.Length);
            }

            XasSignalBlend.FinishContinuous(
                _continuous,
                _continuousWeights,
                _available,
                _continuous.Length);

            var frame = new ControlSignalFrame(
                XasControlSignalProfile.Instance,
                time,
                delta,
                discontinuous ? ControlPlaybackDirection.Discontinuous : direction,
                discontinuous,
                _continuous,
                _available,
                _pulseStrengths,
                _pulseMagnitudes,
                _pulseStarted);
            _target.Process(frame);
            _previousTime = time;
            _previousDirection = direction;
        }

        public override void OnGraphStop(Playable playable)
        {
            _target?.ResetEvaluationState();
            _previousTime = double.NaN;
            _previousDirection = ControlPlaybackDirection.Stationary;
        }
    }
}
