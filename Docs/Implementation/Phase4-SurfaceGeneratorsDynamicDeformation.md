# Phase 4 - Surface Generators and Dynamic Deformation

## 1. Purpose

Phase 4 makes the procedural framework generator-independent and adds the first
spatially authored deformation workflow. It is a performance-focused real-time
system: generation, deformation, simulation and rendering are designed for
interactive frame budgets, bounded GPU work and predictable memory, rather than
offline accuracy or visual-only evaluation. It must:

1. generate a family of regular parametric surfaces and tube meshes;
2. include closed tubes around torus knots;
3. exercise existing simulation and field infrastructure without
   icosphere-specific paths;
4. provide temporally varying simplex and Voronoi deformations;
5. localise deformation with movable world-space centres and controlled
   falloff;
6. let scene authors trigger impulses and waves from inspector controls;
7. preserve deterministic topology, GPU ownership, stability and rendering.

This phase is complete when a grid, standard wrapped shapes and a torus knot can
all be generated, deformed, inspected and rendered through the same runtime
contracts.

## 2. Scope

### 2.1 In scope

1. A runtime `IGeometryGenerator` contract and a renderer that accepts any
   compatible generator.
2. A reusable regular-grid topology plan.
3. Parametric plane, sine, saddle, twist, cylinder, torus and Möbius shapes.
4. Rotation-minimising tube generation for sampled or analytic curves.
5. Line, helix, circle, Lissajous and configurable `(p, q)` torus-knot curves.
6. Curve-length, cross-section, boundary and seam metadata.
7. Simplex and Voronoi deformation fields evaluated on the GPU.
8. Temporal noise controls, including speed, phase, seed and octave controls.
9. Source-centred spatial falloff with transform-aware inspector authoring.
10. A bounded active-event model for impulses and radial waves.
11. Inspector buttons for triggering events and Scene gizmos for source editing.
12. Shared automated tests and focused interactive scenes.

### 2.2 Explicitly out of scope

- Audio analysis or audio mappings; these move to Phase 5.
- Branching graphs, particle networks or cell/Voronoi *geometry* generation.
  Voronoi in this phase is a deformation function only.
- Ray-marched, SDF or implicit surfaces.
- Boolean operations, remeshing or adaptive tessellation.
- Character skinning, collisions, constraints or fluid simulation.
- A general visual-scripting system for fields.
- XR/provider interaction.
- Dedicated runtime control panels. Phase 4 test scenes expose controls through
  functional component inspectors in the Unity Editor, with UI Toolkit used for
  custom inspectors where needed.

### 2.3 Entry gates

Phase 4 starts from the implemented Phase 1-3 contracts:

- `GeometryResource` owns generation, topology, attributes and live state.
- `NeighbourSimulation` consumes the external-force buffer and has no knowledge
  of individual fields.
- `IGeometryField` validates dependencies and records GPU contributions.
- Phase 3 graph operators and debug views remain available.
- GPU buffer lifecycle is tracked centrally.
- Existing icosphere generation, simulation and Phase 3 tests remain green.

The Phase 4 generator work must not regress the existing icosphere path while
removing its hard-coded assumptions from the renderer.

## 3. Design decisions

1. **Generation is replaceable.** Scene components configure serializable
   generator settings and create an `IGeometryGenerator`; simulation, fields and
   rendering consume only `GeometryResource`.
2. **Topology is committed once.** Deformation never changes vertex count,
   rendering indices, seam welding or connectivity.
3. **A knot is a closed tube surface, not a line-only primitive.** The curve
   drives ring positions and frames; `(u, v)` indices define a welded,
   closed-in-both-directions mesh.
4. **Noise is generic input, not a generator.** The same noise fields operate on
   all generated vertex domains with the required attributes.
5. **Transform positions define field origins.** A source component supplies a
   stable world-space centre every dispatch; the target transforms it to its
   local space before binding it to the compute shader.
6. **Continuous and event deformation are separate.** Noise is time-continuous;
   impulses and waves use bounded event records so they are not inferred from
   frame-to-frame UI state.
7. **The physical solver is authoritative.** Phase 4 deformation normally
   contributes force or acceleration. A render-only offset mode may remain a
   diagnostic option but cannot be the basis for acceptance.
8. **Runtime event counts are bounded.** Fixed-capacity records avoid resizing,
   GPU allocations and unbounded work during play.
9. **Real-time cost is part of correctness.** New algorithms require dispatch,
   frame-time and memory measurements against supported topology presets.
   Added visual flexibility does not justify per-frame readback, allocation,
   topology rebuilding or unbounded iteration.

## 4. Generator contract and package layout

Refactor the current icosphere-specific ownership without changing the canonical
buffers:

```text
Runtime/
  Generators/
    IGeometryGenerator.cs
    IcosphereGenerator.cs
    ParametricSurfaceGenerator.cs
    ParametricSurfacePlan.cs
    ParametricSurfaceSettings.cs
    TubeGenerator.cs
    TubePlan.cs
    TubeSettings.cs
  Parametrics/
    CurveDefinition.cs
    SurfaceDefinition.cs
    ParallelTransportFrameBuilder.cs
  Fields/
    SpatialFieldSource.cs
    NoiseDeformationField.cs
    ImpulseDeformationField.cs
    RadialWaveDeformationField.cs
    DeformationEventPool.cs
  Editor/
    Generators/
      ProceduralMeshRendererEditor.cs
      ParametricSurfaceSettingsElement.cs
    Deformation/
      NoiseDeformationSourceEditor.cs
      ImpulseWaveSourceEditor.cs
  Authoring/
    DeformationSource.cs
    NoiseDeformationSource.cs
    ImpulseWaveSource.cs
  Shaders/Resources/ProcGeo/
    ParametricSurface.compute
    Tube.compute
    NoiseFields.compute
    EventFields.compute
Tests/
  EditMode/
    ParametricGeneratorTests.cs
    TubeGeneratorTests.cs
    NoiseFieldTests.cs
    DeformationEventTests.cs
  PlayMode/
    GeneratorRenderingTests.cs
    SpatialFieldRenderingTests.cs
```

Introduce an interface matching the framework contract:

```text
IGeometryGenerator
  GeometryLayout Plan()
  void Record(CommandBuffer command, GeometryResource target)
```

`ProceduralMeshRenderer` becomes a host for generated geometry rather than an
icosphere component. Preserve convenience APIs during the refactor, but move
icosphere settings behind a selected generator configuration. Regeneration
rebuilds simulation, fields, graph-derived attributes, normals and bounds in one
lifecycle path.

## 5. Parametric grid and surface generation

### 5.1 Shared regular-grid plan

All regular surfaces use deterministic U/V indexing:

```text
id(u, v) = v * uVertexCount + u
```

The plan describes:

- segment counts;
- whether U and/or V are wrapped;
- duplicate or welded seam policy;
- triangle orientation;
- boundary vertex sets;
- optional U/V diagonals and edge types;
- region assignment.

Closed axes weld the first and last vertices where practical. The mesh builder
must never introduce a physical seam when the mathematical surface is closed.
Primitive winding must point outward for analytic volume-like surfaces and use
the documented U-cross-V convention for open surfaces.

Vertex attributes:

- `LocalCoord` as `float2` in the normalised parameter domain;
- `RestNormal`;
- `RestTangent` along U;
- `Direction` along V or the intended structural direction;
- `BoundaryWeight`;
- `RegionId`;
- `Mass` and optional `DeformationWeight`.

Primitive or edge attributes:

- U/V cell region;
- seam edge type;
- structural versus diagonal edge type;
- edge rest length.

### 5.2 Grid and analytic shapes

Implement in this order:

| Shape | U/V interpretation | Closures and validation |
| --- | --- | --- |
| Plane | centred width/depth rectangle | open, four boundaries |
| Sine | plane displaced by configurable frequency/phase | open, finite derivatives |
| Saddle | hyperbolic paraboloid | open, expected symmetry |
| Twist | rectangle strips with rotation over U | open, finite near twist axis |
| Cylinder | angle U, height V | closed U, two boundary rings |
| Torus | major angle U, minor angle V | closed U and V |
| Möbius | angle U, cross-width V with half twist | globally closed seam, non-orientable material case |

The generator uses analytic position and derivative functions. Normal and
tangents are generated from `dP/dU` and `dP/dV`; finite-difference fallback is
only for explicitly sampled custom curves.

Möbius strip acceptance must explicitly account for orientation and seam normal
reversal. It may use mesh geometry while being excluded from simulations that
require a globally consistently oriented closed volume.

### 5.3 Tube grid

A tube is a regular V/U parameterization:

- U: distance or sample index along the source curve;
- V: angle around the curve ring.

Generate each ring from a rotation-minimising frame. Use the previous ring
normal to parallel transport; at the seam for a closed curve, compute a frame
twist correction and distribute it across rings so the end frame matches the
start frame for zero unintended twist.

Support:

- open line;
- helix;
- circle;
- Lissajous curve;
- torus knot defined by coprime integer parameters `(p, q)`.
- optional sampled Catmull-Rom curve after the analytic cases are stable.

Expose a torus-knot preset of `(2, 3)` (trefoil), plus independently editable
`p`, `q`, major radius, minor radius and tube radius. Validate coprime values and
document the visual/self-intersection behaviour of unsupported parameter pairs.

Tube attributes include:

- normalised curve distance `u`;
- ring angle `v`;
- longitudinal `Direction`;
- rest tangent;
- centreline/normal data required by deformation;
- boundary/cap weights;
- curve or branch region ID.

Open tubes emit end caps as an explicit option; closed tubes weld the first and
last ring. Knot simulation topology must be closed along U and V.

## 6. Continuous noise deformation

### 6.1 Field contract

Add a `NoiseDeformationField` that satisfies `IGeometryField` and contributes
force. It requires:

- `RestPosition`;
- `RestNormal`;
- optional `DeformationWeight`.

The field computes:

```text
samplePosition = restPosition * spatialFrequency + timeOffset
noise = simplex(...) or voronoi(...)
falloff = spatialFalloff(distance(restPositionWorld, sourceWorld))
force = restNormal * amplitude * mappedNoise * falloff * weight
```

Time is supplied by a bounded CPU parameter, not by reading a scene clock in
the shader. The source centre is transformed into target local space once per
record.

### 6.2 Noise modes

Implement at least:

- 3D simplex noise;
- 3D Voronoi F1 value;
- Voronoi edge distance.

Use a compact shared noise include and a fixed mapping contract:

- `Signed`: [-1, 1] for positive/negative displacement;
- `Offset`: [0, 1] for non-negative expansion;
- `Abs`: absolute centred magnitude.

Controls:

- seed/offset;
- spatial frequency and optional axis scale;
- temporal speed and time phase;
- amplitude;
- octaves;
- lacunarity;
- gain;
- normalised output mode;
- force weight.

Cap octave count to a documented small range. Validate frequencies, finite
numbers, seeds and amplitude before dispatch. Deterministic output means the
same seed, phase and rest position produce the same CPU/GPU-tested result within
a documented tolerance, not cross-platform bit equality.

### 6.3 Spatial centre and falloff

Every spatial source provides:

- world-space centre from its `Transform`;
- radius or inner/outer radius;
- falloff mode: constant, inverse, smoothstep or custom curve;
- local/world transform interpretation;
- enable state and weight.

Prefer inner/outer radius parameters over ad-hoc exponential constants. The
result must be exactly zero at or beyond the outer radius and clamped to `[0,
1]`. A source outside the geometry or a zero-size falloff must be valid and
simply contribute no force.

Multiple fields use independent source components and contribution buffers.
Their results enter the deterministic field accumulator; no atomics are used
unless a measured bottleneck justifies a later change.

## 7. Impulses and waves

### 7.1 Fixed-capacity event records

Add one GPU structured buffer of bounded active events. A separate event pool
owns CPU ring-buffer records, sequence IDs and expiration:

```text
Impulse/Wave event:
  sequence id
  world/local centre
  start simulation time
  amplitude
  radius or wave speed
  width/duration
  direction mode
  active state
```

Default capacities are conservative and visible in diagnostics. A full event
pool must log an explicit warning and define whether the oldest event is
replaced or the new event is rejected; use replacement for interactive scenes
but document it.

### 7.2 Impulses

An impulse is a one-shot contribution to velocity or a single bounded force
dispatch. It is emitted only once for each event sequence. Spatial falloff is
applied from the source centre, and the direction can be:

- outward;
- target rest normal;
- source forward;
- fixed inspector direction.

The event remains visible in the source history for debugging after its GPU
application, but cannot be applied again when simulation steps are repeated.

### 7.3 Waves

A radial wave stores a start time, speed, width and attenuation. At each field
dispatch:

```text
waveDistance = speed * elapsed
band = gaussian or compact pulse(distance(point, centre) - waveDistance, width)
amplitude = sourceAmplitude * attenuation(elapsed, distance) * band
```

Use compact support to avoid an entire mesh receiving near-zero work. The event
expires deterministically when elapsed exceeds duration. Controls include
frequency/phase shape, repeat count and force direction.

Continuous oscillating waves may be represented as repeated events only if the
event budget and behaviour are explicit. Otherwise expose a continuous radial
wave field with a loop toggle.

### 7.4 Time and reset semantics

Fields use `SimulationScheduler` time or another monotonic simulation-time
counter, not display frame count. Reset clears velocities, fields and events;
pause freezes event elapsed time; regeneration invalidates all events associated
with the old resource.

## 8. Authoring components and inspectors

The Phase 4 user interface is intentionally inspector-first. Test scenes are
configured by selecting the target or source GameObject and using its component
inspectors; they do not need a separate HUD or runtime control panel. Use a
small, functional UI Toolkit custom inspector where a button, grouped section,
validation message or event action improves usability. Keep these inspectors
plain: ordinary fields, foldouts, labels and buttons with minimal or no custom
styling. Do not spend Phase 4 on visual polish or a reusable showcase UI.

Where the default serialized-field Inspector is already sufficient, no custom
Inspector is required. Any custom Inspector still maps directly to serialized
component state and public component methods; it must not introduce a second
configuration model.

### 8.1 Generator authoring

Add serializable generator settings with safe inspector defaults:

- generator type;
- segment counts;
- dimensions, scale and radii;
- analytic shape parameters;
- seam/wrapping options;
- caps;
- curve type;
- knot `p`, `q` and radii.

The inspector prevents invalid capacity counts before regeneration and displays
the resulting vertex, primitive and edge counts. Regeneration must be explicit
enough to avoid allocation churn while dragging high-frequency values; use
deferred or explicit regeneration where necessary.

### 8.2 Source authoring

Use simple scene objects as source markers. Suggested components:

- `NoiseDeformationSource`: noise mode and temporal/spatial parameters;
- `ImpulseWaveSource`: impulse and wave parameters plus event actions;
- `DeformationSourceGizmo`: radius, falloff and direction visualisation.

The source transform is the spatial centre and can be moved, rotated and scaled
with standard Scene view gizmos. `OnDrawGizmosSelected` draws:

- centre marker;
- source forward;
- inner/outer falloff radii;
- active wave fronts;
- recent impulse direction.

The `ImpulseWaveSource` inspector includes these action buttons:

- `Trigger Impulse`;
- `Trigger Wave`;
- `Reset Active Events`;
- optional `Impulse + Wave`.

Use Editor-time commands only for authoring actions; the same public C# methods
remain callable from scripts and tests. Custom inspectors use Unity UI Toolkit
(`UIElements`/`UI Toolkit` Editor code and, only when useful, minimal UXML/USS).
Do not add Canvas, TMP or `UnityEngine.UI` controls. Phase 4 test scenes should
not depend on a `UIDocument`, panel settings or runtime UI controller solely to
operate their components.

## 9. Runtime integration

Per-frame and fixed-step order:

1. collect enabled source transforms and parameter snapshots;
2. validate and convert world-space source data to geometry local space;
3. update or expire event records;
4. clear the external-force accumulator;
5. dispatch continuous fields;
6. dispatch active impulse/wave contributions;
7. deterministically combine contributions;
8. execute fixed simulation substeps;
9. rebuild dynamic normals and publish bounds;
10. draw the geometry.

The renderer must not search or discover fields per frame in an allocating way.
Source registration is stable and rebuilt only when scene references change.
Generation remains an explicit event and never occurs as a side effect of field
updates.

Bounds use the same conservative margin as Phase 3, enlarged by maximum force
or documented displacement assumptions only when required. No per-frame GPU
readback is allowed.

## 10. Inspector-driven test scenes

Create:

```text
ReactiveGeometry/Assets/TestScenes/Phase4/
  README.md
  01_ParametricGallery.unity
  02_KnotAndFrames.unity
  03_NoiseDeformation.unity
  04_ImpulseAndWaveSources.unity
```

Use named target and source GameObjects with primitive sphere/capsule marker
meshes at the origin of each source. Their mesh representation is only a visual
marker; the field centre is always the transform position. Each scene has no
required runtime panel. Select the relevant GameObject, then parameterise it and
invoke actions from its component inspector.

### 10.1 Parametric gallery

Show representative sizes of:

- plane;
- sine;
- saddle;
- twist;
- cylinder;
- torus;
- Möbius strip.

Enable rest/live comparison, normals and edge view. Acceptance:

- each shape regenerates from inspector settings;
- seam and boundary highlighting matches the mathematical closure;
- counts, normals, winding and topology validation agree;
- switching shapes disposes the previous GPU resources.

### 10.2 Knot and frames

Show a torus-knot tube with a visible centreline and frame glyphs.

Expose:

- curve type;
- knot `p` and `q`;
- major/minor radius;
- tube radius;
- longitudinal and radial segments;
- cap policy;
- frame twist;
- wireframe and normal views.

Acceptance:

- `(2, 3)` trefoil is closed and self-consistent at the seam;
- rings have finite orthonormal frames;
- changing `p` or `q` updates topology only through regeneration;
- the first and last welded rings do not duplicate vertices or indices.

### 10.3 Noise deformation

Place one target grid and one simplex/Voronoi source marker. The target may also
switch to torus or knot to prove generator independence.

The source inspector exposes noise type, seed, frequency, speed, amplitude,
octaves, output mapping, radius and falloff. Scene-view movement must immediately
move the deformation centre. Acceptance:

- pause freezes the same noise state;
- changing seed produces deterministic changes;
- moving outside the mesh falloff region removes the field effect;
- simplex and Voronoi remain finite at maximum documented settings;
- reset returns the target to rest.

### 10.4 Impulse and wave sources

Place two source markers over a grid or cylinder:

- one impulse source;
- one radial-wave source.

The inspector provides trigger buttons and all event parameters. Acceptance:

- each impulse affects the target only once;
- waves move visibly outward from the marker at the configured speed;
- moving the source changes future events without teleporting active events;
- pause freezes active waves;
- reset expires all events;
- source scene objects remain movable with standard gizmos.

## 11. Automated testing

### 11.1 Generator tests

EditMode GPU tests cover:

- exact vertex, primitive and edge counts for each shape;
- U/V index mapping and wrapped seam welds;
- no duplicate unintended edges;
- manifold and boundary expectations;
- consistent winding and finite analytic normals;
- plane/sine/saddle/twist symmetry and closed-form sample positions;
- cylinder and torus closure;
- Möbius seam policy and orientation flag;
- open caps and closed tube closure;
- torus-knot trefoil sample positions and no seam gap;
- resource disposal and repeated regeneration.

### 11.2 Field and event tests

Add tests for:

- simplex/Voronoi determinism for fixed seed and phase;
- valid signed/offset/absolute ranges;
- temporal phase changing the sampled deformation;
- octave and axis-scale limits;
- inner/outer radius and exact zero beyond the outer radius;
- world-to-local source transformation;
- one-shot impulse sequence consumption;
- event pool overflow policy;
- radial wave position at known elapsed times;
- event expiration and reset;
- pause-time semantics;
- finite force at maximum documented settings.

### 11.3 Shared compatibility tests

Run every new generator through:

- topology validator;
- attribute processor;
- graph-distance operator where applicable;
- neighbour simulation rest and impulse tests;
- normal recomputation;
- URP render bridge.

Add a test that uses the same deformation field on an icosphere, grid and knot
without generator-specific branches.

### 11.4 PlayMode tests

Cover:

- shape and knot generation without shader/console errors;
- scene gizmo/source parameter changes reaching the GPU field;
- source motion changing local deformation but not topology;
- impulse fired by the public trigger API;
- wave front progression over controlled simulation time;
- reset and regeneration lifecycle;
- representative editor captures for gallery, knot and deformation scenes.

## 12. Milestones

### M0 - Generic generator host

- Introduce `IGeometryGenerator`.
- Refactor `ProceduralMeshRenderer` to consume selected generator plans.
- Preserve the icosphere API and make its tests pass through the generic path.

**Gate:** no Phase 1-3 regression, and the renderer has no direct icosphere
branch in simulation or field recording.

### M1 - Parametric grid and open surfaces

- Implement regular-grid planning, plane, sine, saddle and twist.
- Add analytic normals, local coordinates, boundaries and test scenes.

**Gate:** count, index, winding, topology, simulation and render tests pass.

### M2 - Wrapped surfaces

- Add cylinder, torus and Möbius.
- Prove seam welding and boundary/closure metadata.

**Gate:** wrapped topology is validated and all resources regenerate cleanly.

### M3 - Curves and knot tubes

- Add parallel-transport frames and tube topology.
- Add line, helix, circle, Lissajous and `(p, q)` torus knot.
- Add caps and closed-loop frame correction.

**Gate:** a `(2, 3)` trefoil passes finite-frame, closure, topology and rendering
tests.

### M4 - Spatial noise fields

- Add simplex and Voronoi GPU functions.
- Add temporal controls, centre transforms, falloff and masks.
- Add the noise deformation test scene.

**Gate:** both noise modes are deterministic, finite, spatially bounded and
generator-independent.

### M5 - Events and authoring controls

- Add bounded impulse/wave records and source inspectors.
- Add Scene gizmos and trigger/reset actions.
- Add the event source scene.

**Gate:** impulse-once and wave-propagation tests pass; simple UI Toolkit
component inspectors expose all required parameters and actions, and those
actions use the same API as scripted calls.

### M6 - Compatibility and performance gate

- Exercise all shapes with fields and simulation.
- Capture representative scenes.
- Measure generation, dispatch, frame and memory costs.
- Document conservative size limits and unsupported cases.

**Gate:** all Phase 4 acceptance tests, existing suites, lifecycle checks and
documented performance budgets pass.

## 13. Validation and performance

Suggested editor validation commands should be kept current with the project:

```text
Unity -batchmode -projectPath ReactiveGeometry -runTests -testPlatform EditMode
Unity -batchmode -projectPath ReactiveGeometry -runTests -testPlatform PlayMode
```

Record for representative small, medium and large cases:

- generation dispatch count and completion time;
- vertex/edge/primitive counts;
- knot frame-generation cost;
- continuous field and active-event dispatch cost;
- memory per vertex and event pool capacity;
- fixed simulation steps and dropped time;
- maximum finite force and displacement at documented presets.

Prefer a medium preset that remains interactive on the minimum supported
platform. Increasing segments should not require shader recompilation or
per-frame GPU allocation. Feature acceptance includes meeting the documented
real-time frame budget, not only producing the expected shape or deformation.
When quality controls increase work, expose bounded presets or LOD rather than
allowing unchecked cost growth.

## 14. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Icosphere-specific renderer ownership leaks into later work | extract `IGeometryGenerator` and run existing tests through the shared host first |
| Wrapped seams create duplicate simulation vertices | define U/V weld policy in the plan and validate edge/position closure explicitly |
| Parallel transport flips near curve inflection points | detect degenerate tangents, use documented fallback axes and test limiting cases |
| Closed knot frames have residual twist at the seam | use closed-loop twist correction and validate first/last frame alignment |
| Möbius orientation conflicts with normal recomputation | mark orientation semantics in geometry flags and restrict volume/closed-surface assumptions |
| Noise functions disagree visually across shader backends | use canonical test vectors and tolerances, and avoid backend-branch shortcuts |
| Excessive noise octaves reduce frame rate | cap octave count and publish measured presets |
| World/local source transforms drift with target scaling | define and unit-test target-space conversion |
| Events apply repeatedly across fixed substeps | consume impulses by sequence ID at event dispatch, independent of solver step count |
| Event records grow without bound | use fixed capacity with a documented overflow policy |
| Wave/impulse force destabilises simulation | clamp contribution, preserve substeps and test worst documented presets |
| Inspector and component behaviour diverge | inspectors call the same public component APIs covered by tests |

## 15. Definition of done

Phase 4 is complete when:

1. every planned parametric shape and a `(2, 3)` torus knot generate through the
   generic `IGeometryGenerator` path;
2. seam, closure, winding, normals and boundaries are validated for each
   generator;
3. the same simulation and field stack operates on the icosphere, grid, wrapped
   surfaces and knot tube;
4. simplex, Voronoi F1 and Voronoi edge modes are temporally variable,
   transform-centred and spatially attenuated;
5. impulse and wave controls apply bounded events exactly according to their
   documented time and reset semantics;
6. scene source objects can be moved with standard gizmos and parameterised or
   triggered from simple, functional UI Toolkit component inspectors;
7. Phase 1-3 suites remain green;
8. EditMode and PlayMode Phase 4 tests cover generators, fields, events and
   lifecycle;
9. no per-frame GPU readback or unbounded GPU buffer allocation occurs;
10. all four test scenes run without runtime UI setup or console errors;
11. representative captures, performance data, limits and public authoring
    controls are documented;
12. supported presets meet the documented real-time frame budget with bounded
    dispatch, memory and allocation behaviour;
13. audio analysis remains absent and is left cleanly for Phase 5.
