using System;
using UnityEngine;

namespace Sonosthesia.Signals
{
    [DisallowMultipleComponent]
    public sealed class HybridControlConsumer :
        MonoBehaviour,
        IControlSlotTarget,
        IControlVector2SlotTarget,
        IControlVector3SlotTarget
    {
        private static readonly ControlSignalId Position2Id =
            ControlSignalId.FromString("hybrid.position2");
        private static readonly ControlSignalId Position3Id =
            ControlSignalId.FromString("hybrid.position3");

        [SerializeField] private Transform vector2Target;
        [SerializeField] private Transform vector3Target;

        public int ControlSlotCount => 2;
        public Vector2 LastVector2 { get; private set; }
        public Vector3 LastVector3 { get; private set; }

        public bool TryGetControlSlot(ControlSignalId id, out int slotHandle)
        {
            if (id == Position2Id)
            {
                slotHandle = 0;
                return true;
            }
            if (id == Position3Id)
            {
                slotHandle = 1;
                return true;
            }
            slotHandle = -1;
            return false;
        }

        public ControlSignalType GetControlSlotType(int slotHandle)
        {
            return slotHandle == 0
                ? ControlSignalType.Vector2
                : slotHandle == 1
                    ? ControlSignalType.Vector3
                    : throw new ArgumentOutOfRangeException(nameof(slotHandle));
        }

        public void WriteFloat(int slotHandle, float value)
        {
            throw new InvalidOperationException("This consumer accepts typed vector signals only.");
        }

        public void WritePulse(int slotHandle, in ControlPulse pulse)
        {
            throw new InvalidOperationException("This consumer accepts typed vector signals only.");
        }

        public void WriteVector2(int slotHandle, Vector2 value)
        {
            if (slotHandle != 0)
                throw new ArgumentOutOfRangeException(nameof(slotHandle));
            LastVector2 = value;
            if (vector2Target != null)
            {
                Vector3 position = vector2Target.localPosition;
                position.x = value.x;
                position.y = value.y;
                vector2Target.localPosition = position;
            }
        }

        public void WriteVector3(int slotHandle, Vector3 value)
        {
            if (slotHandle != 1)
                throw new ArgumentOutOfRangeException(nameof(slotHandle));
            LastVector3 = value;
            if (vector3Target != null)
                vector3Target.localPosition = value;
        }

        public void Configure(Transform vector2, Transform vector3)
        {
            vector2Target = vector2;
            vector3Target = vector3;
        }
    }
}
