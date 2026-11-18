# S1 - Compute writes to mesh buffers

Question: Can a compute pipeline write a raw mesh vertex/index backing and have `MeshRenderer` render it through URP Lit?
Environment: Unity 6000.6.9f1, URP 17.6, NVIDIA GeForce RTX 3060 Laptop GPU, Direct3D12
Approach: `GeometryResource` marks dynamic/static vertex streams and the index buffer as raw targets. The icosphere compute kernel writes positions, normals, tangents, UVs, and indices directly; `ProceduralMeshRenderer` assigns the mesh to a `MeshRenderer` with URP Lit.
Outcome: confirmed with caveats
Findings: Mesh-buffer compute writes, index readback, URP Lit rendering, bounds, and PlayMode visibility work. Shadows, SSAO, SRP Batcher behavior, and GPU Resident Drawer exclusion remain secondary verification items rather than Phase 1 acceptance blockers.
Evidence: `PlatformSpikeTests.S1_ComputeWrittenMeshBuffersAreReadable`, `RenderingSmokeTests.Phase1Mesh_IsGeneratedAndRenderedByUrp`, `ReactiveGeometry/Captures/Phase1-Icosphere.png`
Design impact: Phase 1 uses direct writes to an interleaved mesh stream-0 buffer and has no redundant mesh-export copy.
