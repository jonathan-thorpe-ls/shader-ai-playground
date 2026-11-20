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
            : this(
                plan,
                BranchMeshVariant.ImplicitUnion,
                12,
                2,
                1,
                0.75f,
                meshCellSize,
                meshSmoothingRadius,
                shader)
        {
        }

        public BranchGeometryGenerator(
            BranchGraphPlan plan,
            BranchMeshVariant variant,
            int radialSegments,
            int junctionRings = 2,
            int junctionSubdivisions = 1,
            float junctionThroatScale = 0.75f,
            float meshCellSize = 0.08f,
            float meshSmoothingRadius = 0.12f,
            ComputeShader shader = null)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _data = Build(
                plan,
                variant,
                radialSegments,
                junctionRings,
                junctionSubdivisions,
                junctionThroatScale,
                meshCellSize,
                meshSmoothingRadius);
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
            BranchMeshVariant variant,
            int radialSegments,
            int junctionRings,
            int junctionSubdivisions,
            float junctionThroatScale,
            float meshCellSize,
            float meshSmoothingRadius)
        {
            return variant switch
            {
                BranchMeshVariant.ImplicitUnion =>
                    BuildImplicitUnion(plan, meshCellSize, meshSmoothingRadius),
                BranchMeshVariant.PortJunction =>
                    BuildPortJunction(
                        plan,
                        radialSegments,
                        junctionRings,
                        junctionThroatScale,
                        junctionSubdivisions),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(variant), variant, null)
            };
        }

        private static BranchMeshData BuildImplicitUnion(
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

        private static BranchMeshData BuildPortJunction(
            BranchGraphPlan plan,
            int radialSegments,
            int junctionRings,
            float junctionThroatScale,
            int junctionSubdivisions)
        {
            radialSegments = Mathf.Clamp(radialSegments, 3, 32);
            junctionRings = Mathf.Clamp(junctionRings, 1, 4);
            if ((radialSegments & 1) != 0)
                radialSegments++;

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
            List<int> skinSamples = new();
            List<Vector3Int> triangles = new();
            List<uint> regions = new();
            int[][] rings = new int[plan.SampleCount][];
            List<int>[] children = new List<int>[plan.BranchCount];
            for (int branch = 0; branch < plan.BranchCount; branch++)
            {
                children[branch] = new List<int>();
                BranchRecord record = plan.Branches[branch];
                if (record.ParentId != uint.MaxValue)
                    children[(int)record.ParentId].Add(branch);
            }

            for (int branchIndex = 0;
                branchIndex < plan.BranchCount;
                branchIndex++)
            {
                BranchRecord branch = plan.Branches[branchIndex];
                int first = branch.ParentId == uint.MaxValue ? 0 : 1;
                int last = branch.Terminal
                    ? (int)branch.SampleCount - 1
                    : (int)branch.SampleCount - 2;
                if (last < first)
                {
                    throw new InvalidOperationException(
                        $"Branch {branch.Id} does not contain enough samples for PortJunction.");
                }
                for (int sample = first; sample <= last; sample++)
                {
                    int pathSample = (int)branch.SampleOffset + sample;
                    rings[pathSample] = CreateRing(
                        pathSample,
                        radialSegments,
                        sample / (float)Mathf.Max(1, branch.SampleCount - 1));
                    if (sample < last)
                    {
                        int next = (int)branch.SampleOffset + sample + 1;
                        rings[next] = CreateRing(
                            next,
                            radialSegments,
                            (sample + 1) /
                                (float)Mathf.Max(1, branch.SampleCount - 1));
                        ConnectFullLoops(
                            rings[pathSample],
                            rings[next],
                            triangles,
                            regions,
                            branch.Id);
                    }
                }
                if (branch.ParentId == uint.MaxValue)
                    AddCap(rings[(int)branch.SampleOffset], true, branch.Id);
                if (branch.Terminal)
                    AddCap(
                        rings[(int)(branch.SampleOffset + branch.SampleCount - 1)],
                        false,
                        branch.Id);
            }

            for (int parentIndex = 0;
                parentIndex < plan.BranchCount;
                parentIndex++)
            {
                List<int> childBranches = children[parentIndex];
                if (childBranches.Count == 0)
                    continue;
                if (childBranches.Count > 2)
                {
                    throw new InvalidOperationException(
                        $"PortJunction supports one or two children per node; branch {parentIndex} has {childBranches.Count}.");
                }
                BranchRecord parent = plan.Branches[parentIndex];
                int parentSample =
                    (int)(parent.SampleOffset + parent.SampleCount - 2);
                int[] parentRing = rings[parentSample];
                int junctionSample =
                    (int)(parent.SampleOffset + parent.SampleCount - 1);
                int firstChildSample =
                    (int)plan.Branches[childBranches[0]].SampleOffset + 1;
                int[] firstChildRing = rings[firstChildSample];
                List<int[]> parentRings = CreateJunctionRings(
                    parentRing,
                    parentSample,
                    junctionSample);
                List<int[]> firstChildRings = CreateJunctionRings(
                    firstChildRing,
                    firstChildSample,
                    junctionSample);
                if (childBranches.Count == 1)
                {
                    int[] previous = parentRing;
                    foreach (int[] ring in parentRings)
                    {
                        ConnectFullLoops(
                            previous, AlignLoop(ring, previous),
                            triangles, regions, parent.Id);
                        previous = AlignLoop(ring, previous);
                    }
                    for (int i = firstChildRings.Count - 1; i >= 0; i--)
                    {
                        int[] next = AlignLoop(
                            firstChildRings[i], previous);
                        ConnectFullLoops(
                            previous, next,
                            triangles, regions, parent.Id);
                        previous = next;
                    }
                    ConnectFullLoops(
                        previous,
                        AlignLoop(firstChildRing, previous),
                        triangles,
                        regions,
                        parent.Id);
                    continue;
                }
                int secondChildSample =
                    (int)plan.Branches[childBranches[1]].SampleOffset + 1;
                List<int[]> secondChildRings = CreateJunctionRings(
                    rings[secondChildSample],
                    secondChildSample,
                    junctionSample);
                ConnectPortRings(parentRing, parentRings, parent.Id);
                ConnectPortRings(
                    rings[firstChildSample],
                    firstChildRings,
                    (uint)childBranches[0]);
                ConnectPortRings(
                    rings[secondChildSample],
                    secondChildRings,
                    (uint)childBranches[1]);
                ConnectYJunction(
                    parentRings[parentRings.Count - 1],
                    firstChildRings[firstChildRings.Count - 1],
                    secondChildRings[secondChildRings.Count - 1],
                    triangles,
                    regions,
                    parent.Id);
            }

            OrientTrianglesConsistently(positions, triangles);
            for (int subdivision = 0;
                subdivision < Mathf.Clamp(junctionSubdivisions, 0, 2);
                subdivision++)
            {
                SubdividePortJunction(
                    plan,
                    positions,
                    normals,
                    tangents,
                    coordinates,
                    branchIds,
                    rootDistances,
                    tipDistances,
                    localDistances,
                    localPositions,
                    depths,
                    radii,
                    tipWeights,
                    flexibilities,
                    stiffnesses,
                    skinSamples,
                    triangles,
                    regions);
            }
            RelaxJunctionPositions(
                positions,
                triangles,
                6);
            List<uint> skinIndices = new(positions.Count * 4);
            List<float> skinWeights = new(positions.Count * 4);
            List<Vector3> skinOffsets = new(positions.Count * 4);
            BuildDirectSkinBindings(
                skinSamples,
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

            int[] CreateRing(
                int pathSample,
                int sides,
                float ringPosition)
            {
                if (rings[pathSample] != null)
                    return rings[pathSample];
                BranchSample sample = plan.Samples[pathSample];
                int[] result = new int[sides];
                for (int side = 0; side < sides; side++)
                {
                    float angle = side / (float)sides * Mathf.PI * 2f;
                    Vector2 section = new(
                        Mathf.Cos(angle) * sample.Radius,
                        Mathf.Sin(angle) * sample.Radius);
                    Vector3 radial =
                        sample.Normal * section.x +
                        sample.Binormal * section.y;
                    int vertex = positions.Count;
                    result[side] = vertex;
                    positions.Add(sample.Position + radial);
                    normals.Add(radial.normalized);
                    tangents.Add(new Vector4(
                        sample.Tangent.x,
                        sample.Tangent.y,
                        sample.Tangent.z,
                        1f));
                    coordinates.Add(new Vector2(
                        ringPosition,
                        side / (float)sides));
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
                    skinSamples.Add(pathSample);
                }
                rings[pathSample] = result;
                return result;
            }

            List<int[]> CreateJunctionRings(
                int[] portRing,
                int portSample,
                int junctionSample)
            {
                List<int[]> result = new();
                BranchSample port = plan.Samples[portSample];
                BranchSample junction = plan.Samples[junctionSample];
                Vector3 away = port.Position - junction.Position;
                Vector3 awayDirection = away.sqrMagnitude > 1e-8f
                    ? away.normalized
                    : port.Tangent;
                Vector3 targetTangent = awayDirection;
                float throatDistance =
                    Mathf.Max(port.Radius, junction.Radius) *
                    junctionThroatScale;
                Vector3 target = junction.Position +
                    awayDirection * throatDistance;
                int[] previous = portRing;
                for (int level = 0; level < junctionRings; level++)
                {
                    float linear = (level + 1f) /
                        (junctionRings + 1f);
                    float t = linear * linear * (3f - 2f * linear);
                    Vector3 centre = Vector3.Lerp(
                        port.Position,
                        target,
                        t);
                    Vector3 ringTangent =
                        Vector3.Slerp(port.Tangent, targetTangent, t);
                    Vector3 ringNormal = port.Normal -
                        ringTangent * Vector3.Dot(port.Normal, ringTangent);
                    if (ringNormal.sqrMagnitude < 1e-8f)
                        ringNormal = port.Binormal;
                    ringNormal.Normalize();
                    Vector3 ringBinormal =
                        Vector3.Cross(ringTangent, ringNormal).normalized;
                    float radius = Mathf.Lerp(
                        port.Radius,
                        port.Radius * 0.9f,
                        t);
                    int[] ring = new int[radialSegments];
                    for (int side = 0; side < radialSegments; side++)
                    {
                        float angle = side /
                            (float)radialSegments * Mathf.PI * 2f;
                        Vector3 radial = (
                            ringNormal * Mathf.Cos(angle) +
                            ringBinormal * Mathf.Sin(angle)) * radius;
                        int vertex = positions.Count;
                        ring[side] = vertex;
                        positions.Add(centre + radial);
                        normals.Add(radial.normalized);
                        tangents.Add(new Vector4(
                            ringTangent.x,
                            ringTangent.y,
                            ringTangent.z,
                            1f));
                        coordinates.Add(new Vector2(
                            Mathf.Lerp(
                                port.LocalPosition,
                                junction.LocalPosition,
                                t),
                            side / (float)radialSegments));
                        branchIds.Add(port.BranchId);
                        rootDistances.Add(Mathf.Lerp(
                            port.DistanceFromRoot,
                            junction.DistanceFromRoot,
                            t));
                        tipDistances.Add(Mathf.Lerp(
                            port.DistanceToTip,
                            junction.DistanceToTip,
                            t));
                        localDistances.Add(Mathf.Lerp(
                            port.LocalDistance,
                            junction.LocalDistance,
                            t));
                        localPositions.Add(Mathf.Lerp(
                            port.LocalPosition,
                            junction.LocalPosition,
                            t));
                        depths.Add(port.GenerationDepth);
                        radii.Add(radius);
                        tipWeights.Add(port.TipWeight);
                        flexibilities.Add(port.Flexibility);
                        stiffnesses.Add(port.Stiffness);
                        skinSamples.Add(portSample);
                    }
                    result.Add(AlignLoop(ring, previous));
                    previous = result[result.Count - 1];
                }
                return result;
            }

            void ConnectPortRings(
                int[] port,
                List<int[]> transitionRings,
                uint branchId)
            {
                int[] previous = port;
                foreach (int[] ring in transitionRings)
                {
                    ConnectFullLoops(
                        previous, ring, triangles, regions, branchId);
                    previous = ring;
                }
            }

            void AddCap(int[] ring, bool root, uint branchId)
            {
                BranchSample sample = plan.Samples[
                    skinSamples[ring[0]]];
                int centre = positions.Count;
                positions.Add(sample.Position);
                normals.Add(root
                    ? -sample.Tangent
                    : sample.Tangent);
                tangents.Add(new Vector4(
                    sample.Tangent.x,
                    sample.Tangent.y,
                    sample.Tangent.z,
                    1f));
                coordinates.Add(new Vector2(
                    sample.LocalPosition, 0.5f));
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
                skinSamples.Add(skinSamples[ring[0]]);
                for (int i = 0; i < ring.Length; i++)
                {
                    int next = (i + 1) % ring.Length;
                    AddTriangle(
                        triangles,
                        regions,
                        centre,
                        root ? ring[next] : ring[i],
                        root ? ring[i] : ring[next],
                        branchId);
                }
            }

            int[] AlignLoop(int[] loop, int[] reference)
            {
                int count = loop.Length;
                float best = float.MaxValue;
                bool reverse = false;
                int offset = 0;
                for (int direction = 0; direction < 2; direction++)
                for (int shift = 0; shift < count; shift++)
                {
                    float error = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        int mapped = direction == 0
                            ? (i + shift) % count
                            : (shift - i + count) % count;
                        error += (
                            positions[loop[mapped]] -
                            positions[reference[i]]).sqrMagnitude;
                    }
                    if (error >= best)
                        continue;
                    best = error;
                    reverse = direction != 0;
                    offset = shift;
                }
                int[] result = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int mapped = reverse
                        ? (offset - i + count) % count
                        : (i + offset) % count;
                    result[i] = loop[mapped];
                }
                return result;
            }

            void ConnectYJunction(
                int[] parent,
                int[] first,
                int[] second,
                List<Vector3Int> targetTriangles,
                List<uint> targetRegions,
                uint branchId)
            {
                int count = parent.Length;
                int half = count / 2;
                int[] p = (int[])parent.Clone();
                int[] a = AlignLoop(first, p);
                int[] b = AlignLoop(second, p);
                ConnectArc(p, 0, a, 0);
                ConnectArc(p, half, b, half);
                ConnectRemainingHalves(a, b);

                void ConnectArc(
                    int[] source,
                    int sourceStart,
                    int[] target,
                    int targetStart)
                {
                    for (int i = 0; i < half; i++)
                    {
                        int s0 = source[(sourceStart + i) % count];
                        int s1 = source[(sourceStart + i + 1) % count];
                        int t0 = target[(targetStart + i) % count];
                        int t1 = target[(targetStart + i + 1) % count];
                        AddTriangle(
                            triangles, regions, s0, t0, t1, branchId);
                        AddTriangle(
                            triangles, regions, s0, t1, s1, branchId);
                    }
                }

                void ConnectRemainingHalves(int[] left, int[] right)
                {
                    for (int i = 0; i < half; i++)
                    {
                        int l0 = left[(half + i) % count];
                        int l1 = left[(half + i + 1) % count];
                        int r0 = right[
                            (half - i + count) % count];
                        int r1 = right[
                            (half - i - 1 + count) % count];
                        AddTriangle(
                            triangles, regions, l0, r0, r1, branchId);
                        AddTriangle(
                            triangles, regions, l0, r1, l1, branchId);
                    }
                    AddTriangle(
                        triangles, regions, p[0], a[0], b[0], branchId);
                    AddTriangle(
                        triangles, regions, p[half], a[half], b[half], branchId);
                }
            }

            void ConnectFullLoops(
                int[] left,
                int[] right,
                List<Vector3Int> targetTriangles,
                List<uint> targetRegions,
                uint branchId)
            {
                for (int i = 0; i < left.Length; i++)
                {
                    int next = (i + 1) % left.Length;
                    AddTriangle(
                        targetTriangles,
                        targetRegions,
                        left[i],
                        right[i],
                        right[next],
                        branchId);
                    AddTriangle(
                        targetTriangles,
                        targetRegions,
                        left[i],
                        right[next],
                        left[next],
                        branchId);
                }
            }

        }

        private static void BuildDirectSkinBindings(
            List<int> samples,
            BranchGraphPlan plan,
            List<Vector3> positions,
            List<uint> indices,
            List<float> weights,
            List<Vector3> offsets)
        {
            for (int vertex = 0; vertex < positions.Count; vertex++)
            {
                int sampleIndex = samples[vertex];
                BranchSample sample = plan.Samples[sampleIndex];
                Vector3 local = positions[vertex] - sample.Position;
                Vector3 offset = new(
                    Vector3.Dot(local, sample.Tangent),
                    Vector3.Dot(local, sample.Normal),
                    Vector3.Dot(local, sample.Binormal));
                for (int influence = 0; influence < 4; influence++)
                {
                    indices.Add((uint)sampleIndex);
                    weights.Add(influence == 0 ? 1f : 0f);
                    offsets.Add(influence == 0 ? offset : Vector3.zero);
                }
            }
        }

        private static void SubdividePortJunction(
                    BranchGraphPlan plan,
                    List<Vector3> positions,
                    List<Vector3> normals,
                    List<Vector4> tangents,
                    List<Vector2> coordinates,
                    List<uint> branchIds,
                    List<float> rootDistances,
                    List<float> tipDistances,
                    List<float> localDistances,
                    List<float> localPositions,
                    List<uint> depths,
                    List<float> radii,
                    List<float> tipWeights,
                    List<float> flexibilities,
                    List<float> stiffnesses,
                    List<int> skinSamples,
                    List<Vector3Int> triangles,
                    List<uint> regions)
                {
                    int originalVertexCount = positions.Count;
                    int originalTriangleCount = triangles.Count;
                    Dictionary<long, List<int>> edgeFaces = new();
                    Dictionary<long, int> edgeMidpoints = new();
                    HashSet<int>[] neighbours = new HashSet<int>[originalVertexCount];
                    for (int i = 0; i < neighbours.Length; i++)
                        neighbours[i] = new HashSet<int>();
                    for (int face = 0; face < originalTriangleCount; face++)
                    {
                        Vector3Int triangle = triangles[face];
                        Register(triangle.x, triangle.y, face);
                        Register(triangle.y, triangle.z, face);
                        Register(triangle.z, triangle.x, face);
                    }

                    Vector3[] smoothPositions = positions.GetRange(
                        0, originalVertexCount).ToArray();
                    Vector3[] normalVectors = normals.GetRange(
                        0, originalVertexCount).ToArray();
                    for (int vertex = 0; vertex < originalVertexCount; vertex++)
                    {
                        HashSet<int> adjacent = neighbours[vertex];
                        float beta = adjacent.Count == 3
                            ? 3f / 16f
                            : 3f / (8f * adjacent.Count);
                        Vector3 average = Vector3.zero;
                        foreach (int neighbour in adjacent)
                            average += positions[neighbour];
                        smoothPositions[vertex] =
                            positions[vertex] * (1f - beta * adjacent.Count) +
                            average * beta;
                        if (adjacent.Count == 0)
                            smoothPositions[vertex] = positions[vertex];
                    }
                    for (int vertex = 0; vertex < originalVertexCount; vertex++)
                    {
                        positions[vertex] = smoothPositions[vertex];
                        normals[vertex] = normalVectors[vertex];
                    }

                    foreach (KeyValuePair<long, List<int>> edge in edgeFaces)
                    {
                        DecodeEdge(edge.Key, out int a, out int b);
                        Vector3 midpoint;
                        if (edge.Value.Count == 2)
                        {
                            Vector3Int first = triangles[edge.Value[0]];
                            Vector3Int second = triangles[edge.Value[1]];
                            int c = Opposite(first, a, b);
                            int d = Opposite(second, a, b);
                            midpoint =
                                (positions[a] + positions[b]) * 3f / 8f +
                                (positions[c] + positions[d]) * 1f / 8f;
                        }
                        else
                        {
                            midpoint = (positions[a] + positions[b]) * 0.5f;
                        }
                        int vertex = positions.Count;
                        edgeMidpoints.Add(edge.Key, vertex);
                        positions.Add(midpoint);
                        BranchSample sample = NearestSample(plan, midpoint);
                        int pathSample = IndexOfNearestSample(plan, midpoint);
                        Vector3 restNormal = (
                            normals[a] + normals[b]).normalized;
                        normals.Add(restNormal);
                        tangents.Add(((Vector3)(
                            (Vector4)tangents[a] +
                            (Vector4)tangents[b])).normalized);
                        coordinates.Add(
                            (coordinates[a] + coordinates[b]) * 0.5f);
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
                        skinSamples.Add(pathSample);
                    }

                    List<Vector3Int> refinedTriangles = new(
                        originalTriangleCount * 4);
                    List<uint> refinedRegions = new(
                        originalTriangleCount * 4);
                    for (int face = 0; face < originalTriangleCount; face++)
                    {
                        Vector3Int triangle = triangles[face];
                        int ab = edgeMidpoints[EdgeKey(
                            triangle.x, triangle.y)];
                        int bc = edgeMidpoints[EdgeKey(
                            triangle.y, triangle.z)];
                        int ca = edgeMidpoints[EdgeKey(
                            triangle.z, triangle.x)];
                        AddTriangle(
                            refinedTriangles,
                            refinedRegions,
                            triangle.x, ab, ca,
                            regions[face]);
                        AddTriangle(
                            refinedTriangles,
                            refinedRegions,
                            ab, triangle.y, bc,
                            regions[face]);
                        AddTriangle(
                            refinedTriangles,
                            refinedRegions,
                            ca, bc, triangle.z,
                            regions[face]);
                        AddTriangle(
                            refinedTriangles,
                            refinedRegions,
                            ab, bc, ca,
                            regions[face]);
                    }
                    triangles.Clear();
                    triangles.AddRange(refinedTriangles);
                    regions.Clear();
                    regions.AddRange(refinedRegions);

                    void Register(int a, int b, int face)
                    {
                        neighbours[a].Add(b);
                        neighbours[b].Add(a);
                        long key = EdgeKey(a, b);
                        if (!edgeFaces.TryGetValue(key, out List<int> faces))
                        {
                            faces = new List<int>(2);
                            edgeFaces.Add(key, faces);
                        }
                        faces.Add(face);
                    }
                }

                private static void RelaxJunctionPositions(
                    List<Vector3> positions,
                    List<Vector3Int> triangles,
                    int iterations)
                {
                    List<HashSet<int>> neighbours = new(positions.Count);
                    for (int i = 0; i < positions.Count; i++)
                        neighbours.Add(new HashSet<int>());
                    foreach (Vector3Int triangle in triangles)
                    {
                        neighbours[triangle.x].Add(triangle.y);
                        neighbours[triangle.x].Add(triangle.z);
                        neighbours[triangle.y].Add(triangle.x);
                        neighbours[triangle.y].Add(triangle.z);
                        neighbours[triangle.z].Add(triangle.x);
                        neighbours[triangle.z].Add(triangle.y);
                    }
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        Vector3[] next = positions.ToArray();
                        for (int vertex = 0; vertex < positions.Count; vertex++)
                        {
                            if (neighbours[vertex].Count == 0)
                                continue;
                            Vector3 average = Vector3.zero;
                            foreach (int neighbour in neighbours[vertex])
                                average += positions[neighbour];
                            average /= neighbours[vertex].Count;
                            next[vertex] = Vector3.Lerp(
                                positions[vertex], average, 0.16f);
                        }
                        positions.Clear();
                        positions.AddRange(next);
                    }
                }

                private static long EdgeKey(int a, int b) =>
                    ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

                private static void DecodeEdge(
                    long key,
                    out int a,
                    out int b)
                {
                    a = (int)(key >> 32);
                    b = (int)(key & 0xFFFFFFFF);
                }

                private static int Opposite(
                    Vector3Int triangle,
                    int a,
                    int b)
                {
                    if (triangle.x != a && triangle.x != b)
                        return triangle.x;
                    if (triangle.y != a && triangle.y != b)
                        return triangle.y;
                    return triangle.z;
                }

                private static int IndexOfNearestSample(
                    BranchGraphPlan plan,
                    Vector3 position)
                {
                    int result = 0;
                    float best = float.MaxValue;
                    for (int i = 0; i < plan.SampleCount; i++)
                    {
                        float distance =
                            (plan.Samples[i].Position - position).sqrMagnitude;
                        if (distance >= best)
                            continue;
                        best = distance;
                        result = i;
                    }
                    return result;
                }

        private static void AddTriangle(
            List<Vector3Int> triangles,
            List<uint> regions,
            int a,
            int b,
            int c,
            uint region)
        {
            triangles.Add(new Vector3Int(a, b, c));
            regions.Add(region);
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
