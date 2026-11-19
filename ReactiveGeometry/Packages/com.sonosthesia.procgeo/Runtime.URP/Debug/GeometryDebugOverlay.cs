using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralMeshRenderer))]
    public sealed class GeometryDebugOverlay : MonoBehaviour
    {
        private ProceduralMeshRenderer _geometry;
        private GameObject _wireframe;
        private Mesh _wireframeMesh;
        private GameObject _vertices;
        private Mesh _verticesMesh;
        private GameObject _normals;
        private Mesh _normalsMesh;
        private Material _lineMaterial;
        private Vector3[] _positions;

        private void Awake()
        {
            _geometry = GetComponent<ProceduralMeshRenderer>();
        }

        public bool WireframeVisible
        {
            get => _wireframe != null && _wireframe.activeSelf;
            set
            {
                EnsureWireframe();
                _wireframe.SetActive(value);
            }
        }

        public bool VerticesVisible
        {
            get => _vertices != null && _vertices.activeSelf;
            set
            {
                EnsureVertices();
                _vertices.SetActive(value);
            }
        }

        public bool NormalsVisible
        {
            get => _normals != null && _normals.activeSelf;
            set
            {
                EnsureNormals();
                _normals.SetActive(value);
            }
        }

        public int PickRestVertex(Camera camera, Vector3 screenPosition)
        {
            GeometryResource resource = _geometry.Resource;
            if (resource == null)
                throw new InvalidOperationException("Geometry has not been generated.");
            if (_positions == null)
            {
                _positions = new Vector3[resource.Layout.VertexCount];
                resource.Attributes.Require(AttributeIds.RestPosition)
                    .Buffer.GetData(_positions);
            }
            int result = -1;
            float best = float.MaxValue;
            for (int index = 0; index < _positions.Length; index++)
            {
                Vector3 screen = camera.WorldToScreenPoint(
                    transform.TransformPoint(_positions[index]));
                float distance = Vector2.SqrMagnitude(
                    (Vector2)screen - (Vector2)screenPosition);
                if (distance < best)
                {
                    best = distance;
                    result = index;
                }
            }
            return result;
        }

        public void Refresh()
        {
            _positions = null;
            DestroyOverlay(_wireframe, _wireframeMesh);
            DestroyOverlay(_vertices, _verticesMesh);
            DestroyOverlay(_normals, _normalsMesh);
            _wireframe = null;
            _wireframeMesh = null;
            _vertices = null;
            _verticesMesh = null;
            _normals = null;
            _normalsMesh = null;
        }

        public GeometryInspection Inspect(int vertex)
        {
            GeometryResource resource = _geometry.Resource;
            if (resource == null)
                throw new InvalidOperationException("Geometry has not been generated.");
            if ((uint)vertex >= (uint)resource.Layout.VertexCount)
                throw new ArgumentOutOfRangeException(nameof(vertex));
            if (_positions == null)
            {
                _positions = new Vector3[resource.Layout.VertexCount];
                resource.Attributes.Require(AttributeIds.RestPosition)
                    .Buffer.GetData(_positions);
            }
            float curvature = ReadScalar(AttributeIds.Curvature, vertex);
            float boundary = ReadScalar(AttributeIds.BoundaryWeight, vertex);
            float distance = float.NaN;
            if (resource.Attributes.TryGet(
                    AttributeIds.GraphDistance, out AttributeSet.Entry distanceEntry))
            {
                float[] values = new float[distanceEntry.Buffer.count];
                distanceEntry.Buffer.GetData(values);
                distance = values[vertex];
            }
            return new GeometryInspection(
                vertex,
                _positions[vertex],
                curvature,
                boundary,
                distance);

            float ReadScalar(AttributeId id, int index)
            {
                GraphicsBuffer buffer = resource.Attributes.Require(id).Buffer;
                float[] values = new float[buffer.count];
                buffer.GetData(values);
                return values[index];
            }
        }

        private void EnsureWireframe()
        {
            if (_wireframe != null)
                return;
            GeometryResource resource = _geometry.Resource;
            if (resource == null)
                throw new InvalidOperationException("Geometry has not been generated.");
            if (_positions == null)
            {
                _positions = new Vector3[resource.Layout.VertexCount];
                resource.Attributes.Require(AttributeIds.RestPosition)
                    .Buffer.GetData(_positions);
            }
            uint[] edges = new uint[resource.Topology.Edges.count * 2];
            resource.Topology.Edges.GetData(edges);
            List<Vector3> linePositions = new(edges.Length);
            int[] lineIndices = new int[edges.Length];
            for (int index = 0; index < edges.Length; index++)
            {
                linePositions.Add(_positions[edges[index]]);
                lineIndices[index] = index;
            }

            _wireframe = new GameObject(
                "Rest Topology Wireframe",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            _wireframe.transform.SetParent(transform, false);
            _wireframeMesh = new Mesh
            {
                name = "ProcGeo Rest Topology",
                hideFlags = HideFlags.DontSave
            };
            _wireframeMesh.SetVertices(linePositions);
            _wireframeMesh.SetIndices(
                lineIndices, MeshTopology.Lines, 0);
            _wireframeMesh.RecalculateBounds();
            _wireframe.GetComponent<MeshFilter>().sharedMesh = _wireframeMesh;
            Shader shader = Shader.Find("Sonosthesia/ProcGeo/Lines");
            _lineMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _lineMaterial.color = new Color(0.15f, 0.95f, 0.85f, 0.9f);
            _wireframe.GetComponent<MeshRenderer>().sharedMaterial =
                _lineMaterial;
        }

        private void EnsureVertices()
        {
            if (_vertices != null)
                return;
            EnsureRestData();
            int[] indices = new int[_positions.Length];
            for (int index = 0; index < indices.Length; index++)
                indices[index] = index;
            (_vertices, _verticesMesh) =
                CreateOverlay(
                    "Rest Vertices",
                    _positions,
                    indices,
                    MeshTopology.Points,
                    new Color(1f, 0.82f, 0.25f, 0.95f));
        }

        private void EnsureNormals()
        {
            if (_normals != null)
                return;
            GeometryResource resource = _geometry.Resource;
            if (resource == null)
                throw new InvalidOperationException("Geometry has not been generated.");
            EnsureRestData();
            Vector3[] normals = new Vector3[resource.Layout.VertexCount];
            resource.Attributes.Require(AttributeIds.RestNormal)
                .Buffer.GetData(normals);
            Vector3[] positions = new Vector3[normals.Length * 2];
            int[] indices = new int[positions.Length];
            for (int index = 0; index < normals.Length; index++)
            {
                int position = index * 2;
                positions[position] = _positions[index];
                positions[position + 1] =
                    _positions[index] + normals[index] * 0.12f;
                indices[position] = position;
                indices[position + 1] = position + 1;
            }
            (_normals, _normalsMesh) =
                CreateOverlay(
                    "Rest Normals",
                    positions,
                    indices,
                    MeshTopology.Lines,
                    new Color(0.98f, 0.42f, 0.18f, 0.95f));
        }

        private void EnsureRestData()
        {
            if (_positions != null)
                return;
            GeometryResource resource = _geometry.Resource;
            if (resource == null)
                throw new InvalidOperationException("Geometry has not been generated.");
            _positions = new Vector3[resource.Layout.VertexCount];
            resource.Attributes.Require(AttributeIds.RestPosition)
                .Buffer.GetData(_positions);
        }

        private (GameObject, Mesh) CreateOverlay(
            string name,
            Vector3[] positions,
            int[] indices,
            MeshTopology topology,
            Color color)
        {
            GameObject result = new(
                name,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            result.transform.SetParent(transform, false);
            Mesh mesh = new()
            {
                name = $"ProcGeo {name}",
                hideFlags = HideFlags.DontSave
            };
            mesh.SetVertices(positions);
            mesh.SetIndices(indices, topology, 0);
            mesh.RecalculateBounds();
            result.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (_lineMaterial == null)
            {
                Shader shader = Shader.Find("Sonosthesia/ProcGeo/Lines");
                _lineMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            Material material = Instantiate(_lineMaterial);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = color;
            result.GetComponent<MeshRenderer>().sharedMaterial = material;
            return (result, mesh);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_lineMaterial);
                else
                    DestroyImmediate(_lineMaterial);
            }
            if (_wireframeMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_wireframeMesh);
                else
                    DestroyImmediate(_wireframeMesh);
            }
            if (_verticesMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_verticesMesh);
                else
                    DestroyImmediate(_verticesMesh);
            }
            if (_normalsMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_normalsMesh);
                else
                    DestroyImmediate(_normalsMesh);
            }
            if (_vertices != null)
            {
                Material material =
                    _vertices.GetComponent<MeshRenderer>().sharedMaterial;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            if (_normals != null)
            {
                Material material =
                    _normals.GetComponent<MeshRenderer>().sharedMaterial;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }

        private void DestroyOverlay(GameObject overlay, Mesh mesh)
        {
            if (overlay != null)
            {
                Material material =
                    overlay.GetComponent<MeshRenderer>()?.sharedMaterial;
                if (material != null && material != _lineMaterial)
                {
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
                if (Application.isPlaying) Destroy(overlay);
                else DestroyImmediate(overlay);
            }
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }
    }

    public readonly struct GeometryInspection
    {
        public GeometryInspection(
            int vertex,
            Vector3 restPosition,
            float curvature,
            float boundaryWeight,
            float graphDistance)
        {
            Vertex = vertex;
            RestPosition = restPosition;
            Curvature = curvature;
            BoundaryWeight = boundaryWeight;
            GraphDistance = graphDistance;
        }

        public int Vertex { get; }
        public Vector3 RestPosition { get; }
        public float Curvature { get; }
        public float BoundaryWeight { get; }
        public float GraphDistance { get; }
    }
}
