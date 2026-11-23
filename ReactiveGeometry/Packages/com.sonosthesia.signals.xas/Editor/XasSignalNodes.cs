using System;
using Sonosthesia.Signals.Editor;
using Unity.GraphToolkit.Editor;

namespace Sonosthesia.Signals.Xas.Editor
{
    [Serializable]
    [Node("XAS/Continuous", null, "RMS")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasRmsNode : ContinuousSignalInputNode
    {
        public override string SourceName => XasControlSignalProfile.Rms;
    }

    [Serializable]
    [Node("XAS/Continuous", null, "Lows")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasLowsNode : ContinuousSignalInputNode
    {
        public override string SourceName => XasControlSignalProfile.Lows;
    }

    [Serializable]
    [Node("XAS/Continuous", null, "Mids")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasMidsNode : ContinuousSignalInputNode
    {
        public override string SourceName => XasControlSignalProfile.Mids;
    }

    [Serializable]
    [Node("XAS/Continuous", null, "Highs")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasHighsNode : ContinuousSignalInputNode
    {
        public override string SourceName => XasControlSignalProfile.Highs;
    }

    [Serializable]
    [Node("XAS/Continuous", null, "Centroid")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasCentroidNode : ContinuousSignalInputNode
    {
        public override string SourceName => XasControlSignalProfile.Centroid;
    }

    [Serializable]
    [Node("XAS/Pulses", null, "Main Pulse")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasMainPulseNode : PulseStrengthInputNode
    {
        public override string SourceName => XasControlSignalProfile.PulseMain;
    }

    [Serializable]
    [Node("XAS/Pulses", null, "Lows Pulse")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasLowsPulseNode : PulseStrengthInputNode
    {
        public override string SourceName => XasControlSignalProfile.PulseLows;
    }

    [Serializable]
    [Node("XAS/Pulses", null, "Mids Pulse")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasMidsPulseNode : PulseStrengthInputNode
    {
        public override string SourceName => XasControlSignalProfile.PulseMids;
    }

    [Serializable]
    [Node("XAS/Pulses", null, "Highs Pulse")]
    [UseWithGraph(typeof(ControlSignalGraph))]
    public sealed class XasHighsPulseNode : PulseStrengthInputNode
    {
        public override string SourceName => XasControlSignalProfile.PulseHighs;
    }
}
