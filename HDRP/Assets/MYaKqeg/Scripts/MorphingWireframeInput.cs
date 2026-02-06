using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles input for the MorphingWireframeController using Unity's new Input System.
/// Triggers pulses on left mouse click at the current mouse position.
/// </summary>
public class MorphingWireframeInput : MonoBehaviour
{
    [SerializeField]
    private MorphingWireframeController controller;

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
