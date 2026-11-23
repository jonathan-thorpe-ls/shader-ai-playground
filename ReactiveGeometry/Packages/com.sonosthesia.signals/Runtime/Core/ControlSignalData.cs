using System;
using UnityEngine;

namespace Sonosthesia.Signals
{
    public enum ControlSignalType
    {
        Continuous = 0,
        Pulse = 1
    }

    public enum ControlPlaybackDirection : byte
    {
        Forward = 0,
        Reverse = 1,
        Stationary = 2,
        Discontinuous = 3
    }

    [Serializable]
    public readonly struct ControlSignalId : IEquatable<ControlSignalId>
    {
        public readonly long Value;

        public ControlSignalId(long value)
        {
            Value = value;
        }

        public static ControlSignalId FromString(string name)
        {
            if (string.IsNullOrEmpty(name))
                return default;

            const long offsetBasis = unchecked((long)0xcbf29ce484222325);
            const long prime = 0x100000001b3;
            long hash = offsetBasis;
            for (int index = 0; index < name.Length; index++)
            {
                hash ^= name[index];
                hash *= prime;
            }
            return new ControlSignalId(hash);
        }

        public bool Equals(ControlSignalId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ControlSignalId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ControlSignalId left, ControlSignalId right) => left.Equals(right);
        public static bool operator !=(ControlSignalId left, ControlSignalId right) => !left.Equals(right);
    }

    [Serializable]
    public struct ControlSignalDescriptor
    {
        public string Name;
        public ControlSignalType Type;

        public ControlSignalId Id => ControlSignalId.FromString(Name);
    }

    [Serializable]
    public sealed class ControlSignalProfile
    {
        [SerializeField] private string displayName;
        [SerializeField] private ControlSignalDescriptor[] continuous = Array.Empty<ControlSignalDescriptor>();
        [SerializeField] private ControlSignalDescriptor[] pulses = Array.Empty<ControlSignalDescriptor>();

        private int[] _continuousHashes;
        private int[] _pulseHashes;

        public ControlSignalProfile()
        {
        }

        public ControlSignalProfile(
            string displayName,
            ControlSignalDescriptor[] continuous,
            ControlSignalDescriptor[] pulses)
        {
            this.displayName = displayName;
            this.continuous = continuous ?? Array.Empty<ControlSignalDescriptor>();
            this.pulses = pulses ?? Array.Empty<ControlSignalDescriptor>();
            RebuildCache();
        }

        public string DisplayName => displayName;
        public int ContinuousCount => continuous?.Length ?? 0;
        public int PulseCount => pulses?.Length ?? 0;

        public ControlSignalDescriptor GetContinuous(int index) => continuous[index];
        public ControlSignalDescriptor GetPulse(int index) => pulses[index];

        public bool TryGetContinuousIndex(ControlSignalId id, out int index)
        {
            return TryFind(continuous, id, out index);
        }

        public bool TryGetPulseIndex(ControlSignalId id, out int index)
        {
            return TryFind(pulses, id, out index);
        }

        public void RebuildCache()
        {
            _continuousHashes = BuildHashes(continuous);
            _pulseHashes = BuildHashes(pulses);
        }

        private static int[] BuildHashes(ControlSignalDescriptor[] descriptors)
        {
            var hashes = new int[descriptors?.Length ?? 0];
            for (int index = 0; index < hashes.Length; index++)
                hashes[index] = descriptors[index].Id.GetHashCode();
            return hashes;
        }

        private static bool TryFind(
            ControlSignalDescriptor[] descriptors,
            ControlSignalId id,
            out int index)
        {
            for (index = 0; index < (descriptors?.Length ?? 0); index++)
            {
                if (descriptors[index].Id == id)
                    return true;
            }
            index = -1;
            return false;
        }
    }

    public readonly struct ControlSignalFrame
    {
        public readonly ControlSignalProfile Profile;
        public readonly double TimelineTime;
        public readonly double DeltaTime;
        public readonly ControlPlaybackDirection Direction;
        public readonly bool IsDiscontinuous;
        public readonly float[] ContinuousValues;
        public readonly byte[] ContinuousAvailable;
        public readonly float[] PulseStrengths;
        public readonly float[] PulseMagnitudes;
        public readonly byte[] PulseStarted;

        public ControlSignalFrame(
            ControlSignalProfile profile,
            double timelineTime,
            double deltaTime,
            ControlPlaybackDirection direction,
            bool isDiscontinuous,
            float[] continuousValues,
            byte[] continuousAvailable,
            float[] pulseStrengths,
            float[] pulseMagnitudes,
            byte[] pulseStarted)
        {
            Profile = profile;
            TimelineTime = timelineTime;
            DeltaTime = deltaTime;
            Direction = direction;
            IsDiscontinuous = isDiscontinuous;
            ContinuousValues = continuousValues;
            ContinuousAvailable = continuousAvailable;
            PulseStrengths = pulseStrengths;
            PulseMagnitudes = pulseMagnitudes;
            PulseStarted = pulseStarted;
        }
    }

    public readonly struct ControlPulse
    {
        public readonly float Strength;
        public readonly float Magnitude;
        public readonly bool Started;

        public ControlPulse(float strength, float magnitude, bool started)
        {
            Strength = strength;
            Magnitude = magnitude;
            Started = started;
        }
    }

    public interface IControlSignalProvider
    {
        ControlSignalProfile Profile { get; }
        bool TrySample(
            double time,
            float[] continuousValues,
            byte[] continuousAvailable,
            float[] pulseStrengths,
            float[] pulseMagnitudes,
            byte[] pulseStarted);
        void ResetEvaluationState();
    }

    public interface IControlSlotTarget
    {
        int ControlSlotCount { get; }
        bool TryGetControlSlot(ControlSignalId id, out int slotHandle);
        ControlSignalType GetControlSlotType(int slotHandle);
        void WriteFloat(int slotHandle, float value);
        void WritePulse(int slotHandle, in ControlPulse pulse);
    }
}
