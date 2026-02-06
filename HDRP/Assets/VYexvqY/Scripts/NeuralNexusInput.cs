using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Companion input handler for NeuralNexusController.
/// Uses Unity's new Input System to map inputs to the controller's public API.
/// </summary>
[RequireComponent(typeof(NeuralNexusController))]
public class NeuralNexusInput : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private InputActionReference cycleModeAction;
    [SerializeField] private InputActionReference mode1Action;
    [SerializeField] private InputActionReference mode2Action;
    [SerializeField] private InputActionReference mode3Action;
    [SerializeField] private InputActionReference mode4Action;
    [SerializeField] private InputActionReference toggleRotationAction;
    [SerializeField] private InputActionReference toggleDataFlowAction;
    [SerializeField] private InputActionReference toggleTrailsAction;
    [SerializeField] private InputActionReference toggleBackgroundAction;
    [SerializeField] private InputActionReference toggleGlowAction;

    [Header("Default Key Bindings (if no InputActionAsset assigned)")]
    [SerializeField] private bool useDefaultBindings = true;

    private NeuralNexusController controller;

    // Default input actions when no InputActionAsset is assigned
    private InputAction defaultCycleModeAction;
    private InputAction defaultMode1Action;
    private InputAction defaultMode2Action;
    private InputAction defaultMode3Action;
    private InputAction defaultMode4Action;
    private InputAction defaultToggleRotationAction;
    private InputAction defaultToggleDataFlowAction;
    private InputAction defaultToggleTrailsAction;
    private InputAction defaultToggleBackgroundAction;
    private InputAction defaultToggleGlowAction;

    private void Awake()
    {
        controller = GetComponent<NeuralNexusController>();
        
        if (useDefaultBindings)
        {
            CreateDefaultActions();
        }
    }

    private void CreateDefaultActions()
    {
        defaultCycleModeAction = new InputAction("CycleMode", InputActionType.Button);
        defaultCycleModeAction.AddBinding("<Keyboard>/space");
        defaultCycleModeAction.AddBinding("<Gamepad>/buttonSouth");

        defaultMode1Action = new InputAction("Mode1", InputActionType.Button);
        defaultMode1Action.AddBinding("<Keyboard>/1");
        defaultMode1Action.AddBinding("<Keyboard>/numpad1");

        defaultMode2Action = new InputAction("Mode2", InputActionType.Button);
        defaultMode2Action.AddBinding("<Keyboard>/2");
        defaultMode2Action.AddBinding("<Keyboard>/numpad2");

        defaultMode3Action = new InputAction("Mode3", InputActionType.Button);
        defaultMode3Action.AddBinding("<Keyboard>/3");
        defaultMode3Action.AddBinding("<Keyboard>/numpad3");

        defaultMode4Action = new InputAction("Mode4", InputActionType.Button);
        defaultMode4Action.AddBinding("<Keyboard>/4");
        defaultMode4Action.AddBinding("<Keyboard>/numpad4");

        defaultToggleRotationAction = new InputAction("ToggleRotation", InputActionType.Button);
        defaultToggleRotationAction.AddBinding("<Keyboard>/r");

        defaultToggleDataFlowAction = new InputAction("ToggleDataFlow", InputActionType.Button);
        defaultToggleDataFlowAction.AddBinding("<Keyboard>/f");

        defaultToggleTrailsAction = new InputAction("ToggleTrails", InputActionType.Button);
        defaultToggleTrailsAction.AddBinding("<Keyboard>/t");

        defaultToggleBackgroundAction = new InputAction("ToggleBackground", InputActionType.Button);
        defaultToggleBackgroundAction.AddBinding("<Keyboard>/b");

        defaultToggleGlowAction = new InputAction("ToggleGlow", InputActionType.Button);
        defaultToggleGlowAction.AddBinding("<Keyboard>/g");
    }

    private void OnEnable()
    {
        SubscribeAction(cycleModeAction, defaultCycleModeAction, OnCycleMode);
        SubscribeAction(mode1Action, defaultMode1Action, OnMode1);
        SubscribeAction(mode2Action, defaultMode2Action, OnMode2);
        SubscribeAction(mode3Action, defaultMode3Action, OnMode3);
        SubscribeAction(mode4Action, defaultMode4Action, OnMode4);
        SubscribeAction(toggleRotationAction, defaultToggleRotationAction, OnToggleRotation);
        SubscribeAction(toggleDataFlowAction, defaultToggleDataFlowAction, OnToggleDataFlow);
        SubscribeAction(toggleTrailsAction, defaultToggleTrailsAction, OnToggleTrails);
        SubscribeAction(toggleBackgroundAction, defaultToggleBackgroundAction, OnToggleBackground);
        SubscribeAction(toggleGlowAction, defaultToggleGlowAction, OnToggleGlow);
    }

    private void OnDisable()
    {
        UnsubscribeAction(cycleModeAction, defaultCycleModeAction, OnCycleMode);
        UnsubscribeAction(mode1Action, defaultMode1Action, OnMode1);
        UnsubscribeAction(mode2Action, defaultMode2Action, OnMode2);
        UnsubscribeAction(mode3Action, defaultMode3Action, OnMode3);
        UnsubscribeAction(mode4Action, defaultMode4Action, OnMode4);
        UnsubscribeAction(toggleRotationAction, defaultToggleRotationAction, OnToggleRotation);
        UnsubscribeAction(toggleDataFlowAction, defaultToggleDataFlowAction, OnToggleDataFlow);
        UnsubscribeAction(toggleTrailsAction, defaultToggleTrailsAction, OnToggleTrails);
        UnsubscribeAction(toggleBackgroundAction, defaultToggleBackgroundAction, OnToggleBackground);
        UnsubscribeAction(toggleGlowAction, defaultToggleGlowAction, OnToggleGlow);
    }

    private void Start()
    {
        // State is managed by the controller, no sync needed
    }

    private void OnDestroy()
    {
        // Dispose default actions
        defaultCycleModeAction?.Dispose();
        defaultMode1Action?.Dispose();
        defaultMode2Action?.Dispose();
        defaultMode3Action?.Dispose();
        defaultMode4Action?.Dispose();
        defaultToggleRotationAction?.Dispose();
        defaultToggleDataFlowAction?.Dispose();
        defaultToggleTrailsAction?.Dispose();
        defaultToggleBackgroundAction?.Dispose();
        defaultToggleGlowAction?.Dispose();
    }

    private void SubscribeAction(InputActionReference reference, InputAction defaultAction, System.Action<InputAction.CallbackContext> callback)
    {
        InputAction action = reference != null ? reference.action : (useDefaultBindings ? defaultAction : null);
        if (action != null)
        {
            action.performed += callback;
            action.Enable();
        }
    }

    private void UnsubscribeAction(InputActionReference reference, InputAction defaultAction, System.Action<InputAction.CallbackContext> callback)
    {
        InputAction action = reference != null ? reference.action : (useDefaultBindings ? defaultAction : null);
        if (action != null)
        {
            action.performed -= callback;
            action.Disable();
        }
    }

    private void OnCycleMode(InputAction.CallbackContext context)
    {
        controller.CycleMode();
    }

    private void OnMode1(InputAction.CallbackContext context)
    {
        controller.SetMode(0);
    }

    private void OnMode2(InputAction.CallbackContext context)
    {
        controller.SetMode(1);
    }

    private void OnMode3(InputAction.CallbackContext context)
    {
        controller.SetMode(2);
    }

    private void OnMode4(InputAction.CallbackContext context)
    {
        controller.SetMode(3);
    }

    private void OnToggleRotation(InputAction.CallbackContext context)
    {
        controller.AutoRotate = !controller.AutoRotate;
    }

    private void OnToggleDataFlow(InputAction.CallbackContext context)
    {
        controller.DataFlow = !controller.DataFlow;
    }

    private void OnToggleTrails(InputAction.CallbackContext context)
    {
        controller.TrailsVisible = !controller.TrailsVisible;
    }

    private void OnToggleBackground(InputAction.CallbackContext context)
    {
        controller.BackgroundVisible = !controller.BackgroundVisible;
    }

    private void OnToggleGlow(InputAction.CallbackContext context)
    {
        controller.GlowEnabled = !controller.GlowEnabled;
    }

    /// <summary>
    /// Cycle to the next visualization mode.
    /// </summary>
    public void CycleMode()
    {
        controller.CycleMode();
    }

    /// <summary>
    /// Set a specific visualization mode by index.
    /// </summary>
    public void SetMode(int modeIndex)
    {
        controller.SetMode(modeIndex);
    }

    /// <summary>
    /// Toggle auto rotation on/off.
    /// </summary>
    public void ToggleAutoRotate()
    {
        controller.AutoRotate = !controller.AutoRotate;
    }

    /// <summary>
    /// Toggle data flow animation on/off.
    /// </summary>
    public void ToggleDataFlow()
    {
        controller.DataFlow = !controller.DataFlow;
    }

    /// <summary>
    /// Toggle trail visibility on/off.
    /// </summary>
    public void ToggleTrails()
    {
        controller.TrailsVisible = !controller.TrailsVisible;
    }

    /// <summary>
    /// Toggle background visibility on/off.
    /// </summary>
    public void ToggleBackground()
    {
        controller.BackgroundVisible = !controller.BackgroundVisible;
    }

    /// <summary>
    /// Toggle glow effect on/off.
    /// </summary>
    public void ToggleGlow()
    {
        controller.GlowEnabled = !controller.GlowEnabled;
    }
}
