# Phase 1 - GPU Geometry Test Scenes

These scenes exercise the Phase 1 icosphere vertical slice: compute generation, canonical
vertex deduplication, deterministic topology, metadata, GPU validation, and URP mesh-bridge
rendering. Each scene uses the shared UI Toolkit control panel.

## Scenes

### `01_IcosphereGeneration`

Compare the base, medium, and high frequency presets. Confirm that regeneration changes the
vertex and triangle counts while preserving a smooth sphere and stable radius.

### `02_MetadataAndTopology`

Inspect the deterministic geometry counts. For each regeneration, confirm the vertex, face,
edge, corner, region, and attribute totals correspond to one closed manifold icosphere.

### `03_DetailAndRegeneration`

Exercise the stress preset and repeated regeneration at the highest supported test detail.
Confirm every operation completes without compute, validation, buffer, or shader errors.

## Manual checks

For each scene:

1. Enter Play mode and confirm the object renders.
2. Use every quality preset and run a manual radius/frequency regeneration.
3. Confirm diagnostics update to the newly generated counts.
4. Watch the Console for compute, topology, validation, material, or UI errors.
5. Exit Play mode and confirm the saved scene state is restored.
