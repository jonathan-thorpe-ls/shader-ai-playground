using UnityEngine;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    [RequireComponent(
        typeof(ProceduralMeshRenderer),
        typeof(GeometryDebugOverlay),
        typeof(UIDocument))]
    public sealed class Phase3SceneController : MonoBehaviour
    {
        [SerializeField] private SceneMode sceneMode = SceneMode.GraphDistance;
        [SerializeField] private string sceneTitle = "Fields and debugging";
        [SerializeField, TextArea] private string sceneSubtitle =
            "Configure metadata, fields, and debug views.";
        [SerializeField] private GeometryFieldSettings fieldSettings =
            GeometryFieldSettings.Default;

        private ProceduralMeshRenderer _renderer;
        private GeometryDebugOverlay _debug;
        private UIDocument _document;
        private SliderInt _density;
        private SliderInt _source;
        private Slider _amplitude;
        private Slider _width;
        private Slider _pulseRate;
        private Toggle _pulse;
        private Toggle _directional;
        private Toggle _wireframe;
        private Toggle _vertices;
        private Toggle _normals;
        private Label _diagnostics;
        private Label _inspection;
        private Label _pauseButtonLabel;
        private Button _pauseButton;
        private bool _paused;
        private bool _bound;
        private float _nextDiagnosticsTime;
        private VisualElement _root;
        private DebugView _debugView = DebugView.GraphDistance;

        public enum SceneMode
        {
            GraphDistance,
            FieldComposition,
            DebugInspection
        }

        private enum DebugView
        {
            Lit,
            GraphDistance,
            Curvature
        }

        private void Awake()
        {
            _renderer = GetComponent<ProceduralMeshRenderer>();
            _debug = GetComponent<GeometryDebugOverlay>();
            _document = GetComponent<UIDocument>();
        }

        private void Start()
        {
            ConfigureForScene();
            BindUi();
            ApplyConfiguration();
        }

        private void Update()
        {
            if (!_bound || Time.unscaledTime < _nextDiagnosticsTime)
                return;
            _nextDiagnosticsTime = Time.unscaledTime + 0.25f;
            SimulationStatus status = _renderer.ReadSimulationStatus();
            _diagnostics.text =
                $"Source {fieldSettings.graphSourceVertex}  " +
                $"Speed {status.MaximumSpeed:F2}  " +
                $"Displacement {status.MaximumDisplacement:F2}\n" +
                $"Invalid {status.InvalidValueCount}  " +
                $"Clamps V/D " +
                $"{status.ClampedVelocityCount}/{status.ClampedDisplacementCount}";
        }

        private void OnDisable()
        {
            UnbindUi();
        }

        private void ConfigureForScene()
        {
            fieldSettings.fieldsEnabled = true;
            fieldSettings.graphSourceVertex = Mathf.Clamp(
                fieldSettings.graphSourceVertex,
                0,
                Mathf.Max(0, _renderer.VertexCount - 1));
            fieldSettings.pulseEnabled =
                sceneMode != SceneMode.GraphDistance;
            fieldSettings.directionalEnabled =
                sceneMode == SceneMode.DebugInspection;
        }

        private void BindUi()
        {
            VisualElement root = _document.rootVisualElement;
            _root = root.Q<VisualElement>("root");
            _density = root.Q<SliderInt>("densitySlider");
            _source = root.Q<SliderInt>("sourceSlider");
            _amplitude = root.Q<Slider>("amplitudeSlider");
            _width = root.Q<Slider>("widthSlider");
            _pulseRate = root.Q<Slider>("pulseRateSlider");
            _pulse = root.Q<Toggle>("pulseToggle");
            _directional = root.Q<Toggle>("directionalToggle");
            _wireframe = root.Q<Toggle>("wireframeToggle");
            _vertices = root.Q<Toggle>("verticesToggle");
            _normals = root.Q<Toggle>("normalsToggle");
            _diagnostics = root.Q<Label>("diagnostics");
            _inspection = root.Q<Label>("inspection");
            _pauseButton = root.Q<Button>("pauseButton");
            if (_density == null || _source == null ||
                _amplitude == null || _width == null ||
                _pulseRate == null || _pulse == null || _directional == null ||
                _wireframe == null || _vertices == null || _normals == null ||
                _diagnostics == null || _inspection == null || _pauseButton == null)
            {
                Debug.LogError("Phase 3 UI Toolkit document is missing controls.", this);
                enabled = false;
                return;
            }

            root.Q<Label>("sceneTitle").text = sceneTitle;
            root.Q<Label>("sceneSubtitle").text = sceneSubtitle;
            _density.SetValueWithoutNotify(_renderer.Frequency);
            _source.highValue = Mathf.Max(0, _renderer.VertexCount - 1);
            _source.SetValueWithoutNotify(fieldSettings.graphSourceVertex);
            _amplitude.SetValueWithoutNotify(fieldSettings.pulseAmplitude);
            _width.SetValueWithoutNotify(fieldSettings.pulseWidth);
            _pulseRate.SetValueWithoutNotify(fieldSettings.pulseFrequency);
            _pulse.SetValueWithoutNotify(fieldSettings.pulseEnabled);
            _directional.SetValueWithoutNotify(fieldSettings.directionalEnabled);
            _wireframe.SetValueWithoutNotify(false);
            _vertices.SetValueWithoutNotify(false);
            _normals.SetValueWithoutNotify(false);
            _debugView = sceneMode == SceneMode.FieldComposition
                ? DebugView.Lit
                : DebugView.GraphDistance;
            _pauseButtonLabel = new Label("Pause")
            {
                pickingMode = PickingMode.Ignore
            };
            _pauseButton.Add(_pauseButtonLabel);
            _pauseButton.text = string.Empty;

            root.Q<Button>("applyButton").clicked += ApplyConfiguration;
            root.Q<Button>("resetButton").clicked += ResetSimulation;
            root.Q<Button>("inspectButton").clicked += InspectSource;
            root.Q<Button>("litButton").clicked += UseLitView;
            root.Q<Button>("distanceButton").clicked += UseDistanceView;
            root.Q<Button>("curvatureButton").clicked += UseCurvatureView;
            _pauseButton.clicked += TogglePause;
            _wireframe.RegisterValueChangedCallback(SetWireframe);
            _vertices.RegisterValueChangedCallback(SetVertices);
            _normals.RegisterValueChangedCallback(SetNormals);
            _root.RegisterCallback<PointerDownEvent>(PickFromPointer);
            _bound = true;
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
            root.Q<Button>("applyButton").clicked -= ApplyConfiguration;
            root.Q<Button>("resetButton").clicked -= ResetSimulation;
            root.Q<Button>("inspectButton").clicked -= InspectSource;
            root.Q<Button>("litButton").clicked -= UseLitView;
            root.Q<Button>("distanceButton").clicked -= UseDistanceView;
            root.Q<Button>("curvatureButton").clicked -= UseCurvatureView;
            _pauseButton.clicked -= TogglePause;
            _wireframe.UnregisterValueChangedCallback(SetWireframe);
            _vertices.UnregisterValueChangedCallback(SetVertices);
            _normals.UnregisterValueChangedCallback(SetNormals);
            _root.UnregisterCallback<PointerDownEvent>(PickFromPointer);
            _bound = false;
        }

        private void ApplyConfiguration()
        {
            bool densityChanged = _density.value != _renderer.Frequency;
            if (_source != null)
            {
                fieldSettings.graphSourceVertex = _source.value;
                fieldSettings.pulseAmplitude = _amplitude.value;
                fieldSettings.pulseWidth = _width.value;
                fieldSettings.pulseFrequency = _pulseRate.value;
                fieldSettings.pulseEnabled = _pulse.value;
                fieldSettings.directionalEnabled = _directional.value;
                _inspection.text = "Configuration applied. Inspect the source for details.";
            }
            if (densityChanged)
            {
                _renderer.SetIcosphereSettings(_density.value, _renderer.Radius);
                _source.highValue = Mathf.Max(0, _renderer.VertexCount - 1);
                fieldSettings.graphSourceVertex =
                    Mathf.Clamp(fieldSettings.graphSourceVertex, 0, _source.highValue);
                _source.SetValueWithoutNotify(fieldSettings.graphSourceVertex);
                _debug.Refresh();
                RestoreDebugOverlays();
            }
            _renderer.SetFieldSettings(fieldSettings);
            UpdateDebugAttribute();
            ResetSimulation();
        }

        private void ResetSimulation()
        {
            _renderer.ResetSimulation();
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _renderer.SetSimulationPaused(_paused);
            _pauseButtonLabel.text = _paused ? "Resume" : "Pause";
        }

        private void InspectSource()
        {
            ShowInspection(fieldSettings.graphSourceVertex);
        }

        private void PickFromPointer(PointerDownEvent pointer)
        {
            if (pointer.target != _root || Camera.main == null)
                return;
            Vector2 screen = pointer.position;
            screen.y = Screen.height - screen.y;
            ShowInspection(_debug.PickRestVertex(Camera.main, screen));
            pointer.StopPropagation();
        }

        private void ShowInspection(int vertex)
        {
            GeometryInspection inspection = _debug.Inspect(vertex);
            long bytes = 0;
            foreach (GpuBufferRegistration registration in
                     GpuBufferRegistry.Snapshot)
                bytes += registration.Bytes;
            _inspection.text =
                $"Vertex {inspection.Vertex}  " +
                $"Position {inspection.RestPosition:F2}\n" +
                $"Curvature {inspection.Curvature:F3}  " +
                $"Boundary {inspection.BoundaryWeight:F2}  " +
                $"Graph distance {inspection.GraphDistance:F0}\n" +
                $"{GpuBufferRegistry.Count} GPU buffers  " +
                $"{bytes / 1024f:F1} KB";
        }

        private void SetWireframe(ChangeEvent<bool> change)
        {
            _debug.WireframeVisible = change.newValue;
        }

        private void SetVertices(ChangeEvent<bool> change)
        {
            _debug.VerticesVisible = change.newValue;
        }

        private void SetNormals(ChangeEvent<bool> change)
        {
            _debug.NormalsVisible = change.newValue;
        }

        public void UseLitView()
        {
            _debugView = DebugView.Lit;
            UpdateDebugAttribute();
        }

        public void UseDistanceView()
        {
            _debugView = DebugView.GraphDistance;
            UpdateDebugAttribute();
        }

        public void UseCurvatureView()
        {
            _debugView = DebugView.Curvature;
            UpdateDebugAttribute();
        }

        private void UpdateDebugAttribute()
        {
            if (_debugView == DebugView.Lit)
            {
                _renderer.ShowLitMaterial();
            }
            else if (_debugView == DebugView.GraphDistance)
            {
                _renderer.SetDebugAttribute(AttributeIds.GraphDistance01, 0f, 1f);
            }
            else
            {
                _renderer.SetDebugAttribute(AttributeIds.Curvature, -0.25f, 0.25f);
            }
        }

        private void RestoreDebugOverlays()
        {
            _debug.WireframeVisible = _wireframe.value;
            _debug.VerticesVisible = _vertices.value;
            _debug.NormalsVisible = _normals.value;
        }
    }
}
