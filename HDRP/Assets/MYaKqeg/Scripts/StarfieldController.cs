using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class StarfieldController : MonoBehaviour
{
    [SerializeField]
    private MeshFilter meshFilter;
    [SerializeField]
    private int starCount = 5000;
    [SerializeField]
    private Vector3 boundsSize = new Vector3(200f, 200f, 200f);
    [SerializeField]
    private int randomSeed = 1234;
    [SerializeField]
    private Vector3 rotationPerSecond = new Vector3(0f, 0.5f, 0f);

    private Mesh starMesh;

    private void OnEnable()
    {
        Rebuild();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            Rebuild();
            return;
        }

        transform.Rotate(rotationPerSecond * Time.deltaTime, Space.Self);
    }

    private void OnValidate()
    {
        starCount = Mathf.Max(1, starCount);
        boundsSize = Vector3.Max(boundsSize, Vector3.one);
        if (isActiveAndEnabled)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (!meshFilter)
        {
            return;
        }

        if (!starMesh)
        {
            starMesh = new Mesh { name = "ProceduralStarfield" };
            starMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            meshFilter.sharedMesh = starMesh;
        }
        else
        {
            starMesh.Clear();
        }

        var rng = new System.Random(randomSeed);
        var positions = new List<Vector3>(starCount);
        for (int i = 0; i < starCount; i++)
        {
            float x = (float)rng.NextDouble() * boundsSize.x - boundsSize.x * 0.5f;
            float y = (float)rng.NextDouble() * boundsSize.y - boundsSize.y * 0.5f;
            float z = (float)rng.NextDouble() * boundsSize.z - boundsSize.z * 0.5f;
            positions.Add(new Vector3(x, y, z));
        }

        starMesh.SetVertices(positions);
        var indices = new int[starCount];
        for (int i = 0; i < starCount; i++)
        {
            indices[i] = i;
        }
        starMesh.SetIndices(indices, MeshTopology.Points, 0, false);
        starMesh.bounds = new Bounds(Vector3.zero, boundsSize);
    }
}
