using System.Collections;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class NeighbourSimulationPlayModeTests
    {
        [UnityTest]
        public IEnumerator Renderer_SimulatesMultipleFramesAndResetsCleanly()
        {
            Assume.That(SystemInfo.supportsComputeShaders);
            GameObject gameObject = new("Neighbour Simulation");
            ProceduralMeshRenderer renderer = gameObject.AddComponent<ProceduralMeshRenderer>();
            yield return null;

            renderer.ResetSimulation();
            renderer.ApplyImpulse(0, Vector3.up, 2f);
            for (int frame = 0; frame < 8; frame++)
                yield return null;
            renderer.SingleStepSimulation();

            Assert.That(renderer.Simulation, Is.Not.Null);
            DynamicVertexData[] vertices = new DynamicVertexData[renderer.VertexCount];
            renderer.Resource.Live.DynamicVertices.GetData(vertices);
            Assert.That(vertices.Select(value => value.Position).All(IsFinite), Is.True);
            Assert.That(vertices.Select(value => value.Normal).All(IsFinite), Is.True);
            Assert.That(renderer.ReadSimulationStatus().StepSequence, Is.GreaterThan(0u));

            renderer.ResetSimulation();
            SimulationStatus status = renderer.ReadSimulationStatus();
            Assert.That(status.StepSequence, Is.Zero);
            Assert.That(status.InvalidValueCount, Is.Zero);
            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator Renderer_DisableEnableReleasesAndRecreatesSimulation()
        {
            Assume.That(SystemInfo.supportsComputeShaders);
            GameObject gameObject = new("Neighbour Simulation Lifecycle");
            ProceduralMeshRenderer renderer = gameObject.AddComponent<ProceduralMeshRenderer>();
            yield return null;
            int baseline = GpuBufferRegistry.Count;

            renderer.enabled = false;
            yield return null;
            Assert.That(renderer.Resource, Is.Null);
            Assert.That(GpuBufferRegistry.Count, Is.LessThan(baseline));

            renderer.enabled = true;
            yield return null;
            Assert.That(renderer.Resource, Is.Not.Null);
            Assert.That(renderer.Simulation, Is.Not.Null);
            Object.Destroy(gameObject);
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        [StructLayout(LayoutKind.Sequential)]
        private struct DynamicVertexData
        {
            public Vector3 Position;
            public Vector3 Normal;
        }
    }
}
