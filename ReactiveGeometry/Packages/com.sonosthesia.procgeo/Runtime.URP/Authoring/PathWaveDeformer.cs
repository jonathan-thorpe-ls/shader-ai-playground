using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class PathWaveDeformer : SpatialDeformerComponent, IPathDeformer
    {
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField, Min(0f)] private float amplitude = 0.1f;
        [SerializeField] private Vector3 localDirection = Vector3.up;
        [SerializeField, Min(0f)] private float spatialFrequency = 2f;
        [SerializeField] private float speed = 1f;
        [SerializeField] private float phase;

        bool IPathDeformer.Enabled => isActiveAndEnabled && deformationEnabled;

        public void Record(CommandBuffer command, PathDeformerContext context)
        {
            int kernel = context.Shader.FindKernel("AddPathWave");
            BindSpatialSource(command, context.Shader);
            command.SetComputeVectorParam(
                context.Shader, "_WaveDirection",
                localDirection.sqrMagnitude > 1e-8f
                    ? localDirection.normalized
                    : Vector3.up);
            command.SetComputeFloatParam(context.Shader, "_WaveAmplitude", Mathf.Abs(amplitude));
            command.SetComputeFloatParam(
                context.Shader, "_WaveFrequency", Mathf.Abs(spatialFrequency));
            command.SetComputeFloatParam(context.Shader, "_WaveSpeed", speed);
            command.SetComputeFloatParam(context.Shader, "_WavePhase", phase);
            command.SetComputeFloatParam(context.Shader, "_Time", context.Time);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathDistances",
                context.Path.RestDistances);
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
