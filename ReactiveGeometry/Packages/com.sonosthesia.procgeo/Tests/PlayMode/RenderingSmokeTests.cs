using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class RenderingSmokeTests
    {
        [UnityTest]
        public IEnumerator Phase1Mesh_IsGeneratedAndRenderedByUrp()
        {
            Assume.That(SystemInfo.supportsComputeShaders);
            var gameObject = new GameObject("Rendering Smoke Icosphere");
            ProceduralMeshRenderer renderer = gameObject.AddComponent<ProceduralMeshRenderer>();
            yield return null;

            var filter = gameObject.GetComponent<MeshFilter>();
            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            Assert.That(renderer.IsGenerated, Is.True);
            Assert.That(renderer.VertexCount, Is.EqualTo(162));
            Assert.That(renderer.TriangleCount, Is.EqualTo(320));
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Assert.That(filter.sharedMesh.bounds.size.magnitude, Is.GreaterThan(0f));
            Assert.That(meshRenderer.sharedMaterial, Is.Not.Null);
            Assert.That(meshRenderer.isVisible || meshRenderer.enabled, Is.True);

            Object.Destroy(gameObject);
        }
    }
}
