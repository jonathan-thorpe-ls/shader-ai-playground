using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo.Editor
{
    [CustomEditor(typeof(NoiseDeformationSource))]
    public sealed class NoiseDeformationSourceEditor : UnityEditor.Editor
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

            AddProperty(serializedObject.FindProperty("target"));
            SerializedProperty settings =
                serializedObject.FindProperty("settings");
            Foldout settingsFoldout = new() { text = "Noise Settings" };
            root.Add(settingsFoldout);
            AddProperty(settings.FindPropertyRelative("noise"), settingsFoldout);
            AddProperty(settings.FindPropertyRelative("output"), settingsFoldout);
            AddProperty(
                settings.FindPropertyRelative("spatialFrequency"),
                settingsFoldout);
            AddProperty(settings.FindPropertyRelative("axisScale"), settingsFoldout);
            AddProperty(
                settings.FindPropertyRelative("temporalSpeed"),
                settingsFoldout);
            AddProperty(settings.FindPropertyRelative("timePhase"), settingsFoldout);
            AddProperty(settings.FindPropertyRelative("amplitude"), settingsFoldout);
            AddProperty(settings.FindPropertyRelative("seed"), settingsFoldout);
            AddProperty(
                settings.FindPropertyRelative("octaves"),
                settingsFoldout);
            PropertyField lacunarity = AddProperty(
                settings.FindPropertyRelative("lacunarity"),
                settingsFoldout);
            PropertyField gain = AddProperty(
                settings.FindPropertyRelative("gain"),
                settingsFoldout);

            SerializedProperty source =
                settings.FindPropertyRelative("source");
            Foldout sourceFoldout = new() { text = "Spatial Source" };
            settingsFoldout.Add(sourceFoldout);
            AddProperty(
                source.FindPropertyRelative("enabled"),
                sourceFoldout);
            PropertyField innerRadius = AddProperty(
                source.FindPropertyRelative("innerRadius"),
                sourceFoldout);
            PropertyField outerRadius = AddProperty(
                source.FindPropertyRelative("outerRadius"),
                sourceFoldout);
            PropertyField weight = AddProperty(
                source.FindPropertyRelative("weight"),
                sourceFoldout);
            PropertyField falloff = AddProperty(
                source.FindPropertyRelative("falloff"),
                sourceFoldout);
            AddProperty(serializedObject.FindProperty("drawFalloff"));

            root.Add(new HelpBox(
                "Move this GameObject with Scene view gizmos. Its position is " +
                "the deformation centre; parameters drive a GPU force field.",
                HelpBoxMessageType.Info));

            root.Bind(serializedObject);
            root.TrackSerializedObjectValue(serializedObject, _ => UpdateVisibility());
            UpdateVisibility();
            return root;

            void UpdateVisibility()
            {
                bool multipleOctaves =
                    settings.FindPropertyRelative("octaves").intValue > 1;
                InspectorVisibility.SetVisible(lacunarity, multipleOctaves);
                InspectorVisibility.SetVisible(gain, multipleOctaves);

                bool sourceEnabled =
                    source.FindPropertyRelative("enabled").boolValue;
                bool hasInnerRadius =
                    source.FindPropertyRelative("falloff").enumValueIndex !=
                    (int)SpatialFalloffMode.Constant;
                InspectorVisibility.SetVisible(
                    innerRadius,
                    sourceEnabled && hasInnerRadius);
                InspectorVisibility.SetVisible(outerRadius, sourceEnabled);
                InspectorVisibility.SetVisible(weight, sourceEnabled);
                InspectorVisibility.SetVisible(falloff, sourceEnabled);
            }
        }
    }

    [CustomEditor(typeof(ImpulseWaveSource))]
    public sealed class ImpulseWaveSourceEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new();
            PropertyField AddProperty(string name)
            {
                PropertyField field =
                    new(serializedObject.FindProperty(name));
                root.Add(field);
                return field;
            }

            AddProperty("target");
            AddProperty("eventCapacity");
            Foldout impulse = new() { text = "Impulse" };
            root.Add(impulse);
            impulse.Add(new PropertyField(
                serializedObject.FindProperty("impulseAmplitude")));
            impulse.Add(new PropertyField(
                serializedObject.FindProperty("impulseRadius")));
            impulse.Add(new PropertyField(
                serializedObject.FindProperty("impulseDirection")));

            Foldout wave = new() { text = "Wave" };
            root.Add(wave);
            wave.Add(new PropertyField(
                serializedObject.FindProperty("waveAmplitude")));
            wave.Add(new PropertyField(
                serializedObject.FindProperty("waveSpeed")));
            wave.Add(new PropertyField(
                serializedObject.FindProperty("waveWidth")));
            wave.Add(new PropertyField(
                serializedObject.FindProperty("waveDuration")));
            wave.Add(new PropertyField(
                serializedObject.FindProperty("waveAttenuation")));
            wave.Add(new PropertyField(
                serializedObject.FindProperty("waveFrequency")));
            PropertyField wavePhase = new(
                serializedObject.FindProperty("wavePhase"));
            wave.Add(wavePhase);
            wave.Add(new PropertyField(
                serializedObject.FindProperty("waveDirection")));
            AddProperty("drawEventRanges");

            Label active = new();
            root.Add(active);
            root.Add(new Button(TriggerImpulse) { text = "Trigger Impulse" });
            root.Add(new Button(TriggerWave) { text = "Trigger Wave" });
            root.Add(new Button(ResetEvents) { text = "Reset Active Events" });
            root.Bind(serializedObject);
            root.TrackSerializedObjectValue(serializedObject, _ => UpdateVisibility());
            UpdateVisibility();
            root.schedule.Execute(UpdateStatus).Every(250);
            return root;

            void UpdateVisibility()
            {
                InspectorVisibility.SetVisible(
                    wavePhase,
                    !Mathf.Approximately(
                        serializedObject.FindProperty("waveFrequency").floatValue,
                        0f));
            }

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

    internal static class InspectorVisibility
    {
        public static void SetVisible(VisualElement element, bool visible)
        {
            element.style.display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }
}
