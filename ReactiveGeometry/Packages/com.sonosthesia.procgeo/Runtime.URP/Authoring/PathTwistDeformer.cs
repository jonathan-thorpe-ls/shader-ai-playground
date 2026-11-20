using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class PathTwistDeformer : SpatialDeformerComponent, IPathDeformer
    {
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField, Range(-360f, 360f)] private float angle = 45f;
        [SerializeField, Range(0f, 1f)] private float falloffStart;
        [SerializeField, Range(0f, 1f)] private float falloffEnd = 1f;

        bool IPathDeformer.Enabled => isActiveAndEnabled && deformationEnabled;

        public void Record(CommandBuffer command, PathDeformerContext context)
        {
            int kernel = context.Shader.FindKernel("AddPathTwist");
            BindSpatialSource(command, context.Shader);
            command.SetComputeFloatParam(
                context.Shader, "_Twist", angle * Mathf.Deg2Rad);
            command.SetComputeFloatParam(
                context.Shader, "_TwistMaskStart", Mathf.Clamp01(falloffStart));
            command.SetComputeFloatParam(
                context.Shader, "_TwistMaskEnd",
                Mathf.Clamp(falloffEnd, Mathf.Clamp01(falloffStart), 1f));
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathPositions",
                context.Path.RestPositions);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_PathTwists", context.Buffers.PathTwists);
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
