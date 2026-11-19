using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public enum ParametricGeometryType
    {
        Plane,
        Sine,
        Saddle,
        Twist,
        Cylinder,
        Torus,
        Mobius,
        LineTube,
        CircleTube,
        HelixTube,
        LissajousTube,
        TorusKnotTube
    }

    [Serializable]
    public struct ParametricGeometrySettings
    {
        [Min(2)] public int uSegments;
        [Min(2)] public int vSegments;
        [Min(0.001f)] public float width;
        [Min(0.001f)] public float depth;
        [Min(0f)] public float height;
        [Min(0.001f)] public float radius;
        [Min(0.001f)] public float tubeRadius;
        [Min(0.001f)] public float minorRadius;
        [Min(0f)] public float frequency;
        public float phase;
        public float twist;
        [Min(1)] public int knotP;
        [Min(1)] public int knotQ;
        public float curveLength;
        public Vector3 lissajousFrequency;

        public static ParametricGeometrySettings Default => new()
        {
            uSegments = 32,
            vSegments = 16,
            width = 2f,
            depth = 2f,
            height = 0.4f,
            radius = 1.2f,
            tubeRadius = 0.25f,
            minorRadius = 0.35f,
            frequency = 2f,
            phase = 0f,
            twist = 90f,
            knotP = 2,
            knotQ = 3,
            curveLength = 4f,
            lissajousFrequency = new Vector3(2f, 3f, 4f)
        };
    }

    public sealed class ParametricGeometryGenerator : IGeometryGenerator
    {
        private readonly ParametricGeometryType _type;
        private readonly ParametricGeometrySettings _settings;
        private readonly ComputeShader _shader;
        private readonly GraphicsBuffer _positions;
        private readonly GraphicsBuffer _normals;
        private readonly GraphicsBuffer _tangents;
        private readonly GraphicsBuffer _coords;
        private readonly GraphicsBuffer _triangles;
        private readonly GraphicsBuffer _regions;
        private readonly GeometryPlanData _data;
        private readonly int _verticesKernel;
        private readonly int _attributesKernel;
        private readonly int _topologyKernel;
        private readonly int _headerKernel;
        private bool _disposed;

        public ParametricGeometryGenerator(
            ParametricGeometryType type,
            ParametricGeometrySettings settings,
            ComputeShader shader = null)
        {
            _type = type;
            _settings = Validate(settings);
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/ParametricCopy")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/ParametricCopy.compute was not found.");
            _verticesKernel = _shader.FindKernel("CopyParametricVertices");
            _attributesKernel = _shader.FindKernel("CopyParametricAttributes");
            _topologyKernel = _shader.FindKernel("CopyParametricTopology");
            _headerKernel = _shader.FindKernel("CopyParametricHeader");
            _data = Build(type, _settings);
            _positions = Create(_data.Positions, "Parametric.Positions", this);
            _normals = Create(_data.Normals, "Parametric.Normals", this);
            _tangents = Create(_data.Tangents, "Parametric.Tangents", this);
            _coords = Create(_data.Coordinates, "Parametric.Coordinates", this);
            _triangles = Create(_data.Triangles, "Parametric.Triangles", this);
            _regions = Create(_data.Regions, "Parametric.Regions", this);
        }

        public GeometryLayout Plan()
        {
            AttributeDescriptor[] attributes =
            {
                new(AttributeIds.RestPosition, AttributeDomain.Vertex, AttributeFormat.Float3),
                new(AttributeIds.RestNormal, AttributeDomain.Vertex, AttributeFormat.Float3),
                new(AttributeIds.Mass, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.RestTangent, AttributeDomain.Vertex, AttributeFormat.Float4),
                new(AttributeIds.LocalCoord, AttributeDomain.Vertex, AttributeFormat.Float2),
                new(AttributeIds.RegionId, AttributeDomain.Primitive, AttributeFormat.UInt,
                    AttributeInterpolation.Flat),
                new(AttributeIds.BoundaryWeight, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.NeighbourCount, AttributeDomain.Vertex, AttributeFormat.UInt,
                    AttributeInterpolation.Flat),
                new(AttributeIds.Curvature, AttributeDomain.Vertex, AttributeFormat.Float),
                new(AttributeIds.EdgeRestLength, AttributeDomain.Edge, AttributeFormat.Float),
                new(AttributeIds.DeformationWeight, AttributeDomain.Vertex, AttributeFormat.Float)
            };
            return new GeometryLayout(
                $"Parametric_{_type}",
                PrimitiveTopology.Triangles,
                _data.Positions.Length,
                _data.Triangles.Length,
                _data.EdgeCount,
                _data.RegionCount,
                Mathf.Max(1, _data.MaxDegree * 2),
                attributes);
        }

        public void Record(CommandBuffer command, GeometryResource resource)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ParametricGeometryGenerator));
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (resource.Layout.VertexCount != _data.Positions.Length ||
                resource.Layout.PrimitiveCount != _data.Triangles.Length)
                throw new ArgumentException(
                    "The target resource does not match this generator.", nameof(resource));

            command.SetComputeIntParam(_shader, "_VertexCount", _data.Positions.Length);
            command.SetComputeIntParam(_shader, "_PrimitiveCount", _data.Triangles.Length);
            command.SetComputeIntParam(_shader, "_EdgeCount", _data.EdgeCount);
            command.SetComputeIntParam(_shader, "_RegionCount", _data.RegionCount);
            command.SetComputeVectorParam(_shader, "_BoundsMin", _data.Bounds.min);
            command.SetComputeVectorParam(_shader, "_BoundsMax", _data.Bounds.max);
            command.SetComputeIntParam(_shader, "_Closed", IsClosed(_type) ? 1 : 0);
            Set(_verticesKernel, "_SourcePositions", _positions);
            Set(_verticesKernel, "_SourceNormals", _normals);
            Set(_verticesKernel, "_SourceTangents", _tangents);
            Set(_verticesKernel, "_SourceCoordinates", _coords);
            Set(_verticesKernel, "_DynamicVertices", resource.Live.DynamicVertices);
            Set(_verticesKernel, "_StaticVertices", resource.StaticVertices);
            Bind(_verticesKernel, AttributeIds.RestPosition, "_RestPositions");
            Bind(_verticesKernel, AttributeIds.RestNormal, "_RestNormals");
            Bind(_verticesKernel, AttributeIds.RestTangent, "_RestTangents");
            Bind(_verticesKernel, AttributeIds.LocalCoord, "_LocalCoordinates");
            command.DispatchCompute(
                _shader, _verticesKernel,
                (_data.Positions.Length + 63) / 64, 1, 1);

            Bind(_attributesKernel, AttributeIds.Mass, "_Masses");
            Bind(_attributesKernel, AttributeIds.DeformationWeight, "_DeformationWeights");
            command.DispatchCompute(
                _shader, _attributesKernel,
                (_data.Positions.Length + 63) / 64,
                1, 1);

            Set(_topologyKernel, "_SourceTriangles", _triangles);
            Set(_topologyKernel, "_SourceRegions", _regions);
            Set(_topologyKernel, "_Indices", resource.Indices);
            Bind(_topologyKernel, AttributeIds.RegionId, "_RegionIds");
            command.DispatchCompute(
                _shader, _topologyKernel,
                (_data.Triangles.Length + 63) / 64, 1, 1);

            Set(_headerKernel, "_Header", resource.Header);
            Set(_headerKernel, "_DrawArgs", resource.DrawArgs);
            command.DispatchCompute(_shader, _headerKernel, 1, 1, 1);
            resource.MarkUpdated(_data.Bounds);

            void Set(int kernel, string name, GraphicsBuffer buffer) =>
                command.SetComputeBufferParam(_shader, kernel, name, buffer);
            void Bind(int kernel, AttributeId id, string name) =>
                command.SetComputeBufferParam(
                    _shader, kernel, name, resource.Attributes.Require(id).Buffer);
        }

        public int VertexCount => _data.Positions.Length;
        public int PrimitiveCount => _data.Triangles.Length;
        public int EdgeCount => _data.EdgeCount;
        public Bounds Bounds => _data.Bounds;
        public Vector3[] DebugPositions => _data.Positions;
        public Vector3[] DebugNormals => _data.Normals;
        public Vector3Int[] DebugTriangles => _data.Triangles;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Release(_positions);
            Release(_normals);
            Release(_tangents);
            Release(_coords);
            Release(_triangles);
            Release(_regions);
        }

        private static ParametricGeometrySettings Validate(ParametricGeometrySettings settings)
        {
            settings.uSegments = Mathf.Clamp(settings.uSegments, 2, 256);
            settings.vSegments = Mathf.Clamp(settings.vSegments, 2, 128);
            settings.width = Mathf.Max(settings.width, 0.001f);
            settings.depth = Mathf.Max(settings.depth, 0.001f);
            settings.radius = Mathf.Max(settings.radius, 0.001f);
            settings.tubeRadius = Mathf.Max(settings.tubeRadius, 0.001f);
            settings.minorRadius = Mathf.Max(settings.minorRadius, 0.001f);
            settings.knotP = Mathf.Clamp(settings.knotP, 1, 16);
            settings.knotQ = Mathf.Clamp(settings.knotQ, 1, 16);
            settings.curveLength = Mathf.Max(settings.curveLength, 0.001f);
            return settings;
        }

        private static GeometryPlanData Build(
            ParametricGeometryType type,
            ParametricGeometrySettings settings)
        {
            bool tube = IsTube(type);
            bool wrapU = type == ParametricGeometryType.Cylinder ||
                type == ParametricGeometryType.Torus ||
                type == ParametricGeometryType.Mobius ||
                tube && type != ParametricGeometryType.LineTube &&
                type != ParametricGeometryType.HelixTube;
            bool wrapV = type == ParametricGeometryType.Torus || tube;
            int uCount = settings.uSegments + (wrapU ? 0 : 1);
            int vCount = settings.vSegments + (wrapV ? 0 : 1);
            Vector3[] positions = new Vector3[uCount * vCount];
            Vector3[] normals = new Vector3[positions.Length];
            Vector4[] tangents = new Vector4[positions.Length];
            Vector2[] coordinates = new Vector2[positions.Length];
            float epsilon = 0.0005f;
            Frame[] tubeFrames = tube
                ? BuildTubeFrames(type, settings, wrapU, uCount)
                : null;
            for (int u = 0; u < uCount; u++)
            {
                for (int v = 0; v < vCount; v++)
                {
                    float uf = (float)u / settings.uSegments;
                    float vf = (float)v / settings.vSegments;
                    int id = v * uCount + u;
                    coordinates[id] = new Vector2(uf, vf);
                    Vector3 p;
                    Vector3 normal;
                    Vector3 tangent;
                    if (tube)
                    {
                        Frame frame = tubeFrames[u];
                        float angle = vf * Mathf.PI * 2f;
                        p = frame.Position +
                            (frame.Normal * Mathf.Cos(angle) +
                             frame.Binormal * Mathf.Sin(angle)) *
                            settings.tubeRadius;
                        normal = (frame.Normal * Mathf.Cos(angle) +
                            frame.Binormal * Mathf.Sin(angle)).normalized;
                        tangent = frame.Tangent;
                    }
                    else
                    {
                        p = Evaluate(type, settings, uf, vf);
                        float u0 = wrapU
                            ? Wrap01(uf - epsilon)
                            : Mathf.Max(0f, uf - epsilon);
                        float u1 = wrapU
                            ? Wrap01(uf + epsilon)
                            : Mathf.Min(1f, uf + epsilon);
                        float v0 = wrapV
                            ? Wrap01(vf - epsilon)
                            : Mathf.Max(0f, vf - epsilon);
                        float v1 = wrapV
                            ? Wrap01(vf + epsilon)
                            : Mathf.Min(1f, vf + epsilon);
                        Vector3 pu = (Evaluate(type, settings, u1, vf) -
                            Evaluate(type, settings, u0, vf)) /
                            (wrapU ? 2f * epsilon : u1 - u0);
                        Vector3 pv = (Evaluate(type, settings, uf, v1) -
                            Evaluate(type, settings, uf, v0)) /
                            (wrapV ? 2f * epsilon : v1 - v0);
                        normal = Vector3.Cross(pv, pu);
                        tangent = pu;
                    }
                    if (normal.sqrMagnitude < 1e-12f)
                    {
                        normal = type == ParametricGeometryType.Plane ||
                            type == ParametricGeometryType.Sine ||
                            type == ParametricGeometryType.Saddle ||
                            type == ParametricGeometryType.Twist
                            ? Vector3.up
                            : (p - new Vector3(0f, p.y, 0f)).normalized;
                    }
                    normal.Normalize();
                    tangent = tangent.sqrMagnitude > 1e-12f
                        ? tangent.normalized
                        : Vector3.Cross(normal, Vector3.up).normalized;
                    positions[id] = p;
                    normals[id] = normal;
                    tangents[id] = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
                }
            }

            List<Vector3Int> triangles = new();
            for (int ui = 0; ui < settings.uSegments; ui++)
            {
                for (int vi = 0; vi < settings.vSegments; vi++)
                {
                    int un = ui == settings.uSegments - 1 ? (wrapU ? 0 : ui + 1) : ui + 1;
                    int vn = vi == settings.vSegments - 1 ? (wrapV ? 0 : vi + 1) : vi + 1;
                    if (type == ParametricGeometryType.Mobius && ui == settings.uSegments - 1)
                        un = 0;
                    int a = Index(ui, vi, un, vn, uCount, settings, type, false);
                    int b = Index(un, vi, ui, vi, uCount, settings, type, true);
                    int c = Index(ui, vn, ui, vi, uCount, settings, type, true);
                    int d = Index(un, vn, ui, vi, uCount, settings, type, true);
                    AddTriangle(triangles, positions, normals, a, b, d);
                    AddTriangle(triangles, positions, normals, a, d, c);
                }
            }

            HashSet<long> edges = new();
            HashSet<int>[] degrees = new HashSet<int>[positions.Length];
            foreach (Vector3Int triangle in triangles)
            {
                AddEdge(triangle.x, triangle.y);
                AddEdge(triangle.y, triangle.z);
                AddEdge(triangle.z, triangle.x);
            }
            int maxDegree = 1;
            foreach (HashSet<int> degree in degrees)
                if (degree != null) maxDegree = Mathf.Max(maxDegree, degree.Count);

            Bounds bounds = CalculateBounds(positions);
            uint[] regions = new uint[triangles.Count];
            for (int i = 0; i < regions.Length; i++)
                regions[i] = (uint)(i / (settings.vSegments * 2));
            return new GeometryPlanData(
                positions, normals, tangents, coordinates, triangles.ToArray(),
                regions, edges.Count, maxDegree, Mathf.Max(1, settings.uSegments),
                bounds);

            void AddEdge(int source, int target)
            {
                int low = Mathf.Min(source, target);
                int high = Mathf.Max(source, target);
                edges.Add(((long)low << 32) | (uint)high);
                (degrees[source] ??= new HashSet<int>()).Add(target);
                (degrees[target] ??= new HashSet<int>()).Add(source);
            }
        }

        private static int Index(
            int u,
            int v,
            int previousU,
            int previousV,
            int uCount,
            ParametricGeometrySettings settings,
            ParametricGeometryType type,
            bool next)
        {
            if (type == ParametricGeometryType.Mobius && next && previousU == settings.uSegments - 1 && u == 0)
                v = settings.vSegments - v;
            return v * uCount + u;
        }

        private static void AddTriangle(
            List<Vector3Int> triangles,
            Vector3[] positions,
            Vector3[] normals,
            int a,
            int b,
            int c)
        {
            if (a == b || b == c || c == a) return;
            Vector3 geometricNormal = Vector3.Cross(
                positions[b] - positions[a], positions[c] - positions[a]);
            if (geometricNormal.sqrMagnitude < 1e-12f) return;
            Vector3 expected = (normals[a] + normals[b] + normals[c]).normalized;
            triangles.Add(Vector3.Dot(geometricNormal, expected) >= 0f
                ? new Vector3Int(a, b, c)
                : new Vector3Int(a, c, b));
        }

        private static Vector3 Evaluate(
            ParametricGeometryType type,
            ParametricGeometrySettings settings,
            float u,
            float v)
        {
            float angleU = u * Mathf.PI * 2f;
            float angleV = v * Mathf.PI * 2f;
            float x = (u - 0.5f) * settings.width;
            float z = (v - 0.5f) * settings.depth;
            switch (type)
            {
                case ParametricGeometryType.Plane:
                    return new Vector3(x, 0f, z);
                case ParametricGeometryType.Sine:
                    return new Vector3(
                        x, settings.height *
                        Mathf.Sin(angleU * settings.frequency + settings.phase), z);
                case ParametricGeometryType.Saddle:
                    return new Vector3(x, settings.height * (x * x - z * z), z);
                case ParametricGeometryType.Twist:
                {
                    float twist = (u - 0.5f) * settings.twist * Mathf.Deg2Rad;
                    Vector2 yz = Rotate(new Vector2(z, 0f), twist);
                    return new Vector3(x, yz.y, yz.x);
                }
                case ParametricGeometryType.Cylinder:
                    return new Vector3(
                        settings.radius * Mathf.Cos(angleU),
                        (v - 0.5f) * settings.depth,
                        settings.radius * Mathf.Sin(angleU));
                case ParametricGeometryType.Torus:
                {
                    float ring = settings.radius + settings.minorRadius * Mathf.Cos(angleV);
                    return new Vector3(
                        ring * Mathf.Cos(angleU),
                        settings.minorRadius * Mathf.Sin(angleV),
                        ring * Mathf.Sin(angleU));
                }
                case ParametricGeometryType.Mobius:
                {
                    float offset = (v - 0.5f) * settings.width;
                    float radius = settings.radius + offset * Mathf.Cos(angleU * 0.5f);
                    return new Vector3(
                        radius * Mathf.Cos(angleU),
                        offset * Mathf.Sin(angleU * 0.5f),
                        radius * Mathf.Sin(angleU));
                }
                default:
                    throw new InvalidOperationException(
                        $"Tube geometry '{type}' is evaluated from transported frames.");
            }
        }

        private static Frame[] BuildTubeFrames(
            ParametricGeometryType type,
            ParametricGeometrySettings settings,
            bool closed,
            int count)
        {
            int frameCount = count + (closed ? 1 : 0);
            Frame[] frames = new Frame[frameCount];
            for (int sample = 0; sample < frameCount; sample++)
            {
                float u = (float)sample / settings.uSegments;
                Vector3 previousPosition = CurvePosition(
                    type, settings, closed ? u - 0.001f : Mathf.Max(0f, u - 0.001f));
                Vector3 nextPosition = CurvePosition(
                    type, settings, closed ? u + 0.001f :
                        Mathf.Min(1f, u + 0.001f));
                Vector3 tangent = (nextPosition - previousPosition).normalized;
                Vector3 normal;
                if (sample == 0)
                {
                    Vector3 helper =
                        Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f
                            ? Vector3.right
                            : Vector3.up;
                    normal = Vector3.ProjectOnPlane(helper, tangent).normalized;
                }
                else
                {
                    normal = Vector3.ProjectOnPlane(
                        frames[sample - 1].Normal, tangent).normalized;
                }
                frames[sample] = new Frame(
                    CurvePosition(type, settings, closed ? Wrap01(u) : u),
                    tangent, normal, Vector3.Cross(tangent, normal));
            }

            if (closed)
            {
                float twist = Vector3.SignedAngle(
                    frames[frames.Length - 1].Normal,
                    frames[0].Normal,
                    frames[0].Tangent);
                for (int sample = 1; sample < frames.Length; sample++)
                {
                    Frame frame = frames[sample];
                    float t = (float)sample / settings.uSegments;
                    Vector3 corrected = Quaternion.AngleAxis(
                        twist * t, frame.Tangent) * frame.Normal;
                    frames[sample] = new Frame(
                        frame.Position,
                        frame.Tangent,
                        corrected,
                        Vector3.Cross(frame.Tangent, corrected).normalized);
                }
            }

            Frame[] result = new Frame[count];
            Array.Copy(frames, result, count);
            return result;
        }

        private static Vector3 CurvePosition(
            ParametricGeometryType type,
            ParametricGeometrySettings settings,
            float u)
        {
            float angle = u * Mathf.PI * 2f;
            return type switch
            {
                ParametricGeometryType.LineTube =>
                    new Vector3((u - 0.5f) * settings.curveLength, 0f, 0f),
                ParametricGeometryType.CircleTube =>
                    new Vector3(
                        settings.radius * Mathf.Cos(angle),
                        0f,
                        settings.radius * Mathf.Sin(angle)),
                ParametricGeometryType.HelixTube =>
                    new Vector3(
                        settings.radius * Mathf.Cos(angle),
                        (u - 0.5f) * settings.curveLength,
                        settings.radius * Mathf.Sin(angle)),
                ParametricGeometryType.LissajousTube =>
                    new Vector3(
                        settings.radius * Mathf.Sin(angle * settings.lissajousFrequency.x),
                        settings.radius * Mathf.Sin(angle * settings.lissajousFrequency.y + settings.phase),
                        settings.radius * Mathf.Sin(angle * settings.lissajousFrequency.z)),
                _ => KnotPosition(settings, angle)
            };
        }

        private static Vector3 KnotPosition(
            ParametricGeometrySettings settings,
            float angle)
        {
            float pAngle = angle * settings.knotP;
            float qAngle = angle * settings.knotQ;
            float ring = settings.radius + settings.minorRadius * Mathf.Cos(qAngle);
            return new Vector3(
                ring * Mathf.Cos(pAngle),
                settings.minorRadius * Mathf.Sin(qAngle),
                ring * Mathf.Sin(pAngle));
        }

        private static bool IsTube(ParametricGeometryType type) =>
            type is ParametricGeometryType.LineTube or
            ParametricGeometryType.CircleTube or
                ParametricGeometryType.HelixTube or
                ParametricGeometryType.LissajousTube or
                ParametricGeometryType.TorusKnotTube;

        private static bool IsClosed(ParametricGeometryType type) =>
            type is ParametricGeometryType.Cylinder or
                ParametricGeometryType.Torus or
                ParametricGeometryType.Mobius or
                ParametricGeometryType.CircleTube or
                ParametricGeometryType.LissajousTube or
                ParametricGeometryType.TorusKnotTube;

        private static float Wrap01(float value) => value - Mathf.Floor(value);
        private static Vector2 Rotate(Vector2 value, float angle) =>
            new(
                value.x * Mathf.Cos(angle) - value.y * Mathf.Sin(angle),
                value.x * Mathf.Sin(angle) + value.y * Mathf.Cos(angle));

        private static Bounds CalculateBounds(Vector3[] positions)
        {
            Bounds result = new(positions[0], Vector3.zero);
            foreach (Vector3 position in positions)
                result.Encapsulate(position);
            return result;
        }

        private static GraphicsBuffer Create<T>(T[] values, string name, object owner)
            where T : struct
        {
            GraphicsBuffer buffer = new(
                GraphicsBuffer.Target.Structured,
                values.Length,
                System.Runtime.InteropServices.Marshal.SizeOf<T>());
            buffer.SetData(values);
            GpuBufferRegistry.Register(buffer, name, owner);
            return buffer;
        }

        private static void Release(GraphicsBuffer buffer)
        {
            if (buffer == null) return;
            GpuBufferRegistry.Unregister(buffer);
            buffer.Release();
        }

        private readonly struct Frame
        {
            public readonly Vector3 Position;
            public readonly Vector3 Tangent;
            public readonly Vector3 Normal;
            public readonly Vector3 Binormal;

            public Frame(
                Vector3 position,
                Vector3 tangent,
                Vector3 normal,
                Vector3 binormal)
            {
                Position = position;
                Tangent = tangent;
                Normal = normal;
                Binormal = binormal;
            }
        }

        private readonly struct GeometryPlanData
        {
            public readonly Vector3[] Positions;
            public readonly Vector3[] Normals;
            public readonly Vector4[] Tangents;
            public readonly Vector2[] Coordinates;
            public readonly Vector3Int[] Triangles;
            public readonly uint[] Regions;
            public readonly int EdgeCount;
            public readonly int MaxDegree;
            public readonly int RegionCount;
            public readonly Bounds Bounds;

            public GeometryPlanData(
                Vector3[] positions,
                Vector3[] normals,
                Vector4[] tangents,
                Vector2[] coordinates,
                Vector3Int[] triangles,
                uint[] regions,
                int edgeCount,
                int maxDegree,
                int regionCount,
                Bounds bounds)
            {
                Positions = positions;
                Normals = normals;
                Tangents = tangents;
                Coordinates = coordinates;
                Triangles = triangles;
                Regions = regions;
                EdgeCount = edgeCount;
                MaxDegree = maxDegree;
                RegionCount = regionCount;
                Bounds = bounds;
            }
        }
    }
}
