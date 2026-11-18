# S0 - Baseline

Question: Does the Phase 1 Unity/URP/Pipeline baseline support GPU geometry development?
Environment: Unity 6000.6.9f1, URP 17.6, NVIDIA GeForce RTX 3060 Laptop GPU, Direct3D12
Approach: Use the existing `ReactiveGeometry` project, Pipeline 1.0.0, Test Framework 1.8.0, and the `PC_RPAsset` URP configuration. Confirm the active pipeline through a Spike-category EditMode test.
Outcome: confirmed
Findings: The active render pipeline is `UniversalRenderPipelineAsset`. Pipeline commands, compilation, GPU EditMode tests, PlayMode, and Game-view capture work on the development machine.
Evidence: `PlatformSpikeTests.S0_ActivePipelineIsUrp`
Design impact: M0 and M1 can proceed on the selected Unity 6.6 / URP 17.6 baseline.
