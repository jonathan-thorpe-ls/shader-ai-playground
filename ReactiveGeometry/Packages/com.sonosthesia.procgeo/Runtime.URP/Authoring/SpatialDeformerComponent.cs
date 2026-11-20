using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public abstract class SpatialDeformerComponent : MonoBehaviour
    {
        [SerializeField] private ProceduralMeshRenderer target;
        [SerializeField] private SpatialSourceParameters spatialSource = new()
        {
            enabled = false,
            localCenter = Vector3.zero,
            localForward = Vector3.forward,
            innerRadius = 0.5f,
            outerRadius = 2f,
            weight = 1f,
            falloff = SpatialFalloffMode.Smoothstep
        };
        [SerializeField] private bool drawFalloff = true;

        public ProceduralMeshRenderer Target
        {
            get => ResolveTarget();
            set
            {
                Unregister();
                target = value;
                Register();
            }
        }

        protected virtual void OnEnable()
        {
            Register();
        }

        protected virtual void OnDisable()
        {
            Unregister();
        }

        protected virtual void OnValidate()
        {
            spatialSource = SpatialDeformationValidation.ValidateSource(spatialSource);
        }

        protected void BindSpatialSource(CommandBuffer command, ComputeShader shader)
        {
            SpatialSourceParameters source =
                SpatialDeformationValidation.ValidateSource(spatialSource);
            ProceduralMeshRenderer renderer = ResolveTarget();
            if (renderer == null)
            {
                command.SetComputeIntParam(shader, "_SourceEnabled", 0);
                command.SetComputeFloatParam(shader, "_SourceWeight", 1f);
                return;
            }

            source.localCenter =
                renderer.transform.InverseTransformPoint(transform.position);
            command.SetComputeVectorParam(
                shader, "_SourceCenter", source.localCenter);
            command.SetComputeFloatParam(
                shader, "_InnerRadius", source.innerRadius);
            command.SetComputeFloatParam(
                shader, "_OuterRadius", source.outerRadius);
            command.SetComputeFloatParam(
                shader, "_SourceWeight", source.weight);
            command.SetComputeIntParam(
                shader, "_FalloffMode", (int)source.falloff);
            command.SetComputeIntParam(
                shader, "_SourceEnabled",
                source.enabled && source.outerRadius > 0f &&
                source.weight != 0f
                    ? 1
                    : 0);
        }

        protected ProceduralMeshRenderer ResolveTarget()
        {
            if (target == null)
                target = GetComponentInParent<ProceduralMeshRenderer>();
            return target;
        }

        protected void DrawSpatialGizmos(Color color)
        {
            if (!drawFalloff || !spatialSource.enabled) return;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, spatialSource.innerRadius);
            Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.35f);
            Gizmos.DrawWireSphere(transform.position, spatialSource.outerRadius);
        }

        protected abstract void RegisterWith(ProceduralMeshRenderer renderer);
        protected abstract void UnregisterWith(ProceduralMeshRenderer renderer);

        private void Register()
        {
            ProceduralMeshRenderer renderer = ResolveTarget();
            if (renderer != null)
                RegisterWith(renderer);
        }

        private void Unregister()
        {
            ProceduralMeshRenderer renderer = ResolveTarget();
            if (renderer != null)
                UnregisterWith(renderer);
        }
    }
}
