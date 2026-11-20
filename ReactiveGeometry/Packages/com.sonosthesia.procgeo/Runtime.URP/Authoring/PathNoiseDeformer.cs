using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class PathNoiseDeformer : SpatialDeformerComponent, IPathDeformer
    {
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField, Min(0f)] private float amplitude = 0.15f;
        [SerializeField, Min(0.0001f)] private float spatialFrequency = 3f;
        [SerializeField] private float temporalSpeed = 0.3f;
        [SerializeField] private float phase;
        [SerializeField] private int seed;
        [SerializeField, Range(1, 6)] private int octaves = 3;
        [SerializeField, Min(1f)] private float lacunarity = 2f;
        [SerializeField, Range(0f, 1f)] private float gain = 0.5f;
        [SerializeField] private Vector3 localBasis = new(0f, 1f, 0.35f);

        bool IPathDeformer.Enabled => isActiveAndEnabled && deformationEnabled;
        public float Amplitude
        {
            get => amplitude;
            set => amplitude = Mathf.Max(0f, value);
        }

        public void Record(CommandBuffer command, PathDeformerContext context)
        {
            int kernel = context.Shader.FindKernel("AddPathNoise");
            BindSpatialSource(command, context.Shader);
            command.SetComputeIntParam(
                context.Shader, "_PathNoiseSeed", seed);
            command.SetComputeIntParam(
                context.Shader, "_PathNoiseOctaves", Mathf.Clamp(octaves, 1, 6));
            command.SetComputeFloatParam(
                context.Shader, "_PathNoiseFrequency",
                Mathf.Max(spatialFrequency, 0.0001f));
            command.SetComputeFloatParam(
                context.Shader, "_PathNoiseSpeed", temporalSpeed);
            command.SetComputeFloatParam(
                context.Shader, "_PathNoisePhase", phase);
            command.SetComputeFloatParam(
                context.Shader, "_PathNoiseLacunarity",
                Mathf.Max(lacunarity, 1f));
            command.SetComputeFloatParam(
                context.Shader, "_PathNoiseGain", Mathf.Clamp01(gain));
            command.SetComputeFloatParam(
                context.Shader, "_PathNoiseAmplitude", Mathf.Max(amplitude, 0f));
            command.SetComputeVectorParam(
                context.Shader, "_PathNoiseBasis", localBasis);
            command.SetComputeFloatParam(context.Shader, "_Time", context.Time);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathDistances",
                context.Path.RestDistances);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathPositions",
                context.Path.RestPositions);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathTangents",
                context.Path.RestTangents);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathNormals",
                context.Path.RestNormals);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathBinormals",
                context.Path.RestBinormals);
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
