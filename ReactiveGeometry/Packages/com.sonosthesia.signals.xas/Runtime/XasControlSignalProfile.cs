using Sonosthesia.Signals;

namespace Sonosthesia.Signals.Xas
{
    public static class XasControlSignalProfile
    {
        public const string Rms = "xas.continuous.rms";
        public const string Lows = "xas.continuous.lows";
        public const string Mids = "xas.continuous.mids";
        public const string Highs = "xas.continuous.highs";
        public const string Centroid = "xas.continuous.centroid";
        public const string PulseMain = "xas.pulse.main";
        public const string PulseLows = "xas.pulse.lows";
        public const string PulseMids = "xas.pulse.mids";
        public const string PulseHighs = "xas.pulse.highs";

        private static readonly ControlSignalProfile Shared = Create();

        public static ControlSignalProfile Instance => Shared;
        public static int ContinuousCount => Shared.ContinuousCount;
        public static int PulseCount => Shared.PulseCount;

        private static ControlSignalProfile Create()
        {
            var continuous = new[]
            {
                Descriptor(Rms, ControlSignalType.Continuous),
                Descriptor(Lows, ControlSignalType.Continuous),
                Descriptor(Mids, ControlSignalType.Continuous),
                Descriptor(Highs, ControlSignalType.Continuous),
                Descriptor(Centroid, ControlSignalType.Continuous)
            };
            var pulses = new[]
            {
                Descriptor(PulseMain, ControlSignalType.Pulse),
                Descriptor(PulseLows, ControlSignalType.Pulse),
                Descriptor(PulseMids, ControlSignalType.Pulse),
                Descriptor(PulseHighs, ControlSignalType.Pulse)
            };
            return new ControlSignalProfile("Sonosthesia XAS v1", continuous, pulses);
        }

        private static ControlSignalDescriptor Descriptor(
            string name,
            ControlSignalType type)
        {
            return new ControlSignalDescriptor
            {
                Name = name,
                Type = type
            };
        }
    }
}
