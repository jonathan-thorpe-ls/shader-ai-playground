using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ImpulseWaveSource : MonoBehaviour
    {
        [SerializeField] private ProceduralMeshRenderer target;
        [SerializeField, Min(1)] private int eventCapacity =
            DeformationEventPool.DefaultCapacity;
        [SerializeField] private float impulseAmplitude = 12f;
        [SerializeField, Min(0f)] private float impulseRadius = 2f;
        [SerializeField] private DeformationDirectionMode impulseDirection =
            DeformationDirectionMode.Outward;
        [SerializeField] private float waveAmplitude = 8f;
        [SerializeField, Min(0f)] private float waveSpeed = 2f;
        [SerializeField, Min(0.001f)] private float waveWidth = 0.35f;
        [SerializeField, Min(0.001f)] private float waveDuration = 4f;
        [SerializeField, Min(0f)] private float waveAttenuation = 0.25f;
        [SerializeField] private float waveFrequency = 0f;
        [SerializeField] private float wavePhase = 0f;
        [SerializeField] private DeformationDirectionMode waveDirection =
            DeformationDirectionMode.RestNormal;
        [SerializeField] private bool drawEventRanges = true;

        private DeformationEventPool _pool;
        private ImpulseDeformationField _impulseField;
        private RadialWaveDeformationField _waveField;

        public int ActiveEventCount => _pool?.ActiveCount ?? 0;

        public uint TriggerImpulse()
        {
            if (!TryGetEventContext(out Vector3 center, out Vector3 direction))
                return 0;
            return _pool.AddImpulse(
                center,
                target.SimulationTime,
                impulseAmplitude,
                impulseRadius,
                impulseDirection,
                direction);
        }

        public uint TriggerWave()
        {
            if (!TryGetEventContext(out Vector3 center, out Vector3 direction))
                return 0;
            return _pool.AddWave(
                center,
                target.SimulationTime,
                waveAmplitude,
                waveSpeed,
                waveWidth,
                waveDuration,
                waveAttenuation,
                waveFrequency,
                wavePhase,
                waveDirection,
                direction);
        }

        public void ResetEvents()
        {
            _pool?.Reset();
            target?.ResetSimulation();
        }

        private void OnEnable()
        {
            _pool = new DeformationEventPool(
                Mathf.Clamp(eventCapacity, 1, 256),
                $"{name}.DeformationEvents");
            _impulseField = new ImpulseDeformationField(_pool);
            _waveField = new RadialWaveDeformationField(_pool);
            target?.RegisterField(_impulseField);
            target?.RegisterField(_waveField);
        }

        private void OnDisable()
        {
            target?.UnregisterField(_waveField);
            target?.UnregisterField(_impulseField);
            _pool?.Dispose();
            _pool = null;
            _impulseField = null;
            _waveField = null;
        }

        private bool TryGetEventContext(
            out Vector3 center,
            out Vector3 direction)
        {
            center = Vector3.zero;
            direction = Vector3.forward;
            if (target == null || _pool == null)
            {
                Debug.LogError(
                    "ImpulseWaveSource requires an enabled target renderer.", this);
                return false;
            }
            center = target.transform.InverseTransformPoint(transform.position);
            direction = target.transform.InverseTransformDirection(transform.forward);
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawEventRanges) return;
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, impulseRadius);
            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.4f);
            float radius = Mathf.Max(waveSpeed * waveDuration, waveWidth);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(
                transform.position,
                transform.position + transform.forward * 1.25f);
        }
    }
}
