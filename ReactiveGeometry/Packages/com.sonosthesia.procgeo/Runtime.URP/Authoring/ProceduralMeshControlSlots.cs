using Sonosthesia.Signals;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class ProceduralMeshControlSlots : MonoBehaviour, IControlSlotTarget
    {
        private static readonly ControlSignalId PulseAmplitudeId =
            ControlSignalId.FromString("procgeo.field.pulseAmplitude");
        private static readonly ControlSignalId DirectionalStrengthId =
            ControlSignalId.FromString("procgeo.field.directionalStrength");
        private static readonly ControlSignalId StiffnessId =
            ControlSignalId.FromString("procgeo.simulation.stiffness");
        private static readonly ControlSignalId DampingId =
            ControlSignalId.FromString("procgeo.simulation.damping");
        private static readonly ControlSignalId ImpulseTriggerId =
            ControlSignalId.FromString("procgeo.impulse.trigger");

        [SerializeField] private ProceduralMeshRenderer target;
        [SerializeField] private ImpulseWaveSource impulseSource;
        [SerializeField] private bool resetOnDisable = true;

        private SimulationParameters _authoringSimulation;
        private GeometryFieldSettings _authoringFields;
        private bool _hasAuthoringState;

        public int ControlSlotCount => 5;

        public void Configure(
            ProceduralMeshRenderer meshRenderer,
            ImpulseWaveSource impulse)
        {
            target = meshRenderer;
            impulseSource = impulse;
            _authoringSimulation = target != null
                ? target.Parameters
                : SimulationParameters.Nominal;
            _authoringFields = target != null
                ? target.FieldSettings
                : GeometryFieldSettings.Default;
            _hasAuthoringState = target != null;
        }

        public bool TryGetControlSlot(ControlSignalId id, out int slotHandle)
        {
            if (id == PulseAmplitudeId)
            {
                slotHandle = 0;
                return true;
            }
            if (id == DirectionalStrengthId)
            {
                slotHandle = 1;
                return true;
            }
            if (id == StiffnessId)
            {
                slotHandle = 2;
                return true;
            }
            if (id == DampingId)
            {
                slotHandle = 3;
                return true;
            }
            if (id == ImpulseTriggerId)
            {
                slotHandle = 4;
                return true;
            }

            slotHandle = -1;
            return false;
        }

        public ControlSignalType GetControlSlotType(int slotHandle)
        {
            return slotHandle == 4
                ? ControlSignalType.Pulse
                : ControlSignalType.Continuous;
        }

        public void WriteFloat(int slotHandle, float value)
        {
            if ((uint)slotHandle >= 4)
                throw new System.ArgumentOutOfRangeException(
                    nameof(slotHandle),
                    "The procedural control slot is not a float.");
            if (target == null || float.IsNaN(value) || float.IsInfinity(value))
                return;

            switch (slotHandle)
            {
                case 0:
                    target.SetRuntimeFieldControls(
                        value,
                        target.FieldSettings.directionalStrength);
                    break;
                case 1:
                    target.SetRuntimeFieldControls(
                        target.FieldSettings.pulseAmplitude,
                        value);
                    break;
                case 2:
                    target.SetRuntimeSimulationControls(value, target.Parameters.damping);
                    break;
                case 3:
                    target.SetRuntimeSimulationControls(target.Parameters.stiffness, value);
                    break;
            }
        }

        public void WritePulse(int slotHandle, in ControlPulse pulse)
        {
            if (slotHandle != 4)
                throw new System.ArgumentOutOfRangeException(
                    nameof(slotHandle),
                    "The procedural control slot is not a pulse.");
            if (pulse.Started && pulse.Strength > 0f)
                impulseSource?.TriggerImpulse();
        }

        public void ResetToAuthoringValues()
        {
            if (!_hasAuthoringState || target == null)
                return;
            target.SetSimulationParameters(_authoringSimulation);
            target.SetFieldSettings(_authoringFields);
        }

        private void OnEnable()
        {
            if (target == null)
                return;
            _authoringSimulation = target.Parameters;
            _authoringFields = target.FieldSettings;
            _hasAuthoringState = true;
        }

        private void OnDisable()
        {
            if (resetOnDisable)
                ResetToAuthoringValues();
        }
    }
}
