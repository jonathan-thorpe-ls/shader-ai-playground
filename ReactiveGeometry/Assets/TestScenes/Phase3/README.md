# Phase 3 - Fields, Operators and Debugging

The Phase 3 scenes demonstrate metadata operators, graph-derived deformation fields and
one-off debug inspection. All runtime UI uses UI Toolkit.

## Scenes

### `01_GraphDistance`

Select a source and apply the recipe. The shader colours the mesh by normalised graph
distance. Inspect the selected vertex to compare the displayed result with the numeric
distance, curvature and boundary values.

### `02_FieldComposition`

Enable the graph pulse and optional directional field, change its source/amplitude/width,
and apply the configuration. Pause, reset and inspect simulation diagnostics while the
field moves through the graph.

### `03_DebugInspection`

Use graph-distance or curvature colourisation, rest-topology wireframe, source inspection,
pause/reset and combined fields to examine the relationship between metadata and live
deformation.

## Manual checks

For each scene:

1. Enter Play mode and confirm the UI and geometry render.
2. Change the graph source, apply the recipe and inspect the source.
3. Change every field control that is visible in the scene.
4. Toggle debug colourisation and wireframe without changing simulation state.
5. Pause, reset and resume; confirm the simulation diagnostics remain finite.
6. Check the Console for operator, shader, buffer-lifecycle or UI errors.
