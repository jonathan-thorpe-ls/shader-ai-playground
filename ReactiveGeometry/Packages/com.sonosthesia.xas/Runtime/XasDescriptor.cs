using System;

namespace Sonosthesia.Xas
{
    [Serializable]
    public sealed class XasDescriptor
    {
        public string format;
        public int version;
        public int sampleRate;
        public int analysisHop;
        public double duration;
        public double timelineOriginSeconds;
        public XasAnalysisInfo info;
        public XasStreamDescriptor[] streams;
    }

    [Serializable]
    public sealed class XasStreamDescriptor
    {
        public int id;
        public string kind;
        public string name;
        public long firstTick;
        public int periodTicks;
        public string interpolation;
        public string encoding;
        public XasComponentDescriptor[] components;
        public XasChannelDescriptor[] channels;
    }

    [Serializable]
    public sealed class XasComponentDescriptor
    {
        public string name;
        public string unit;
        public string codec;
    }

    [Serializable]
    public sealed class XasChannelDescriptor
    {
        public int id;
        public string name;
    }

    [Serializable]
    public sealed class XasAnalysisInfo
    {
        public double duration;
        public XasBandInfo main;
        public XasBandInfo lows;
        public XasBandInfo mids;
        public XasBandInfo highs;
        public XasRange centroid;
    }

    [Serializable]
    public sealed class XasBandInfo
    {
        public XasRange band;
        public XasRange magnitude;
        public int peaks;
    }

    [Serializable]
    public sealed class XasRange
    {
        public double lower;
        public double upper;
    }
}
