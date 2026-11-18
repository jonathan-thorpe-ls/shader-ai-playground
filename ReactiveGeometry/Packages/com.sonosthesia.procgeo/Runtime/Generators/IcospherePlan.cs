using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    internal readonly struct IcospherePlan
    {
        internal readonly struct Locator
        {
            public readonly uint Face;
            public readonly uint I;
            public readonly uint J;
            public readonly uint K;

            public Locator(uint face, uint i, uint j, uint k)
            {
                Face = face;
                I = i;
                J = j;
                K = k;
            }
        }

        private static readonly Vector3[] BaseCorners =
        {
            new(-1, Phi, 0),
            new(1, Phi, 0),
            new(-1, -Phi, 0),
            new(1, -Phi, 0),
            new(0, -1, Phi),
            new(0, 1, Phi),
            new(0, -1, -Phi),
            new(0, 1, -Phi),
            new(Phi, 0, -1),
            new(Phi, 0, 1),
            new(-Phi, 0, -1),
            new(-Phi, 0, 1)
        };

        private static readonly Vector3Int[] BaseFaces =
        {
            new(0, 11, 5),
            new(0, 5, 1),
            new(0, 1, 7),
            new(0, 7, 10),
            new(0, 10, 11),
            new(1, 5, 9),
            new(5, 11, 4),
            new(11, 10, 2),
            new(10, 7, 6),
            new(7, 1, 8),
            new(3, 9, 4),
            new(3, 4, 2),
            new(3, 2, 6),
            new(3, 6, 8),
            new(3, 8, 9),
            new(4, 9, 5),
            new(2, 4, 11),
            new(6, 2, 10),
            new(8, 6, 7),
            new(9, 8, 1)
        };

        private const float Phi = 1.618033988749895f;

        private IcospherePlan(
            int frequency,
            Vector3[] corners,
            Vector3Int[] faces,
            Locator[] locators,
            Vector3Int[] triangles,
            uint[] triangleRegions)
        {
            Frequency = frequency;
            Corners = corners;
            Faces = faces;
            Locators = locators;
            Triangles = triangles;
            TriangleRegions = triangleRegions;
        }

        public int Frequency { get; }
        public Vector3[] Corners { get; }
        public Vector3Int[] Faces { get; }
        public Locator[] Locators { get; }
        public Vector3Int[] Triangles { get; }
        public uint[] TriangleRegions { get; }
        public int VertexCount => 10 * Frequency * Frequency + 2;
        public int PrimitiveCount => 20 * Frequency * Frequency;
        public int EdgeCount => 30 * Frequency * Frequency;

        public static IcospherePlan Create(int frequency)
        {
            if (frequency <= 0)
                throw new ArgumentOutOfRangeException(nameof(frequency), "Frequency must be positive.");

            Vector3[] corners = new Vector3[BaseCorners.Length];
            for (int i = 0; i < corners.Length; i++)
                corners[i] = BaseCorners[i].normalized;

            Vector3Int[] faces = OrientFaces(corners);
            List<Vector2Int> edgeList = BuildEdges(faces);
            Dictionary<Vector2Int, int> edgeIds = new();
            for (int i = 0; i < edgeList.Count; i++)
                edgeIds.Add(edgeList[i], i);

            int vertexCount = 10 * frequency * frequency + 2;
            Locator[] locators = new Locator[vertexCount];
            bool[] assigned = new bool[vertexCount];

            for (int face = 0; face < faces.Length; face++)
            {
                Vector3Int faceCorners = faces[face];
                for (int i = 0; i <= frequency; i++)
                {
                    for (int j = 0; i + j <= frequency; j++)
                    {
                        int id = ResolveVertex(
                            frequency, edgeIds, face, faceCorners, i, j);
                        if (!assigned[id])
                        {
                            locators[id] = new Locator(
                                (uint)face,
                                (uint)i,
                                (uint)j,
                                (uint)(frequency - i - j));
                            assigned[id] = true;
                        }
                    }
                }
            }

            for (int i = 0; i < assigned.Length; i++)
            {
                if (!assigned[i])
                    throw new InvalidOperationException($"Icosphere vertex {i} was not assigned.");
            }

            List<Vector3Int> triangles = new();
            List<uint> regions = new();
            for (int face = 0; face < faces.Length; face++)
            {
                Vector3Int faceCorners = faces[face];
                for (int i = 0; i < frequency; i++)
                {
                    for (int j = 0; i + j < frequency; j++)
                    {
                        triangles.Add(new Vector3Int(
                            ResolveVertex(frequency, edgeIds, face, faceCorners, i, j),
                            ResolveVertex(frequency, edgeIds, face, faceCorners, i + 1, j),
                            ResolveVertex(frequency, edgeIds, face, faceCorners, i, j + 1)));
                        regions.Add((uint)face);

                        if (i + j <= frequency - 2)
                        {
                            triangles.Add(new Vector3Int(
                                ResolveVertex(frequency, edgeIds, face, faceCorners, i + 1, j),
                                ResolveVertex(frequency, edgeIds, face, faceCorners, i + 1, j + 1),
                                ResolveVertex(frequency, edgeIds, face, faceCorners, i, j + 1)));
                            regions.Add((uint)face);
                        }
                    }
                }
            }

            if (triangles.Count != 20 * frequency * frequency)
                throw new InvalidOperationException("Icosphere triangle plan has an invalid count.");

            return new IcospherePlan(
                frequency,
                corners,
                faces,
                locators,
                triangles.ToArray(),
                regions.ToArray());
        }

        private static Vector3Int[] OrientFaces(Vector3[] corners)
        {
            Vector3Int[] result = new Vector3Int[BaseFaces.Length];
            for (int i = 0; i < BaseFaces.Length; i++)
            {
                Vector3Int face = BaseFaces[i];
                Vector3 a = corners[face.x];
                Vector3 b = corners[face.y];
                Vector3 c = corners[face.z];
                Vector3 centre = (a + b + c) / 3f;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), centre) >= 0f)
                    result[i] = face;
                else
                    result[i] = new Vector3Int(face.x, face.z, face.y);
            }
            return result;
        }

        private static List<Vector2Int> BuildEdges(Vector3Int[] faces)
        {
            HashSet<Vector2Int> edges = new();
            foreach (Vector3Int face in faces)
            {
                edges.Add(SortedEdge(face.x, face.y));
                edges.Add(SortedEdge(face.y, face.z));
                edges.Add(SortedEdge(face.z, face.x));
            }

            List<Vector2Int> result = new(edges);
            result.Sort((a, b) =>
            {
                int x = a.x.CompareTo(b.x);
                return x != 0 ? x : a.y.CompareTo(b.y);
            });
            return result;
        }

        private static int ResolveVertex(
            int frequency,
            IReadOnlyDictionary<Vector2Int, int> edgeIds,
            int faceId,
            Vector3Int corners,
            int i,
            int j)
        {
            int k = frequency - i - j;
            if (i == 0 && j == 0) return corners.x;
            if (j == 0 && k == 0) return corners.y;
            if (i == 0 && k == 0) return corners.z;

            if (i > 0 && j > 0 && k > 0)
            {
                int faceInteriorBase =
                    12 + 30 * (frequency - 1) +
                    faceId * ((frequency - 1) * (frequency - 2) / 2);
                int before = 0;
                for (int x = 1; x < i; x++)
                    before += frequency - 1 - x;
                return faceInteriorBase + before + j - 1;
            }

            (int first, int second, int steps) = j == 0
                ? (corners.x, corners.y, i)
                : i == 0
                    ? (corners.x, corners.z, j)
                    : (corners.y, corners.z, j);

            Vector2Int edge = SortedEdge(first, second);
            int parameter = first == edge.x ? steps : frequency - steps;
            return 12 + edgeIds[edge] * (frequency - 1) + parameter - 1;
        }

        private static Vector2Int SortedEdge(int a, int b) =>
            a < b ? new Vector2Int(a, b) : new Vector2Int(b, a);
    }
}
