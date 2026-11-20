using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo.Editor
{
    [CustomEditor(typeof(ProceduralBranchRenderer))]
    public sealed class ProceduralBranchRendererEditor :
        UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            ProceduralBranchRenderer renderer =
                (ProceduralBranchRenderer)target;
            VisualElement root = new();
            root.Add(new HelpBox(
                "Branch paths reuse Phase 5 frame reconstruction and skinning. " +
                "Topological changes require regeneration; runtime deformation " +
                "stays GPU-owned.",
                HelpBoxMessageType.Info));
            root.Add(new PropertyField(
                serializedObject.FindProperty("settings")));
            root.Add(new PropertyField(
                serializedObject.FindProperty("autoGenerate")));
            root.Add(new PropertyField(
                serializedObject.FindProperty("deformationEnabled")));
            root.Add(new PropertyField(
                serializedObject.FindProperty("color")));
            root.Add(new PropertyField(
                serializedObject.FindProperty("drawRestGraph")));

            Label counts = new(
                $"{renderer.BranchCount} branches, " +
                $"{renderer.PathSampleCount} path samples, " +
                $"{renderer.VertexCount} unified mesh vertices");
            root.Add(counts);

            VisualElement actions = new();
            actions.Add(new Button(renderer.Generate)
            {
                text = "Generate"
            });
            actions.Add(new Button(() =>
            {
                if (renderer.IsGenerated)
                    renderer.ResetPathDeformation();
            })
            {
                text = "Reset Paths"
            });
            actions.Add(new Button(renderer.RefreshDeformers)
            {
                text = "Refresh Deformers"
            });
            root.Add(actions);
            root.Bind(serializedObject);
            return root;
        }
    }

    [CustomEditor(typeof(BranchTurbulenceDeformer))]
    public sealed class BranchTurbulenceDeformerEditor :
        BranchDeformerEditorBase
    {
        protected override string[] Properties => new[]
        {
            "deformationEnabled",
            "amplitude",
            "localWindDirection",
            "spatialFrequency",
            "temporalSpeed",
            "phase",
            "seed",
            "octaves",
            "lacunarity",
            "gain"
        };
    }

    [CustomEditor(typeof(BranchAttractorDeformer))]
    public sealed class BranchAttractorDeformerEditor :
        BranchDeformerEditorBase
    {
        protected override string[] Properties => new[]
        {
            "deformationEnabled",
            "attractor",
            "localTarget",
            "strength",
            "power"
        };
    }

    public abstract class BranchDeformerEditorBase :
        UnityEditor.Editor
    {
        protected abstract string[] Properties { get; }

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new();
            root.Add(new HelpBox(
                "Branch path deformer: contributions are masked by radius, " +
                "generation and branch-local tip position, then resolved before " +
                "live frames and tube skinning.",
                HelpBoxMessageType.Info));
            AddField("target");
            foreach (string property in Properties)
                AddField(property);
            Foldout spatial = new() { text = "Spatial Falloff" };
            spatial.Add(new PropertyField(
                serializedObject.FindProperty("spatialSource")));
            root.Add(spatial);
            AddField("drawFalloff");
            root.Add(new Button(() =>
            {
                BranchDeformerComponent component =
                    (BranchDeformerComponent)target;
                ProceduralBranchRenderer renderer = component.Target;
                if (renderer != null)
                    renderer.RefreshDeformers();
            })
            {
                text = "Refresh Branch Deformer Registration"
            });
            root.Bind(serializedObject);
            return root;

            void AddField(string name)
            {
                SerializedProperty property =
                    serializedObject.FindProperty(name);
                if (property != null)
                    root.Add(new PropertyField(property));
            }
        }
    }

    [CustomEditor(typeof(BranchGraphDebugOverlay))]
    public sealed class BranchGraphDebugOverlayEditor :
        UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            BranchGraphDebugOverlay overlay =
                (BranchGraphDebugOverlay)target;
            VisualElement root = new();
            root.Add(new PropertyField(
                serializedObject.FindProperty("restColor")));
            root.Add(new PropertyField(
                serializedObject.FindProperty("liveColor")));
            root.Add(new PropertyField(
                serializedObject.FindProperty("pointSize")));
            root.Add(new PropertyField(
                serializedObject.FindProperty("liveSnapshotVisible")));
            root.Add(new Button(overlay.Refresh)
            {
                text = "Refresh Rest/Live Overlays"
            });
            root.Add(new Button(overlay.CaptureLiveSnapshot)
            {
                text = "Capture Live Snapshot Once"
            });
            root.Add(new Button(
                () => overlay.SetRestVisible(!overlay.RestVisible))
            {
                text = "Toggle Rest Graph"
            });
            root.Add(new Button(
                () => overlay.SetLiveVisible(!overlay.LiveVisible))
            {
                text = "Toggle Live Snapshot"
            });
            root.Bind(serializedObject);
            return root;
        }
    }
}
