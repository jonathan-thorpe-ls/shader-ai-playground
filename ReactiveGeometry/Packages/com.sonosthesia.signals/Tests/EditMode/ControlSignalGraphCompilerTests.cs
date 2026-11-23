using System.IO;
using NUnit.Framework;
using Sonosthesia.Signals.Editor;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Sonosthesia.Signals.Tests
{
    public sealed class ControlSignalGraphCompilerTests
    {
        private const string Root = "Assets/Phase7TestOutput";
        private const string AssetPath = Root + "/Test.controlgraph";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root))
                AssetDatabase.CreateFolder("Assets", "Phase7TestOutput");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(AssetPath);
            if (AssetDatabase.IsValidFolder(Root))
                AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void Importer_ProducesFlatRuntimeGraph()
        {
            ControlSignalGraph graph = GraphDatabase.CreateGraph<ControlSignalGraph>(AssetPath);
            graph.UndoBeginRecordGraph("Build Phase 7 test graph");

            var source = new ContinuousSignalInputNode { Position = new Vector2(0, 0) };
            var remap = new RemapNode { Position = new Vector2(250, 0) };
            var output = new FloatControlOutputNode { Position = new Vector2(500, 0) };
            graph.AddNode(source);
            graph.AddNode(remap);
            graph.AddNode(output);

            source.GetNodeOptionByName("Signal").TrySetValue("provider.test");
            output.GetNodeOptionByName("Name").TrySetValue("consumer.test");
            graph.Connect(
                source.GetOutputPortByName("Value"),
                remap.GetInputPortByName("A"));
            graph.Connect(
                remap.GetOutputPortByName("Value"),
                output.GetInputPortByName("Value"));

            GraphDatabase.SaveGraph(graph);
            graph.UndoEndRecordGraph();
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);

            var runtime = AssetDatabase.LoadMainAssetAtPath(AssetPath)
                as CompiledControlGraph;

            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.OperationCount, Is.EqualTo(2));
            Assert.That(runtime.OutputCount, Is.EqualTo(1));
            Assert.That(runtime.Profile.ContinuousCount, Is.EqualTo(1));
            Assert.That(runtime.GetOutput(0).Name, Is.EqualTo("consumer.test"));
            Assert.That(
                runtime.Profile.GetContinuous(0).Name,
                Is.EqualTo("provider.test"));
        }
    }
}
