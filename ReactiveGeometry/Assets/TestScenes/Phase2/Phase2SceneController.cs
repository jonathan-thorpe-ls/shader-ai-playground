using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralMeshRenderer))]
    public sealed class Phase2SceneController : MonoBehaviour
    {
        [SerializeField] private int impulseVertex;
        [SerializeField] private Vector3 impulseDirection = Vector3.up;
        [SerializeField, Min(0f)] private float impulseMagnitude = 2f;
        [SerializeField] private bool repeatImpulses;
        [SerializeField, Min(0.05f)] private float repeatInterval = 0.5f;
        [SerializeField] private Preset initialPreset = Preset.Nominal;

        private ProceduralMeshRenderer _renderer;
        private TMPro.TextMeshProUGUI _diagnostics;
        private float _nextDiagnosticsTime;
        private float _nextImpulseTime;
        private bool _paused;
        private readonly List<GameObject> _ownedObjects = new();

        public enum Preset
        {
            Gentle,
            Nominal,
            Maximum
        }

        private void Awake()
        {
            _renderer = GetComponent<ProceduralMeshRenderer>();
            _renderer.SetSimulationParameters(ParametersFor(initialPreset));
        }

        private void Start()
        {
            BuildUi();
        }

        private void Update()
        {
            if (repeatImpulses && !_paused && Time.time >= _nextImpulseTime)
            {
                ApplyImpulse();
                _nextImpulseTime = Time.time + repeatInterval;
            }

            if (_diagnostics == null || Time.unscaledTime < _nextDiagnosticsTime)
                return;
            _nextDiagnosticsTime = Time.unscaledTime + 0.25f;
            SimulationStatus status = _renderer.ReadSimulationStatus();
            _diagnostics.text =
                $"Vertex {Mathf.Clamp(impulseVertex, 0, Mathf.Max(0, _renderer.VertexCount - 1))}  " +
                $"Steps/frame {_renderer.LastSimulationStepCount}  " +
                $"Dropped {(_renderer.LastDroppedSimulationTime * 1000f):F1} ms\n" +
                $"Speed {status.MaximumSpeed:F2}  Displacement {status.MaximumDisplacement:F2}  " +
                $"Invalid {status.InvalidValueCount}  Clamps V/D " +
                $"{status.ClampedVelocityCount}/{status.ClampedDisplacementCount}";
        }

        public void ApplyImpulse()
        {
            if (!_renderer.IsGenerated) return;
            impulseVertex = Mathf.Clamp(impulseVertex, 0, _renderer.VertexCount - 1);
            _renderer.ApplyImpulse(impulseVertex, impulseDirection, impulseMagnitude);
        }

        public void ResetSimulation() => _renderer.ResetSimulation();

        public void TogglePause()
        {
            _paused = !_paused;
            _renderer.SetSimulationPaused(_paused);
        }

        public void SingleStep()
        {
            _renderer.SetSimulationPaused(true);
            _paused = true;
            _renderer.SingleStepSimulation();
        }

        public void UseGentlePreset() => UsePreset(Preset.Gentle);
        public void UseNominalPreset() => UsePreset(Preset.Nominal);
        public void UseMaximumPreset() => UsePreset(Preset.Maximum);

        private void UsePreset(Preset preset)
        {
            SimulationParameters parameters = ParametersFor(preset);
            parameters.paused = _paused;
            _renderer.SetSimulationParameters(parameters);
            initialPreset = preset;
            ResetSimulation();
        }

        private void BuildUi()
        {
            EnsureEventSystem();
            Canvas canvas = CreateCanvas();
            GameObject panel = CreateUiObject("Phase2Controls", canvas.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16f, -16f);
            panelRect.sizeDelta = new Vector2(410f, 520f);
            UnityEngine.UI.Image background = panel.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(0.03f, 0.05f, 0.08f, 0.92f);
            background.raycastTarget = false;
            UnityEngine.UI.VerticalLayoutGroup layout =
                panel.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            _diagnostics = CreateText(panel.transform, "Phase 2 Diagnostics", 18);
            CreateButton(panel.transform, "Impulse", ApplyImpulse);
            CreateButton(panel.transform, "Reset", ResetSimulation);
            CreateButton(panel.transform, "Pause / Resume", TogglePause);
            CreateButton(panel.transform, "Single Step", SingleStep);
            CreateButton(panel.transform, "Gentle", UseGentlePreset);
            CreateButton(panel.transform, "Nominal", UseNominalPreset);
            CreateButton(panel.transform, "Maximum", UseMaximumPreset);
            CreateParameterSlider(panel.transform, "Structural stiffness", 0f, 240f,
                _renderer.Parameters.stiffness, value =>
                {
                    SimulationParameters parameters = _renderer.Parameters;
                    parameters.stiffness = value;
                    _renderer.SetSimulationParameters(parameters);
                });
            CreateParameterSlider(panel.transform, "Restoring stiffness", 0f, 30f,
                _renderer.Parameters.restoringStiffness, value =>
                {
                    SimulationParameters parameters = _renderer.Parameters;
                    parameters.restoringStiffness = value;
                    _renderer.SetSimulationParameters(parameters);
                });
            CreateParameterSlider(panel.transform, "Damping", 0f, 16f,
                _renderer.Parameters.damping, value =>
                {
                    SimulationParameters parameters = _renderer.Parameters;
                    parameters.damping = value;
                    _renderer.SetSimulationParameters(parameters);
                });
        }

        private Canvas CreateCanvas()
        {
            GameObject canvasObject = CreateUiObject("Phase2Canvas", null);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private void CreateButton(
            Transform parent,
            string label,
            UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = CreateUiObject(label.Replace(" ", string.Empty), parent);
            UnityEngine.UI.Image image = buttonObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.14f, 0.34f, 0.48f, 1f);
            UnityEngine.UI.Button button = buttonObject.AddComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(action);
            UnityEngine.UI.LayoutElement element =
                buttonObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.minHeight = 30f;
            element.preferredHeight = 30f;
            TMPro.TextMeshProUGUI text = CreateText(buttonObject.transform, label, 16);
            text.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private void CreateParameterSlider(
            Transform parent,
            string label,
            float minimum,
            float maximum,
            float current,
            UnityEngine.Events.UnityAction<float> action)
        {
            CreateText(parent, label, 14);
            GameObject sliderObject = CreateUiObject(label.Replace(" ", string.Empty) + "Slider", parent);
            UnityEngine.UI.LayoutElement element =
                sliderObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.minHeight = 22f;
            element.preferredHeight = 22f;
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.sizeDelta = new Vector2(0f, 22f);
            UnityEngine.UI.Slider slider = sliderObject.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.SetValueWithoutNotify(Mathf.Clamp(current, minimum, maximum));
            slider.onValueChanged.AddListener(action);

            GameObject background = CreateUiObject("Background", sliderObject.transform);
            Stretch(background.GetComponent<RectTransform>());
            background.AddComponent<UnityEngine.UI.Image>().color =
                new Color(0.12f, 0.14f, 0.16f, 1f);
            GameObject fillArea = CreateUiObject("FillArea", sliderObject.transform);
            Stretch(fillArea.GetComponent<RectTransform>());
            GameObject fill = CreateUiObject("Fill", fillArea.transform);
            Stretch(fill.GetComponent<RectTransform>());
            UnityEngine.UI.Image fillImage = fill.AddComponent<UnityEngine.UI.Image>();
            fillImage.color = new Color(0.2f, 0.72f, 0.9f, 1f);
            GameObject handle = CreateUiObject("Handle", sliderObject.transform);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(16f, 22f);
            UnityEngine.UI.Image handleImage = handle.AddComponent<UnityEngine.UI.Image>();
            handleImage.color = Color.white;
            slider.targetGraphic = handleImage;
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRect;
        }

        private TMPro.TextMeshProUGUI CreateText(Transform parent, string value, int fontSize)
        {
            GameObject textObject = CreateUiObject("Text", parent);
            TMPro.TextMeshProUGUI text = textObject.AddComponent<TMPro.TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TMPro.TextWrappingModes.Normal;
            UnityEngine.UI.LayoutElement layout =
                textObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = fontSize + 8f;
            return text;
        }

        private GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject result = new(name, typeof(RectTransform));
            if (parent != null)
                result.transform.SetParent(parent, false);
            _ownedObjects.Add(result);
            return result;
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
                return;
            GameObject eventSystem = new(
                "EventSystem",
                typeof(EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            _ownedObjects.Add(eventSystem);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static SimulationParameters ParametersFor(Preset preset)
        {
            return preset switch
            {
                Preset.Gentle => SimulationParameters.Gentle,
                Preset.Maximum => SimulationParameters.Maximum,
                _ => SimulationParameters.Nominal
            };
        }

        private void OnDestroy()
        {
            for (int index = _ownedObjects.Count - 1; index >= 0; index--)
            {
                if (_ownedObjects[index] != null)
                    Destroy(_ownedObjects[index]);
            }
            _ownedObjects.Clear();
        }
    }
}
