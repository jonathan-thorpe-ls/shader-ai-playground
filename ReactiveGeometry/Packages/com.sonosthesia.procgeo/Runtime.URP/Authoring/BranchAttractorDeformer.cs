using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    [DisallowMultipleComponent]
    public sealed class BranchAttractorDeformer :
        BranchDeformerComponent,
        IBranchPathDeformer
    {
        [SerializeField] private bool deformationEnabled = true;
        [SerializeField] private Transform attractor;
        [SerializeField] private Vector3 localTarget = new(0f, 3f, 2f);
        [SerializeField, Range(0f, 1f)] private float strength = 0.35f;
        [SerializeField, Min(0.01f)] private float power = 1f;

        bool IBranchPathDeformer.Enabled =>
            isActiveAndEnabled && deformationEnabled;

        public void Record(
            CommandBuffer command,
            BranchPathDeformerContext context)
        {
            ProceduralBranchRenderer renderer = Target;
            if (renderer == null)
                return;
            int kernel = context.Shader.FindKernel("AddBranchAttractor");
            Vector3 target = attractor != null
                ? renderer.transform.InverseTransformPoint(attractor.position)
                : localTarget;
            BindSpatialSource(command, context);
            command.SetComputeVectorParam(
                context.Shader, "_AttractorTarget", target);
            command.SetComputeFloatParam(
                context.Shader, "_AttractorStrength",
                Mathf.Clamp(strength, 0f, 1f));
            command.SetComputeFloatParam(
                context.Shader, "_AttractorPower", Mathf.Max(0.01f, power));
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
                context.Shader, kernel, "_PathFlexibilities",
                context.Paths.Flexibilities);
            command.SetComputeBufferParam(
                context.Shader, kernel, "_PathContributions",
                context.Buffers.PathContributions);
            command.DispatchCompute(
                context.Shader, kernel, context.SampleThreadGroups, 1, 1);
        }

        private void OnDrawGizmosSelected()
        {
            DrawSpatialGizmos(new Color(1f, 0.72f, 0.15f, 1f));
            ProceduralBranchRenderer renderer = Target;
            if (renderer == null)
                return;
            Gizmos.color = new Color(1f, 0.3f, 0.1f, 1f);
            Vector3 point = attractor != null
                ? attractor.position
                : renderer.transform.TransformPoint(localTarget);
            Gizmos.DrawWireSphere(point, 0.1f);
        }
    }
}
