using System;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Sonosthesia.Signals.Editor
{
    [Graph(AssetExtension)]
    [Serializable]
    public sealed class ControlSignalGraph : Graph
    {
        public const string AssetExtension = "controlgraph";

        [MenuItem("Assets/Create/Sonosthesia/Control Signal Graph")]
        private static void CreateAsset()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<ControlSignalGraph>();
        }

        public override void OnGraphChanged(GraphLogger logger)
        {
            var outputs = GetNodes().OfType<ControlOutputNode>().ToArray();
            if (outputs.Length == 0)
                logger.LogError("Add at least one control output.");

            foreach (ControlOutputNode output in outputs)
            {
                if (string.IsNullOrWhiteSpace(output.ReadOutputName()))
                    logger.LogError("Control output names cannot be empty.", output);
            }

            foreach (IControlRuntimeNode node in GetNodes().OfType<IControlRuntimeNode>())
            {
                foreach (IPort input in node.GetRequiredInputs())
                {
                    if (!input.IsConnected)
                        logger.LogError(
                            $"Input '{input.DisplayName}' is not connected.",
                            input);
                }
            }
        }
    }
}
