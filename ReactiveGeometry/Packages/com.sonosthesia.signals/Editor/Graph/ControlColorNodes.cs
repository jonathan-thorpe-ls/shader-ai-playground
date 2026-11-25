using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Sonosthesia.Signals.Editor
{
    [Serializable]
    [Node("Signals/Color", null, "Color Constant")]
    public sealed class ColorConstantNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.ColorConstant;
        public override ControlSignalType OutputType => ControlSignalType.Color;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Color>("Color").WithDefaultValue(Color.white).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<Color>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override IEnumerable<IPort> GetRequiredInputs()
        {
            yield break;
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterColorA = ReadOption("Color", Color.white);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Color", null, "Color Lerp")]
    public sealed class ColorLerpNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.ColorLerp;
        public override ControlSignalType OutputType => ControlSignalType.Color;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Color>("A").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<Color>("B").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Amount").WithDefaultValue(0.5f).Build();
            context.AddOutputPort<Color>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Color", null, "Color Scale")]
    public sealed class ColorScaleNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.ColorMultiplyScalar;
        public override ControlSignalType OutputType => ControlSignalType.Color;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Color>("Color").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Scale").WithDefaultValue(1f).Build();
            context.AddOutputPort<Color>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Color", null, "Color Multiply")]
    public sealed class ColorMultiplyNode : BinaryControlNode<Color>
    {
        public override ControlOperationKind Kind => ControlOperationKind.ColorMultiply;
        public override ControlSignalType OutputType => ControlSignalType.Color;
    }

    [Serializable]
    [Node("Signals/Color", null, "HSV")]
    public sealed class HsvNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.HsvToColor;
        public override ControlSignalType OutputType => ControlSignalType.Color;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Hue").WithDefaultValue(0f).Build();
            context.AddInputPort<float>("Saturation").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Value").WithDefaultValue(1f).Build();
            context.AddOutputPort<Color>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Color", null, "Gradient")]
    public sealed class GradientNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Gradient;
        public override ControlSignalType OutputType => ControlSignalType.Color;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Color>("Start").WithDefaultValue(Color.black).Build();
            context.AddOption<Color>("End").WithDefaultValue(Color.white).Build();
            context.AddOption<int>("Wrap").WithDefaultValue(0).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Position").WithDefaultValue(0f).Build();
            context.AddOutputPort<Color>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.Gradient = new GradientRuntimeData
            {
                WrapMode = Mathf.Clamp(ReadOption("Wrap", 0), 0, 2),
                Keys = new[]
                {
                    new GradientRuntimeKey
                    {
                        Time = 0f,
                        Color = ReadOption("Start", Color.black)
                    },
                    new GradientRuntimeKey
                    {
                        Time = 1f,
                        Color = ReadOption("End", Color.white)
                    }
                }
            };
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Color", null, "Palette")]
    public sealed class PaletteNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Palette;
        public override ControlSignalType OutputType => ControlSignalType.Color;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Color>("Color 0").WithDefaultValue(Color.black).Build();
            context.AddOption<Color>("Color 1").WithDefaultValue(Color.red).Build();
            context.AddOption<Color>("Color 2").WithDefaultValue(Color.blue).Build();
            context.AddOption<Color>("Color 3").WithDefaultValue(Color.white).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Position").WithDefaultValue(0f).Build();
            context.AddOutputPort<Color>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.Palette = new[]
            {
                ReadOption("Color 0", Color.black),
                ReadOption("Color 1", Color.red),
                ReadOption("Color 2", Color.blue),
                ReadOption("Color 3", Color.white)
            };
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Color", null, "Tint")]
    public sealed class ColorTintNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.ColorTint;
        public override ControlSignalType OutputType => ControlSignalType.Color;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Color>("Tint").WithDefaultValue(Color.white).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Color>("Color").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Strength").WithDefaultValue(1f).Build();
            context.AddOutputPort<Color>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterColorA = ReadOption("Tint", Color.white);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Outputs", null, "Vector2 Output")]
    public sealed class Vector2ControlOutputNode : ControlOutputNode
    {
        public override ControlSignalType SignalType => ControlSignalType.Vector2;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector2>(ValueInputName).WithCapacity(PortCapacity.Single).Build();
        }
    }

    [Serializable]
    [Node("Signals/Outputs", null, "Vector3 Output")]
    public sealed class Vector3ControlOutputNode : ControlOutputNode
    {
        public override ControlSignalType SignalType => ControlSignalType.Vector3;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector3>(ValueInputName).WithCapacity(PortCapacity.Single).Build();
        }
    }

    [Serializable]
    [Node("Signals/Outputs", null, "Color Output")]
    public sealed class ColorControlOutputNode : ControlOutputNode
    {
        public override ControlSignalType SignalType => ControlSignalType.Color;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Color>(ValueInputName).WithCapacity(PortCapacity.Single).Build();
        }
    }
}
