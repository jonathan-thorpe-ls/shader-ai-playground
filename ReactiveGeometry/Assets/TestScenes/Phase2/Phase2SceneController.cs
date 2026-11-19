using UnityEngine;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralMeshRenderer), typeof(UIDocument))]
    public sealed class Phase2SceneController : MonoBehaviour
    {
        [SerializeField] private int impulseVertex;
        [SerializeField] private Vector3 impulseDirection = Vector3.up;
        [SerializeField, Min(0f)] private float impulseMagnitude = 8f;
        [SerializeField] private bool repeatImpulses;
        [SerializeField, Min(0.05f)] private float repeatInterval = 0.5f;
        [SerializeField] private Preset initialPreset = Preset.Nominal;
        [SerializeField] private string sceneTitle = "Dynamic neighbour simulation";
        [SerializeField, TextArea] private string sceneSubtitle =
            "Impulse, recover, and inspect the simulation state.";

        private ProceduralMeshRenderer _renderer;
        private UIDocument _document;
        private Label _diagnostics;
        private Label _sceneTitle;
        private Label _sceneSubtitle;
        private Button _pauseButton;
        private SliderInt _density;
        private Label _densityValue;
        private Slider _stiffness;
        private Slider _restoringStiffness;
        private Slider _damping;
        private float _nextDiagnosticsTime;
        private float _nextImpulseTime;
        private bool _paused;
        private bool _bound;

        public enum Preset
        {
            Gentle,
            Nominal,
            Maximum
        }

        private void Awake()
        {
            _renderer = GetComponent<ProceduralMeshRenderer>();
            _document = GetComponent<UIDocument>();
            _renderer.SetSimulationParameters(ParametersFor(initialPreset));
        }

        private void Start()
        {
            BindUi();
        }

        private void OnDisable()
        {
            UnbindUi();
        }

        private void Update()
        {
            if (repeatImpulses && !_paused && Time.time >= _nextImpulseTime)
            {
                ApplyImpulse();
                _nextImpulseTime = Time.time + repeatInterval;
            }

            if (!_bound || Time.unscaledTime < _nextDiagnosticsTime)
                return;
            _nextDiagnosticsTime = Time.unscaledTime + 0.25f;
            UpdateDiagnostics();
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
            UpdatePauseButton();
        }

        public void SingleStep()
        {
            _renderer.SetSimulationPaused(true);
            _paused = true;
            _renderer.SingleStepSimulation();
            UpdatePauseButton();
        }

        private void BindUi()
        {
            VisualElement root = _document.rootVisualElement;
            _sceneTitle = root.Q<Label>("sceneTitle");
            _sceneSubtitle = root.Q<Label>("sceneSubtitle");
            _diagnostics = root.Q<Label>("diagnostics");
            _pauseButton = root.Q<Button>("pauseButton");
            _density = root.Q<SliderInt>("densitySlider");
            _densityValue = root.Q<Label>("densityValue");
            _stiffness = root.Q<Slider>("stiffnessSlider");
            _restoringStiffness = root.Q<Slider>("restoringStiffnessSlider");
            _damping = root.Q<Slider>("dampingSlider");

            if (_sceneTitle == null || _sceneSubtitle == null || _diagnostics == null ||
                _pauseButton == null || _density == null || _densityValue == null ||
                _stiffness == null || _restoringStiffness == null ||
                _damping == null)
            {
                Debug.LogError("Phase 2 UI Toolkit document is missing required controls.", this);
                enabled = false;
                return;
            }

            _sceneTitle.text = sceneTitle;
            _sceneSubtitle.text = sceneSubtitle;
            _density.SetValueWithoutNotify(_renderer.Frequency);
            _densityValue.text = _renderer.Frequency.ToString();
            _stiffness.SetValueWithoutNotify(_renderer.Parameters.stiffness);
            _restoringStiffness.SetValueWithoutNotify(_renderer.Parameters.restoringStiffness);
            _damping.SetValueWithoutNotify(_renderer.Parameters.damping);
            root.Q<Button>("impulseButton").clicked += ApplyImpulse;
            root.Q<Button>("resetButton").clicked += ResetSimulation;
            _pauseButton.clicked += TogglePause;
            root.Q<Button>("singleStepButton").clicked += SingleStep;
            root.Q<Button>("regenerateButton").clicked += RegenerateMesh;
            root.Q<Button>("gentleButton").clicked += UseGentlePreset;
            root.Q<Button>("nominalButton").clicked += UseNominalPreset;
            root.Q<Button>("maximumButton").clicked += UseMaximumPreset;
            _stiffness.RegisterValueChangedCallback(SetStiffness);
            _restoringStiffness.RegisterValueChangedCallback(SetRestoringStiffness);
            _damping.RegisterValueChangedCallback(SetDamping);
            _density.RegisterValueChangedCallback(SetDensityLabel);
            _bound = true;
            UpdatePauseButton();
            UpdateDiagnostics();
        }

        private void UnbindUi()
        {
            if (!_bound || _document == null)
                return;
            VisualElement root = _document.rootVisualElement;
            if (root == null)
            {
                _bound = false;
                return;
            }
            root.Q<Button>("impulseButton").clicked -= ApplyImpulse;
            root.Q<Button>("resetButton").clicked -= ResetSimulation;
            _pauseButton.clicked -= TogglePause;
            root.Q<Button>("singleStepButton").clicked -= SingleStep;
            root.Q<Button>("regenerateButton").clicked -= RegenerateMesh;
            root.Q<Button>("gentleButton").clicked -= UseGentlePreset;
            root.Q<Button>("nominalButton").clicked -= UseNominalPreset;
            root.Q<Button>("maximumButton").clicked -= UseMaximumPreset;
            _stiffness.UnregisterValueChangedCallback(SetStiffness);
            _restoringStiffness.UnregisterValueChangedCallback(SetRestoringStiffness);
            _damping.UnregisterValueChangedCallback(SetDamping);
            _density.UnregisterValueChangedCallback(SetDensityLabel);
            _bound = false;
        }

        private void UseGentlePreset() => UsePreset(Preset.Gentle);
        private void UseNominalPreset() => UsePreset(Preset.Nominal);
        private void UseMaximumPreset() => UsePreset(Preset.Maximum);

        private void UsePreset(Preset preset)
        {
            SimulationParameters parameters = ParametersFor(preset);
            parameters.paused = _paused;
            _renderer.SetSimulationParameters(parameters);
            initialPreset = preset;
            _stiffness.SetValueWithoutNotify(parameters.stiffness);
            _restoringStiffness.SetValueWithoutNotify(parameters.restoringStiffness);
            _damping.SetValueWithoutNotify(parameters.damping);
            ResetSimulation();
            UpdateDiagnostics();
        }

        private void SetStiffness(ChangeEvent<float> change)
        {
            SimulationParameters parameters = _renderer.Parameters;
            parameters.stiffness = change.newValue;
            _renderer.SetSimulationParameters(parameters);
        }

        private void SetRestoringStiffness(ChangeEvent<float> change)
        {
            SimulationParameters parameters = _renderer.Parameters;
            parameters.restoringStiffness = change.newValue;
            _renderer.SetSimulationParameters(parameters);
        }

        private void SetDamping(ChangeEvent<float> change)
        {
            SimulationParameters parameters = _renderer.Parameters;
            parameters.damping = change.newValue;
            _renderer.SetSimulationParameters(parameters);
        }

        public void RegenerateMesh()
        {
            impulseVertex = Mathf.Clamp(
                impulseVertex, 0, Mathf.Max(0, 10 * _density.value * _density.value + 1));
            _renderer.SetIcosphereSettings(_density.value, _renderer.Radius);
            ResetSimulation();
            UpdateDiagnostics();
        }

        private void SetDensityLabel(ChangeEvent<int> change)
        {
            _densityValue.text = change.newValue.ToString();
        }

        private void UpdatePauseButton()
        {
            if (_pauseButton != null)
                _pauseButton.text = _paused ? "Resume" : "Pause";
        }

        private void UpdateDiagnostics()
        {
            SimulationStatus status = _renderer.ReadSimulationStatus();
            _diagnostics.text =
                $"Vertex {Mathf.Clamp(impulseVertex, 0, Mathf.Max(0, _renderer.VertexCount - 1))}  " +
                $"Mesh {_renderer.VertexCount:N0}V/{_renderer.TriangleCount:N0}T  " +
                $"Steps/frame {_renderer.LastSimulationStepCount}  " +
                $"Dropped {(_renderer.LastDroppedSimulationTime * 1000f):F1} ms\n" +
                $"Speed {status.MaximumSpeed:F2}  Displacement {status.MaximumDisplacement:F2}  " +
                $"Invalid {status.InvalidValueCount}  Clamps V/D " +
                $"{status.ClampedVelocityCount}/{status.ClampedDisplacementCount}";
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
    }
}
