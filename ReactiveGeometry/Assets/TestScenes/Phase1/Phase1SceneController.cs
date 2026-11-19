using UnityEngine;
using UnityEngine.UIElements;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralMeshRenderer), typeof(UIDocument))]
    public sealed class Phase1SceneController : MonoBehaviour
    {
        [SerializeField] private string sceneTitle = "GPU icosphere";
        [SerializeField, TextArea] private string sceneSubtitle =
            "Inspect generation, metadata, and committed topology.";

        private ProceduralMeshRenderer _renderer;
        private UIDocument _document;
        private Label _sceneTitle;
        private Label _sceneSubtitle;
        private Label _diagnostics;
        private SliderInt _frequency;
        private Slider _radius;
        private bool _bound;

        private void Awake()
        {
            _renderer = GetComponent<ProceduralMeshRenderer>();
            _document = GetComponent<UIDocument>();
        }

        private void Start()
        {
            BindUi();
        }

        private void OnDisable()
        {
            UnbindUi();
        }

        public void Regenerate()
        {
            if (_frequency == null || _radius == null)
                return;
            _renderer.SetIcosphereSettings(_frequency.value, _radius.value);
            UpdateDiagnostics();
        }

        private void BindUi()
        {
            VisualElement root = _document.rootVisualElement;
            _sceneTitle = root.Q<Label>("sceneTitle");
            _sceneSubtitle = root.Q<Label>("sceneSubtitle");
            _diagnostics = root.Q<Label>("diagnostics");
            _frequency = root.Q<SliderInt>("frequencySlider");
            _radius = root.Q<Slider>("radiusSlider");

            if (_sceneTitle == null || _sceneSubtitle == null || _diagnostics == null ||
                _frequency == null || _radius == null)
            {
                Debug.LogError("Phase 1 UI Toolkit document is missing required controls.", this);
                enabled = false;
                return;
            }

            _sceneTitle.text = sceneTitle;
            _sceneSubtitle.text = sceneSubtitle;
            _frequency.SetValueWithoutNotify(_renderer.Frequency);
            _radius.SetValueWithoutNotify(_renderer.Radius);
            root.Q<Button>("regenerateButton").clicked += Regenerate;
            root.Q<Button>("baseButton").clicked += UseBase;
            root.Q<Button>("mediumButton").clicked += UseMedium;
            root.Q<Button>("highButton").clicked += UseHigh;
            root.Q<Button>("stressButton").clicked += UseStress;
            _frequency.RegisterValueChangedCallback(SetFrequencyLabel);
            _radius.RegisterValueChangedCallback(SetRadiusLabel);
            _bound = true;
            UpdateSliderLabels();
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
            root.Q<Button>("regenerateButton").clicked -= Regenerate;
            root.Q<Button>("baseButton").clicked -= UseBase;
            root.Q<Button>("mediumButton").clicked -= UseMedium;
            root.Q<Button>("highButton").clicked -= UseHigh;
            root.Q<Button>("stressButton").clicked -= UseStress;
            _frequency.UnregisterValueChangedCallback(SetFrequencyLabel);
            _radius.UnregisterValueChangedCallback(SetRadiusLabel);
            _bound = false;
        }

        private void UseBase() => UseFrequency(1);
        private void UseMedium() => UseFrequency(4);
        private void UseHigh() => UseFrequency(8);
        private void UseStress() => UseFrequency(16);

        private void UseFrequency(int value)
        {
            _frequency.SetValueWithoutNotify(value);
            Regenerate();
            UpdateSliderLabels();
        }

        private void SetFrequencyLabel(ChangeEvent<int> change)
        {
            UpdateSliderLabels();
        }

        private void SetRadiusLabel(ChangeEvent<float> change)
        {
            UpdateSliderLabels();
        }

        private void UpdateSliderLabels()
        {
            _document.rootVisualElement.Q<Label>("frequencyValue").text =
                _frequency.value.ToString();
            _document.rootVisualElement.Q<Label>("radiusValue").text =
                $"{_radius.value:F2}";
        }

        private void UpdateDiagnostics()
        {
            GeometryResource resource = _renderer.Resource;
            if (resource == null)
            {
                _diagnostics.text = "Not generated";
                return;
            }

            GeometryLayout layout = resource.Layout;
            _diagnostics.text =
                $"{layout.VertexCount:N0} vertices  {layout.PrimitiveCount:N0} triangles  " +
                $"{layout.EdgeCapacity:N0} edges\n" +
                $"{layout.RegionCount} regions  {layout.CornerCount:N0} corners  " +
                $"{layout.Attributes.Count} attributes  revision {resource.Version}";
        }
    }
}
