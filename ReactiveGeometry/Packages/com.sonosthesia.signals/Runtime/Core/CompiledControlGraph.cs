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
        PulseMerge = 24,
        Vector2Constant = 30,
        Vector2Add = 31,
        Vector2Subtract = 32,
        Vector2Scale = 33,
        Vector2Normalize = 34,
        Vector2Lerp = 35,
        Vector2Length = 36,
        Vector2FromScalars = 37,
        Polar = 38,
        Vector3Constant = 50,
        Vector3Add = 51,
        Vector3Subtract = 52,
        Vector3Scale = 53,
        Vector3Normalize = 54,
        Vector3Lerp = 55,
        Vector3Length = 56,
        Vector3FromScalars = 57,
        Cylindrical = 58,
        Spherical = 59,
        DirectionWithLength = 60,
        Vector2Oscillator = 61,
        Vector3Oscillator = 62,
        Vector2Brownian = 63,
        Vector3Brownian = 64,
        ColorConstant = 70,
        ColorLerp = 71,
        ColorMultiplyScalar = 72,
        ColorMultiply = 73,
        ColorToHsv = 74,
        HsvToColor = 75,
        Gradient = 76,
        Palette = 77,
        ColorTint = 78
    }

    [Serializable]
    public struct ControlOperation
    {
        public ControlOperationKind Kind;
        public ControlSignalType OutputType;
        public int InputA;
        public int InputB;
        public int InputC;
        public int InputD;
        public int SourceIndex;
        public int StateOffset;
        public int StateComponentCount;
        public int DelayCapacity;
        public float ParameterA;
        public float ParameterB;
        public float ParameterC;
        public float ParameterD;
        public Color ParameterColorA;
        public Color ParameterColorB;
        public Color[] Palette;
        public GradientRuntimeData Gradient;
    }

    [Serializable]
    public struct GradientRuntimeKey
    {
        public float Time;
        public Color Color;
    }

    [Serializable]
    public sealed class GradientRuntimeData
    {
        public GradientRuntimeKey[] Keys = Array.Empty<GradientRuntimeKey>();
        public int WrapMode;
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
        private int totalStateComponentCount;

        public ControlSignalProfile Profile => profile;
        public int OperationCount => operations.Length;
        public int OutputCount => outputs.Length;
        public int FloatStateCount => floatStateCount;
        public int IntegerStateCount => integerStateCount;
        public int TotalStateComponentCount => totalStateComponentCount;

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
            totalStateComponentCount = 0;
            for (int index = 0; index < operations.Length; index++)
            {
                if (operations[index].StateComponentCount <= 0
                    && (operations[index].Kind == ControlOperationKind.LowPass
                        || operations[index].Kind == ControlOperationKind.AttackRelease
                        || operations[index].Kind == ControlOperationKind.Slew
                        || operations[index].Kind == ControlOperationKind.Threshold
                        || operations[index].Kind == ControlOperationKind.SampleAndHold
                        || operations[index].Kind == ControlOperationKind.PulseDecay))
                {
                    operations[index].StateComponentCount = 1;
                }
                if (operations[index].StateComponentCount <= 0
                    && operations[index].Kind == ControlOperationKind.Delay)
                {
                    operations[index].StateComponentCount =
                        Math.Max(2, operations[index].DelayCapacity);
                }
                totalStateComponentCount = Math.Max(
                    totalStateComponentCount,
                    operations[index].StateOffset + operations[index].StateComponentCount);
            }
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
