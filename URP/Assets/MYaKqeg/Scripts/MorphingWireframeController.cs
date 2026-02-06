using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class MorphingWireframeController : MonoBehaviour
{
    [Header("Geometry")]
    [SerializeField, Range(0, 6)]
    private int subdivisions = 4;
    [SerializeField]
    private float sphereRadius = 15f;

    [Header("Scene References")]
    [SerializeField]
    private MeshFilter nodeMeshFilter;
    [SerializeField]
    private MeshRenderer nodeRenderer;
    [SerializeField]
    private MeshFilter lineMeshFilter;
    [SerializeField]
    private MeshRenderer lineRenderer;

    [Header("Interaction")]
    [SerializeField]
    private Camera interactionCamera;
    [SerializeField]
    private float morphLerpRate = 5f;
    [SerializeField]
    private float pulseSpeed = 15f;
    [SerializeField]
    private float pulseLifetime = 3.5f;

    [Header("Themes")]
    [SerializeField]
    private List<ColorPaletteDefinition> palettes = new();
    [SerializeField]
    private int activePaletteIndex;

    private struct Pulse
    {
        public Vector3 position;
        public float startTime;
    }

    private const float InactivePulseTime = -10000f;

    private readonly Pulse[] pulses = new Pulse[3];
    private int lastPulseIndex = -1;

    private readonly List<Vector3> sphereVertices = new();
    private readonly List<Vector3> starVertices = new();
    private readonly List<Vector3> workingVertices = new();
    private readonly List<int> triangles = new();
    private readonly List<Vector3> lineSphereVertices = new();
    private readonly List<Vector3> lineStarVertices = new();
    private readonly List<int> lineIndices = new();
    private readonly List<Color> nodeColors = new();
    private readonly List<Color> lineColors = new();

    private MaterialPropertyBlock nodePropertyBlock;
    private MaterialPropertyBlock linePropertyBlock;

    private float targetMorph;
    private float morphAmount;
    private float geometryHash;
    private int appliedPaletteIndex = -1;

    private static readonly int MorphAmountId = Shader.PropertyToID("_MorphAmount");
    private static readonly int CurrentTimeId = Shader.PropertyToID("_CurrentTime");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int PulseLifetimeId = Shader.PropertyToID("_PulseLifetime");
    private static readonly int PulsePositionsId = Shader.PropertyToID("_PulsePositions");
    private static readonly int PulseTimesId = Shader.PropertyToID("_PulseTimes");

    private readonly Vector4[] pulsePositionBuffer = new Vector4[3];

    private void Reset()
    {
        if (!interactionCamera)
        {
            interactionCamera = Camera.main;
        }
    }

    private void Awake()
    {
        EnsurePropertyBlocks();
        InitializePulses();
        if (!interactionCamera)
        {
            interactionCamera = Camera.main;
        }
    }

    private void OnEnable()
    {
        BuildIfNeeded();
        ApplyPalette(activePaletteIndex);
    }

    private void Update()
    {
        BuildIfNeeded();
        morphAmount = Mathf.MoveTowards(morphAmount, targetMorph, morphLerpRate * Time.deltaTime);
        PushMaterialState();
    }

    private void OnValidate()
    {
        subdivisions = Mathf.Clamp(subdivisions, 0, 6);
        sphereRadius = Mathf.Max(0.1f, sphereRadius);
        morphLerpRate = Mathf.Max(0.01f, morphLerpRate);
        pulseSpeed = Mathf.Max(0.1f, pulseSpeed);
        pulseLifetime = Mathf.Max(0.1f, pulseLifetime);
        if (isActiveAndEnabled)
        {
            BuildIfNeeded(force: true);
            ApplyPalette(activePaletteIndex, force: true);
            PushMaterialState();
        }
    }

    public void SetMorphTarget(bool morphToStar)
    {
        targetMorph = morphToStar ? 1f : 0f;
    }

    public void ToggleMorph()
    {
        targetMorph = 1f - targetMorph;
    }

    public void SetPalette(int paletteIndex)
    {
        ApplyPalette(paletteIndex);
    }

    public void TriggerPulse(Vector2 screenPosition)
    {
        if (!interactionCamera)
        {
            return;
        }

        Ray ray = interactionCamera.ScreenPointToRay(screenPosition);
        if (IntersectSphere(ray, out Vector3 hitPoint))
        {
            lastPulseIndex = (lastPulseIndex + 1) % pulses.Length;
            pulses[lastPulseIndex].position = hitPoint;
            pulses[lastPulseIndex].startTime = GetCurrentTime();
        }
    }

    private void EnsurePropertyBlocks()
    {
        nodePropertyBlock ??= new MaterialPropertyBlock();
        linePropertyBlock ??= new MaterialPropertyBlock();
    }

    private void InitializePulses()
    {
        for (int i = 0; i < pulses.Length; i++)
        {
            pulses[i].startTime = InactivePulseTime;
        }
    }

    private void BuildIfNeeded(bool force = false)
    {
        float hash = subdivisions * 17f + sphereRadius * 3.7f;
        if (!force && Mathf.Approximately(hash, geometryHash))
        {
            return;
        }

        geometryHash = hash;
        BuildGeometry();
    }

    private void BuildGeometry()
    {
        IcoSphereBuilder.Build(subdivisions, sphereRadius, workingVertices, triangles);

        sphereVertices.Clear();
        sphereVertices.AddRange(workingVertices);

        starVertices.Clear();
        for (int i = 0; i < workingVertices.Count; i++)
        {
            Vector3 spherePoint = workingVertices[i];
            starVertices.Add(ComputeStarDisplacement(spherePoint));
        }

        BuildNodeMesh();
        BuildLineMesh();
    }

    private void BuildNodeMesh()
    {
        if (!nodeMeshFilter)
        {
            return;
        }

        Mesh mesh = nodeMeshFilter.sharedMesh;
        if (!mesh)
        {
            mesh = new Mesh { name = "MorphingNodes" };
            nodeMeshFilter.sharedMesh = mesh;
        }
        else
        {
            mesh.Clear();
        }

        mesh.SetVertices(sphereVertices);
        nodeColors.Resize(sphereVertices.Count);
        mesh.SetColors(nodeColors);
        mesh.SetUVs(1, starVertices);

        var indices = new int[sphereVertices.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }
        mesh.SetIndices(indices, MeshTopology.Points, 0, false);

        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * sphereRadius * 4f);
    }

    private void BuildLineMesh()
    {
        ExtractEdges();

        if (!lineMeshFilter)
        {
            return;
        }

        Mesh mesh = lineMeshFilter.sharedMesh;
        if (!mesh)
        {
            mesh = new Mesh { name = "MorphingLines" };
            lineMeshFilter.sharedMesh = mesh;
        }
        else
        {
            mesh.Clear();
        }

        mesh.SetVertices(lineSphereVertices);
        lineColors.Resize(lineSphereVertices.Count);
        mesh.SetColors(lineColors);
        mesh.SetUVs(1, lineStarVertices);

        if (lineIndices.Count != lineSphereVertices.Count)
        {
            lineIndices.Clear();
            for (int i = 0; i < lineSphereVertices.Count; i++)
            {
                lineIndices.Add(i);
            }
        }

        mesh.SetIndices(lineIndices, MeshTopology.Lines, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * sphereRadius * 4f);
    }

    private void ExtractEdges()
    {
        lineSphereVertices.Clear();
        lineStarVertices.Clear();
        lineIndices.Clear();

        var edgeSet = new HashSet<long>();
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            AddEdge(a, b, edgeSet);
            AddEdge(b, c, edgeSet);
            AddEdge(c, a, edgeSet);
        }

        foreach (long edge in edgeSet)
        {
            int v0 = (int)(edge >> 32);
            int v1 = (int)(edge & 0xffffffff);
            lineSphereVertices.Add(sphereVertices[v0]);
            lineSphereVertices.Add(sphereVertices[v1]);
            lineStarVertices.Add(starVertices[v0]);
            lineStarVertices.Add(starVertices[v1]);
        }
    }

    private void AddEdge(int v0, int v1, HashSet<long> edgeSet)
    {
        if (v0 > v1)
        {
            (v0, v1) = (v1, v0);
        }

        long key = ((long)v0 << 32) + v1;
        if (edgeSet.Add(key))
        {
            // indices generated later
        }
    }

    private Vector3 ComputeStarDisplacement(Vector3 spherePoint)
    {
        var spherical = new SphericalCoordinates(spherePoint.normalized);
        float spikeFactor = 0.4f * Mathf.Sin(spherical.Inclination * 6f) * Mathf.Sin(spherical.Azimuth * 6f);
        float radius = spherePoint.magnitude * (1f + spikeFactor);
        return spherical.ToCartesian(radius);
    }

    private void ApplyPalette(int paletteIndex, bool force = false)
    {
        if (palettes.Count == 0)
        {
            return;
        }

        paletteIndex = Mathf.Clamp(paletteIndex, 0, palettes.Count - 1);
        if (!force && paletteIndex == appliedPaletteIndex)
        {
            return;
        }

        appliedPaletteIndex = paletteIndex;
        activePaletteIndex = paletteIndex;

        ColorPaletteDefinition palette = palettes[paletteIndex];
        if (palette == null || palette.Colors.Count == 0)
        {
            return;
        }

        var rng = new System.Random(paletteIndex + 173);

        nodeColors.Resize(sphereVertices.Count);
        for (int i = 0; i < nodeColors.Count; i++)
        {
            nodeColors[i] = SelectRandomColor(palette, rng);
        }
        if (nodeMeshFilter && nodeMeshFilter.sharedMesh)
        {
            nodeMeshFilter.sharedMesh.SetColors(nodeColors);
        }

        lineColors.Resize(lineSphereVertices.Count);
        for (int i = 0; i < lineColors.Count; i += 2)
        {
            Color c = SelectRandomColor(palette, rng);
            lineColors[i] = c;
            if (i + 1 < lineColors.Count)
            {
                lineColors[i + 1] = c;
            }
        }
        if (lineMeshFilter && lineMeshFilter.sharedMesh)
        {
            lineMeshFilter.sharedMesh.SetColors(lineColors);
        }
    }

    private static Color SelectRandomColor(ColorPaletteDefinition palette, System.Random rng)
    {
        int index = rng.Next(palette.Colors.Count);
        return palette.Colors[index];
    }

    private void PushMaterialState()
    {
        EnsurePropertyBlocks();
        float currentTime = GetCurrentTime();

        for (int i = 0; i < pulses.Length; i++)
        {
            float active = pulses[i].startTime > InactivePulseTime ? 1f : 0f;
            Vector3 pos = pulses[i].position;
            pulsePositionBuffer[i] = new Vector4(pos.x, pos.y, pos.z, active);
        }

        Vector4 times = Vector4.zero;
        for (int i = 0; i < pulses.Length && i < 4; i++)
        {
            times[i] = pulses[i].startTime;
        }

        nodePropertyBlock.SetFloat(MorphAmountId, morphAmount);
        nodePropertyBlock.SetFloat(CurrentTimeId, currentTime);
        nodePropertyBlock.SetFloat(PulseSpeedId, pulseSpeed);
        nodePropertyBlock.SetFloat(PulseLifetimeId, pulseLifetime);
        nodePropertyBlock.SetVectorArray(PulsePositionsId, pulsePositionBuffer);
        nodePropertyBlock.SetVector(PulseTimesId, times);
        if (nodeRenderer)
        {
            nodeRenderer.SetPropertyBlock(nodePropertyBlock);
        }

        linePropertyBlock.SetFloat(MorphAmountId, morphAmount);
        linePropertyBlock.SetFloat(CurrentTimeId, currentTime);
        linePropertyBlock.SetFloat(PulseSpeedId, pulseSpeed);
        linePropertyBlock.SetFloat(PulseLifetimeId, pulseLifetime);
        linePropertyBlock.SetVectorArray(PulsePositionsId, pulsePositionBuffer);
        linePropertyBlock.SetVector(PulseTimesId, times);
        if (lineRenderer)
        {
            lineRenderer.SetPropertyBlock(linePropertyBlock);
        }
    }

    private float GetCurrentTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return (float)UnityEditor.EditorApplication.timeSinceStartup;
        }
#endif
        return Time.time;
    }

    private bool IntersectSphere(Ray ray, out Vector3 hitPoint)
    {
        Vector3 center = transform.position;
        Vector3 originToCenter = ray.origin - center;
        float radius = sphereRadius * transform.lossyScale.x;

        float b = Vector3.Dot(originToCenter, ray.direction);
        float c = originToCenter.sqrMagnitude - radius * radius;
        float discriminant = b * b - c;
        if (discriminant < 0f)
        {
            hitPoint = default;
            return false;
        }

        float t = -b - Mathf.Sqrt(discriminant);
        if (t < 0f)
        {
            t = -b + Mathf.Sqrt(discriminant);
            if (t < 0f)
            {
                hitPoint = default;
                return false;
            }
        }

        hitPoint = ray.origin + ray.direction * t;
        return true;
    }

    private readonly struct SphericalCoordinates
    {
        public readonly float Inclination;
        public readonly float Azimuth;

        public SphericalCoordinates(Vector3 normalized)
        {
            Inclination = Mathf.Acos(Mathf.Clamp(normalized.y, -1f, 1f));
            Azimuth = Mathf.Atan2(normalized.z, normalized.x);
        }

        public Vector3 ToCartesian(float radius)
        {
            float sinInclination = Mathf.Sin(Inclination);
            float x = radius * Mathf.Cos(Azimuth) * sinInclination;
            float y = radius * Mathf.Cos(Inclination);
            float z = radius * Mathf.Sin(Azimuth) * sinInclination;
            return new Vector3(x, y, z);
        }
    }
}

internal static class ListExtensions
{
    public static void Resize<T>(this List<T> list, int size)
    {
        int current = list.Count;
        if (size < current)
        {
            list.RemoveRange(size, current - size);
        }
        else if (size > current)
        {
            if (size > list.Capacity)
            {
                list.Capacity = size;
            }
            int itemsToAdd = size - current;
            for (int i = 0; i < itemsToAdd; i++)
            {
                list.Add(default(T));
            }
        }
    }
}
