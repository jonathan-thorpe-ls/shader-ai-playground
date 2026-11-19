using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class NoiseDeformationSource : MonoBehaviour
    {
        [SerializeField] private ProceduralMeshRenderer target;
        [SerializeField] private NoiseDeformationSettings settings =
            NoiseDeformationSettings.Default;
        [SerializeField] private bool drawFalloff = true;

        private NoiseDeformationField _field;

        public ProceduralMeshRenderer Target => target;

        private void OnEnable()
        {
            _field = new NoiseDeformationField(settings);
            UpdateField();
            target?.RegisterField(_field);
        }

        private void Update()
        {
            UpdateField();
        }

        private void OnDisable()
        {
            target?.UnregisterField(_field);
            _field = null;
        }

        private void OnValidate()
        {
            UpdateField();
        }

        private void UpdateField()
        {
            if (_field == null || target == null) return;
            SpatialSourceParameters source = settings.source;
            source.localCenter = target.transform.InverseTransformPoint(transform.position);
            source.localForward =
                target.transform.InverseTransformDirection(transform.forward);
            settings.source = source;
            _field.Settings = settings;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawFalloff) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, settings.source.innerRadius);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, settings.source.outerRadius);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(
                transform.position,
                transform.position + transform.forward *
                Mathf.Max(settings.source.outerRadius * 0.35f, 0.5f));
        }
    }
}
