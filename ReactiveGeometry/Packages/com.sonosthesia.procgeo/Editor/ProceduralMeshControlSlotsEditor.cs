using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo
{
    [CustomEditor(typeof(ProceduralMeshControlSlots))]
    public sealed class ProceduralMeshControlSlotsEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            root.Add(new HelpBox(
                "Routes accept pulse amplitude, directional strength, stiffness, "
                + "damping and impulse trigger controls.",
                HelpBoxMessageType.Info));
            root.Add(new Button(
                () => ((ProceduralMeshControlSlots)target).ResetToAuthoringValues())
            {
                text = "Restore Authoring Values"
            });
            return root;
        }
    }
}
