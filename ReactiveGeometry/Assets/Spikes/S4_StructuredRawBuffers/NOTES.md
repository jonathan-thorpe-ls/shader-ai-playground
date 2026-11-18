# S4 - Structured and Raw buffer capability

Question: Can a buffer be created with both `Structured` and `Raw` targets for a reusable ByteAddressBuffer/structured-buffer debug view?
Environment: Unity 6000.6.9f1, NVIDIA GeForce RTX 3060 Laptop GPU, Direct3D12
Approach: Allocate a small `GraphicsBuffer` with combined targets, upload deterministic integers, and read them back.
Outcome: confirmed
Findings: Combined target creation, upload, and readback succeed on the tested backend. Phase 1 attributes remain structured buffers; a combined view can be introduced selectively for later generic debugging.
Evidence: `PlatformSpikeTests.S4_StructuredAndRawBufferCombinationIsSupported`
Design impact: No fallback debug-copy kernel is required on the tested D3D12 path.
