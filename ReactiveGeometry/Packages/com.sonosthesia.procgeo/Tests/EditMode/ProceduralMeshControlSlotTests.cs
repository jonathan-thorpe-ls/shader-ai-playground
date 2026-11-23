using NUnit.Framework;
using Sonosthesia.Signals;
using UnityEngine;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class ProceduralMeshControlSlotTests
    {
        [Test]
        public void Adapter_ExposesStableFloatAndPulseSlots()
        {
            var gameObject = new GameObject("Procedural Mesh Controls");
            ProceduralMeshControlSlots adapter =
                gameObject.AddComponent<ProceduralMeshControlSlots>();

            Assert.That(adapter.ControlSlotCount, Is.EqualTo(5));
            Assert.That(
                adapter.TryGetControlSlot(
                    ControlSignalId.FromString("procgeo.field.pulseAmplitude"),
                    out int pulseAmplitude),
                Is.True);
            Assert.That(
                adapter.GetControlSlotType(pulseAmplitude),
                Is.EqualTo(ControlSignalType.Continuous));
            Assert.That(
                adapter.TryGetControlSlot(
                    ControlSignalId.FromString("procgeo.impulse.trigger"),
                    out int impulse),
                Is.True);
            Assert.That(
                adapter.GetControlSlotType(impulse),
                Is.EqualTo(ControlSignalType.Pulse));

            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void Adapter_RejectsUnknownAndMismatchedSlots()
        {
            var gameObject = new GameObject("Procedural Mesh Controls");
            ProceduralMeshControlSlots adapter =
                gameObject.AddComponent<ProceduralMeshControlSlots>();

            Assert.That(
                adapter.TryGetControlSlot(
                    ControlSignalId.FromString("procgeo.unknown"),
                    out _),
                Is.False);
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                adapter.WriteFloat(4, 1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                adapter.WritePulse(0, new ControlPulse(1f, 1f, true)));

            Object.DestroyImmediate(gameObject);
        }
    }
}
