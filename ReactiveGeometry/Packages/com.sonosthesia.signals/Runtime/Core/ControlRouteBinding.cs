using System;
using UnityEngine;

namespace Sonosthesia.Signals
{
    [Serializable]
    public sealed class ControlRouteBinding
    {
        [SerializeField] private string outputName;
        [SerializeField] private Component target;
        [SerializeField] private string slotName;
        [SerializeField, Min(0f)] private float gain = 1f;
        [SerializeField] private float bias;
        [SerializeField] private bool enabled = true;

        internal string OutputName => outputName;
        internal string SlotName => slotName;
        internal Component Target => target;
        internal bool Enabled => enabled && target is IControlSlotTarget;
        internal float Gain => gain;
        internal float Bias => bias;

        public void Configure(
            string graphOutputName,
            Component slotTarget,
            string targetSlotName,
            float routeGain = 1f,
            float routeBias = 0f)
        {
            outputName = graphOutputName;
            target = slotTarget;
            slotName = targetSlotName;
            gain = Math.Max(routeGain, 0f);
            bias = routeBias;
            enabled = true;
        }
    }
}
