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

        private GeometryResource _resource;
        private IcosphereGenerator _generator;
        private GeometryCommandScheduler _scheduler;
        private Material _material;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private bool _pendingRegeneration;

        public int VertexCount => _resource?.Layout.VertexCount ?? 0;
        public int TriangleCount => _resource?.Layout.PrimitiveCount ?? 0;
        public GeometryResource Resource => _resource;
        public bool IsGenerated => _resource != null && _resource.Version > 0;

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
        }

        private void OnEnable()
        {
            Generate();
        }

        private void OnValidate()
        {
            frequency = Mathf.Max(1, frequency);
            radius = Mathf.Max(0.001f, radius);
            if (isActiveAndEnabled)
                _pendingRegeneration = true;
        }

        private void Update()
        {
            if (!_pendingRegeneration)
                return;
            _pendingRegeneration = false;
            Generate();
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
            _generator?.Dispose();
            _generator = null;
            _resource?.Dispose();
            _resource = null;
        }

        private static void DestroyOwned(Object target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
