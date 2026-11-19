using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralMeshRenderer : MonoBehaviour
    {
        [SerializeField, Min(1)] private int frequency = 4;
        [SerializeField, Min(0.001f)] private float radius = 1f;
        [SerializeField] private Color color = new(0.2f, 0.6f, 1f, 1f);
        [SerializeField] private bool simulationEnabled = true;
        [SerializeField] private SimulationParameters simulationParameters = SimulationParameters.Nominal;
        [SerializeField] private GeometryFieldSettings fieldSettings = GeometryFieldSettings.Default;

        private GeometryResource _resource;
        private IcosphereGenerator _generator;
        private GeometryCommandScheduler _scheduler;
        private NeighbourSimulation _simulation;
        private FieldAccumulator _fields;
        private GraphDistanceOperator _graphDistance;
        private SimulationScheduler _simulationScheduler;
        private Material _material;
        private Material _debugMaterial;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private bool _pendingRegeneration;
        private string _parameterWarning;
        private SimulationParameters _validatedParameters;
        private Bounds _restBounds;

        public int VertexCount => _resource?.Layout.VertexCount ?? 0;
        public int TriangleCount => _resource?.Layout.PrimitiveCount ?? 0;
        public int Frequency => frequency;
        public float Radius => radius;
        public GeometryResource Resource => _resource;
        public NeighbourSimulation Simulation => _simulation;
        public SimulationParameters Parameters => _validatedParameters;
        public string ParameterWarning => _parameterWarning;
        public GeometryFieldSettings FieldSettings => fieldSettings;
        public int LastSimulationStepCount => _simulationScheduler?.LastStepCount ?? 0;
        public float LastDroppedSimulationTime => _simulationScheduler?.LastDroppedTime ?? 0f;
        public bool SimulationEnabled
        {
            get => simulationEnabled;
            set
            {
                if (simulationEnabled == value) return;
                simulationEnabled = value;
                if (isActiveAndEnabled)
                    Generate();
            }
        }
        public bool IsGenerated => _resource != null && _resource.Version > 0;

        public void SetIcosphereSettings(int newFrequency, float newRadius)
        {
            frequency = Mathf.Max(1, newFrequency);
            radius = Mathf.Max(0.001f, newRadius);
            if (isActiveAndEnabled)
                Generate();
        }

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            if (SimulationParameters.IsUninitialized(simulationParameters))
                simulationParameters = SimulationParameters.Nominal;
            ValidateSimulationParameters();
        }

        private void OnEnable()
        {
            Generate();
        }

        private void OnValidate()
        {
            frequency = Mathf.Max(1, frequency);
            radius = Mathf.Max(0.001f, radius);
            bool geometryChanged = _resource != null &&
                (_resource.Layout.VertexCount != new IcospherePlanInfo(frequency).VertexCount ||
                 !Mathf.Approximately(_restBounds.extents.x, radius));
            ValidateSimulationParameters();
            if (isActiveAndEnabled && geometryChanged)
                _pendingRegeneration = true;
        }

        private void Update()
        {
            if (_pendingRegeneration)
            {
                _pendingRegeneration = false;
                Generate();
                return;
            }

            if (!Application.isPlaying || !simulationEnabled || _simulation == null)
                return;

            if (_fields != null)
            {
                _fields.Record(new FieldParameters
                {
                    phase = Mathf.Repeat(
                        Time.time * fieldSettings.pulseFrequency, 1f),
                    pulseAmplitude = fieldSettings.pulseAmplitude,
                    pulseWidth = fieldSettings.pulseWidth,
                    directionalStrength = fieldSettings.directionalStrength,
                    direction = transform.InverseTransformDirection(
                        fieldSettings.directionalDirection.normalized),
                    pulseWeight = fieldSettings.pulseWeight,
                    directionalWeight = fieldSettings.directionalWeight
                });
            }

            int steps = _simulationScheduler.Accumulate(Time.deltaTime, _validatedParameters);
            if (steps == 0) return;

            _simulation.RecordFixedSteps(steps, _validatedParameters);
            _resource.MarkUpdated(ExpandedBounds());
        }

        public void Generate()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogError("ProceduralMeshRenderer requires compute shader support.", this);
                return;
            }

            Release();
            try
            {
                _generator = new IcosphereGenerator(frequency, radius);
                _resource = new GeometryResource(_generator.Plan());
                _scheduler = new GeometryCommandScheduler($"ProcGeo Generate {name}");
                TopologyBuilder topology = new();
                AttributeProcessor attributes = new();
                GeometryValidator validator = new();
                _scheduler.Record(command =>
                {
                    _generator.Record(command, _resource);
                    topology.Record(command, _resource);
                    attributes.Record(command, _resource);
                    validator.Record(command, _resource);
                });
                _scheduler.ExecuteOnce();

                uint[] failure = new uint[1];
                _resource.Topology.Scratch.Failure.GetData(failure);
                if (failure[0] != 0)
                    throw new GeometryGenerationException("The topology builder reported a generation failure.");
                ValidationResult[] validation = new ValidationResult[1];
                _resource.ValidationResult.GetData(validation);
                if (validation[0].ErrorCount != 0)
                {
                    throw new GeometryGenerationException(
                        $"GPU validation failed with {validation[0].ErrorCount} error(s); first element {validation[0].FirstElement}.");
                }

                ValidateSimulationParameters();
                _restBounds = _resource.Mesh.bounds;
                if (simulationEnabled)
                {
                    _simulation = new NeighbourSimulation(_resource);
                    _simulationScheduler = new SimulationScheduler();
                    RebuildFields();
                    _resource.MarkUpdated(ExpandedBounds());
                }

                _filter.sharedMesh = _resource.Mesh;
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                _material = new Material(shader)
                {
                    name = $"{name} Procedural Lit",
                    hideFlags = HideFlags.DontSave
                };
                if (_material.HasProperty("_BaseColor"))
                    _material.SetColor("_BaseColor", color);
                _renderer.sharedMaterial = _material;
                Debug.Log(
                    $"Generated {_resource.Layout.VertexCount} vertices and {_resource.Layout.PrimitiveCount} triangles for {name}.",
                    this);
            }
            catch
            {
                Release();
                throw;
            }
        }

        public void SetSimulationParameters(SimulationParameters parameters)
        {
            simulationParameters = parameters;
            ValidateSimulationParameters();
        }

        public void SetFieldSettings(GeometryFieldSettings settings)
        {
            fieldSettings = settings;
            if (_resource != null && simulationEnabled)
                RebuildFields();
        }

        public void SetDebugAttribute(AttributeId attribute, float minimum, float maximum)
        {
            if (_resource == null)
                throw new InvalidOperationException("Geometry has not been generated.");
            AttributeSet.Entry entry = _resource.Attributes.Require(attribute);
            if (entry.Descriptor.Domain != AttributeDomain.Vertex ||
                entry.Descriptor.Format != AttributeFormat.Float)
            {
                throw new InvalidOperationException(
                    $"Debug attribute '{attribute}' must be a vertex scalar.");
            }
            if (_debugMaterial == null)
            {
                Shader shader = Shader.Find("Sonosthesia/ProcGeo/AttributeDebug")
                    ?? throw new InvalidOperationException(
                        "Sonosthesia/ProcGeo/AttributeDebug shader was not found.");
                _debugMaterial = new Material(shader)
                {
                    name = $"{name} Attribute Debug",
                    hideFlags = HideFlags.DontSave
                };
            }
            _debugMaterial.SetBuffer("_DebugAttribute", entry.Buffer);
            _debugMaterial.SetFloat("_DebugMinimum", minimum);
            _debugMaterial.SetFloat(
                "_DebugRange", Mathf.Max(maximum - minimum, 0.0001f));
            _renderer.sharedMaterial = _debugMaterial;
        }

        public void ShowLitMaterial()
        {
            if (_renderer != null && _material != null)
                _renderer.sharedMaterial = _material;
        }

        public void ApplyImpulse(int vertex, Vector3 direction, float magnitude)
        {
            if (_simulation == null)
                throw new InvalidOperationException("Simulation is not enabled.");
            _simulation.ApplyImpulse(vertex, direction, magnitude);
            _simulation.Publish();
        }

        public void ResetSimulation()
        {
            _simulation?.Reset();
            _simulationScheduler?.Reset();
            if (_resource != null && _simulation != null)
                _resource.MarkUpdated(ExpandedBounds());
        }

        public void SingleStepSimulation()
        {
            if (_simulation == null)
                throw new InvalidOperationException("Simulation is not enabled.");
            SimulationParameters single = _validatedParameters;
            single.paused = false;
            _simulation.RecordFixedSteps(1, single);
            _resource.MarkUpdated(ExpandedBounds());
        }

        public void SetSimulationPaused(bool paused)
        {
            simulationParameters.paused = paused;
            ValidateSimulationParameters();
        }

        public SimulationStatus ReadSimulationStatus() =>
            _simulation != null
                ? SimulationDiagnostics.ReadStatus(_resource)
                : default;

        private void OnDisable()
        {
            Release();
        }

        private void OnDestroy()
        {
            Release();
        }

        private void Release()
        {
            if (_filter != null)
                _filter.sharedMesh = null;
            if (_renderer != null)
                _renderer.sharedMaterial = null;
            if (_material != null)
            {
                DestroyOwned(_material);
                _material = null;
            }
            _scheduler?.Dispose();
            _scheduler = null;
            _simulation?.Dispose();
            _simulation = null;
            _simulationScheduler = null;
            _fields?.Dispose();
            _fields = null;
            _graphDistance?.Dispose();
            _graphDistance = null;
            _generator?.Dispose();
            _generator = null;
            _resource?.Dispose();
            _resource = null;
            if (_debugMaterial != null)
            {
                DestroyOwned(_debugMaterial);
                _debugMaterial = null;
            }
        }

        private void RebuildFields()
        {
            _fields?.Dispose();
            _fields = null;
            _graphDistance?.Dispose();
            _graphDistance = null;
            if (!fieldSettings.fieldsEnabled || !simulationEnabled || _resource == null)
            {
                return;
            }

            fieldSettings.graphSourceVertex = Mathf.Clamp(
                fieldSettings.graphSourceVertex, 0, _resource.Layout.VertexCount - 1);
            _graphDistance = new GraphDistanceOperator(
                new[] { fieldSettings.graphSourceVertex });
            _graphDistance.Execute(_resource);
            List<IGeometryField> fields = new();
            if (fieldSettings.pulseEnabled)
                fields.Add(new GraphPulseField());
            if (fieldSettings.directionalEnabled)
                fields.Add(new DirectionalField());
            _fields = new FieldAccumulator(_resource, fields);
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private void ValidateSimulationParameters()
        {
            if (SimulationParameters.IsUninitialized(simulationParameters))
                simulationParameters = SimulationParameters.Nominal;
            _validatedParameters = SimulationParameters.Validate(
                simulationParameters, out var issues);
            string warning = issues.Count == 0
                ? string.Empty
                : string.Join(" ", issues);
            if (warning != _parameterWarning)
            {
                _parameterWarning = warning;
                if (!string.IsNullOrEmpty(warning) && Application.isPlaying)
                    Debug.LogWarning($"Using clamped simulation parameters: {warning}", this);
            }
        }

        private Bounds ExpandedBounds()
        {
            Bounds bounds = _restBounds;
            if (simulationEnabled)
                bounds.Expand(_validatedParameters.maximumDisplacement * 2f);
            return bounds;
        }

        private readonly struct IcospherePlanInfo
        {
            public readonly int VertexCount;

            public IcospherePlanInfo(int value)
            {
                VertexCount = 10 * value * value + 2;
            }
        }
    }
}
