using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class Phase5PathRenderingTests
    {
        [UnityTest]
        public IEnumerator TubeRendererRunsPathThenVertexDeformers()
        {
            Assume.That(SystemInfo.supportsComputeShaders);
            GameObject gameObject = new("Phase 5 Path Rendering");
            ProceduralMeshRenderer renderer =
                gameObject.AddComponent<ProceduralMeshRenderer>();
            ParametricGeometrySettings settings = ParametricGeometrySettings.Default;
            settings.uSegments = 16;
            settings.vSegments = 8;
            renderer.SetParametricSettings(
                ParametricGeometryType.TorusKnotTube, settings);

            GameObject pathObject = new("Path Source");
            PathOffsetDeformer path =
                pathObject.AddComponent<PathOffsetDeformer>();
            path.Target = renderer;
            path.Offset = Vector3.up * 0.2f;
            GameObject vertexObject = new("Vertex Source");
            VertexNormalOffsetDeformer vertex =
                vertexObject.AddComponent<VertexNormalOffsetDeformer>();
            vertex.Target = renderer;
            vertex.Amplitude = 0.05f;

            yield return null;

            Assert.That(renderer.PathDeformation, Is.Not.Null);
            Assert.That(renderer.Simulation, Is.Null);
            Assert.That(renderer.Resource, Is.Not.Null);
            Assert.That(renderer.PathDeformation.Buffers.BasePositions.count,
                Is.EqualTo(renderer.VertexCount));
            Assert.That(renderer.PathDeformation.Buffers.FinalPositions.count,
                Is.EqualTo(renderer.VertexCount));
            Assert.That(gameObject.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);

            Object.Destroy(gameObject);
            Object.Destroy(pathObject);
            Object.Destroy(vertexObject);
            yield return null;
        }
    }
}
