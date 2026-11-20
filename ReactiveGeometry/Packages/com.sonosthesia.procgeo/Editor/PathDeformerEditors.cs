using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo.Editor
{
    [CustomEditor(typeof(PathOffsetDeformer))]
    public sealed class PathOffsetDeformerEditor : PathDeformerEditorBase
    {
        protected override string[] Properties =>
            new[] { "deformationEnabled", "offset" };
    }

    [CustomEditor(typeof(PathBendDeformer))]
    public sealed class PathBendDeformerEditor : PathDeformerEditorBase
    {
        protected override string[] Properties =>
            new[]
            {
                "deformationEnabled", "target", "pivot", "localPivot",
                "localAxis", "angle", "falloffStart", "falloffEnd"
            };
    }

    [CustomEditor(typeof(PathWaveDeformer))]
    public sealed class PathWaveDeformerEditor : PathDeformerEditorBase
    {
        protected override string[] Properties =>
            new[]
            {
                "deformationEnabled", "amplitude", "localDirection",
                "spatialFrequency", "speed", "phase"
            };
    }

    [CustomEditor(typeof(PathTwistDeformer))]
    public sealed class PathTwistDeformerEditor : PathDeformerEditorBase
    {
        protected override string[] Properties =>
            new[]
            {
                "deformationEnabled", "angle", "falloffStart", "falloffEnd"
            };
    }

    [CustomEditor(typeof(DynamicPathWaveDeformer))]
    public sealed class DynamicPathWaveDeformerEditor : PathDeformerEditorBase
    {
        protected override string[] Properties =>
            new[]
            {
                "deformationEnabled", "forceAmplitude", "localDirection",
                "spatialFrequency", "speed", "phase"
            };
    }

    [CustomEditor(typeof(PathNoiseDeformer))]
    public sealed class PathNoiseDeformerEditor : PathDeformerEditorBase
    {
        protected override string[] Properties =>
            new[]
            {
                "deformationEnabled", "amplitude",
                "spatialFrequency", "temporalSpeed", "phase", "seed",
                "octaves", "lacunarity", "gain", "localBasis"
            };
    }

    [CustomEditor(typeof(VertexNormalOffsetDeformer))]
    public sealed class VertexNormalOffsetDeformerEditor : PathDeformerEditorBase
    {
        protected override bool IsPathDeformer => false;
        protected override string[] Properties =>
            new[]
            {
                "deformationEnabled", "amplitude", "spatialFrequency",
                "speed", "phase"
            };
    }

    public abstract class PathDeformerEditorBase : UnityEditor.Editor
    {
        protected virtual bool IsPathDeformer => true;
        protected abstract string[] Properties { get; }

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new();
            root.Add(new HelpBox(
                IsPathDeformer
                    ? "Path deformer: runs before path skinning and cannot modify vertex buffers."
                    : "Vertex deformer: runs after path skinning and uses the committed live normal basis.",
                HelpBoxMessageType.Info));
            AddField("target");
            foreach (string propertyName in Properties)
            {
                if (propertyName != "target")
                    AddField(propertyName);
            }
            Foldout spatial = new() { text = "Spatial Falloff" };
            SerializedProperty source =
                serializedObject.FindProperty("spatialSource");
            spatial.Add(new PropertyField(source));
            root.Add(spatial);
            AddField("drawFalloff");

            Button refresh = new(() =>
            {
                ProceduralMeshRenderer renderer =
                    ((Component)target).GetComponent<ProceduralMeshRenderer>();
                if (renderer != null)
                    renderer.RefreshDeformers();
            })
            {
                text = "Refresh Deformer Registration"
            };
            root.Add(refresh);
            Button reset = new(() =>
            {
                ProceduralMeshRenderer renderer =
                    ((Component)target).GetComponent<ProceduralMeshRenderer>();
                if (renderer != null && renderer.PathDeformation != null)
                    renderer.ResetPathDeformation();
            })
            {
                text = "Reset Path Base Pose"
            };
            root.Add(reset);
            root.Bind(serializedObject);
            return root;

            void AddField(string propertyName)
            {
                SerializedProperty property =
                    serializedObject.FindProperty(propertyName);
                if (property != null)
                    root.Add(new PropertyField(property));
            }
        }
    }
}
