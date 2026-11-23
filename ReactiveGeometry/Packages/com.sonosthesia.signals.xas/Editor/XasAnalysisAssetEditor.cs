using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Sonosthesia.Signals.Xas.Editor
{
    [CustomEditor(typeof(XasAnalysisAsset))]
    public sealed class XasAnalysisAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            var status = new Label();
            root.Add(status);
            root.schedule.Execute(() =>
            {
                var asset = target as XasAnalysisAsset;
                if (asset == null)
                    return;
                bool valid = asset.IsValid();
                bool exists = valid && File.Exists(asset.ResolvePath());
                status.text = !valid
                    ? "Invalid StreamingAssets relative path."
                    : exists
                        ? $"Ready: {asset.DurationSeconds:0.###} seconds"
                        : $"Missing file: {asset.ResolvePath()}";
            }).Every(250);
            return root;
        }
    }
}
