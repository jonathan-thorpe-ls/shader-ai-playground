using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo.Editor
{
    [CustomEditor(typeof(NoiseDeformationSource))]
    public sealed class NoiseDeformationSourceEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            root.Add(new HelpBox(
                "Move this GameObject with Scene view gizmos. Its position is " +
                "the deformation centre; parameters drive a GPU force field.",
                HelpBoxMessageType.Info));
            return root;
        }
    }

    [CustomEditor(typeof(ImpulseWaveSource))]
    public sealed class ImpulseWaveSourceEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            Label active = new();
            root.Add(active);
            root.Add(new Button(TriggerImpulse) { text = "Trigger Impulse" });
            root.Add(new Button(TriggerWave) { text = "Trigger Wave" });
            root.Add(new Button(ResetEvents) { text = "Reset Active Events" });
            root.schedule.Execute(UpdateStatus).Every(250);
            return root;

            void TriggerImpulse()
            {
                Undo.RecordObject(target, "Trigger Deformation Impulse");
                ((ImpulseWaveSource)target).TriggerImpulse();
            }

            void TriggerWave()
            {
                Undo.RecordObject(target, "Trigger Deformation Wave");
                ((ImpulseWaveSource)target).TriggerWave();
            }

            void ResetEvents()
            {
                Undo.RecordObject(target, "Reset Deformation Events");
                ((ImpulseWaveSource)target).ResetEvents();
            }

            void UpdateStatus()
            {
                if (target != null)
                    active.text =
                        $"Active events: {((ImpulseWaveSource)target).ActiveEventCount}";
            }
        }
    }
}
