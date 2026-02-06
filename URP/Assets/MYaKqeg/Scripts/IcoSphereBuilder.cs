using System.Collections.Generic;
using UnityEngine;

public static class IcoSphereBuilder
{
    private struct Triangle
    {
        public int v1;
        public int v2;
        public int v3;
    }

    public static void Build(int subdivisions, float radius, List<Vector3> vertices, List<int> triangles)
    {
        vertices.Clear();
        triangles.Clear();

        const float t = 1.61803398875f; // golden ratio approximation
        vertices.AddRange(new[]
        {
            new Vector3(-1,  t,  0),
            new Vector3( 1,  t,  0),
            new Vector3(-1, -t,  0),
            new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t),
            new Vector3( 0,  1,  t),
            new Vector3( 0, -1, -t),
            new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1),
            new Vector3( t,  0,  1),
            new Vector3(-t,  0, -1),
            new Vector3(-t,  0,  1)
        });

        Normalize(vertices);

        var faces = new List<Triangle>
        {
            new Triangle { v1 = 0, v2 = 11, v3 = 5 },
            new Triangle { v1 = 0, v2 = 5, v3 = 1 },
            new Triangle { v1 = 0, v2 = 1, v3 = 7 },
            new Triangle { v1 = 0, v2 = 7, v3 = 10 },
            new Triangle { v1 = 0, v2 = 10, v3 = 11 },
            new Triangle { v1 = 1, v2 = 5, v3 = 9 },
            new Triangle { v1 = 5, v2 = 11, v3 = 4 },
            new Triangle { v1 = 11, v2 = 10, v3 = 2 },
            new Triangle { v1 = 10, v2 = 7, v3 = 6 },
            new Triangle { v1 = 7, v2 = 1, v3 = 8 },
            new Triangle { v1 = 3, v2 = 9, v3 = 4 },
            new Triangle { v1 = 3, v2 = 4, v3 = 2 },
            new Triangle { v1 = 3, v2 = 2, v3 = 6 },
            new Triangle { v1 = 3, v2 = 6, v3 = 8 },
            new Triangle { v1 = 3, v2 = 8, v3 = 9 },
            new Triangle { v1 = 4, v2 = 9, v3 = 5 },
            new Triangle { v1 = 2, v2 = 4, v3 = 11 },
            new Triangle { v1 = 6, v2 = 2, v3 = 10 },
            new Triangle { v1 = 8, v2 = 6, v3 = 7 },
            new Triangle { v1 = 9, v2 = 8, v3 = 1 }
        };

        var midpointCache = new Dictionary<long, int>();

        for (var i = 0; i < subdivisions; i++)
        {
            var newFaces = new List<Triangle>(faces.Count * 4);
            midpointCache.Clear();

            foreach (var face in faces)
            {
                int a = GetMidpoint(face.v1, face.v2, vertices, midpointCache);
                int b = GetMidpoint(face.v2, face.v3, vertices, midpointCache);
                int c = GetMidpoint(face.v3, face.v1, vertices, midpointCache);

                newFaces.Add(new Triangle { v1 = face.v1, v2 = a, v3 = c });
                newFaces.Add(new Triangle { v1 = face.v2, v2 = b, v3 = a });
                newFaces.Add(new Triangle { v1 = face.v3, v2 = c, v3 = b });
                newFaces.Add(new Triangle { v1 = a, v2 = b, v3 = c });
            }

            faces = newFaces;
        }

        Normalize(vertices);
        Scale(vertices, radius);

        foreach (var face in faces)
        {
            triangles.Add(face.v1);
            triangles.Add(face.v2);
            triangles.Add(face.v3);
        }
    }

    private static void Normalize(List<Vector3> vertices)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] = vertices[i].normalized;
        }
    }

    private static void Scale(List<Vector3> vertices, float radius)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] *= radius;
        }
    }

    private static int GetMidpoint(int i0, int i1, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        long key = i0 < i1 ? ((long)i0 << 32) + i1 : ((long)i1 << 32) + i0;
        if (cache.TryGetValue(key, out int existing))
        {
            return existing;
        }

        Vector3 midpoint = (vertices[i0] + vertices[i1]) * 0.5f;
        int index = vertices.Count;
        vertices.Add(midpoint.normalized);
        cache[key] = index;
        return index;
    }
}
