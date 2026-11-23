using System;
using Sonosthesia.Signals;
using Sonosthesia.Xas;

namespace Sonosthesia.Signals.Xas
{
    public sealed class XasControlSignalProvider :
        IControlSignalProvider,
        IDisposable
    {
        private const int EventBufferCapacity = 2048;

        private readonly XasReader _reader;
        private readonly XasPeakEvent[] _events = new XasPeakEvent[EventBufferCapacity];
        private bool _hasSample;
        private double _previousTime;

        public XasControlSignalProvider(XasAnalysisAsset asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));
            _reader = new XasReader(asset.ResolvePath());
        }

        public ControlSignalProfile Profile => XasControlSignalProfile.Instance;
        public double DurationSeconds => _reader.DurationSeconds;
        public int ContinuousChunkCount => _reader.ContinuousChunkCount;
        public int EventChunkCount => _reader.EventChunkCount;

        public bool TrySample(
            double time,
            float[] continuousValues,
            byte[] continuousAvailable,
            float[] pulseStrengths,
            float[] pulseMagnitudes,
            byte[] pulseStarted)
        {
            double previous = _hasSample ? _previousTime : time;
            bool result = TrySample(
                time,
                previous,
                time > previous,
                continuousValues,
                continuousAvailable,
                pulseStrengths,
                pulseMagnitudes,
                pulseStarted);
            if (result)
            {
                _previousTime = time;
                _hasSample = true;
            }
            return result;
        }

        public bool TrySample(
            double time,
            double previousTime,
            bool forward,
            float[] continuousValues,
            byte[] continuousAvailable,
            float[] pulseStrengths,
            float[] pulseMagnitudes,
            byte[] pulseStarted)
        {
            Array.Clear(continuousAvailable, 0, continuousAvailable.Length);
            Array.Clear(pulseStrengths, 0, pulseStrengths.Length);
            Array.Clear(pulseMagnitudes, 0, pulseMagnitudes.Length);
            Array.Clear(pulseStarted, 0, pulseStarted.Length);

            bool continuous = _reader.TryEvaluateContinuous(
                time,
                out XasContinuousFrame frame);
            if (continuous)
            {
                continuousValues[0] = frame.Rms;
                continuousValues[1] = frame.Lows;
                continuousValues[2] = frame.Mids;
                continuousValues[3] = frame.Highs;
                continuousValues[4] = frame.Centroid;
                for (int index = 0; index < XasControlSignalProfile.ContinuousCount; index++)
                    continuousAvailable[index] = 1;
            }

            double rangeStart = Math.Min(previousTime, time);
            double rangeEnd = Math.Max(previousTime, time);
            int eventCount = _reader.GetEventsOverlapping(
                rangeStart,
                rangeEnd,
                _events);
            for (int index = 0; index < eventCount; index++)
            {
                XasPeakEvent peak = _events[index];
                int channel = (int)peak.Channel;
                if ((uint)channel >= (uint)pulseStrengths.Length)
                    continue;

                if (peak.StartSeconds <= time && peak.EndSeconds >= time)
                {
                    pulseStrengths[channel] = Math.Max(pulseStrengths[channel], peak.Strength);
                    pulseMagnitudes[channel] = peak.Magnitude;
                }

                if (forward
                    && peak.StartSeconds > previousTime
                    && peak.StartSeconds <= time)
                {
                    pulseStarted[channel] = 1;
                }
            }

            return continuous || eventCount > 0;
        }

        public void ResetEvaluationState()
        {
            _hasSample = false;
        }

        public void Dispose()
        {
            _reader.Dispose();
        }
    }
}
