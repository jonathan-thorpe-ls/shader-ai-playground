using System.Collections.Generic;
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
            PropertyField AddProperty(
                SerializedProperty property,
                VisualElement container = null)
            {
                PropertyField field = new(property);
                (container ?? root).Add(field);
                return field;
            }

            SerializedProperty generatorKind =
                serializedObject.FindProperty("generatorKind");
            SerializedProperty parametricType =
                serializedObject.FindProperty("parametricType");
            SerializedProperty parametricSettings =
                serializedObject.FindProperty("parametricSettings");

            PropertyField frequency =
                AddProperty(serializedObject.FindProperty("frequency"));
            PropertyField radius =
                AddProperty(serializedObject.FindProperty("radius"));
            PropertyField generatorKindField = AddProperty(generatorKind);
            AddProperty(serializedObject.FindProperty("autoGenerate"));
            PropertyField parametricTypeField = AddProperty(parametricType);

            Foldout parametricFoldout = new() { text = "Parametric Settings" };
            Dictionary<string, PropertyField> parametricFields = new();
            root.Add(parametricFoldout);
            foreach (string name in new[]
            {
                "uSegments",
                "vSegments",
                "width",
                "depth",
                "height",
                "radius",
                "tubeRadius",
                "minorRadius",
                "frequency",
                "phase",
                "twist",
                "knotP",
                "knotQ",
                "curveLength",
                "lissajousFrequency"
            })
            {
                parametricFields.Add(
                    name,
                    AddProperty(
                        parametricSettings.FindPropertyRelative(name),
                        parametricFoldout));
            }

            AddProperty(serializedObject.FindProperty("color"));
            AddProperty(serializedObject.FindProperty("simulationEnabled"));
            AddProperty(serializedObject.FindProperty("simulationParameters"));
            AddProperty(serializedObject.FindProperty("fieldSettings"));

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

            root.Bind(serializedObject);
            root.TrackSerializedObjectValue(serializedObject, _ => UpdateVisibility());
            UpdateVisibility();
            root.schedule.Execute(UpdateCounts).Every(500);
            return root;

            void UpdateVisibility()
            {
                bool isParametric =
                    generatorKind.enumValueIndex ==
                    (int)ProceduralMeshRenderer.GeneratorKind.Parametric;
                SetVisible(frequency, !isParametric);
                SetVisible(radius, !isParametric);
                SetVisible(parametricTypeField, isParametric);
                SetVisible(parametricFoldout, isParametric);
                if (!isParametric) return;

                HashSet<string> visible = VisibleParametricSettings(
                    (ParametricGeometryType)parametricType.enumValueIndex);
                foreach (KeyValuePair<string, PropertyField> field in parametricFields)
                    SetVisible(field.Value, visible.Contains(field.Key));
            }

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

        private static HashSet<string> VisibleParametricSettings(
            ParametricGeometryType type)
        {
            HashSet<string> fields = new()
            {
                "uSegments",
                "vSegments"
            };

            switch (type)
            {
                case ParametricGeometryType.Plane:
                    fields.UnionWith(new[] { "width", "depth" });
                    break;
                case ParametricGeometryType.Sine:
                    fields.UnionWith(new[]
                    {
                        "width",
                        "depth",
                        "height",
                        "frequency",
                        "phase"
                    });
                    break;
                case ParametricGeometryType.Saddle:
                    fields.UnionWith(new[] { "width", "depth", "height" });
                    break;
                case ParametricGeometryType.Twist:
                    fields.UnionWith(new[] { "width", "depth", "twist" });
                    break;
                case ParametricGeometryType.Cylinder:
                    fields.UnionWith(new[] { "radius", "depth" });
                    break;
                case ParametricGeometryType.Torus:
                    fields.UnionWith(new[] { "radius", "minorRadius" });
                    break;
                case ParametricGeometryType.Mobius:
                    fields.UnionWith(new[] { "width", "radius" });
                    break;
                case ParametricGeometryType.LineTube:
                    fields.UnionWith(new[] { "tubeRadius", "curveLength" });
                    break;
                case ParametricGeometryType.CircleTube:
                    fields.UnionWith(new[] { "tubeRadius", "radius" });
                    break;
                case ParametricGeometryType.HelixTube:
                    fields.UnionWith(new[]
                    {
                        "tubeRadius",
                        "radius",
                        "curveLength"
                    });
                    break;
                case ParametricGeometryType.LissajousTube:
                    fields.UnionWith(new[]
                    {
                        "tubeRadius",
                        "radius",
                        "phase",
                        "lissajousFrequency"
                    });
                    break;
                case ParametricGeometryType.TorusKnotTube:
                    fields.UnionWith(new[]
                    {
                        "tubeRadius",
                        "radius",
                        "minorRadius",
                        "knotP",
                        "knotQ"
                    });
                    break;
            }

            return fields;
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            element.style.display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }
}
