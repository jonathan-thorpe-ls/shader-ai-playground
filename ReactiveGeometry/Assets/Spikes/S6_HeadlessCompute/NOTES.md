# S6 - CLI compute verification

Question: Can the GPU generation pipeline dispatch and read back through the Unity CLI test workflow?
Environment: Unity 6000.6.9f1, NVIDIA GeForce RTX 3060 Laptop GPU, Direct3D12, compute enabled
Approach: Run EditMode tests through the Pipeline test command and read headers, indices, topology, and validation results from real `GraphicsBuffer` storage.
Outcome: confirmed with caveats
Findings: CLI-driven EditMode GPU tests and readbacks work with a real graphics device while the Editor runs in the background. This is not a `-nographics` verification; a CI runner still needs a working graphics device.
Evidence: `PlatformSpikeTests.S6_ComputeGenerationAndReadbackWork`; 23-test EditMode suite and one PlayMode smoke test pass.
Design impact: GPU tests must run on a GPU-enabled CI agent and use `Assume` only when compute support is unavailable.
