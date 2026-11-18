# Phase 2 - Interactive Test Scenes

This folder is the manual exploration area for the dynamic neighbour simulation planned in
[`Docs/Implementation/Phase2-DynamicNeighbourSimulation.md`](../../../../Docs/Implementation/Phase2-DynamicNeighbourSimulation.md).

The scenes are scaffolded with Phase 1 procedural icospheres so their location and visual baseline exist during implementation. The simulation controls described below become available as Phase 2 milestones are implemented.

## Scenes

### `01_RestAndRecovery`

Enter Play mode, apply a single impulse, and verify that the icosphere returns to rest. The final scene controller provides reset, pause, resume, impulse and single-step controls.

### `02_NeighbourPropagation`

Use a coarse icosphere to observe a local impulse spreading through committed graph neighbours. Compare the selected origin and different stiffness/damping combinations.

### `03_StabilityStress`

Switch between gentle, nominal and maximum documented presets. Exercise repeated impulses, pause/resume, reset and regeneration while watching numeric clamp and dropped-step diagnostics.

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
