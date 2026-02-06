#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(NeuralNexusController))]
public class NeuralNexusControllerEditor : Editor
{
    private Label activeModeLabel;
    private Button autoRotateButton;
    private Button dataFlowButton;
    private Button trailsButton;
    private Button backgroundButton;

    private static readonly Color EnabledColor = new Color(0.3f, 0.6f, 0.3f);
    private static readonly Color DisabledColor = new Color(0.6f, 0.3f, 0.3f);

    public override VisualElement CreateInspectorGUI()
    {
        VisualElement root = new VisualElement();

        // Draw default inspector
        InspectorElement.FillDefaultInspector(root, serializedObject, this);

        // Add spacing
        root.Add(new VisualElement { style = { height = 10 } });

        // Runtime Controls header
        Label headerLabel = new Label("Runtime Controls");
        headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        headerLabel.style.fontSize = 12;
        headerLabel.style.marginTop = 10;
        headerLabel.style.marginBottom = 5;
        root.Add(headerLabel);

        // Mode Selection foldout
        Foldout modeFoldout = new Foldout { text = "Mode Selection", value = true };
        modeFoldout.style.marginBottom = 5;
        root.Add(modeFoldout);

        // Cycle Mode button
        Button cycleModeButton = new Button(() => GetController().CycleMode()) { text = "Cycle Mode" };
        cycleModeButton.style.height = 30;
        cycleModeButton.style.marginBottom = 5;
        modeFoldout.Add(cycleModeButton);

        // Mode buttons row
        VisualElement modeButtonsRow = new VisualElement();
        modeButtonsRow.style.flexDirection = FlexDirection.Row;
        modeButtonsRow.style.marginBottom = 5;
        modeFoldout.Add(modeButtonsRow);

        string[] modeLabels = { "Mode 1\nHarmonic", "Mode 2\nToroidal", "Mode 3\nNebula", "Mode 4" };
        for (int i = 0; i < 4; i++)
        {
            int modeIndex = i;
            Button modeButton = new Button(() => GetController().SetMode(modeIndex)) { text = modeLabels[i] };
            modeButton.style.height = 40;
            modeButton.style.flexGrow = 1;
            modeButton.style.whiteSpace = WhiteSpace.Normal;
            modeButtonsRow.Add(modeButton);
        }

        // Active mode label
        activeModeLabel = new Label();
        activeModeLabel.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
        activeModeLabel.style.paddingTop = 4;
        activeModeLabel.style.paddingBottom = 4;
        activeModeLabel.style.paddingLeft = 8;
        activeModeLabel.style.paddingRight = 8;
        activeModeLabel.style.borderTopLeftRadius = 3;
        activeModeLabel.style.borderTopRightRadius = 3;
        activeModeLabel.style.borderBottomLeftRadius = 3;
        activeModeLabel.style.borderBottomRightRadius = 3;
        modeFoldout.Add(activeModeLabel);

        // Toggle Controls foldout
        Foldout toggleFoldout = new Foldout { text = "Toggle Controls", value = true };
        toggleFoldout.style.marginTop = 5;
        root.Add(toggleFoldout);

        // First row of toggles
        VisualElement toggleRow1 = new VisualElement();
        toggleRow1.style.flexDirection = FlexDirection.Row;
        toggleRow1.style.marginBottom = 3;
        toggleFoldout.Add(toggleRow1);

        autoRotateButton = CreateToggleButton("Auto Rotate", "autoRotate", () => GetController().AutoRotate = !GetController().AutoRotate);
        dataFlowButton = CreateToggleButton("Data Flow", "dataFlow", () => GetController().DataFlow = !GetController().DataFlow);
        toggleRow1.Add(autoRotateButton);
        toggleRow1.Add(dataFlowButton);

        // Second row of toggles
        VisualElement toggleRow2 = new VisualElement();
        toggleRow2.style.flexDirection = FlexDirection.Row;
        toggleRow2.style.marginBottom = 3;
        toggleFoldout.Add(toggleRow2);

        trailsButton = CreateToggleButton("Trails", "trailsVisible", () => GetController().TrailsVisible = !GetController().TrailsVisible);
        backgroundButton = CreateToggleButton("Background", "backgroundVisible", () => GetController().BackgroundVisible = !GetController().BackgroundVisible);
        toggleRow2.Add(trailsButton);
        toggleRow2.Add(backgroundButton);

        // Glow buttons row
        VisualElement glowRow = new VisualElement();
        glowRow.style.flexDirection = FlexDirection.Row;
        toggleFoldout.Add(glowRow);

        Button enableGlowButton = new Button(() => GetController().GlowEnabled = true) { text = "Enable Glow" };
        enableGlowButton.style.height = 28;
        enableGlowButton.style.flexGrow = 1;
        glowRow.Add(enableGlowButton);

        Button disableGlowButton = new Button(() => GetController().GlowEnabled = false) { text = "Disable Glow" };
        disableGlowButton.style.height = 28;
        disableGlowButton.style.flexGrow = 1;
        glowRow.Add(disableGlowButton);

        // Schedule periodic updates for play mode
        root.schedule.Execute(UpdateUI).Every(100);

        return root;
    }

    private Button CreateToggleButton(string label, string propertyName, System.Action onClick)
    {
        Button button = new Button(() =>
        {
            onClick?.Invoke();
            serializedObject.Update();
            UpdateToggleButtonStyle(button, propertyName);
        })
        { text = label };

        button.style.height = 28;
        button.style.flexGrow = 1;
        UpdateToggleButtonStyle(button, propertyName);

        return button;
    }

    private void UpdateToggleButtonStyle(Button button, string propertyName)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        bool isEnabled = prop != null && prop.boolValue;
        button.style.backgroundColor = isEnabled ? EnabledColor : DisabledColor;
    }

    private void UpdateUI()
    {
        if (target == null) return;

        serializedObject.Update();

        NeuralNexusController controller = GetController();
        activeModeLabel.text = $"Active Mode: {controller.GetActiveModeLabel()}";

        UpdateToggleButtonStyle(autoRotateButton, "autoRotate");
        UpdateToggleButtonStyle(dataFlowButton, "dataFlow");
        UpdateToggleButtonStyle(trailsButton, "trailsVisible");
        UpdateToggleButtonStyle(backgroundButton, "backgroundVisible");
    }

    private NeuralNexusController GetController()
    {
        return (NeuralNexusController)target;
    }
}
#endif
