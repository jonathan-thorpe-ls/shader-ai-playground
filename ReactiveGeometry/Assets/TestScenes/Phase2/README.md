# Phase 2 - Interactive Test Scenes

This folder is the manual exploration area for the dynamic neighbour simulation planned in
[`Docs/Implementation/Phase2-DynamicNeighbourSimulation.md`](../../../../Docs/Implementation/Phase2-DynamicNeighbourSimulation.md).

The scenes contain simulation-enabled procedural icospheres and a shared on-screen operator panel. The panel provides impulse, reset, pause/resume, single-step, parameter sliders and gentle/nominal/maximum presets.

## Scenes

### `01_RestAndRecovery`

Enter Play mode, apply a single impulse, and verify that the icosphere returns to rest. Use reset, pause/resume and single-step to inspect the transition.

### `02_NeighbourPropagation`

Use a coarse icosphere to observe a local impulse spreading through committed graph neighbours. Compare the selected origin and different stiffness/damping combinations.

The impulse origin is configured on `Phase2SceneController`. Propagation uses only the committed CSR topology; it does not search by world-space distance.

### `03_StabilityStress`

Switch between gentle, nominal and maximum documented presets. Exercise repeated impulses, pause/resume, reset and regeneration while watching numeric clamp and dropped-step diagnostics.

This scene enables repeated impulses at a bounded interval. Numeric validity, clamp and dropped-time indicators are diagnostic; repeated clamp activity means the chosen stress setup is outside its stable operating region.

## Manual checks

For each scene:

1. Confirm there is no console or shader error before entering Play mode.
2. Confirm the object is stationary at rest.
3. Exercise reset, pause and resume.
4. Apply at least one impulse and allow the system to settle.
5. Regenerate or change the scene's supported quality preset.
6. Exit Play mode and verify the scene returns to its saved setup.

## Interpretation

These scenes support exploration and evidence capture; they do not replace the EditMode and PlayMode automated suites. A scene does not count as Phase 2 complete until its corresponding documented acceptance checks and tests pass.
