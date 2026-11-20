using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sonosthesia.ProcGeo.Tests
{
    public sealed class Phase6BranchRenderingTests
    {
        [UnityTest]
        public IEnumerator BranchRendererPublishesTubesAndRunsDeformers()
        {
            Assume.That(SystemInfo.supportsComputeShaders);
            GameObject branchObject = new("Phase 6 Branch Rendering");
            ProceduralBranchRenderer renderer =
                branchObject.AddComponent<ProceduralBranchRenderer>();
            BranchGenerationSettings settings =
                BranchGenerationSettings.Default;
            settings.maximumDepth = 2;
            settings.meshVariant = BranchMeshVariant.PortJunction;
            settings.branchesPerNode = 2;
            settings.meshCellSize = 0.09f;
            settings.branchesPerNode = 2;
            settings.minimumSamples = 3;
            settings.samplesPerUnitLength = 3;
            settings.maximumSamplesPerBranch = 10;
            settings.maximumBranches = 24;
            settings.maximumPathSamples = 128;
            renderer.SetSettings(settings);

            GameObject turbulenceObject = new("Branch Turbulence");
            turbulenceObject.AddComponent<BranchTurbulenceDeformer>().Target =
                renderer;
            GameObject attractorObject = new("Branch Attractor");
            attractorObject.transform.position = new Vector3(2f, 3f, 1f);
            BranchAttractorDeformer attractor =
                attractorObject.AddComponent<BranchAttractorDeformer>();
            attractor.Target = renderer;
            BranchGraphDebugOverlay overlay =
                branchObject.AddComponent<BranchGraphDebugOverlay>();
            overlay.Refresh();

            yield return null;
            yield return null;

            Assert.That(renderer.PathDeformation, Is.Not.Null);
            Assert.That(renderer.Resource, Is.Not.Null);
            Assert.That(renderer.BranchCount, Is.EqualTo(7));
            Assert.That(renderer.PathDeformation.Paths.LivePositions.count,
                Is.EqualTo(renderer.PathSampleCount));
            Assert.That(
                branchObject.GetComponent<MeshFilter>().sharedMesh,
                Is.Not.Null);
            Assert.That(overlay.RestVisible, Is.True);

            Object.Destroy(branchObject);
            Object.Destroy(turbulenceObject);
            Object.Destroy(attractorObject);
            yield return null;
        }
    }
}
