using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Sonosthesia.Signals.Editor
{
    public abstract class PulsePort
    {
    }

    public interface IControlRuntimeNode
    {
        ControlOperationKind Kind { get; }
        ControlSignalType OutputType { get; }
        int FloatStateCount { get; }
        int ControlStateComponentCount { get; }
        bool UsesIntegerState { get; }
        IEnumerable<IPort> GetRequiredInputs();
        ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset);
        ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int inputC,
            int inputD,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset);
        string SourceName { get; }
        ControlSignalType SourceType { get; }
    }

    [Serializable]
    public abstract class ControlRuntimeNode : Node, IControlRuntimeNode
    {
        internal const string InputAName = "A";
        internal const string InputBName = "B";
        internal const string OutputName = "Value";

        public abstract ControlOperationKind Kind { get; }
        public virtual ControlSignalType OutputType => ControlSignalType.Continuous;
        public virtual int FloatStateCount => 0;
        public virtual int ControlStateComponentCount =>
            Kind == ControlOperationKind.Delay
                ? FloatStateCount
                : FloatStateCount > 0 ? 1 : 0;
        public virtual bool UsesIntegerState => false;
        public virtual string SourceName => null;
        public virtual ControlSignalType SourceType => ControlSignalType.Continuous;

        public virtual IEnumerable<IPort> GetRequiredInputs()
        {
            IPort first = GetInputPortByName(InputAName);
            if (first != null)
                yield return first;
            IPort second = GetInputPortByName(InputBName);
            if (second != null)
                yield return second;
        }

        public virtual ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            return new ControlOperation
            {
                Kind = Kind,
                InputA = inputA,
                InputB = inputB,
                SourceIndex = sourceIndex,
                StateOffset = stateOffset
            };
        }

        public virtual ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int inputC,
            int inputD,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.InputC = inputC;
            operation.InputD = inputD;
            operation.OutputType = OutputType;
            operation.StateComponentCount = ControlStateComponentCount;
            return operation;
        }

        protected T ReadOption<T>(string name, T fallback)
        {
            INodeOption option = GetNodeOptionByName(name);
            return option != null && option.TryGetValue(out T value) ? value : fallback;
        }
    }

    [Serializable]
    [Node("Signals/Sources", null, "Continuous Input")]
    public class ContinuousSignalInputNode : ControlRuntimeNode
    {
        private const string SignalOption = "Signal";
        public override ControlOperationKind Kind => ControlOperationKind.ContinuousSource;
        public override string SourceName => ReadOption(SignalOption, "signal");

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(SignalOption)
                .WithDefaultValue("signal")
                .Delayed();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override IEnumerable<IPort> GetRequiredInputs()
        {
            yield break;
        }
    }

    [Serializable]
    public abstract class PulseSignalInputNode : ControlRuntimeNode
    {
        private const string SignalOption = "Signal";
        public override string SourceName => ReadOption(SignalOption, "pulse");
        public override ControlSignalType SourceType => ControlSignalType.Pulse;
        public override ControlSignalType OutputType => ControlSignalType.Pulse;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(SignalOption)
                .WithDefaultValue("pulse")
                .Delayed();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<PulsePort>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override IEnumerable<IPort> GetRequiredInputs()
        {
            yield break;
        }
    }

    [Serializable]
    [Node("Signals/Sources", null, "Pulse Strength")]
    public class PulseStrengthInputNode : PulseSignalInputNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.PulseStrengthSource;
    }

    [Serializable]
    [Node("Signals/Sources", null, "Pulse Magnitude")]
    public sealed class PulseMagnitudeInputNode : PulseSignalInputNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.PulseMagnitudeSource;
    }

    [Serializable]
    [Node("Signals/Sources", null, "Pulse Started")]
    public sealed class PulseStartedInputNode : PulseSignalInputNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.PulseStartedSource;
    }

    [Serializable]
    [Node("Signals/Constants", null, "Constant")]
    public sealed class ConstantNode : ControlRuntimeNode
    {
        private const string ValueOption = "Value";
        public override ControlOperationKind Kind => ControlOperationKind.Constant;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>(ValueOption).WithDefaultValue(0f).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override IEnumerable<IPort> GetRequiredInputs()
        {
            yield break;
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption(ValueOption, 0f);
            return operation;
        }
    }

    [Serializable]
    public abstract class BinaryFloatNode : ControlRuntimeNode
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>(InputBName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    public abstract class UnaryControlNode<TValue> : ControlRuntimeNode
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<TValue>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<TValue>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    public abstract class BinaryControlNode<TValue> : ControlRuntimeNode
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<TValue>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<TValue>(InputBName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<TValue>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Operators", null, "Add")]
    public sealed class AddNode : BinaryFloatNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Add;
    }

    [Serializable]
    [Node("Signals/Operators", null, "Multiply")]
    public sealed class MultiplyNode : BinaryFloatNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Multiply;
    }

    [Serializable]
    [Node("Signals/Operators", null, "Subtract")]
    public sealed class SubtractNode : BinaryFloatNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Subtract;
    }

    [Serializable]
    [Node("Signals/Operators", null, "Divide")]
    public sealed class DivideNode : BinaryFloatNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Divide;

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = 0f;
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Operators", null, "Minimum")]
    public sealed class MinimumNode : BinaryFloatNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Minimum;
    }

    [Serializable]
    [Node("Signals/Operators", null, "Maximum")]
    public sealed class MaximumNode : BinaryFloatNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Maximum;
    }

    [Serializable]
    [Node("Signals/Operators", null, "Mix")]
    public sealed class MixNode : BinaryFloatNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Mix;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Amount").WithDefaultValue(0.5f).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Amount", 0.5f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Operators", null, "Invert")]
    public sealed class InvertNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Invert;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Operators", null, "Smoothstep")]
    public sealed class SmoothstepNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Smoothstep;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Minimum").WithDefaultValue(0f).Build();
            context.AddOption<float>("Maximum").WithDefaultValue(1f).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Minimum", 0f);
            operation.ParameterB = ReadOption("Maximum", 1f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Operators", null, "Clamp")]
    public sealed class ClampNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Clamp;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Minimum").WithDefaultValue(0f).Build();
            context.AddOption<float>("Maximum").WithDefaultValue(1f).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override IEnumerable<IPort> GetRequiredInputs()
        {
            yield return GetInputPortByName(InputAName);
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Minimum", 0f);
            operation.ParameterB = ReadOption("Maximum", 1f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Operators", null, "Remap")]
    public sealed class RemapNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Remap;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Input Min").WithDefaultValue(-80f).Build();
            context.AddOption<float>("Input Max").WithDefaultValue(-5f).Build();
            context.AddOption<float>("Output Min").WithDefaultValue(0f).Build();
            context.AddOption<float>("Output Max").WithDefaultValue(1f).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Input Min", -80f);
            operation.ParameterB = ReadOption("Input Max", -5f);
            operation.ParameterC = ReadOption("Output Min", 0f);
            operation.ParameterD = ReadOption("Output Max", 1f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Filters", null, "Low Pass")]
    public sealed class LowPassNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.LowPass;
        public override int FloatStateCount => 1;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Time").WithDefaultValue(0.1f).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Time", 0.1f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Filters", null, "Attack Release")]
    public sealed class AttackReleaseNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.AttackRelease;
        public override int FloatStateCount => 1;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Attack").WithDefaultValue(0.02f).Build();
            context.AddOption<float>("Release").WithDefaultValue(0.2f).Build();
        }

        [Serializable]
        [Node("Signals/Filters", null, "Slew")]
        public sealed class SlewNode : ControlRuntimeNode
        {
            public override ControlOperationKind Kind => ControlOperationKind.Slew;
            public override int FloatStateCount => 1;

            protected override void OnDefineOptions(IOptionDefinitionContext context)
            {
                context.AddOption<float>("Units Per Second").WithDefaultValue(1f).Build();
            }

            protected override void OnDefinePorts(IPortDefinitionContext context)
            {
                context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
                context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
            }

            public override ControlOperation CreateOperation(
                int inputA,
                int inputB,
                int sourceIndex,
                int stateOffset,
                int integerStateOffset)
            {
                ControlOperation operation = base.CreateOperation(
                    inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
                operation.ParameterA = ReadOption("Units Per Second", 1f);
                return operation;
            }
        }

        [Serializable]
        [Node("Signals/Filters", null, "Threshold Hysteresis")]
        public sealed class ThresholdNode : ControlRuntimeNode
        {
            public override ControlOperationKind Kind => ControlOperationKind.Threshold;
            public override int FloatStateCount => 1;

            protected override void OnDefineOptions(IOptionDefinitionContext context)
            {
                context.AddOption<float>("Threshold").WithDefaultValue(0.5f).Build();
                context.AddOption<float>("Hysteresis").WithDefaultValue(0.05f).Build();
            }

            protected override void OnDefinePorts(IPortDefinitionContext context)
            {
                context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
                context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
            }

            public override ControlOperation CreateOperation(
                int inputA,
                int inputB,
                int sourceIndex,
                int stateOffset,
                int integerStateOffset)
            {
                ControlOperation operation = base.CreateOperation(
                    inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
                operation.ParameterA = ReadOption("Threshold", 0.5f);
                operation.ParameterB = Mathf.Max(ReadOption("Hysteresis", 0.05f), 0f);
                return operation;
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Attack", 0.02f);
            operation.ParameterB = ReadOption("Release", 0.2f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Filters", null, "Delay")]
    public sealed class DelayNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Delay;
        public override int FloatStateCount => 256;
        public override bool UsesIntegerState => true;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Seconds").WithDefaultValue(0.1f).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, integerStateOffset, sourceIndex, stateOffset, integerStateOffset);
            operation.DelayCapacity = FloatStateCount;
            operation.ParameterA = ReadOption("Seconds", 0.1f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Pulses", null, "Pulse Decay")]
    public sealed class PulseDecayNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.PulseDecay;
        public override int FloatStateCount => 1;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Decay").WithDefaultValue(1f).Build();
        }

        [Serializable]
        [Node("Signals/Pulses", null, "Pulse Merge")]
        public sealed class PulseMergeNode : ControlRuntimeNode
        {
            public override ControlOperationKind Kind => ControlOperationKind.PulseMerge;

            protected override void OnDefinePorts(IPortDefinitionContext context)
            {
                context.AddInputPort<PulsePort>(InputAName).WithCapacity(PortCapacity.Single).Build();
                context.AddInputPort<PulsePort>(InputBName).WithCapacity(PortCapacity.Single).Build();
                context.AddOutputPort<PulsePort>(OutputName).WithCapacity(PortCapacity.Multi).Build();
            }
        }

        [Serializable]
        [Node("Signals/Filters", null, "Sample And Hold")]
        public sealed class SampleAndHoldNode : ControlRuntimeNode
        {
            public override ControlOperationKind Kind => ControlOperationKind.SampleAndHold;
            public override int FloatStateCount => 1;

            protected override void OnDefinePorts(IPortDefinitionContext context)
            {
                context.AddInputPort<float>(InputAName).WithCapacity(PortCapacity.Single).Build();
                context.AddInputPort<PulsePort>(InputBName).WithCapacity(PortCapacity.Single).Build();
                context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<PulsePort>(InputAName).WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override IEnumerable<IPort> GetRequiredInputs()
        {
            yield return GetInputPortByName(InputAName);
        }

        public override ControlOperation CreateOperation(
            int inputA,
            int inputB,
            int sourceIndex,
            int stateOffset,
            int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputA, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Decay", 1f);
            return operation;
        }
    }

    [Serializable]
    public abstract class ControlOutputNode : Node
    {
        protected const string ValueInputName = "Value";
        internal const string NameOption = "Name";

        public abstract ControlSignalType SignalType { get; }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(NameOption)
                .WithDefaultValue("control.output")
                .Delayed();
        }

        public string ReadOutputName()
        {
            return GetNodeOptionByName(NameOption).TryGetValue<string>(out string value)
                ? value
                : null;
        }
    }

    [Serializable]
    [Node("Signals/Outputs", null, "Float Output")]
    public sealed class FloatControlOutputNode : ControlOutputNode
    {
        public override ControlSignalType SignalType => ControlSignalType.Continuous;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(ValueInputName).WithCapacity(PortCapacity.Single).Build();
        }
    }

    [Serializable]
    [Node("Signals/Outputs", null, "Pulse Output")]
    public sealed class PulseControlOutputNode : ControlOutputNode
    {
        public override ControlSignalType SignalType => ControlSignalType.Pulse;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<PulsePort>(ValueInputName).WithCapacity(PortCapacity.Single).Build();
        }
    }
}
