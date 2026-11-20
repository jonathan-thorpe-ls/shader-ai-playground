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
            uint[] vertexPathIndices,
            Vector2[] crossSectionPositions)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            VertexPathIndices = vertexPathIndices ??
                throw new ArgumentNullException(nameof(vertexPathIndices));
            CrossSectionPositions = crossSectionPositions ??
                throw new ArgumentNullException(nameof(crossSectionPositions));
            if (vertexPathIndices.Length != crossSectionPositions.Length)
                throw new ArgumentException(
                    "Path binding arrays must have matching counts.");
        }

        public BranchGraphPlan Plan { get; }
        public uint[] VertexPathIndices { get; }
        public Vector2[] CrossSectionPositions { get; }
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
            int radialSegments,
            ComputeShader shader = null)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _data = Build(plan, radialSegments);
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
                12,
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
            new(_plan, _data.VertexPathIndices, _data.CrossSectionPositions);

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
            int radialSegments)
        {
            radialSegments = Mathf.Clamp(radialSegments, 3, 32);
            int vertexCount = plan.SampleCount * radialSegments;
            Vector3[] positions = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector4[] tangents = new Vector4[vertexCount];
            Vector2[] coordinates = new Vector2[vertexCount];
            uint[] branchIds = new uint[vertexCount];
            float[] rootDistances = new float[vertexCount];
            float[] tipDistances = new float[vertexCount];
            float[] localDistances = new float[vertexCount];
            float[] localPositions = new float[vertexCount];
            uint[] depths = new uint[vertexCount];
            float[] radii = new float[vertexCount];
            float[] tipWeights = new float[vertexCount];
            float[] flexibilities = new float[vertexCount];
            float[] stiffnesses = new float[vertexCount];
            uint[] vertexPathIndices = new uint[vertexCount];
            Vector2[] crossSections = new Vector2[vertexCount];
            List<Vector3Int> triangles = new();
            List<uint> regions = new();
            int edgeCount = 0;
            for (int branchIndex = 0; branchIndex < plan.BranchCount; branchIndex++)
            {
                BranchRecord branch = plan.Branches[branchIndex];
                for (int sample = 0; sample < branch.SampleCount; sample++)
                {
                    int pathSample = (int)branch.SampleOffset + sample;
                    BranchSample metadata = plan.Samples[pathSample];
                    for (int side = 0; side < radialSegments; side++)
                    {
                        int vertex = pathSample * radialSegments + side;
                        float angle = side / (float)radialSegments * Mathf.PI * 2f;
                        Vector2 section = new(
                            Mathf.Cos(angle) * metadata.Radius,
                            Mathf.Sin(angle) * metadata.Radius);
                        Vector3 radial = metadata.Normal * section.x +
                            metadata.Binormal * section.y;
                        positions[vertex] = metadata.Position + radial;
                        normals[vertex] = radial.normalized;
                        tangents[vertex] = new Vector4(
                            metadata.Tangent.x,
                            metadata.Tangent.y,
                            metadata.Tangent.z,
                            1f);
                        coordinates[vertex] = new Vector2(
                            metadata.LocalPosition,
                            side / (float)radialSegments);
                        branchIds[vertex] = branch.Id;
                        rootDistances[vertex] = metadata.DistanceFromRoot;
                        tipDistances[vertex] = metadata.DistanceToTip;
                        localDistances[vertex] = metadata.LocalDistance;
                        localPositions[vertex] = metadata.LocalPosition;
                        depths[vertex] = metadata.GenerationDepth;
                        radii[vertex] = metadata.Radius;
                        tipWeights[vertex] = metadata.TipWeight;
                        flexibilities[vertex] = metadata.Flexibility;
                        stiffnesses[vertex] = metadata.Stiffness;
                        vertexPathIndices[vertex] = (uint)pathSample;
                        crossSections[vertex] = section;
                    }
                    if (sample == branch.SampleCount - 1)
                        continue;
                    for (int side = 0; side < radialSegments; side++)
                    {
                        int nextSide = (side + 1) % radialSegments;
                        int a = pathSample * radialSegments + side;
                        int c = pathSample * radialSegments + nextSide;
                        int b = (pathSample + 1) * radialSegments + side;
                        int d = (pathSample + 1) * radialSegments + nextSide;
                        triangles.Add(new Vector3Int(a, c, b));
                        triangles.Add(new Vector3Int(c, d, b));
                        regions.Add(branch.Id);
                        regions.Add(branch.Id);
                    }
                }
                edgeCount += radialSegments *
                    (3 * (int)branch.SampleCount - 2);
            }
            return new BranchMeshData(
                positions,
                normals,
                tangents,
                coordinates,
                triangles.ToArray(),
                regions.ToArray(),
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
                vertexPathIndices,
                crossSections,
                edgeCount);
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
                uint[] vertexPathIndices,
                Vector2[] crossSectionPositions,
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
                VertexPathIndices = vertexPathIndices;
                CrossSectionPositions = crossSectionPositions;
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
            public uint[] VertexPathIndices { get; }
            public Vector2[] CrossSectionPositions { get; }
            public int EdgeCount { get; }
        }
    }
}
