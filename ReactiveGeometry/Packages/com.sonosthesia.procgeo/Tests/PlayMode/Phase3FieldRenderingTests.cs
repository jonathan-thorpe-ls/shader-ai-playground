using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class Phase3FieldRenderingTests
    {
        [UnityTest]
        public IEnumerator Renderer_ExecutesConfiguredFieldsAndStaysFinite()
        {
            Assume.That(SystemInfo.supportsComputeShaders);
            GameObject gameObject = new("Phase 3 Field Rendering");
            ProceduralMeshRenderer renderer =
                gameObject.AddComponent<ProceduralMeshRenderer>();
            yield return null;

            GeometryFieldSettings fields = GeometryFieldSettings.Default;
            fields.fieldsEnabled = true;
            fields.pulseEnabled = true;
            fields.directionalEnabled = true;
            fields.graphSourceVertex = 20;
            fields.pulseAmplitude = 12f;
            fields.directionalStrength = 2f;
            renderer.SetFieldSettings(fields);

            for (int frame = 0; frame < 12; frame++)
                yield return null;

            Assert.That(
                renderer.Resource.Attributes.TryGet(
                    AttributeIds.GraphDistance01, out _),
                Is.True);
            Vector3[] positions = new Vector3[renderer.VertexCount];
            renderer.Resource.Live.Simulation.Position(
                    renderer.Simulation.ReadIndex)
                .GetData(positions);
            Assert.That(positions.All(IsFinite), Is.True);
            Assert.That(
                renderer.ReadSimulationStatus().InvalidValueCount,
                Is.Zero);
            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator Renderer_FieldLifecycleLeavesRegistryUnchanged()
        {
            Assume.That(SystemInfo.supportsComputeShaders);
            int baseline = GpuBufferRegistry.Count;
            GameObject gameObject = new("Phase 3 Field Lifecycle");
            ProceduralMeshRenderer renderer =
                gameObject.AddComponent<ProceduralMeshRenderer>();
            yield return null;
            GeometryFieldSettings fields = GeometryFieldSettings.Default;
            fields.fieldsEnabled = true;
            renderer.SetFieldSettings(fields);
            yield return null;

            Assert.That(GpuBufferRegistry.Count, Is.GreaterThan(baseline));
            Object.Destroy(gameObject);
            yield return null;
            Assert.That(GpuBufferRegistry.Count, Is.EqualTo(baseline));
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
