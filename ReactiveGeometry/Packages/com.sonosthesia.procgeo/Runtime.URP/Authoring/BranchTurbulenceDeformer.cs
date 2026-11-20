using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class BranchTurbulenceDeformer :
        BranchDeformerComponent,
        IBranchPathDeformer
    {
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField, Range(0f, 1f)] private float amplitude = 0.25f;
        [SerializeField] private Vector3 localWindDirection = Vector3.right;
        [SerializeField, Min(0.0001f)] private float spatialFrequency = 2.5f;
        [SerializeField] private float temporalSpeed = 0.35f;
        [SerializeField] private float phase;
        [SerializeField] private int seed = 731;
        [SerializeField, Range(1, 6)] private int octaves = 3;
        [SerializeField, Min(1f)] private float lacunarity = 2f;
        [SerializeField, Range(0f, 1f)] private float gain = 0.5f;

        bool IBranchPathDeformer.Enabled =>
            isActiveAndEnabled && deformationEnabled;

        public void Record(
            CommandBuffer command,
            BranchPathDeformerContext context)
        {
            ProceduralBranchRenderer renderer = Target;
            if (renderer == null)
                return;
            int kernel = context.Shader.FindKernel("AddBranchTurbulence");
            Vector3 wind = renderer.transform.InverseTransformDirection(
                transform.TransformDirection(localWindDirection)).normalized;
            BindSpatialSource(command, context);
            command.SetComputeVectorParam(
                context.Shader, "_WindDirection", wind);
            command.SetComputeFloatParam(
                context.Shader, "_TurbulenceAmplitude",
                Mathf.Clamp(amplitude, 0f, 1f));
            command.SetComputeFloatParam(
                context.Shader, "_TurbulenceFrequency",
                Mathf.Max(0.0001f, spatialFrequency));
            command.SetComputeFloatParam(
                context.Shader, "_TurbulenceSpeed", temporalSpeed);
            command.SetComputeFloatParam(
                context.Shader, "_TurbulencePhase", phase);
            command.SetComputeIntParam(
                context.Shader, "_TurbulenceSeed", seed);
            command.SetComputeIntParam(
                context.Shader, "_TurbulenceOctaves",
                Mathf.Clamp(octaves, 1, 6));
            command.SetComputeFloatParam(
                context.Shader, "_TurbulenceLacunarity",
                Mathf.Max(1f, lacunarity));
            command.SetComputeFloatParam(
                context.Shader, "_TurbulenceGain", Mathf.Clamp01(gain));
            command.SetComputeFloatParam(
                context.Shader, "_Time", context.Time);
            BindRequiredBuffers(command, kernel, context);
            command.DispatchCompute(
                context.Shader, kernel, context.SampleThreadGroups, 1, 1);
        }

        private static void BindRequiredBuffers(
            CommandBuffer command,
            int kernel,
            BranchPathDeformerContext context)
        {
            command.SetComputeBufferParam(
                context.Shader, kernel, "_BranchHeaders",
                context.Paths.Headers);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_PathBranchIds",
                context.Paths.BranchIds);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathPositions",
                context.Paths.RestPositions);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathTangents",
                context.Paths.RestTangents);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_RestPathNormals",
                context.Paths.RestNormals);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_PathLocalPositions",
                context.Paths.LocalPositions);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_PathFlexibilities",
                context.Paths.Flexibilities);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_PathContributions",
                context.Buffers.PathContributions);
        }

        private void OnDrawGizmosSelected() =>
            DrawSpatialGizmos(new Color(0.15f, 0.85f, 1f, 1f));
    }
}
