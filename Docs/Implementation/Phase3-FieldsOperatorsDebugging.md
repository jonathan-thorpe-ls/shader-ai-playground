# Phase 3 - Fields, Operators and Debugging

Implementation plan for the third phase of the GPU procedural-geometry framework described in [ProceduralGeometryFramework.md](../ProceduralGeometryFramework.md).

| Item | Decision |
| --- | --- |
| Engine | Unity 6.6 |
| Pipeline | URP 17.6 |
| Target project | `ReactiveGeometry/` |
| Phase 3 gate | One metadata-configured field runs on an icosphere and can be inspected through reusable debugging views |
| First operators | graph distance, min/max normalisation, remap, curvature, boundary and weighted combine |
| First field | a graph-distance pulse that contributes force through the shared simulation interface |
| UI for example/test scenes | UI Toolkit only (`UXML`, `USS`, `UIDocument`, C# event binding) |
| Testing | deterministic EditMode GPU tests plus PlayMode rendering and interaction checks |

---

## Implementation status

The Phase 3 vertical slice is implemented in `ReactiveGeometry/`:

- dynamic recipe-owned attributes with exact domain/format validation and buffer reuse;
- fill, remap, combine, graph-distance and graph-distance normalisation operators;
- reusable graph-pulse and directional force fields;
- deterministic external-force accumulation into the Phase 2 solver;
- GPU attribute colourisation;
- rest wireframe, vertex and normal debug overlays;
- explicit picking/inspection with curvature, boundary, graph-distance and GPU ownership data;
- three UI Toolkit-only Phase 3 scenes;
- EditMode operator/field tests and PlayMode rendering/lifecycle tests.

Current deliberate implementation limits:

- graph distance uses bounded full-graph relaxation dispatches rather than a compacted
  frontier; supported scene sizes are documented by the Phase 3 profiles before this is
  changed to a frontier implementation;
- picking reads explicit one-off snapshots after a user selection; it is not a per-frame
  GPU picker;
- debug topology snapshots are regenerated when the relevant overlay is enabled and are
  not updated every simulation frame;
- GPU timing and cross-backend baselines remain hardening data for Phase 9.

---

## 1. Purpose

Phase 2 established a stable, GPU-resident neighbour simulation. Phase 3 makes that simulation configurable through geometry metadata instead of geometry-specific solver code.

The phase is complete when:

- a source attribute or selected vertex drives a GPU graph-distance field;
- an operator recipe produces a normalised control attribute;
- a deformation field declares and validates its required attributes;
- the field contributes bounded force to the existing simulation;
- the active attributes, rest/live state, topology, sources and field result can be inspected;
- the same configured field can run on a different compatible geometry without solver changes;
- no production frame performs CPU readback.

Phase 3 adds the architectural layer used by later audio and XR systems. It does not add audio analysis, XR providers, new generators or production editor tooling.

---

## 2. Scope

### 2.1 In scope

1. A reusable attribute-operator contract with explicit inputs, outputs and domain validation.
2. On-demand allocation of recipe-produced and dynamic attributes.
3. GPU graph-distance computation over committed topology.
4. Min/max reductions and normalisation.
5. Fill, remap, combine and curve-evaluation operators.
6. Extraction of the existing curvature and boundary metadata into the operator/recipe workflow.
7. A serializable operator recipe and deterministic scheduling order.
8. Reusable deformation fields with declared attribute dependencies.
9. A shared external-force accumulation buffer consumed by `NeighbourSimulation`.
10. A graph-pulse field and a small directional test field.
11. URP debug draws for attributes, wireframe, vertices, normals and rest/live overlays.
12. One-off GPU picking and inspected-element diagnostics.
13. UI Toolkit test-scene controls and automated verification.

### 2.2 Explicitly out of scope

- FFT, RMS or onset analysis;
- audio mapping assets and parameter smoothing;
- XR/controller providers and gesture recognition;
- collision, volume, area, bend or positional constraints;
- topology modification during a field evaluation;
- new surface, tube, branching or particle generators;
- material-authoring workflows beyond a focused attribute-colourisation sample;
- per-frame production readback;
- a full Geometry Lab editor window.

The UI Toolkit operator panel is required for Phase 3 test scenes, but the phase does not promise a complete editor authoring product.

### 2.3 Dependencies and entry gates

Phase 3 starts from the Phase 2 contract:

- `GeometryResource` owns all GPU resources and lifecycle registration.
- `AttributeSet` provides descriptor and buffer lookup.
- `TopologyBuffers` provides deterministic CSR connectivity and edge access.
- `NeighbourSimulation` owns live position/velocity state and publishes to the mesh.
- `ProceduralMeshRenderer` updates bounds and publishes the rendered state.
- `GpuBufferRegistry` remains the lifecycle authority.

Phase 3 must preserve all existing Phase 1 and Phase 2 tests. Canonical generation output and committed topology remain immutable. Operators may create derived attributes; they must not rewrite rest geometry or connectivity.

---

## 3. Package and project layout

```text
ReactiveGeometry/Packages/com.sonosthesia.procgeo/
  Runtime/
    Attributes/
      AttributeDescriptor.cs
      AttributeSet.cs
      AttributeRecipe.cs
      AttributeRecipeRunner.cs
      AttributeStats.cs
      IAttributeOperator.cs
      Operations/
        FillOperator.cs
        RemapOperator.cs
        CombineOperator.cs
        GraphDistanceOperator.cs
        NormaliseOperator.cs
    Fields/
      IGeometryField.cs
      GeometryFieldContext.cs
      FieldContribution.cs
      FieldAccumulator.cs
      GraphPulseField.cs
      DirectionalField.cs
    Debug/
      GeometryDebugView.cs
      GeometryPicker.cs
      GeometryInspection.cs
    Shaders/Resources/ProcGeo/
      AttributeOps.compute
      GraphDistance.compute
      Reduce.compute
      FieldGraphPulse.compute
      FieldDirectional.compute
  Runtime.URP/
    Debug/
      GeometryDebugDrawRenderer.cs
      Shaders/
        ProcGeoAttributeDebug.shader
        ProcGeoLines.shader
        ProcGeoGlyphs.shader
  Samples~/Phase3/
    Scenes/
    UI/
  Tests/
    EditMode/
      AttributeOperatorTests.cs
      GraphDistanceTests.cs
      FieldCompositionTests.cs
      DebugLifecycleTests.cs
    PlayMode/
      FieldRenderingTests.cs
      PickingTests.cs
```

Interactive developer scenes remain in:

```text
ReactiveGeometry/Assets/TestScenes/Phase3/
```

Runtime and debug UI must use UI Toolkit. Do not add Canvas, RectTransform, TMP or programmatic `UnityEngine.UI` controls to examples or test scenes. Shared styles belong beside the Phase 3 UXML and should reuse the project test-scene panel where practical.

---

## 4. Attribute and recipe contract

### 4.1 Dynamic attributes

Extend `AttributeSet` so attributes can be:

- generation-owned and present at resource creation;
- recipe-owned and allocated once during recipe compilation;
- dynamic and allocated only when a field or debug view requests them.

Every allocated attribute keeps one `AttributeDescriptor` describing:

- stable `AttributeId`;
- domain and format;
- interpolation;
- lifetime;
- units;
- nominal/actual range;
- producer and expected update cadence.

Do not create a dynamic buffer merely because its ID exists in a global registry. Allocation follows a validated recipe or consumer dependency.

Attribute buffers remain owned by `GeometryResource` and are released through `GpuBufferRegistry`. Rebuilding a recipe with an identical schema reuses buffers; a shape or format change disposes and reallocates only the affected derived attributes.

### 4.2 Operator interface

```text
IAttributeOperator
  name
  required inputs: (AttributeId, domain, format)
  outputs: (AttributeId, domain, format, lifetime)
  parameters
  Validate(AttributeSet, GeometryLayout)
  Record(CommandBuffer, AttributeOperationContext)
  dirty rule
```

Validation is performed before the operator dispatch is scheduled. A missing attribute, incompatible domain, unsupported format or topology requirement fails with the operation name and expected contract; it must never be replaced with a zero/default buffer.

### 4.3 Operator recipe

An `AttributeRecipe` is an ordered, serializable list of operations. It contains:

- recipe version;
- ordered operation list;
- input overrides/mappings;
- output attribute declarations;
- parameters and curves;
- validation result;
- generation hash or schema version.

Recipe compilation is explicit and performed:

- after generation;
- when the recipe is edited;
- after a topology or domain shape changes.

Dirty static operations run at generation/reset. Dynamic operations run at the cadence declared by the producer. Recipe order is the dependency order; no operation may silently depend on a later output.

### 4.4 Statistics and ranges

Operators that need a range write a compact `AttributeStats` block on the GPU:

```text
minimum
maximum
mean
finiteCount
invalidCount
```

Reductions support explicit component selection. Debug UI and colourisation may read this block as a user-requested diagnostic. Field execution uses the GPU range only when its schedule explicitly runs the reduction; it must not synchronously read a CPU value.

---

## 5. Phase 3 operators

### 5.1 Graph distance

Add a GPU breadth-first operator over CSR topology.

Inputs:

- CSR offsets and neighbours;
- one or more source vertices or a Boolean source mask;
- edge mode filter.

Outputs:

- `GraphDistance` in edge hops or edge-rest-length units;
- `GraphDistance01` after explicit normalisation.

The source set belongs to the operation, not to the geometry generator. A visited distance of `UINT_MAX` remains unreachable and is handled explicitly before conversion to float. For deterministic tests, use a fixed number of expansion rounds bounded by `VertexCount - 1`.

Two paths are acceptable:

1. compacted frontier, with ping-pong vertex lists; or
2. iterative relax/changed-flag dispatches.

Select the first option if profiling shows repeated full-graph passes are dominant. Both paths must detect incomplete traversal and expose a convergence status.

### 5.2 Normalise

Normalise a scalar attribute from a declared source range or GPU min/max reduction:

```text
output = saturate((input - minimum) / max(maximum - minimum, epsilon))
```

If the source range is constant, write a documented deterministic value rather than divide by zero. Invalid inputs increment diagnostics and produce a finite fallback.

### 5.3 Remap and curves

Provide:

- linear remap between finite ranges;
- clamp and saturate modes;
- optional sampled `AnimationCurve`/texture conversion;
- component and channel selection.

Animation curves are serializable editor inputs but must be converted to immutable GPU-ready lookup data before runtime dispatch. Do not sample a CPU curve per frame.

### 5.4 Combine

Support deterministic combinations:

- weighted sum;
- multiply;
- minimum/maximum;
- blend by mask;
- per-component vector operations for supported formats.

The combine operator declares both its output domain and the output format. No implicit cross-domain broadcast is allowed without an explicit `RegionToVertex`, `EdgeToVertex` or similar operation.

### 5.5 Curvature and boundary

The current curvature and boundary values are already generated. Refactor them into reusable operator outputs where that clarifies ownership:

- curvature may be recomputed on rest or live geometry;
- boundary weight derives from committed topology;
- neighbour count remains a topology diagnostic;
- a graph `DistanceToBoundary` can use boundary vertices as graph-distance sources.

The Phase 3 refactor must retain the Phase 1 numeric results and descriptors.

---

## 6. Deformation fields

### 6.1 Field contract

A field is a declared mapping from geometry state, attributes and external parameters to a contribution. It does not own geometry-specific generation or simulation.

```text
IGeometryField
  id
  required topology capabilities
  required attributes
  optional attributes
  contribution mode: Force, Acceleration, VisualOffset, DynamicAttribute
  Validate(FieldBindContext)
  Record(FieldDispatchContext)
```

The default Phase 3 mode is `Force`. Direct `VisualOffset` support is allowed only for the test deformer/debugging path and must be clearly separated from physical simulation state.

### 6.2 Force accumulator

Add a GPU buffer, one float3 per vertex, that is owned by `LiveStateBuffers`:

```text
externalForces[vertex]
```

Before fields run, clear the accumulator. Each field atomically or deterministically adds one contribution for every vertex. The structural solver reads the accumulated external force in addition to spring/rest/damping terms.

The preferred implementation avoids atomics by binding each field to an independent contribution buffer and combining fields in a deterministic dispatch pass. Use atomics only if performance evidence justifies their use.

`NeighbourSimulation` remains topology-aware but field-agnostic. It reads:

- `ExternalForce`;
- the validated simulation parameters;
- the existing rest/topology data.

It must not know what a `GraphPulseField` is.

### 6.3 First field: graph pulse

The graph pulse field uses:

- a normalised graph distance;
- a rest normal;
- a pulse phase;
- amplitude;
- width;
- decay;
- optional deformation mask.

It emits force along the rest normal:

```text
falloff = exp(-distanceFromPulse² / width²)
force = restNormal * amplitude * falloff * mask
```

The field accepts an external phase updated by its controller. This is intentionally compatible with later audio envelope, onset and XR event sources without depending on them.

### 6.4 Directional field

Add a small field to demonstrate compatibility with vector metadata:

```text
force = direction * strength * mask
```

The direction may be a CPU-provided world/local vector or an existing directional attribute. This is the one field used by the Phase 3 manual test deformer and must respect transform space explicitly.

### 6.5 Composition and bounds

Field contributions are combined before simulation integration. A per-field weight and bounded total force prevent one field from hiding solver instability. Total force magnitude is tracked in the status block for diagnostics.

Simulation bounds remain conservative. Phase 3 fields must either reuse the configured simulation displacement margin or explicitly update the bound margin before a draw.

---

## 7. Debug and inspection system

### 7.1 Attribute colourisation

Add a debug material/renderer capable of binding an attribute by:

- attribute ID;
- component;
- scalar/vector/ID interpretation;
- manual or GPU-reduced range;
- ramp or direct colour mode.

IDs must use stable hashed colours rather than normalisation. Vector values may map XYZ to RGB. Missing attributes and incompatible formats must be reported at bind time.

The debug view must not copy attribute values to CPU. For mesh-bridge rendering, use a supported raw/structured binding strategy established by the platform spikes.

### 7.2 Topology views

Provide independent overlay toggles for:

- wireframe/edge lines;
- edge type;
- vertices;
- normals;
- tangents;
- region/primitive IDs;
- graph-distance rings;
- boundary vertices/edges.

Debug line and glyph data may be produced with procedural draws. This renderer remains separate from the production Lit material.

### 7.3 Rest/live comparison

Display:

- live shaded mesh;
- rest-wireframe overlay;
- displacement vectors;
- a selected attribute in the same viewport.

Overlays use conservative bounds and do not alter simulation state. The test scene must make the comparison optional so normal rendering can be captured without debug overlays.

### 7.4 GPU picking and inspection

Add a one-off pick operation for a screen ray:

- compute candidate distance to vertices/edges/regions;
- select by a packed deterministic minimum;
- read the selected ID and required scalar data only after the operation is explicitly requested;
- highlight the selection, one-ring neighbours and incident edges.

Picking is an event-driven diagnostic, not a per-frame production path. If the GPU/URP integration is not stable, the first milestone may use CPU-side ray/triangle selection in the test scene while retaining the GPU-picking spike as a tracked follow-up.

Inspection output includes:

- selected domain and ID;
- position/rest position;
- degree, boundary weight and curvature;
- configured scalar attributes;
- incident edges/regions;
- graph distance from the current source.

### 7.5 Ownership and performance report

Use `GpuBufferRegistry` to report:

- buffer label and owner;
- target, count, stride and bytes;
- live, static, topology, field and scratch categories;
- expected and unexpected allocation growth.

Show dispatch and GPU timing for:

- operator recipe;
- graph distance;
- field clear/accumulate/combine;
- neighbour integration;
- debug draw;
- final publish.

---

## 8. UI Toolkit test scenes

Create:

```text
ReactiveGeometry/Assets/TestScenes/Phase3/
  README.md
  Phase3Controls.uxml
  Phase3Controls.uss
  Phase3SceneController.cs
  01_GraphDistance.unity
  02_FieldComposition.unity
  03_DebugInspection.unity
```

All three scenes use `UIDocument`, `PanelSettings`, UXML and USS. Reuse the common test-scene visual language from Phases 1 and 2, but keep Phase 3 controls separate enough to bind recipe/field-specific controls without duplicating the base panel.

### 8.1 Graph distance

Demonstrate a source vertex and graph-distance rings.

Controls:

- source vertex;
- edge-distance units (hops/rest length);
- normalised/raw view;
- range mode;
- colour ramp;
- recompute.

Acceptance:

- adjacent vertices have the expected first ring;
- closed-geometry traversal has no unreachable vertices;
- the source can change without regenerating topology;
- visual and numeric output agree.

### 8.2 Field composition

Demonstrate graph pulse plus an optional directional field on the Phase 2 icosphere simulation.

Controls:

- pulse and directional enable toggles;
- amplitude, width and weight;
- source vertex;
- play/pause/reset/single-step;
- maximum contribution and clamp diagnostics.

Acceptance:

- the field effect changes with metadata without geometry-specific solver code;
- fields compose deterministically;
- disabling a field removes its contribution after reset/clear;
- the nominal preset recovers and remains finite.

### 8.3 Debug inspection

Demonstrate rest/live overlay, topology views, attribute colourisation and picking.

Controls:

- live view;
- debug attribute selector;
- vertices/edges/normals/regions toggles;
- rest overlay and displacement vectors;
- selected element readout;
- GPU buffer ownership summary.

Acceptance:

- one-off selection returns a deterministic element;
- the same attribute is shown by colour and inspection data;
- all debug modes can be disabled without affecting simulation;
- lifecycle transitions leave no debug resources registered.

---

## 9. Automated testing

### 9.1 EditMode GPU tests

| Test | Expected result |
| --- | --- |
| recipe validation | missing/incompatible attributes fail with a clear operation-specific message |
| graph distance from one source | source is 0, one-ring is 1, deterministic rings match a CPU BFS |
| graph distance from multiple sources | the minimum source distance is produced |
| unreachable/constant cases | finite documented fallback and convergence status |
| normalise known range | exact 0/1 endpoints and bounded interior values |
| curve remap | GPU values match a sampled CPU reference |
| combine deterministic | weighted operations match the declared order |
| curvature/boundary parity | refactored results match Phase 1 output |
| graph pulse force | normal direction, falloff and mask match a CPU reference |
| field composition | disabling/order/weight produces the expected finite accumulator |
| solver integration | structural-only mode matches Phase 2; field mode changes only force-driven state |
| dynamic attribute lifecycle | buffers allocate once and release on recipe/resource disposal |
| repeated recipe rebuild | no registry growth for unchanged schemas |

One-off readback is permitted in tests and explicit diagnostics. Production update, field and debug-draw paths must not call synchronous `GetData`.

### 9.2 PlayMode tests

Cover:

- recipe dispatch across multiple frames;
- field enable/disable/reset;
- field-induced deformation and recovery;
- finite dynamic normals;
- attribute debug-material rendering;
- picking a known projected vertex or stable test ray;
- lifecycle transitions and resource cleanup;
- scene UI event binding and displayed diagnostics.

Avoid visual assertions that depend on a nondeterministic game camera. Use stable state checks and one reference capture after the field reaches a known step or condition.

### 9.3 CPU references

Use small icospheres for:

- topology BFS;
- scalar normalisation and remap;
- graph-pulse contribution;
- field accumulation;
- one-step force integration.

Cross-backend bit identity is not required. Document tolerances per operator and preserve deterministic integer distance/topology results.

---

## 10. Milestones

### M0 - Operator and field spike

- Prototype one generic scalar operator dispatch.
- Extend `AttributeSet` for one recipe-owned attribute.
- Record a small graph distance and field contribution into explicit GPU buffers.

**Gate:** buffer ownership, dispatch order and format validation are proven without changing Phase 2 behaviour.

### M1 - Recipes and core operators

- Add recipe compilation and dependency validation.
- Implement fill, remap, combine, normalise, stats and dynamic allocation.
- Preserve existing curvature/boundary outputs.

**Gate:** deterministic operator tests and lifecycle tests pass.

### M2 - Graph distance

- Implement and optimise graph traversal.
- Add `DistanceToBoundary` and arbitrary source sets.
- Add CPU reference tests and unreachable/convergence diagnostics.

**Gate:** raw and normalised graph distances match CPU BFS.

### M3 - Composable fields

- Add the external-force accumulator.
- Integrate it with the neighbour solver.
- Implement graph pulse and directional fields.

**Gate:** Phase 2 solver parity remains intact with no field; field tests are finite and bounded.

### M4 - Debug rendering and picking

- Add attribute, topology, glyph and rest/live debug views.
- Add one-off inspection and picking.
- Verify every view works without changing production simulation state.

**Gate:** stable PlayMode captures and deterministic selection tests pass.

### M5 - UI Toolkit test scenes

- Complete the three Phase 3 scenes with shared UXML/USS controls.
- Add manual acceptance checklists and evidence captures.
- Record supported field counts, geometry sizes and display modes.

**Gate:** all controls are UI Toolkit, scenes run without manual setup, and reset restores the saved state.

### M6 - Performance and compatibility gate

- Profile icospheres at frequencies 4, 8 and 16.
- Compare graph traversal strategies and field batching.
- Run supported graphics backends and lifecycle suites.

**Gate:** supported costs, limits, memory and backend caveats are documented.

---

## 11. Validation commands

```powershell
unity command script recompile --project-path ReactiveGeometry
unity test ReactiveGeometry --mode EditMode --filter "Attribute;GraphDistance;Field;Recipe;Lifecycle"
unity test ReactiveGeometry --mode PlayMode --filter "Field;Debug;Picking;Rendering"
```

For each Phase 3 scene:

1. confirm the Editor compiles with no Console error;
2. enter PlayMode and verify frames advance;
3. exercise every operator/field preset;
4. select a known element and compare numeric/visual state;
5. reset and confirm derived attributes are rebuilt deterministically;
6. capture Game and Scene views with debug modes documented;
7. exit PlayMode and confirm no registered GPU buffers remain.

---

## 12. Performance and observability

Measure:

- source count and graph traversal rounds;
- graph distance dispatches and visited vertices;
- recipe operator count;
- bytes for derived attributes and statistics;
- field contribution buffers and peak total force;
- simulation extra cost with zero, one and multiple fields;
- debug draw primitive/vertex count;
- one-off picking latency;
- peak and steady-state GPU registry bytes;
- allocation churn during recipe edits and regeneration.

Use stable profiling labels such as:

- `ProcGeo.Attributes.Recipe`
- `ProcGeo.Attributes.GraphDistance`
- `ProcGeo.Fields.Accumulate`
- `ProcGeo.Fields.Combine`
- `ProcGeo.Debug.Draw`
- `ProcGeo.Debug.Pick`

Zero-field mode must retain a fast path close to Phase 2. Expensive reductions, picking readback and ownership reports remain explicit diagnostics.

---

## 13. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| recipes create unbounded buffer variants | derive a finite schema, reuse compatible buffers and test repeated rebuilds |
| graph traversal requires too many full-graph dispatches | compacted frontiers or changed-flag termination, then profile both |
| invalid attribute combinations produce silent fallback values | validate domain/format dependencies before scheduling and fail clearly |
| multiple fields rely on nondeterministic accumulation | combine declared contribution buffers in recipe order |
| field forces destabilise Phase 2 | bounded contribution, existing clamps, CPU-reference integration and phase-parity mode |
| direct position deformations bypass the solver | permit only an explicit visual/test contribution mode |
| per-frame debug readback stalls the GPU | one-off diagnostic reads only; debug drawing consumes GPU buffers directly |
| picking and debug draws become coupled to generation | separate `GeometryPicker` and debug renderers from generator ownership |
| attribute colourisation requires unsupported buffer layouts | retain a debug-copy kernel spike and report backend limitations |
| test scenes drift toward multiple UI systems | require UI Toolkit and reuse a shared UXML/USS base |
| large debug overlays dominate frame cost | each overlay is independently toggleable and excluded from production paths |

---

## 14. Definition of done

Phase 3 is complete only when:

1. operators and recipes are domain/format validated;
2. graph distance and normalisation match deterministic CPU references;
3. a configured field runs through one generic force accumulation path;
4. the Phase 2 solver is unchanged when no field is enabled;
5. composed fields remain finite and bounded at documented settings;
6. attributes, topology, rest/live state and selected elements can be inspected;
7. no field or debug production frame performs CPU readback;
8. all recipe/field/debug resources dispose through `GpuBufferRegistry`;
9. Phase 1 and Phase 2 tests remain green;
10. the three Phase 3 test scenes use UI Toolkit exclusively;
11. each scene opens without manual setup and documents manual acceptance checks;
12. supported sizes, performance, backend limitations and visual evidence are recorded.
