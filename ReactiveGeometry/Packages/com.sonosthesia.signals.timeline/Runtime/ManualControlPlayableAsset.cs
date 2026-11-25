using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sonosthesia.Signals.Timeline
{
    [Serializable]
    public sealed class ManualControlPlayableAsset :
        PlayableAsset,
        ITimelineClipAsset
    {
        [SerializeField] private ManualControlBehaviour template = new();
        [SerializeField] private string signalName = "manual.value";
        [SerializeField, Min(0.001f)] private double clipDuration = 1;

        public ClipCaps clipCaps =>
            ClipCaps.Blending | ClipCaps.Extrapolation | ClipCaps.ClipIn;
        public override double duration => Math.Max(0.001, clipDuration);
        public string SignalName => signalName;

        public void Configure(string name, float initialValue = 0f)
        {
            signalName = string.IsNullOrWhiteSpace(name) ? "manual.value" : name;
            template.value = initialValue;
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<ManualControlBehaviour>.Create(graph, template);
        }
    }
}
