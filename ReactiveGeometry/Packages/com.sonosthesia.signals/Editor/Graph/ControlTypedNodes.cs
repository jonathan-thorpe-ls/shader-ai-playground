using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Sonosthesia.Signals.Editor
{
    [Serializable]
    [Node("Signals/Vector2", null, "Vector2 Constant")]
    public sealed class Vector2ConstantNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Constant;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Vector2>("Value").WithDefaultValue(Vector2.zero).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<Vector2>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override System.Collections.Generic.IEnumerable<IPort> GetRequiredInputs()
        {
            yield break;
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            Vector2 value = ReadOption("Value", Vector2.zero);
            operation.ParameterA = value.x;
            operation.ParameterB = value.y;
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Vector2", null, "Vector2 Add")]
    public sealed class Vector2AddNode : BinaryControlNode<Vector2>
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Add;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;
    }

    [Serializable]
    [Node("Signals/Vector2", null, "Vector2 Subtract")]
    public sealed class Vector2SubtractNode : BinaryControlNode<Vector2>
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Subtract;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;
    }

    [Serializable]
    [Node("Signals/Vector2", null, "Vector2 Scale")]
    public sealed class Vector2ScaleNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Scale;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector2>("Value").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Scale").WithDefaultValue(1f).Build();
            context.AddOutputPort<Vector2>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Vector2", null, "Vector2 Normalize")]
    public sealed class Vector2NormalizeNode : UnaryControlNode<Vector2>
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Normalize;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;
    }

    [Serializable]
    [Node("Signals/Vector2", null, "Vector2 Lerp")]
    public sealed class Vector2LerpNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Lerp;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector2>("A").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<Vector2>("B").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Amount").WithDefaultValue(0.5f).Build();
            context.AddOutputPort<Vector2>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Vector2", null, "Vector2 Length")]
    public sealed class Vector2LengthNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Length;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector2>("Value").WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Vector2", null, "Vector2 From Scalars")]
    public sealed class Vector2FromScalarsNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2FromScalars;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("X").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Y").WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<Vector2>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Generators/Vector2", null, "Polar")]
    public sealed class PolarVector2Node : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Polar;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<bool>("Degrees").WithDefaultValue(false).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Angle").WithDefaultValue(0f).Build();
            context.AddInputPort<float>("Radius").WithDefaultValue(1f).Build();
            context.AddOutputPort<Vector2>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Degrees", false) ? 1f : 0f;
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Vector3", null, "Vector3 Constant")]
    public sealed class Vector3ConstantNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Constant;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<Vector3>("Value").WithDefaultValue(Vector3.zero).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<Vector3>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override System.Collections.Generic.IEnumerable<IPort> GetRequiredInputs()
        {
            yield break;
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            Vector3 value = ReadOption("Value", Vector3.zero);
            operation.ParameterA = value.x;
            operation.ParameterB = value.y;
            operation.ParameterC = value.z;
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Vector3", null, "Vector3 Add")]
    public sealed class Vector3AddNode : BinaryControlNode<Vector3>
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Add;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;
    }

    [Serializable]
    [Node("Signals/Vector3", null, "Vector3 Subtract")]
    public sealed class Vector3SubtractNode : BinaryControlNode<Vector3>
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Subtract;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;
    }

    [Serializable]
    [Node("Signals/Vector3", null, "Vector3 Scale")]
    public sealed class Vector3ScaleNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Scale;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector3>("Value").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Scale").WithDefaultValue(1f).Build();
            context.AddOutputPort<Vector3>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Vector3", null, "Vector3 Normalize")]
    public sealed class Vector3NormalizeNode : UnaryControlNode<Vector3>
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Normalize;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;
    }

    [Serializable]
    [Node("Signals/Vector3", null, "Vector3 Lerp")]
    public sealed class Vector3LerpNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Lerp;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector3>("A").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<Vector3>("B").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Amount").WithDefaultValue(0.5f).Build();
            context.AddOutputPort<Vector3>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Vector3", null, "Vector3 Length")]
    public sealed class Vector3LengthNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Length;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector3>("Value").WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<float>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Vector3", null, "Vector3 From Scalars")]
    public sealed class Vector3FromScalarsNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3FromScalars;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("X").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Y").WithCapacity(PortCapacity.Single).Build();
            context.AddInputPort<float>("Z").WithCapacity(PortCapacity.Single).Build();
            context.AddOutputPort<Vector3>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Generators/Vector3", null, "Cylindrical")]
    public sealed class CylindricalNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Cylindrical;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<bool>("Degrees").WithDefaultValue(false).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Angle").WithDefaultValue(0f).Build();
            context.AddInputPort<float>("Radius").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Height").WithDefaultValue(0f).Build();
            context.AddOutputPort<Vector3>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Degrees", false) ? 1f : 0f;
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Generators/Vector3", null, "Spherical")]
    public sealed class SphericalNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Spherical;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<bool>("Degrees").WithDefaultValue(false).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Azimuth").WithDefaultValue(0f).Build();
            context.AddInputPort<float>("Elevation").WithDefaultValue(0f).Build();
            context.AddInputPort<float>("Radius").WithDefaultValue(1f).Build();
            context.AddOutputPort<Vector3>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Degrees", false) ? 1f : 0f;
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Generators/Vector3", null, "Direction With Length")]
    public sealed class DirectionWithLengthNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.DirectionWithLength;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Vector3>("Direction").WithDefaultValue(Vector3.forward).Build();
            context.AddInputPort<float>("Length").WithDefaultValue(1f).Build();
            context.AddOutputPort<Vector3>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }
    }

    [Serializable]
    [Node("Signals/Generators/Vector2", null, "Oscillator")]
    public sealed class Vector2OscillatorNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Oscillator;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Phase Offset").WithDefaultValue(0f).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Frequency").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Amplitude").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Excitation").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Phase").WithDefaultValue(0f).Build();
            context.AddOutputPort<Vector2>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Phase Offset", 0f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Generators/Vector3", null, "Oscillator")]
    public sealed class Vector3OscillatorNode : ControlRuntimeNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Oscillator;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Y Phase").WithDefaultValue(1f).Build();
            context.AddOption<float>("Z Phase").WithDefaultValue(2f).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Frequency").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Amplitude").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Excitation").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Phase").WithDefaultValue(0f).Build();
            context.AddOutputPort<Vector3>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Y Phase", 1f);
            operation.ParameterB = ReadOption("Z Phase", 2f);
            return operation;
        }
    }

    [Serializable]
    [Node("Signals/Generators/Vector2", null, "Brownian")]
    public sealed class Vector2BrownianNode : BrownianNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector2Brownian;
        public override ControlSignalType OutputType => ControlSignalType.Vector2;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            DefineControlPorts<Vector2>(context);
        }
    }

    [Serializable]
    [Node("Signals/Generators/Vector3", null, "Brownian")]
    public sealed class Vector3BrownianNode : BrownianNode
    {
        public override ControlOperationKind Kind => ControlOperationKind.Vector3Brownian;
        public override ControlSignalType OutputType => ControlSignalType.Vector3;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            DefineControlPorts<Vector3>(context);
        }
    }

    [Serializable]
    public abstract class BrownianNode : ControlRuntimeNode
    {
        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<float>("Seed").WithDefaultValue(1f).Build();
            context.AddOption<int>("Octaves").WithDefaultValue(3).Build();
            context.AddOption<float>("Persistence").WithDefaultValue(0.5f).Build();
            context.AddOption<float>("Lacunarity").WithDefaultValue(2f).Build();
        }

        protected void DefineControlPorts<T>(IPortDefinitionContext context)
        {
            context.AddInputPort<float>("Amplitude").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Frequency").WithDefaultValue(1f).Build();
            context.AddInputPort<float>("Excitation").WithDefaultValue(1f).Build();
            context.AddOutputPort<T>(OutputName).WithCapacity(PortCapacity.Multi).Build();
        }

        public override ControlOperation CreateOperation(
            int inputA, int inputB, int sourceIndex, int stateOffset, int integerStateOffset)
        {
            ControlOperation operation = base.CreateOperation(
                inputA, inputB, sourceIndex, stateOffset, integerStateOffset);
            operation.ParameterA = ReadOption("Seed", 1f);
            operation.ParameterB = Mathf.Clamp(ReadOption("Octaves", 3), 1, 8);
            operation.ParameterC = Mathf.Clamp01(ReadOption("Persistence", 0.5f));
            operation.ParameterD = Mathf.Max(ReadOption("Lacunarity", 2f), 1f);
            return operation;
        }
    }
}
