using System;

namespace Sonosthesia.Xas
{
    public enum XasPeakChannel : byte
    {
        Main = 0,
        Lows = 1,
        Mids = 2,
        Highs = 3
    }

    public readonly struct XasContinuousFrame
    {
        public readonly float Rms;
        public readonly float Lows;
        public readonly float Mids;
        public readonly float Highs;
        public readonly float Centroid;

        public XasContinuousFrame(
            float rms,
            float lows,
            float mids,
            float highs,
            float centroid)
        {
            Rms = rms;
            Lows = lows;
            Mids = mids;
            Highs = highs;
            Centroid = centroid;
        }

        public static XasContinuousFrame Lerp(
            XasContinuousFrame left,
            XasContinuousFrame right,
            float amount)
        {
            return new XasContinuousFrame(
                left.Rms + (right.Rms - left.Rms) * amount,
                left.Lows + (right.Lows - left.Lows) * amount,
                left.Mids + (right.Mids - left.Mids) * amount,
                left.Highs + (right.Highs - left.Highs) * amount,
                left.Centroid + (right.Centroid - left.Centroid) * amount);
        }
    }

    public readonly struct XasPeakEvent
    {
        public readonly XasPeakChannel Channel;
        public readonly double StartSeconds;
        public readonly double DurationSeconds;
        public readonly float Magnitude;
        public readonly float Strength;

        public double EndSeconds => StartSeconds + DurationSeconds;

        public XasPeakEvent(
            XasPeakChannel channel,
            double startSeconds,
            double durationSeconds,
            float magnitude,
            float strength)
        {
            Channel = channel;
            StartSeconds = startSeconds;
            DurationSeconds = durationSeconds;
            Magnitude = magnitude;
            Strength = strength;
        }
    }

    public sealed class XasReaderOptions
    {
        public static XasReaderOptions Default => new XasReaderOptions();

        public bool VerifyCrc { get; set; } = true;
        public int CacheChunksPerStream { get; set; } = 2;
        public int MaxDescriptorBytes { get; set; } = 1024 * 1024;
        public int MaxChunkPayloadBytes { get; set; } = 16 * 1024 * 1024;
        public int MaxIndexEntries { get; set; } = 1_000_000;
    }
}
