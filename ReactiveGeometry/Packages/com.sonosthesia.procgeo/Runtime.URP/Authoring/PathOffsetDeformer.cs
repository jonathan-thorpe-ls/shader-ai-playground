using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class PathOffsetDeformer : SpatialDeformerComponent, IPathDeformer
    {
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField] private Vector3 offset = Vector3.zero;

        bool IPathDeformer.Enabled => isActiveAndEnabled && deformationEnabled;
        public Vector3 Offset
        {
            get => offset;
            set => offset = value;
        }

        public void Record(CommandBuffer command, PathDeformerContext context)
        {
            int kernel = context.Shader.FindKernel("AddPathOffset");
            BindSpatialSource(command, context.Shader);
            command.SetComputeVectorParam(context.Shader, "_PathOffset", offset);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_PathContributions",
                context.Buffers.PathContributions);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathPositions",
                context.Path.RestPositions);
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
