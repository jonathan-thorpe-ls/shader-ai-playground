using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public abstract class BranchDeformerComponent : MonoBehaviour
    {
        [SerializeField] private ProceduralBranchRenderer target;
        [SerializeField] private SpatialSourceParameters spatialSource = new()
        {
            enabled = false,
            localCenter = Vector3.zero,
            localForward = Vector3.forward,
            innerRadius = 0.5f,
            outerRadius = 4f,
            weight = 1f,
            falloff = SpatialFalloffMode.Smoothstep
        };
        [SerializeField] private bool drawFalloff = true;

        public ProceduralBranchRenderer Target
        {
            get => ResolveTarget();
            set
            {
                Unregister();
                target = value;
                Register();
            }
        }

        protected virtual void OnEnable() => Register();
        protected virtual void OnDisable() => Unregister();
        protected virtual void OnValidate() =>
            spatialSource = SpatialDeformationValidation.ValidateSource(
                spatialSource);

        protected void BindSpatialSource(
            CommandBuffer command,
            BranchPathDeformerContext context)
        {
            SpatialSourceParameters source =
                SpatialDeformationValidation.ValidateSource(spatialSource);
            ProceduralBranchRenderer renderer = ResolveTarget();
            if (renderer == null)
            {
                command.SetComputeIntParam(
                    context.Shader, "_SourceEnabled", 0);
                return;
            }
            command.SetComputeVectorParam(
                context.Shader,
                "_SourceCenter",
                renderer.transform.InverseTransformPoint(transform.position));
            command.SetComputeFloatParam(
                context.Shader, "_InnerRadius", source.innerRadius);
            command.SetComputeFloatParam(
                context.Shader, "_OuterRadius", source.outerRadius);
            command.SetComputeFloatParam(
                context.Shader, "_SourceWeight", source.weight);
            command.SetComputeIntParam(
                context.Shader, "_FalloffMode", (int)source.falloff);
            command.SetComputeIntParam(
                context.Shader,
                "_SourceEnabled",
                source.enabled && source.outerRadius > 0f &&
                source.weight != 0f ? 1 : 0);
        }

        protected ProceduralBranchRenderer ResolveTarget()
        {
            if (target == null)
                target = GetComponentInParent<ProceduralBranchRenderer>();
            return target;
        }

        protected void DrawSpatialGizmos(Color color)
        {
            if (!drawFalloff || !spatialSource.enabled)
                return;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, spatialSource.innerRadius);
            Gizmos.color = new Color(color.r, color.g, color.b, 0.35f);
            Gizmos.DrawWireSphere(transform.position, spatialSource.outerRadius);
        }

        private void Register()
        {
            ProceduralBranchRenderer renderer = ResolveTarget();
            if (renderer != null && this is IBranchPathDeformer deformer)
                renderer.RegisterPathDeformer(deformer);
        }

        private void Unregister()
        {
            ProceduralBranchRenderer renderer = ResolveTarget();
            if (renderer != null && this is IBranchPathDeformer deformer)
                renderer.UnregisterPathDeformer(deformer);
        }
    }
}
