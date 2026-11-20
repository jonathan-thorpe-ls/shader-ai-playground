using System.Collections.Generic;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralBranchRenderer))]
    public sealed class BranchGraphDebugOverlay : MonoBehaviour
    {
        [SerializeField] private Color restColor =
            new(0.18f, 0.95f, 0.82f, 0.9f);
        [SerializeField] private Color liveColor =
            new(1f, 0.72f, 0.15f, 0.9f);
        [SerializeField, Min(0.01f)] private float pointSize = 0.035f;
        [SerializeField] private bool liveSnapshotVisible;

        private ProceduralBranchRenderer _geometry;
        private GameObject _restOverlay;
        private GameObject _liveOverlay;
        private Mesh _restMesh;
        private Mesh _liveMesh;
        private Material _lineMaterial;
        private Vector3[] _liveSnapshot;

        public bool RestVisible =>
            _restOverlay != null && _restOverlay.activeSelf;
        public bool LiveVisible =>
            _liveOverlay != null && _liveOverlay.activeSelf;

        public void Refresh()
        {
            DestroyOverlay(ref _restOverlay, ref _restMesh);
            DestroyOverlay(ref _liveOverlay, ref _liveMesh);
            BuildRestOverlay();
            if (liveSnapshotVisible)
                CaptureLiveSnapshot();
        }

        public void SetRestVisible(bool visible)
        {
            if (visible && _restOverlay == null)
                BuildRestOverlay();
            if (_restOverlay != null)
                _restOverlay.SetActive(visible);
        }

        public void SetLiveVisible(bool visible)
        {
            liveSnapshotVisible = visible;
            if (visible && _liveOverlay == null)
                CaptureLiveSnapshot();
            if (_liveOverlay != null)
                _liveOverlay.SetActive(visible);
        }

        public void CaptureLiveSnapshot()
        {
            DestroyOverlay(ref _liveOverlay, ref _liveMesh);
            ProceduralBranchRenderer renderer = GetGeometry();
            if (renderer.Generator == null)
                return;
            BranchGraphPlan plan = renderer.Generator.GraphPlan;
            Vector3[] positions = renderer.CaptureLivePathPositions();
            _liveSnapshot = positions;
            (_liveOverlay, _liveMesh) = CreateOverlay(
                "Live Branch Graph Snapshot",
                positions,
                plan,
                liveColor);
            _liveOverlay.SetActive(liveSnapshotVisible);
        }

        private void Awake() => _geometry =
            GetComponent<ProceduralBranchRenderer>();

        private ProceduralBranchRenderer GetGeometry()
        {
            if (_geometry == null)
                _geometry = GetComponent<ProceduralBranchRenderer>();
            return _geometry;
        }

        private void OnEnable() => Refresh();

        private void BuildRestOverlay()
        {
            ProceduralBranchRenderer renderer = GetGeometry();
            if (renderer.Generator == null)
                return;
            BranchGraphPlan plan = renderer.Generator.GraphPlan;
            Vector3[] positions = new Vector3[plan.SampleCount];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = plan.Samples[i].Position;
            (_restOverlay, _restMesh) = CreateOverlay(
                "Rest Branch Graph",
                positions,
                plan,
                restColor);
        }

        private (GameObject, Mesh) CreateOverlay(
            string label,
            Vector3[] positions,
            BranchGraphPlan plan,
            Color color)
        {
            List<int> indices = new();
            foreach (BranchRecord branch in plan.Branches)
            {
                int start = (int)branch.SampleOffset;
                int count = (int)branch.SampleCount;
                for (int i = 0; i < count - 1; i++)
                {
                    indices.Add(start + i);
                    indices.Add(start + i + 1);
                }
            }

            GameObject overlay = new(
                label,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            overlay.hideFlags = HideFlags.DontSave;
            overlay.transform.SetParent(transform, false);
            Mesh mesh = new()
            {
                name = $"ProcGeo {label}",
                hideFlags = HideFlags.DontSave
            };
            mesh.indexFormat =
                positions.Length > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            overlay.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (_lineMaterial == null)
            {
                Shader shader = Shader.Find("Sonosthesia/ProcGeo/Lines");
                _lineMaterial = new Material(shader)
                {
                    name = $"{name} Branch Graph Lines",
                    hideFlags = HideFlags.DontSave
                };
            }
            Material material = Instantiate(_lineMaterial);
            material.hideFlags = HideFlags.DontSave;
            material.color = color;
            overlay.GetComponent<MeshRenderer>().sharedMaterial = material;
            return (overlay, mesh);
        }

        private void OnDisable() => RefreshOwnedResources();
        private void OnDestroy() => RefreshOwnedResources();

        private void OnDrawGizmosSelected()
        {
            ProceduralBranchRenderer renderer = GetGeometry();
            BranchGraphPlan plan = renderer?.Generator?.GraphPlan;
            if (plan == null)
                return;
            Gizmos.color = restColor;
            foreach (BranchSample sample in plan.Samples)
                Gizmos.DrawSphere(
                    transform.TransformPoint(sample.Position),
                    pointSize);
            if (_liveSnapshot == null)
                return;
            Gizmos.color = liveColor;
            foreach (Vector3 position in _liveSnapshot)
                Gizmos.DrawSphere(
                    transform.TransformPoint(position),
                    pointSize);
        }

        private void RefreshOwnedResources()
        {
            DestroyOverlay(ref _restOverlay, ref _restMesh);
            DestroyOverlay(ref _liveOverlay, ref _liveMesh);
            _liveSnapshot = null;
            if (_lineMaterial != null)
            {
                DestroyOwned(_lineMaterial);
                _lineMaterial = null;
            }
        }

        private static void DestroyOverlay(
            ref GameObject overlay,
            ref Mesh mesh)
        {
            if (overlay != null)
            {
                Material material =
                    overlay.GetComponent<MeshRenderer>()?.sharedMaterial;
                if (material != null)
                    DestroyOwned(material);
                if (Application.isPlaying) Destroy(overlay);
                else DestroyImmediate(overlay);
                overlay = null;
            }
            if (mesh != null)
            {
                DestroyOwned(mesh);
                mesh = null;
            }
        }

        private static void DestroyOwned(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
