using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Sonosthesia.Signals.Editor
{
    [ScriptedImporter(1, ControlSignalGraph.AssetExtension)]
    public sealed class ControlSignalGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var graph = GraphDatabase.LoadGraphForImporter<ControlSignalGraph>(context.assetPath);
            if (graph == null)
                return;
            if (!graph.GetNodes().OfType<ControlOutputNode>().Any())
                return;

            try
            {
                var compiler = new ControlSignalGraphCompiler(graph);
                compiler.Compile(
                    out ControlSignalProfile profile,
                    out ControlOperation[] operations,
                    out ControlGraphOutput[] outputs,
                    out int floatStateCount,
                    out int integerStateCount);

                var runtime = ScriptableObject.CreateInstance<CompiledControlGraph>();
                runtime.name = "Runtime Control Graph";
                runtime.Configure(
                    profile,
                    operations,
                    outputs,
                    floatStateCount,
                    integerStateCount);
                context.AddObjectToAsset("Runtime", runtime);
                context.SetMainObject(runtime);
            }
            catch (ControlGraphCompileException exception)
            {
                context.LogImportError(
                    $"Control signal graph failed to compile: {exception.Message}");
            }
        }
    }

    internal sealed class ControlSignalGraphCompiler
    {
        private readonly ControlSignalGraph _graph;
        private readonly Dictionary<Hash128, int> _operationIndices = new();
        private readonly HashSet<Hash128> _visiting = new();
        private readonly List<ControlOperation> _operations = new();
        private readonly List<ControlSignalDescriptor> _continuous = new();
        private readonly List<ControlSignalDescriptor> _pulses = new();
        private int _floatStateCount;
        private int _integerStateCount;

        public ControlSignalGraphCompiler(ControlSignalGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public void Compile(
            out ControlSignalProfile profile,
            out ControlOperation[] operations,
            out ControlGraphOutput[] outputs,
            out int floatStateCount,
            out int integerStateCount)
        {
            var runtimeOutputs = new List<ControlGraphOutput>();
            var outputNames = new HashSet<ControlSignalId>();

            foreach (ControlOutputNode output in
                     _graph.GetNodes().OfType<ControlOutputNode>())
            {
                string name = output.ReadOutputName();
                if (string.IsNullOrWhiteSpace(name))
                    throw new ControlGraphCompileException(
                        "A control output has an empty name.",
                        output);

                ControlSignalId outputId = ControlSignalId.FromString(name);
                if (!outputNames.Add(outputId))
                    throw new ControlGraphCompileException(
                        $"Duplicate output '{name}'.",
                        output);

                IPort input = output.GetInputPortByName("Value");
                if (input == null || !input.IsConnected)
                    throw new ControlGraphCompileException(
                        $"Output '{name}' is not connected.",
                        output);

                int operationIndex = CompileNode(input.FirstConnectedPort.GetNode());
                runtimeOutputs.Add(new ControlGraphOutput
                {
                    Name = name,
                    Id = outputId.Value,
                    OperationIndex = operationIndex,
                    Type = output.SignalType,
                    Minimum = float.NegativeInfinity,
                    Maximum = float.PositiveInfinity
                });
            }

            if (runtimeOutputs.Count == 0)
                throw new ControlGraphCompileException("No control outputs were found.");

            if (_operations.Count > 512)
                throw new ControlGraphCompileException(
                    "The control graph exceeds the 512 operation limit.");

            profile = new ControlSignalProfile(
                "Control Signal Graph",
                _continuous.ToArray(),
                _pulses.ToArray());
            operations = _operations.ToArray();
            outputs = runtimeOutputs.ToArray();
            floatStateCount = _floatStateCount;
            integerStateCount = _integerStateCount;
        }

        private int CompileNode(INode node)
        {
            if (node == null)
                throw new ControlGraphCompileException("A graph input has no upstream node.");
            if (_operationIndices.TryGetValue(node.ID, out int existing))
                return existing;
            if (!_visiting.Add(node.ID))
                throw new ControlGraphCompileException("Cycle detected in control graph.", node);

            if (node is not IControlRuntimeNode runtimeNode)
            {
                _visiting.Remove(node.ID);
                throw new ControlGraphCompileException(
                    $"Node '{node.Title}' cannot be compiled as a control operation.",
                    node);
            }

            int inputA = CompileRequiredInput(node, "A");
            int inputB = CompileRequiredInput(node, "B");
            int sourceIndex = -1;
            if (!string.IsNullOrWhiteSpace(runtimeNode.SourceName))
            {
                sourceIndex = RegisterSource(
                    runtimeNode.SourceName,
                    runtimeNode.SourceType);
            }

            int stateOffset = _floatStateCount;
            _floatStateCount += runtimeNode.FloatStateCount;
            int integerOffset = _integerStateCount;
            if (runtimeNode.UsesIntegerState)
                _integerStateCount++;

            int operationIndex = _operations.Count;
            _operations.Add(runtimeNode.CreateOperation(
                inputA,
                inputB,
                sourceIndex,
                stateOffset,
                integerOffset));
            _operationIndices.Add(node.ID, operationIndex);
            _visiting.Remove(node.ID);
            return operationIndex;
        }

        private int CompileRequiredInput(INode node, string portName)
        {
            IPort port = node.GetInputPortByName(portName);
            if (port == null)
                return -1;
            if (!port.IsConnected)
            {
                if (node is IControlRuntimeNode runtime
                    && runtime.GetRequiredInputs().Contains(port))
                {
                    throw new ControlGraphCompileException(
                        $"Input '{port.DisplayName}' is not connected.",
                        node);
                }
                return -1;
            }

            return CompileNode(port.FirstConnectedPort.GetNode());
        }

        private int RegisterSource(string name, ControlSignalType type)
        {
            ControlSignalDescriptor descriptor = new()
            {
                Name = name,
                Type = type
            };
            List<ControlSignalDescriptor> descriptors =
                type == ControlSignalType.Continuous ? _continuous : _pulses;
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (descriptors[index].Id == descriptor.Id)
                    return index;
            }

            descriptors.Add(descriptor);
            return descriptors.Count - 1;
        }
    }

    internal sealed class ControlGraphCompileException : Exception
    {
        public readonly object Context;

        public ControlGraphCompileException(string message, object context = null)
            : base(message)
        {
            Context = context;
        }
    }
}
