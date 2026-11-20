using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class PathBendDeformer : SpatialDeformerComponent, IPathDeformer
    {
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField] private Transform pivot;
        [SerializeField] private Vector3 localPivot = Vector3.zero;
        [SerializeField] private Vector3 localAxis = Vector3.up;
        [SerializeField, Range(-180f, 180f)] private float angle = 30f;
        [SerializeField, Range(0f, 1f)] private float falloffStart;
        [SerializeField, Range(0f, 1f)] private float falloffEnd = 1f;

        bool IPathDeformer.Enabled => isActiveAndEnabled && deformationEnabled;
        public float Angle
        {
            get => angle;
            set => angle = value;
        }

        public void Record(CommandBuffer command, PathDeformerContext context)
        {
            ProceduralMeshRenderer renderer = Target;
            if (renderer == null) return;
            Vector3 pivotLocal = pivot != null
                ? renderer.transform.InverseTransformPoint(pivot.position)
                : localPivot;
            Vector3 axisLocal = renderer.transform.InverseTransformDirection(
                pivot != null ? pivot.up : transform.TransformDirection(localAxis.normalized));
            axisLocal = axisLocal.sqrMagnitude > 1e-8f
                ? axisLocal.normalized
                : Vector3.up;
            int kernel = context.Shader.FindKernel("AddPathBend");
            BindSpatialSource(command, context.Shader);
            command.SetComputeVectorParam(context.Shader, "_BendPivot", pivotLocal);
            command.SetComputeVectorParam(context.Shader, "_BendAxis", axisLocal);
            command.SetComputeFloatParam(
                context.Shader, "_BendAngle", angle * Mathf.Deg2Rad);
            command.SetComputeFloatParam(
                context.Shader, "_BendFalloffStart", Mathf.Clamp01(falloffStart));
            command.SetComputeFloatParam(
                context.Shader, "_BendFalloffEnd",
                Mathf.Clamp(falloffEnd, Mathf.Clamp01(falloffStart), 1f));
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathPositions",
                context.Path.RestPositions);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_PathContributions",
                context.Buffers.PathContributions);
            command.DispatchCompute(
                context.Shader, kernel, context.ThreadGroups, 1, 1);
        }

        protected override void RegisterWith(ProceduralMeshRenderer renderer) =>
            renderer.RegisterPathDeformer(this);
        protected override void UnregisterWith(ProceduralMeshRenderer renderer) =>
            renderer.UnregisterPathDeformer(this);
        private void OnDrawGizmosSelected() => DrawSpatialGizmos(Color.cyan);
    }
}
