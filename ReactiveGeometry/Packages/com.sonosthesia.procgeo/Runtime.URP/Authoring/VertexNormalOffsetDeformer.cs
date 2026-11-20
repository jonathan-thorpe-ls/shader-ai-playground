using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class VertexNormalOffsetDeformer : SpatialDeformerComponent, IVertexDeformer
    {
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField, Min(0f)] private float amplitude = 0.05f;
        [SerializeField, Min(0f)] private float spatialFrequency = 0.15f;
        [SerializeField] private float speed = 1f;
        [SerializeField] private float phase = 90f * Mathf.Deg2Rad;

        bool IVertexDeformer.Enabled => isActiveAndEnabled && deformationEnabled;
        public float Amplitude
        {
            get => amplitude;
            set => amplitude = Mathf.Max(0f, value);
        }

        public void Record(CommandBuffer command, VertexDeformerContext context)
        {
            int kernel = context.Shader.FindKernel("AddVertexNormalOffset");
            BindSpatialSource(command, context.Shader);
            command.SetComputeFloatParam(
                context.Shader, "_VertexNormalAmplitude", amplitude);
            command.SetComputeFloatParam(
                context.Shader, "_VertexWaveFrequency", spatialFrequency);
            command.SetComputeFloatParam(
                context.Shader, "_VertexWaveSpeed", speed);
            command.SetComputeFloatParam(
                context.Shader, "_VertexWavePhase", phase);
            command.SetComputeFloatParam(context.Shader, "_Time", context.Time);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_BasePositions",
                context.Buffers.BasePositions);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_BaseNormals",
                context.Buffers.BaseNormals);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_VertexOffsets",
                context.Buffers.VertexOffsets);
            command.DispatchCompute(
                context.Shader, kernel, context.ThreadGroups, 1, 1);
        }

        protected override void RegisterWith(ProceduralMeshRenderer renderer) =>
            renderer.RegisterVertexDeformer(this);
        protected override void UnregisterWith(ProceduralMeshRenderer renderer) =>
            renderer.UnregisterVertexDeformer(this);
        private void OnDrawGizmosSelected() => DrawSpatialGizmos(Color.magenta);
    }
}
