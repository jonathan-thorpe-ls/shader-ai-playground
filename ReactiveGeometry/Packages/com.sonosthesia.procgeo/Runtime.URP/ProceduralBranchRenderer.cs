using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralBranchRenderer : MonoBehaviour
    {
        [SerializeField] private BranchGenerationSettings settings =
            BranchGenerationSettings.Default;
        [SerializeField] private bool autoGenerate;
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField] private Color color = new(0.2f, 0.55f, 0.32f, 1f);
        [SerializeField] private bool drawRestGraph;

        private BranchGeometryGenerator _generator;
        private GeometryResource _resource;
        private GeometryCommandScheduler _generationScheduler;
        private BranchPathDeformationPipeline _pathDeformation;
        private Material _material;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private BranchGenerationSettings _generatedSettings;
        private readonly List<IBranchPathDeformer> _deformers = new();
        private bool _pendingRegeneration;
        private Bounds _restBounds;

        public BranchGenerationSettings Settings => settings;
        public BranchGeometryGenerator Generator => _generator;
        public GeometryResource Resource => _resource;
        public BranchPathDeformationPipeline PathDeformation => _pathDeformation;
        public int BranchCount => _generator?.GraphPlan.BranchCount ?? 0;
        public int PathSampleCount => _generator?.GraphPlan.SampleCount ?? 0;
        public int VertexCount => _generator?.VertexCount ?? 0;
        public bool IsGenerated => _resource != null && _resource.Version > 0;
        public BranchMeshVariant MeshVariant => settings.meshVariant;

        public void SetSettings(BranchGenerationSettings value)
        {
            settings = value;
            if (isActiveAndEnabled)
                Generate();
        }

        public void Generate()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogError(
                    "ProceduralBranchRenderer requires compute shader support.",
                    this);
                return;
            }

            Release();
            try
            {
                BranchGraphPlan plan =
                    new TreeBranchGenerator().Plan(settings);
                _generator = new BranchGeometryGenerator(
                    plan,
                    settings.meshVariant,
                    settings.radialSegments,
                    settings.junctionRings,
                    settings.junctionSubdivisions,
                    settings.junctionThroatScale,
                    settings.meshCellSize,
                    settings.meshSmoothingRadius);
                _resource = new GeometryResource(_generator.Plan());
                _generationScheduler = new GeometryCommandScheduler(
                    $"ProcGeo Branch Generate {name}");
                TopologyBuilder topology = new();
                AttributeProcessor attributes = new();
                GeometryValidator validator = new();
                _generationScheduler.Record(command =>
                {
                    _generator.Record(command, _resource);
                    topology.Record(command, _resource);
                    attributes.Record(command, _resource);
                    validator.Record(command, _resource);
                });
                _generationScheduler.ExecuteOnce();

                uint[] failure = new uint[1];
                _resource.Topology.Scratch.Failure.GetData(failure);
                if (failure[0] != 0)
                {
                    throw new GeometryGenerationException(
                        "The branch topology builder reported a generation failure.");
                }
                ValidationResult[] validation = new ValidationResult[1];
                _resource.ValidationResult.GetData(validation);
                if (validation[0].ErrorCount != 0)
                {
                    throw new GeometryGenerationException(
                        $"Branch GPU validation failed with {validation[0].ErrorCount} errors.");
                }

                _pathDeformation = new BranchPathDeformationPipeline(
                    _resource, _generator.CreatePathGeometryData());
                _restBounds = _resource.Mesh.bounds;
                RefreshDeformers();
                _generatedSettings = settings;
                _filter.sharedMesh = _resource.Mesh;
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                _material = new Material(shader)
                {
                    name = $"{name} Branch Lit",
                    hideFlags = HideFlags.DontSave
                };
                if (_material.HasProperty("_BaseColor"))
                    _material.SetColor("_BaseColor", color);
                _renderer.sharedMaterial = _material;
                Debug.Log(
                    $"Generated {_generator.GraphPlan.BranchCount} branches, " +
                    $"{_generator.GraphPlan.SampleCount} path samples and " +
                    $"{_generator.VertexCount} closed mesh vertices for {name}.",
                    this);
            }
            catch
            {
                Release();
                throw;
            }
        }

        public void ResetPathDeformation()
        {
            if (_pathDeformation == null)
                throw new InvalidOperationException(
                    "Branch geometry has not been generated.");
            _pathDeformation.Reset();
            _resource.MarkUpdated(ExpandedBounds());
        }

        public Vector3[] CaptureLivePathPositions()
        {
            if (_pathDeformation == null)
                throw new InvalidOperationException(
                    "Branch geometry has not been generated.");
            return _pathDeformation.ReadLivePositionsOnce();
        }

        public void RegisterPathDeformer(IBranchPathDeformer deformer)
        {
            if (deformer == null) throw new ArgumentNullException(nameof(deformer));
            if (!_deformers.Contains(deformer))
                _deformers.Add(deformer);
        }

        public void UnregisterPathDeformer(IBranchPathDeformer deformer)
        {
            if (deformer != null)
                _deformers.Remove(deformer);
        }

        public void RefreshDeformers()
        {
            MonoBehaviour[] behaviours =
                GetComponentsInChildren<MonoBehaviour>(true);
            foreach (IBranchPathDeformer deformer in
                behaviours.OfType<IBranchPathDeformer>())
            {
                RegisterPathDeformer(deformer);
            }
        }

        public void SetColor(Color value)
        {
            color = value;
            if (_material != null && _material.HasProperty("_BaseColor"))
                _material.SetColor("_BaseColor", value);
        }

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
        }

        private void OnEnable()
        {
            Generate();
            DisableScenePicking();
        }

        private void OnValidate()
        {
            settings.meshCellSize = settings.meshCellSize > 0f
                ? Mathf.Max(0.01f, settings.meshCellSize)
                : BranchGenerationSettings.Default.meshCellSize;
            settings.meshSmoothingRadius =
                settings.meshSmoothingRadius > 0f
                    ? settings.meshSmoothingRadius
                    : BranchGenerationSettings.Default.meshSmoothingRadius;
            if (_resource != null && isActiveAndEnabled &&
                !settings.Equals(_generatedSettings))
            {
                _pendingRegeneration = autoGenerate;
            }
            if (_material != null && _material.HasProperty("_BaseColor"))
                _material.SetColor("_BaseColor", color);
        }

        private void Update()
        {
            if (_pendingRegeneration)
            {
                _pendingRegeneration = false;
                Generate();
                return;
            }
            if (!Application.isPlaying || !deformationEnabled ||
                _pathDeformation == null)
            {
                return;
            }
            _pathDeformation.Record(
                _deformers, Time.deltaTime, Time.time);
            _resource.MarkUpdated(ExpandedBounds());
        }

        private Bounds ExpandedBounds()
        {
            Bounds bounds = _restBounds;
            bounds.Expand(3f);
            return bounds;
        }

        private void Release()
        {
            if (_filter != null)
                _filter.sharedMesh = null;
            if (_renderer != null)
                _renderer.sharedMaterial = null;
            _generationScheduler?.Dispose();
            _generationScheduler = null;
            _pathDeformation?.Dispose();
            _pathDeformation = null;
            _generator?.Dispose();
            _generator = null;
            _resource?.Dispose();
            _resource = null;
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
                _material = null;
            }
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private void DisableScenePicking()
        {
#if UNITY_EDITOR
            UnityEditor.SceneVisibilityManager.instance.DisablePicking(
                gameObject, true);
#endif
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawRestGraph || _generator == null)
                return;
            Gizmos.color = new Color(0.2f, 0.95f, 0.72f, 0.9f);
            foreach (BranchSample sample in _generator.GraphPlan.Samples)
                Gizmos.DrawSphere(transform.TransformPoint(sample.Position), 0.025f);
            foreach (BranchRecord branch in _generator.GraphPlan.Branches)
            {
                int start = (int)branch.SampleOffset;
                int end = start + (int)branch.SampleCount - 1;
                for (int i = start; i < end; i++)
                {
                    Gizmos.DrawLine(
                        transform.TransformPoint(_generator.GraphPlan.Samples[i].Position),
                        transform.TransformPoint(_generator.GraphPlan.Samples[i + 1].Position));
                }
            }
        }
    }
}
