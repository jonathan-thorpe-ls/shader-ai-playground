using System;
using UnityEngine;

namespace Sonosthesia.Signals
{
    public enum ControlOperationKind : byte
    {
        ContinuousSource = 0,
        Constant = 1,
        Add = 2,
        Multiply = 3,
        Clamp = 4,
        Remap = 5,
        Smoothstep = 6,
        LowPass = 7,
        AttackRelease = 8,
        Slew = 9,
        Delay = 10,
        Subtract = 11,
        Divide = 12,
        Invert = 13,
        Minimum = 14,
        Maximum = 15,
        Mix = 16,
        Threshold = 17,
        SampleAndHold = 18,
        PulseStrengthSource = 20,
        PulseMagnitudeSource = 21,
        PulseStartedSource = 22,
        PulseDecay = 23,
        PulseMerge = 24
    }

    [Serializable]
    public struct ControlOperation
    {
        public ControlOperationKind Kind;
        public int InputA;
        public int InputB;
        public int SourceIndex;
        public int StateOffset;
        public int DelayCapacity;
        public float ParameterA;
        public float ParameterB;
        public float ParameterC;
        public float ParameterD;
    }

    [Serializable]
    public struct ControlGraphOutput
    {
        public string Name;
        public long Id;
        public int OperationIndex;
        public ControlSignalType Type;
        public float Minimum;
        public float Maximum;
    }

    public sealed class CompiledControlGraph : ScriptableObject
    {
        [SerializeField] private ControlSignalProfile profile;
        [SerializeField] private ControlOperation[] operations = Array.Empty<ControlOperation>();
        [SerializeField] private ControlGraphOutput[] outputs = Array.Empty<ControlGraphOutput>();
        [SerializeField] private int floatStateCount;
        [SerializeField] private int integerStateCount;

        public ControlSignalProfile Profile => profile;
        public int OperationCount => operations.Length;
        public int OutputCount => outputs.Length;
        public int FloatStateCount => floatStateCount;
        public int IntegerStateCount => integerStateCount;

        public void Configure(
            ControlSignalProfile sourceProfile,
            ControlOperation[] compiledOperations,
            ControlGraphOutput[] compiledOutputs,
            int compiledFloatStateCount,
            int compiledIntegerStateCount)
        {
            profile = sourceProfile ?? throw new ArgumentNullException(nameof(sourceProfile));
            operations = compiledOperations ?? Array.Empty<ControlOperation>();
            outputs = compiledOutputs ?? Array.Empty<ControlGraphOutput>();
            floatStateCount = Math.Max(0, compiledFloatStateCount);
            integerStateCount = Math.Max(0, compiledIntegerStateCount);
        }

        public ControlOperation GetOperation(int index) => operations[index];
        public ControlGraphOutput GetOutput(int index) => outputs[index];

        public bool TryGetOutputIndex(ControlSignalId id, out int index)
        {
            for (index = 0; index < outputs.Length; index++)
            {
                if (outputs[index].Id == id.Value)
                    return true;
            }
            index = -1;
            return false;
        }
    }
}
