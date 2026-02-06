using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Handles input for the MorphingWireframeController using Unity's new Input System.
/// Triggers pulses on left mouse click at the current mouse position.
/// </summary>
public class MorphingWireframeInput : MonoBehaviour
{
    [SerializeField]
    private MorphingWireframeController controller;

    public MorphingWireframeController Controller => controller;

    private void Reset()
    {
        controller = GetComponent<MorphingWireframeController>();
    }

    private void Update()
    {
        if (controller == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePosition = mouse.position.ReadValue();
            controller.TriggerPulse(mousePosition);
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(MorphingWireframeInput))]
public class MorphingWireframeInputEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MorphingWireframeInput input = (MorphingWireframeInput)target;

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(input.Controller == null))
        {
            if (GUILayout.Button("Toggle Morph"))
            {
                input.Controller.ToggleMorph();
            }
        }
    }
}
#endif
