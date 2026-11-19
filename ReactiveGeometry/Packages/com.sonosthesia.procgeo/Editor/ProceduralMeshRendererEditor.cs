using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo.Editor
{
    [CustomEditor(typeof(ProceduralMeshRenderer))]
    public sealed class ProceduralMeshRendererEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            HelpBox limits = new(
                "Parametric settings are bounded at 256 by 128 segments. " +
                "Geometry regeneration allocates and rebuilds GPU topology; " +
                "keep deformation at runtime allocation-free.",
                HelpBoxMessageType.Info);
            root.Add(limits);

            Label counts = new();
            counts.style.marginTop = 4;
            root.Add(counts);

            Button regenerate = new(() =>
            {
                ProceduralMeshRenderer component = (ProceduralMeshRenderer)target;
                Undo.RecordObject(component, "Regenerate Procedural Geometry");
                component.Generate();
                EditorUtility.SetDirty(component);
                UpdateCounts();
            })
            {
                text = "Regenerate Geometry"
            };
            root.Add(regenerate);

            root.schedule.Execute(UpdateCounts).Every(500);
            return root;

            void UpdateCounts()
            {
                if (target == null) return;
                ProceduralMeshRenderer component = (ProceduralMeshRenderer)target;
                counts.text = component.IsGenerated
                    ? $"{component.VertexCount:n0} vertices, " +
                      $"{component.TriangleCount:n0} triangles"
                    : "Geometry not generated.";
            }
        }
    }
}
