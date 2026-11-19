# Phase 4 test scenes

These scenes use component inspectors as their interface. Open a scene, select
the named target or source GameObject, and edit its `ProceduralMeshRenderer`,
`NoiseDeformationSource`, or `ImpulseWaveSource` component in the Inspector.
The custom inspectors intentionally provide only functional UI Toolkit controls
and validation; no runtime HUD is required.

## Scenes

- `01_ParametricGallery`: plane, sine, saddle, twist, cylinder, torus and Möbius
  surface generators.
- `02_KnotAndFrames`: a `(2, 3)` torus-knot tube generated with transported
  cross-section frames.
- `03_NoiseDeformation`: a movable source marker driving spatial simplex or
  Voronoi deformation on a target grid.
- `04_ImpulseAndWaveSources`: movable source markers with inspector buttons for
  triggering impulses, waves and resetting active events.

Regeneration deliberately occurs only from the target inspector. Continuous
deformation, source movement and event triggering reuse allocated GPU buffers.
