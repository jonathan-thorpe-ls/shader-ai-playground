using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sonosthesia.Signals.Xas
{
    [Serializable]
    public sealed class XasPlayableAsset : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private XasAnalysisAsset source;
        [SerializeField] private double sourceTrimSeconds;
        [SerializeField, Min(0.001f)] private double playbackSpeed = 1;
        [SerializeField, Range(0f, 1f)] private float gain = 1f;
        [SerializeField] private bool includeEvents = true;

        public ClipCaps clipCaps => ClipCaps.Blending;
        public XasAnalysisAsset Source => source;
        public float Gain => gain;
        public bool IncludeEvents => includeEvents;

        public void Configure(
            XasAnalysisAsset analysis,
            double trimSeconds = 0,
            double speed = 1,
            float clipGain = 1f,
            bool events = true)
        {
            source = analysis;
            sourceTrimSeconds = trimSeconds;
            playbackSpeed = Math.Max(speed, 0.001);
            gain = Mathf.Clamp01(clipGain);
            includeEvents = events;
        }

        public override double duration =>
            source != null
                ? Math.Max(source.DurationSeconds / playbackSpeed, 0.001)
                : 1;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<XasPlayableBehaviour> playable =
                ScriptPlayable<XasPlayableBehaviour>.Create(graph);
            XasPlayableBehaviour behaviour = playable.GetBehaviour();
            behaviour.Asset = source;
            behaviour.SourceTrimSeconds = sourceTrimSeconds;
            behaviour.PlaybackSpeed = playbackSpeed;
            behaviour.Gain = gain;
            behaviour.IncludeEvents = includeEvents;
            return playable;
        }
    }

    public sealed class XasPlayableBehaviour : PlayableBehaviour
    {
        private XasControlSignalProvider _provider;
        private bool _hasPreviousTime;
        private double _previousTime;

        public XasAnalysisAsset Asset;
        public double SourceTrimSeconds;
        public double PlaybackSpeed = 1;
        public float Gain = 1f;
        public bool IncludeEvents = true;

        public readonly float[] Continuous = new float[XasControlSignalProfile.ContinuousCount];
        public readonly byte[] ContinuousAvailable = new byte[XasControlSignalProfile.ContinuousCount];
        public readonly float[] PulseStrengths = new float[XasControlSignalProfile.PulseCount];
        public readonly float[] PulseMagnitudes = new float[XasControlSignalProfile.PulseCount];
        public readonly byte[] PulseStarted = new byte[XasControlSignalProfile.PulseCount];

        public bool IsValid => Asset != null && Asset.IsValid();

        public bool Sample(Playable playable, bool forward)
        {
            if (!IsValid)
                return false;

            try
            {
                _provider ??= new XasControlSignalProvider(Asset);
                double sourceTime = SourceTrimSeconds + playable.GetTime() * PlaybackSpeed;
                double previous = _hasPreviousTime ? _previousTime : sourceTime;
                bool sampled = _provider.TrySample(
                    sourceTime,
                    previous,
                    forward,
                    Continuous,
                    ContinuousAvailable,
                    PulseStrengths,
                    PulseMagnitudes,
                    PulseStarted);
                if (!IncludeEvents)
                {
                    Array.Clear(PulseStrengths, 0, PulseStrengths.Length);
                    Array.Clear(PulseMagnitudes, 0, PulseMagnitudes.Length);
                    Array.Clear(PulseStarted, 0, PulseStarted.Length);
                }
                _previousTime = sourceTime;
                _hasPreviousTime = true;
                return sampled;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Failed to sample XAS asset '{Asset.DisplayName}': {exception.Message}");
                return false;
            }
        }

        public override void OnGraphStop(Playable playable)
        {
            Reset();
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            _provider?.Dispose();
            _provider = null;
        }

        public void Reset()
        {
            _provider?.ResetEvaluationState();
            _hasPreviousTime = false;
        }
    }
}
