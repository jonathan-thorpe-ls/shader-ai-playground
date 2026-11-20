using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class BranchPathGeometryData
    {
        public BranchPathGeometryData(
            BranchGraphPlan plan,
            uint[] skinIndices,
            float[] skinWeights,
            Vector3[] skinOffsets)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            VertexPathIndices = skinIndices ??
                throw new ArgumentNullException(nameof(skinIndices));
            SkinWeights = skinWeights ??
                throw new ArgumentNullException(nameof(skinWeights));
            SkinOffsets = skinOffsets ??
                throw new ArgumentNullException(nameof(skinOffsets));
            if (skinIndices.Length != skinWeights.Length ||
                skinIndices.Length != skinOffsets.Length ||
                skinIndices.Length % 4 != 0)
                throw new ArgumentException(
                    "Path binding arrays must have matching counts.");
        }

        public BranchGraphPlan Plan { get; }
        public uint[] VertexPathIndices { get; }
        public float[] SkinWeights { get; }
        public Vector3[] SkinOffsets { get; }
    }

    public sealed class BranchGeometryGenerator : IGeometryGenerator
    {
        private readonly ComputeShader _shader;
        private readonly BranchGraphPlan _plan;
        private readonly BranchMeshData _data;
        private readonly GraphicsBuffer _positions;
        private readonly GraphicsBuffer _normals;
        private readonly GraphicsBuffer _tangents;
        private readonly GraphicsBuffer _coordinates;
        private readonly GraphicsBuffer _triangles;
        private readonly GraphicsBuffer _regions;
        private readonly GraphicsBuffer _branchIds;
        private readonly GraphicsBuffer _rootDistances;
        private readonly GraphicsBuffer _tipDistances;
        private readonly GraphicsBuffer _localDistances;
        private readonly GraphicsBuffer _localPositions;
        private readonly GraphicsBuffer _depths;
        private readonly GraphicsBuffer _radii;
        private readonly GraphicsBuffer _tipWeights;
        private readonly GraphicsBuffer _flexibilities;
        private readonly GraphicsBuffer _stiffnesses;
        private readonly int _vertices;
        private readonly int _physicalAttributes;
        private readonly int _structureAttributes;
        private readonly int _topology;
        private readonly int _header;
        private bool _disposed;

        public BranchGeometryGenerator(
            BranchGraphPlan plan,
            float meshCellSize = 0.08f,
            float meshSmoothingRadius = 0.12f,
            ComputeShader shader = null)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _data = Build(plan, meshCellSize, meshSmoothingRadius);
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/BranchGeometry")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/BranchGeometry.compute was not found.");
            _vertices = _shader.FindKernel("CopyBranchVertices");
            _physicalAttributes =
                _shader.FindKernel("CopyBranchPhysicalAttributes");
            _structureAttributes =
                _shader.FindKernel("CopyBranchStructureAttributes");
            _topology = _shader.FindKernel("CopyBranchTopology");
            _header = _shader.FindKernel("CopyBranchHeader");
            _positions = Create(_data.Positions, "Branch.Positions");
            _normals = Create(_data.Normals, "Branch.Normals");
            _tangents = Create(_data.Tangents, "Branch.Tangents");
            _coordinates = Create(_data.Coordinates, "Branch.Coordinates");
            _triangles = Create(_data.Triangles, "Branch.Triangles");
            _regions = Create(_data.Regions, "Branch.Regions");
            _branchIds = Create(_data.BranchIds, "Branch.VertexBranchIds");
            _rootDistances = Create(_data.RootDistances, "Branch.RootDistances");
            _tipDistances = Create(_data.TipDistances, "Branch.TipDistances");
            _localDistances = Create(_data.LocalDistances, "Branch.LocalDistances");
            _localPositions = Create(_data.LocalPositions, "Branch.LocalPositions");
            _depths = Create(_data.Depths, "Branch.Depths");
            _radii = Create(_data.Radii, "Branch.Radii");
            _tipWeights = Create(_data.TipWeights, "Branch.TipWeights");
            _flexibilities = Create(_data.Flexibilities, "Branch.Flexibilities");
            _stiffnesses = Create(_data.Stiffnesses, "Branch.Stiffnesses");
        }

        public BranchGraphPlan GraphPlan => _plan;
        public Vector3[] DebugPositions => _data.Positions;
        public Vector3Int[] DebugTriangles => _data.Triangles;
        public int VertexCount => _data.Positions.Length;
        public int PrimitiveCount => _data.Triangles.Length;

        public GeometryLayout Plan()
        {
            AttributeDescriptor[] attributes =
            {
                new(AttributeIds.RestPosition, AttributeDomain.Vertex, AttributeFormat.Float3),
                new(AttributeIds.RestNormal, AttributeDomain.Vertex, AttributeFormat.Float3),
                new(AttributeIds.RestTangent, AttributeDomain.Vertex, AttributeFormat.Float4),
                new(AttributeIds.LocalCoord, AttributeDomain.Vertex, AttributeFormat.Float2),
                new(AttributeIds.Mass, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.DeformationWeight, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.RegionId, AttributeDomain.Primitive, AttributeFormat.UInt,
                    AttributeInterpolation.Flat),
                new(AttributeIds.BoundaryWeight, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.NeighbourCount, AttributeDomain.Vertex, AttributeFormat.UInt,
                    AttributeInterpolation.Flat),
                new(AttributeIds.Curvature, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.EdgeRestLength, AttributeDomain.Edge, AttributeFormat.Float),
                new(AttributeIds.BranchId, AttributeDomain.Vertex, AttributeFormat.UInt,
                    AttributeInterpolation.Flat),
                new(AttributeIds.DistanceFromRoot, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.DistanceToTip, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.LocalBranchDistance, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.LocalBranchPosition, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.GenerationDepth, AttributeDomain.Vertex, AttributeFormat.UInt,
                    AttributeInterpolation.Flat),
                new(AttributeIds.BranchRadius, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.TipWeight, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.BranchFlexibility, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.BranchStiffness, AttributeDomain.Vertex, AttributeFormat.Float)
            };
            return new GeometryLayout(
                "BranchGraph",
                PrimitiveTopology.Triangles,
                _data.Positions.Length,
                _data.Triangles.Length,
                _data.EdgeCount,
                _plan.BranchCount,
                32,
                attributes);
        }

        public void Record(CommandBuffer command, GeometryResource resource)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BranchGeometryGenerator));
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (resource.Layout.VertexCount != _data.Positions.Length ||
                resource.Layout.PrimitiveCount != _data.Triangles.Length)
            {
                throw new ArgumentException(
                    "The target geometry resource does not match this branch graph.",
                    nameof(resource));
            }

            command.SetComputeIntParam(_shader, "_VertexCount", _data.Positions.Length);
            command.SetComputeIntParam(_shader, "_PrimitiveCount", _data.Triangles.Length);
            command.SetComputeIntParam(_shader, "_EdgeCount", _data.EdgeCount);
            command.SetComputeIntParam(_shader, "_RegionCount", _plan.BranchCount);
            command.SetComputeVectorParam(_shader, "_BoundsMin", _plan.Bounds.min);
            command.SetComputeVectorParam(_shader, "_BoundsMax", _plan.Bounds.max);

            Set(_vertices, "_SourcePositions", _positions);
            Set(_vertices, "_SourceNormals", _normals);
            Set(_vertices, "_SourceTangents", _tangents);
            Set(_vertices, "_SourceCoordinates", _coordinates);
            Set(_vertices, "_DynamicVertices", resource.Live.DynamicVertices);
            Set(_vertices, "_StaticVertices", resource.StaticVertices);
            Bind(_vertices, AttributeIds.RestPosition, "_RestPositions");
            Bind(_vertices, AttributeIds.RestNormal, "_RestNormals");
            Bind(_vertices, AttributeIds.RestTangent, "_RestTangents");
            Bind(_vertices, AttributeIds.LocalCoord, "_LocalCoordinates");
            command.DispatchCompute(
                _shader, _vertices, (_data.Positions.Length + 63) / 64, 1, 1);

            Set(_physicalAttributes, "_SourceBranchIds", _branchIds);
            Set(_physicalAttributes, "_SourceRootDistances", _rootDistances);
            Set(_physicalAttributes, "_SourceTipDistances", _tipDistances);
            Set(_physicalAttributes, "_SourceLocalDistances", _localDistances);
            Set(_physicalAttributes, "_SourceRadii", _radii);
            Set(_physicalAttributes, "_SourceFlexibilities", _flexibilities);
            Bind(_physicalAttributes, AttributeIds.Mass, "_Masses");
            Bind(_physicalAttributes, AttributeIds.DeformationWeight, "_DeformationWeights");
            Bind(_physicalAttributes, AttributeIds.BranchId, "_BranchIds");
            Bind(_physicalAttributes, AttributeIds.DistanceFromRoot, "_RootDistances");
            Bind(_physicalAttributes, AttributeIds.DistanceToTip, "_TipDistances");
            Bind(_physicalAttributes, AttributeIds.LocalBranchDistance, "_LocalDistances");
            command.DispatchCompute(
                _shader, _physicalAttributes,
                (_data.Positions.Length + 63) / 64, 1, 1);

            Set(_structureAttributes, "_SourceLocalPositions", _localPositions);
            Set(_structureAttributes, "_SourceDepths", _depths);
            Set(_structureAttributes, "_SourceRadii", _radii);
            Set(_structureAttributes, "_SourceTipWeights", _tipWeights);
            Set(_structureAttributes, "_SourceFlexibilities", _flexibilities);
            Set(_structureAttributes, "_SourceStiffnesses", _stiffnesses);
            Bind(_structureAttributes, AttributeIds.LocalBranchPosition, "_LocalPositions");
            Bind(_structureAttributes, AttributeIds.GenerationDepth, "_Depths");
            Bind(_structureAttributes, AttributeIds.BranchRadius, "_Radii");
            Bind(_structureAttributes, AttributeIds.TipWeight, "_TipWeights");
            Bind(_structureAttributes, AttributeIds.BranchFlexibility, "_Flexibilities");
            Bind(_structureAttributes, AttributeIds.BranchStiffness, "_Stiffnesses");
            command.DispatchCompute(
                _shader, _structureAttributes,
                (_data.Positions.Length + 63) / 64, 1, 1);

            Set(_topology, "_SourceTriangles", _triangles);
            Set(_topology, "_SourceRegions", _regions);
            Set(_topology, "_Indices", resource.Indices);
            Bind(_topology, AttributeIds.RegionId, "_RegionIds");
            command.DispatchCompute(
                _shader, _topology, (_data.Triangles.Length + 63) / 64, 1, 1);

            Set(_header, "_Header", resource.Header);
            Set(_header, "_DrawArgs", resource.DrawArgs);
            command.DispatchCompute(_shader, _header, 1, 1, 1);
            resource.MarkUpdated(_plan.Bounds);

            void Set(int kernel, string name, GraphicsBuffer buffer) =>
                command.SetComputeBufferParam(_shader, kernel, name, buffer);
            void Bind(int kernel, AttributeId id, string name) =>
                command.SetComputeBufferParam(
                    _shader, kernel, name, resource.Attributes.Require(id).Buffer);
        }

        public BranchPathGeometryData CreatePathGeometryData() =>
            new(_plan,
                _data.SkinIndices,
                _data.SkinWeights,
                _data.SkinOffsets);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Release(_positions);
            Release(_normals);
            Release(_tangents);
            Release(_coordinates);
            Release(_triangles);
            Release(_regions);
            Release(_branchIds);
            Release(_rootDistances);
            Release(_tipDistances);
            Release(_localDistances);
            Release(_localPositions);
            Release(_depths);
            Release(_radii);
            Release(_tipWeights);
            Release(_flexibilities);
            Release(_stiffnesses);
        }

        private static void Release(GraphicsBuffer buffer) =>
            GpuBufferRegistry.Release(ref buffer);

        private GraphicsBuffer Create<T>(T[] values, string name)
            where T : struct
        {
            GraphicsBuffer buffer = new(
                GraphicsBuffer.Target.Structured,
                values.Length,
                System.Runtime.InteropServices.Marshal.SizeOf<T>());
            buffer.SetData(values);
            GpuBufferRegistry.Register(buffer, name, this);
            return buffer;
        }

        private static BranchMeshData Build(
            BranchGraphPlan plan,
            float meshCellSize,
            float meshSmoothingRadius)
        {
            meshCellSize = Mathf.Max(meshCellSize, 0.01f);
            meshSmoothingRadius = Mathf.Max(meshSmoothingRadius, 0f);
            List<Vector3> positions = new();
            List<Vector3> normals = new();
            List<Vector4> tangents = new();
            List<Vector2> coordinates = new();
            List<uint> branchIds = new();
            List<float> rootDistances = new();
            List<float> tipDistances = new();
            List<float> localDistances = new();
            List<float> localPositions = new();
            List<uint> depths = new();
            List<float> radii = new();
            List<float> tipWeights = new();
            List<float> flexibilities = new();
            List<float> stiffnesses = new();
            List<Vector3Int> triangles = new();
            List<uint> regions = new();
            float padding = Mathf.Max(meshCellSize * 3f, 0.5f);
            Bounds gridBounds = plan.Bounds;
            gridBounds.Expand(padding * 2f);
            int nx = Mathf.CeilToInt(gridBounds.size.x / meshCellSize) + 1;
            int ny = Mathf.CeilToInt(gridBounds.size.y / meshCellSize) + 1;
            int nz = Mathf.CeilToInt(gridBounds.size.z / meshCellSize) + 1;
            long voxelCount = (long)nx * ny * nz;
            if (voxelCount > 4_000_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(meshCellSize),
                    $"The branch mesh grid would contain {voxelCount} samples.");
            }
            float[] field = new float[voxelCount];
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                Vector3 point = gridBounds.min +
                    new Vector3(x, y, z) * meshCellSize;
                field[GridIndex(x, y, z, nx, ny)] =
                    EvaluateDistance(plan, point, meshSmoothingRadius) +
                    DeterministicGridBias(x, y, z, meshCellSize);
            }

            Dictionary<ulong, int> vertices = new();
            Dictionary<Vector3Int, int> coincidentVertices = new();
            int[][] tetrahedra =
            {
                new[] { 0, 5, 1, 6 },
                new[] { 0, 1, 2, 6 },
                new[] { 0, 2, 3, 6 },
                new[] { 0, 3, 7, 6 },
                new[] { 0, 7, 4, 6 },
                new[] { 0, 4, 5, 6 }
            };
            for (int z = 0; z < nz - 1; z++)
            for (int y = 0; y < ny - 1; y++)
            for (int x = 0; x < nx - 1; x++)
            {
                int[] cube =
                {
                    GridIndex(x, y, z, nx, ny),
                    GridIndex(x + 1, y, z, nx, ny),
                    GridIndex(x + 1, y + 1, z, nx, ny),
                    GridIndex(x, y + 1, z, nx, ny),
                    GridIndex(x, y, z + 1, nx, ny),
                    GridIndex(x + 1, y, z + 1, nx, ny),
                    GridIndex(x + 1, y + 1, z + 1, nx, ny),
                    GridIndex(x, y + 1, z + 1, nx, ny)
                };
                foreach (int[] tetrahedron in tetrahedra)
                {
                    List<int> crossings = new();
                    List<Vector3> crossingPositions = new();
                    List<Vector3> crossingNormals = new();
                    HashSet<int> crossingVertices = new();
                    int[,] tetraEdges =
                    {
                        { 0, 1 }, { 0, 2 }, { 0, 3 },
                        { 1, 2 }, { 1, 3 }, { 2, 3 }
                    };
                    for (int edge = 0; edge < 6; edge++)
                    {
                        int cornerA = cube[tetrahedron[tetraEdges[edge, 0]]];
                        int cornerB = cube[tetrahedron[tetraEdges[edge, 1]]];
                        bool insideA = field[cornerA] <= 0f;
                        bool insideB = field[cornerB] <= 0f;
                        if (insideA == insideB)
                            continue;
                        int low = Mathf.Min(cornerA, cornerB);
                        int high = Mathf.Max(cornerA, cornerB);
                        ulong key = ((ulong)(uint)low << 32) | (uint)high;
                        Vector3 point;
                        int vertex;
                        if (!vertices.TryGetValue(key, out vertex))
                        {
                            Vector3 pointA = GridPoint(
                                cornerA, gridBounds.min, meshCellSize, nx, ny);
                            Vector3 pointB = GridPoint(
                                cornerB, gridBounds.min, meshCellSize, nx, ny);
                            float t = Mathf.Abs(field[cornerA]) /
                                Mathf.Max(
                                    Mathf.Abs(field[cornerA]) +
                                    Mathf.Abs(field[cornerB]),
                                    1e-6f);
                            point = Vector3.Lerp(pointA, pointB, t);
                            Vector3Int positionKey = QuantizePosition(point);
                            if (!coincidentVertices.TryGetValue(
                                    positionKey, out vertex))
                            {
                                BranchSample sample = NearestSample(plan, point);
                                Vector3 normal = DistanceGradient(
                                    plan, point, meshSmoothingRadius);
                                if (normal.sqrMagnitude < 1e-8f)
                                    normal = sample.Normal;
                                vertex = positions.Count;
                                positions.Add(point);
                                normals.Add(normal.normalized);
                                tangents.Add(new Vector4(
                                    sample.Tangent.x,
                                    sample.Tangent.y,
                                    sample.Tangent.z,
                                    1f));
                                coordinates.Add(new Vector2(
                                    sample.LocalPosition,
                                    (sample.BranchId + 1f) /
                                        Mathf.Max(1f, plan.BranchCount)));
                                branchIds.Add(sample.BranchId);
                                rootDistances.Add(sample.DistanceFromRoot);
                                tipDistances.Add(sample.DistanceToTip);
                                localDistances.Add(sample.LocalDistance);
                                localPositions.Add(sample.LocalPosition);
                                depths.Add(sample.GenerationDepth);
                                radii.Add(sample.Radius);
                                tipWeights.Add(sample.TipWeight);
                                flexibilities.Add(sample.Flexibility);
                                stiffnesses.Add(sample.Stiffness);
                                coincidentVertices.Add(positionKey, vertex);
                            }
                            vertices.Add(key, vertex);
                        }
                        else
                        {
                            point = positions[vertex];
                        }
                        if (crossingVertices.Add(vertex))
                        {
                            crossings.Add(vertex);
                            crossingPositions.Add(point);
                            crossingNormals.Add(normals[vertex]);
                        }
                    }
                    if (crossings.Count < 3)
                        continue;
                    Vector3 normalHint = Vector3.zero;
                    foreach (Vector3 crossingNormal in crossingNormals)
                        normalHint += crossingNormal;
                    normalHint.Normalize();
                    Vector3 centre = Vector3.zero;
                    foreach (Vector3 point in crossingPositions)
                        centre += point;
                    centre /= crossingPositions.Count;
                    Vector3 basisU = Vector3.Cross(
                        normalHint,
                        Mathf.Abs(normalHint.y) > 0.8f
                            ? Vector3.right
                            : Vector3.up).normalized;
                    Vector3 basisV = Vector3.Cross(normalHint, basisU);
                    List<int> order = new(crossings.Count);
                    for (int i = 0; i < crossings.Count; i++)
                        order.Add(i);
                    order.Sort((a, b) =>
                    {
                        Vector3 offsetA = crossingPositions[a] - centre;
                        Vector3 offsetB = crossingPositions[b] - centre;
                        float angleA = Mathf.Atan2(
                            Vector3.Dot(offsetA, basisV),
                            Vector3.Dot(offsetA, basisU));
                        float angleB = Mathf.Atan2(
                            Vector3.Dot(offsetB, basisV),
                            Vector3.Dot(offsetB, basisU));
                        return angleA.CompareTo(angleB);
                    });
                    for (int i = 1; i < order.Count - 1; i++)
                    {
                        int a = crossings[order[0]];
                        int b = crossings[order[i]];
                        int c = crossings[order[i + 1]];
                        Vector3 faceNormal = Vector3.Cross(
                            positions[b] - positions[a],
                            positions[c] - positions[a]);
                        if (Vector3.Dot(faceNormal, normalHint) < 0f)
                            (b, c) = (c, b);
                        triangles.Add(new Vector3Int(a, b, c));
                        regions.Add(branchIds[a]);
                    }
                }
            }

            if (positions.Count == 0 || triangles.Count == 0)
                throw new InvalidOperationException(
                    "Branch mesh extraction produced no surface.");
            ResolveDegenerateTriangles(
                positions,
                normals,
                tangents,
                triangles,
                meshCellSize);
            OrientTrianglesConsistently(positions, triangles);
            List<uint> skinIndices = new(positions.Count * 4);
            List<float> skinWeights = new(positions.Count * 4);
            List<Vector3> skinOffsets = new(positions.Count * 4);
            BuildSkinBindings(
                plan,
                positions,
                skinIndices,
                skinWeights,
                skinOffsets);
            HashSet<long> edges = new();
            foreach (Vector3Int triangle in triangles)
            {
                AddEdge(edges, triangle.x, triangle.y);
                AddEdge(edges, triangle.y, triangle.z);
                AddEdge(edges, triangle.z, triangle.x);
            }
            return new BranchMeshData(
                positions.ToArray(),
                normals.ToArray(),
                tangents.ToArray(),
                coordinates.ToArray(),
                triangles.ToArray(),
                regions.ToArray(),
                branchIds.ToArray(),
                rootDistances.ToArray(),
                tipDistances.ToArray(),
                localDistances.ToArray(),
                localPositions.ToArray(),
                depths.ToArray(),
                radii.ToArray(),
                tipWeights.ToArray(),
                flexibilities.ToArray(),
                stiffnesses.ToArray(),
                skinIndices.ToArray(),
                skinWeights.ToArray(),
                skinOffsets.ToArray(),
                edges.Count);
        }

        private static int GridIndex(
            int x,
            int y,
            int z,
            int nx,
            int ny) => x + nx * (y + ny * z);

        private static float DeterministicGridBias(
            int x,
            int y,
            int z,
            float cellSize)
        {
            uint hash = (uint)x * 73856093u ^
                (uint)y * 19349663u ^
                (uint)z * 83492791u;
            hash ^= hash >> 13;
            hash *= 1274126177u;
            float unit = (hash & 0xFFFF) / 65535f - 0.5f;
            return unit * cellSize * 0.0002f;
        }

        private static void ResolveDegenerateTriangles(
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector3Int> triangles,
            float cellSize)
        {
            const float minimumArea = 1e-7f;
            for (int pass = 0; pass < 4; pass++)
            {
                HashSet<int> affected = new();
                foreach (Vector3Int triangle in triangles)
                {
                    float area = Vector3.Cross(
                        positions[triangle.y] - positions[triangle.x],
                        positions[triangle.z] - positions[triangle.x]).magnitude;
                    if (area > minimumArea)
                        continue;
                    affected.Add(triangle.x);
                    affected.Add(triangle.y);
                    affected.Add(triangle.z);
                }
                if (affected.Count == 0)
                    return;
                foreach (int vertex in affected)
                {
                    Vector3 normal = normals[vertex];
                    Vector3 tangent = tangents[vertex];
                    Vector3 binormal = Vector3.Cross(normal, tangent);
                    if (binormal.sqrMagnitude < 1e-8f)
                        binormal = Vector3.up;
                    binormal.Normalize();
                    float angle = vertex * 12.9898f + pass * 78.233f;
                    Vector2 offset = new(
                        Mathf.Sin(angle),
                        Mathf.Cos(angle * 1.618f));
                    positions[vertex] +=
                        (normal * offset.x + binormal * offset.y) *
                        cellSize * 0.0125f;
                }
            }
            foreach (Vector3Int triangle in triangles)
            {
                float area = Vector3.Cross(
                    positions[triangle.y] - positions[triangle.x],
                    positions[triangle.z] - positions[triangle.x]).magnitude;
                if (area <= minimumArea)
                {
                    throw new InvalidOperationException(
                        $"Could not resolve degenerate branch triangle {triangle}.");
                }
            }
        }

        private static void OrientTrianglesConsistently(
            List<Vector3> positions,
            List<Vector3Int> triangles)
        {
            Dictionary<long, List<EdgeFace>> edgeFaces = new();
            for (int face = 0; face < triangles.Count; face++)
            {
                Vector3Int triangle = triangles[face];
                Add(triangle.x, triangle.y);
                Add(triangle.y, triangle.z);
                Add(triangle.z, triangle.x);

                void Add(int a, int b)
                {
                    long key = ((long)Mathf.Min(a, b) << 32) |
                        (uint)Mathf.Max(a, b);
                    if (!edgeFaces.TryGetValue(key, out List<EdgeFace> faces))
                    {
                        faces = new List<EdgeFace>(2);
                        edgeFaces.Add(key, faces);
                    }
                    faces.Add(new EdgeFace(face, a, b));
                }
            }

            bool[] visited = new bool[triangles.Count];
            bool[] flipped = new bool[triangles.Count];
            Queue<int> pending = new();
            for (int componentStart = 0;
                componentStart < triangles.Count;
                componentStart++)
            {
                if (visited[componentStart])
                    continue;
                visited[componentStart] = true;
                pending.Enqueue(componentStart);
                while (pending.Count > 0)
                {
                    int face = pending.Dequeue();
                    Vector3Int triangle = triangles[face];
                    Process(triangle.x, triangle.y);
                    Process(triangle.y, triangle.z);
                    Process(triangle.z, triangle.x);

                    void Process(int a, int b)
                    {
                        long key = ((long)Mathf.Min(a, b) << 32) |
                            (uint)Mathf.Max(a, b);
                        if (!edgeFaces.TryGetValue(
                                key, out List<EdgeFace> faces) ||
                            faces.Count != 2)
                        {
                            return;
                        }
                        EdgeFace neighbour = faces[0].Face == face
                            ? faces[1]
                            : faces[0];
                        bool sameDirection =
                            (a == neighbour.A && b == neighbour.B);
                        bool flipNeighbour =
                            flipped[face] ^ sameDirection;
                        if (!visited[neighbour.Face])
                        {
                            flipped[neighbour.Face] = flipNeighbour;
                            visited[neighbour.Face] = true;
                            pending.Enqueue(neighbour.Face);
                        }
                    }
                }
            }

            for (int i = 0; i < triangles.Count; i++)
            {
                if (!flipped[i])
                    continue;
                Vector3Int triangle = triangles[i];
                triangles[i] = new Vector3Int(
                    triangle.x, triangle.z, triangle.y);
            }

            double signedVolume = 0d;
            foreach (Vector3Int triangle in triangles)
            {
                signedVolume += Vector3.Dot(
                    positions[triangle.x],
                    Vector3.Cross(
                        positions[triangle.y],
                        positions[triangle.z]));
            }
            if (signedVolume >= 0d)
                return;
            for (int i = 0; i < triangles.Count; i++)
            {
                Vector3Int triangle = triangles[i];
                triangles[i] = new Vector3Int(
                    triangle.x, triangle.z, triangle.y);
            }
        }

        private readonly struct EdgeFace
        {
            public EdgeFace(int face, int a, int b)
            {
                Face = face;
                A = a;
                B = b;
            }

            public int Face { get; }
            public int A { get; }
            public int B { get; }
        }

        private static Vector3Int QuantizePosition(Vector3 position)
        {
            const float scale = 100000f;
            return new Vector3Int(
                Mathf.RoundToInt(position.x * scale),
                Mathf.RoundToInt(position.y * scale),
                Mathf.RoundToInt(position.z * scale));
        }

        private static Vector3 GridPoint(
            int index,
            Vector3 origin,
            float cell,
            int nx,
            int ny)
        {
            int x = index % nx;
            int y = index / nx % ny;
            int z = index / (nx * ny);
            return origin + new Vector3(x, y, z) * cell;
        }

        private static float EvaluateDistance(
            BranchGraphPlan plan,
            Vector3 point,
            float smoothingRadius)
        {
            float distance = float.MaxValue;
            foreach (BranchRecord branch in plan.Branches)
            {
                int start = (int)branch.SampleOffset;
                int count = (int)branch.SampleCount;
                for (int i = 0; i < count - 1; i++)
                {
                    BranchSample a = plan.Samples[start + i];
                    BranchSample b = plan.Samples[start + i + 1];
                    Vector3 segment = b.Position - a.Position;
                    float lengthSquared = segment.sqrMagnitude;
                    float t = lengthSquared > 1e-8f
                        ? Mathf.Clamp01(
                            Vector3.Dot(point - a.Position, segment) /
                            lengthSquared)
                        : 0f;
                    Vector3 nearest = Vector3.Lerp(a.Position, b.Position, t);
                    float radius = Mathf.Lerp(a.Radius, b.Radius, t);
                    float capsule = Vector3.Distance(point, nearest) - radius;
                    distance = distance == float.MaxValue
                        ? capsule
                        : SmoothMinimum(
                            distance, capsule, smoothingRadius);
                }
            }
            return distance;
        }

        private static Vector3 DistanceGradient(
            BranchGraphPlan plan,
            Vector3 point,
            float smoothingRadius)
        {
            const float epsilon = 0.001f;
            return new Vector3(
                EvaluateDistance(
                    plan, point + Vector3.right * epsilon, smoothingRadius) -
                    EvaluateDistance(
                        plan, point - Vector3.right * epsilon, smoothingRadius),
                EvaluateDistance(
                    plan, point + Vector3.up * epsilon, smoothingRadius) -
                    EvaluateDistance(
                        plan, point - Vector3.up * epsilon, smoothingRadius),
                EvaluateDistance(
                    plan, point + Vector3.forward * epsilon, smoothingRadius) -
                    EvaluateDistance(
                        plan, point - Vector3.forward * epsilon, smoothingRadius));
        }

        private static float SmoothMinimum(
            float a,
            float b,
            float radius)
        {
            if (radius <= 0f)
                return Mathf.Min(a, b);
            float blend = Mathf.Clamp01(
                0.5f + 0.5f * (b - a) / radius);
            return Mathf.Lerp(b, a, blend) -
                radius * blend * (1f - blend);
        }

        private static BranchSample NearestSample(
            BranchGraphPlan plan,
            Vector3 point)
        {
            BranchSample result = plan.Samples[0];
            float best = float.MaxValue;
            foreach (BranchSample sample in plan.Samples)
            {
                float distance = (
                    sample.Position - point).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    result = sample;
                }
            }
            return result;
        }

        private static void BuildSkinBindings(
            BranchGraphPlan plan,
            List<Vector3> positions,
            List<uint> indices,
            List<float> weights,
            List<Vector3> offsets)
        {
            for (int vertex = 0; vertex < positions.Count; vertex++)
            {
                int[] nearest = { -1, -1, -1, -1 };
                float[] distance = {
                    float.MaxValue,
                    float.MaxValue,
                    float.MaxValue,
                    float.MaxValue
                };
                for (int sample = 0; sample < plan.SampleCount; sample++)
                {
                    float squared =
                        (positions[vertex] -
                         plan.Samples[sample].Position).sqrMagnitude;
                    for (int slot = 0; slot < 4; slot++)
                    {
                        if (squared >= distance[slot])
                            continue;
                        for (int shift = 3; shift > slot; shift--)
                        {
                            distance[shift] = distance[shift - 1];
                            nearest[shift] = nearest[shift - 1];
                        }
                        distance[slot] = squared;
                        nearest[slot] = sample;
                        break;
                    }
                }
                float totalWeight = 0f;
                for (int slot = 0; slot < 4; slot++)
                {
                    if (nearest[slot] < 0)
                    {
                        nearest[slot] = nearest[0];
                        distance[slot] = float.MaxValue;
                    }
                    float weight = 1f /
                        Mathf.Max(distance[slot], 0.0001f);
                    weights.Add(weight);
                    totalWeight += weight;
                    indices.Add((uint)nearest[slot]);
                    BranchSample sample = plan.Samples[nearest[slot]];
                    offsets.Add(new Vector3(
                        Vector3.Dot(
                            positions[vertex] - sample.Position,
                            sample.Tangent),
                        Vector3.Dot(
                            positions[vertex] - sample.Position,
                            sample.Normal),
                        Vector3.Dot(
                            positions[vertex] - sample.Position,
                            sample.Binormal)));
                }
                int first = weights.Count - 4;
                for (int slot = 0; slot < 4; slot++)
                    weights[first + slot] /= totalWeight;
            }
        }

        private static void AddEdge(
            HashSet<long> edges,
            int a,
            int b)
        {
            long low = Mathf.Min(a, b);
            long high = Mathf.Max(a, b);
            edges.Add((low << 32) | high);
        }

        private sealed class BranchMeshData
        {
            public BranchMeshData(
                Vector3[] positions,
                Vector3[] normals,
                Vector4[] tangents,
                Vector2[] coordinates,
                Vector3Int[] triangles,
                uint[] regions,
                uint[] branchIds,
                float[] rootDistances,
                float[] tipDistances,
                float[] localDistances,
                float[] localPositions,
                uint[] depths,
                float[] radii,
                float[] tipWeights,
                float[] flexibilities,
                float[] stiffnesses,
                uint[] skinIndices,
                float[] skinWeights,
                Vector3[] skinOffsets,
                int edgeCount)
            {
                Positions = positions;
                Normals = normals;
                Tangents = tangents;
                Coordinates = coordinates;
                Triangles = triangles;
                Regions = regions;
                BranchIds = branchIds;
                RootDistances = rootDistances;
                TipDistances = tipDistances;
                LocalDistances = localDistances;
                LocalPositions = localPositions;
                Depths = depths;
                Radii = radii;
                TipWeights = tipWeights;
                Flexibilities = flexibilities;
                Stiffnesses = stiffnesses;
                SkinIndices = skinIndices;
                SkinWeights = skinWeights;
                SkinOffsets = skinOffsets;
                EdgeCount = edgeCount;
            }

            public Vector3[] Positions { get; }
            public Vector3[] Normals { get; }
            public Vector4[] Tangents { get; }
            public Vector2[] Coordinates { get; }
            public Vector3Int[] Triangles { get; }
            public uint[] Regions { get; }
            public uint[] BranchIds { get; }
            public float[] RootDistances { get; }
            public float[] TipDistances { get; }
            public float[] LocalDistances { get; }
            public float[] LocalPositions { get; }
            public uint[] Depths { get; }
            public float[] Radii { get; }
            public float[] TipWeights { get; }
            public float[] Flexibilities { get; }
            public float[] Stiffnesses { get; }
            public uint[] SkinIndices { get; }
            public float[] SkinWeights { get; }
            public Vector3[] SkinOffsets { get; }
            public int EdgeCount { get; }
        }
    }
}
