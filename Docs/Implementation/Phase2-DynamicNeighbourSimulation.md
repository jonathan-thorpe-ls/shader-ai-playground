# Phase 2 - Dynamic Neighbour Simulation

## 1. Purpose

Phase 2 adds a reusable, GPU-resident simulation pass to the completed Phase 1 geometry pipeline. The first supported geometry remains the icosphere: it already provides deterministic rest positions, triangle incidence, canonical edges and CSR neighbour connectivity.

The phase is complete when a rendered icosphere can:

- receive a local impulse;
- propagate that disturbance through committed graph neighbours;
- return towards its configured rest shape;
- remain finite and bounded at the documented parameter extremes;
- recompute its dynamic normals without per-frame CPU readback;
- be exercised interactively in the Phase 2 test scenes.

This phase does not add generic deformation fields, audio control, interaction providers, picking, or new geometry generators. It intentionally establishes only the simulation contract those systems will consume later.

---

## 2. Scope

### 2.1 In scope

1. GPU-owned position and velocity ping-pong state.
2. Fixed-timestep accumulation with bounded substeps.
3. Structural spring, restoring-to-rest and damping forces.
4. Per-vertex semi-implicit Euler integration over CSR topology.
5. Configurable maximum displacement and maximum velocity.
6. Numeric validity flags and simulation statistics.
7. Dynamic normal recomputation after the final simulation substep.
8. An explicit one-shot impulse API for tests and sample scenes.
9. Correct generation, resize, reset, disable and disposal lifecycle.
10. EditMode GPU tests, PlayMode graphics/stability tests and interactive test scenes.

### 2.2 Explicitly out of scope

- XPBD as the primary solver;
- audio analysis or audio mappings;
- VR/controller input;
- metadata-driven fields and operators;
- graph-distance visualisation or GPU picking;
- bending, shear and directional edge recipes beyond reserved interfaces;
- topology modification during simulation;
- collision/volume/area constraints;
- additional surface, graph or particle generators.

`EdgeType` remains reserved for later spring types. Phase 2 writes and reads only the structural-edge subset.

### 2.3 Dependencies and exit gates

Phase 2 may start from the completed Phase 1 contract:

- `GeometryResource` owns GPU buffers and releases them through `GpuBufferRegistry`.
- `AttributeSet` exposes `RestPosition`, `RestNormal`, `Mass`, and topology metadata.
- `TopologyBuffers` exposes CSR offsets, neighbours, neighbour-edge IDs, edges and rest lengths.
- `ProceduralMeshRenderer` owns the final dynamic mesh buffers.
- `GeometryCommandScheduler` owns command-buffer ordering.

Phase 2 must not change canonical positions, topology, counts, or Phase 1 determinism tests. Existing generation data stays immutable after generation; live simulation state is separate.

---

## 3. Runtime data contract

### 3.1 Immutable rest data

The following buffers are read-only to the solver:

| Data | Owner | Domain | Purpose |
| --- | --- | --- | --- |
| rest position | `AttributeSet` | vertex | reconstruction force and displacement clamp |
| rest normal | `AttributeSet` | vertex | initial normals and directional future fields |
| mass | `AttributeSet` | vertex | acceleration scaling |
| neighbour offsets | `TopologyBuffers` | vertex + 1 | CSR row boundaries |
| neighbours | `TopologyBuffers` | directed neighbour | graph traversal |
| neighbour edge | `TopologyBuffers` | directed neighbour | rest-length/edge lookup |
| edge rest length | `TopologyBuffers` | edge | spring extension |
| edge type | `TopologyBuffers` | edge | structural-edge filtering |

### 3.2 Live state buffers

Extend `LiveStateBuffers` with:

```text
positions[0]       Structured float3, vertex count
positions[1]       Structured float3, vertex count
velocities[0]      Structured float3, vertex count
velocities[1]      Structured float3, vertex count
forces             Structured float3, vertex count (optional scratch)
simulationStatus   Structured SimulationStatus, one element
```

The renderable mesh stream is not a simulation ping-pong target. It is copied from the current position buffer during the final publish pass together with dynamic normals. This prevents renderer ownership and solver ownership from coupling.

`PreviousPositions` from Phase 1 is retained for later motion vectors. During Phase 2 it must either be explicitly refreshed from the pre-step position or remain unused and documented as reserved.

All buffers are registered in `GpuBufferRegistry`, are dimensioned from `GeometryLayout.VertexCount`, and are disposed with `GeometryResource`.

### 3.3 Simulation parameters

Add a serializable, value-only settings type:

```text
fixedDeltaTime       1/120 ... 1/30 seconds
maximumSubsteps      1 ... 8
maximumCatchUpTime   0.25 seconds
stiffness            structural spring coefficient
restoringStiffness   restoring-to-rest coefficient
damping              velocity-proportional damping
maximumVelocity      finite positive m/s
maximumDisplacement  finite positive distance
maximumInwardDisplacement  maximum inward normal displacement
paused
```

Defaults must be selected by testing a unit-radius icosphere at frequencies 4, 8 and 16. Validate and clamp in an explicit `SimulationParameters.Validate(...)` method. Invalid values must be reported to the component rather than silently converted during every frame.

Runtime parameter edits regenerate neither geometry nor topology. They affect the next simulation step.
Inward displacement is measured along the rest normal and approaches its
configured hard maximum through a smoothstep transition beginning at half the
maximum. This keeps the control to one intuitive distance while avoiding an
abrupt velocity reversal at the limit. Zero disables the limit. Outward and
tangential displacement are unchanged.

### 3.4 Solver status

Store a compact status block on the GPU and read it only for explicit diagnostics or tests:

```text
invalidValueCount
clampedVelocityCount
clampedDisplacementCount
maximumSpeed
maximumDisplacement
stepSequence
```

Production updates must never call `GetData` or synchronously wait for GPU readback. An optional `SimulationDiagnostics` operation performs a one-off readback for editor/debug use, separate from the frame update.

---

## 4. Proposed package layout

```text
ReactiveGeometry/Packages/com.sonosthesia.procgeo/
  Runtime/
    Simulation/
      ISimulationStep.cs
      SimulationParameters.cs
      SimulationState.cs
      NeighbourSimulation.cs
      SimulationScheduler.cs
      SimulationDiagnostics.cs
    Shaders/Resources/ProcGeo/
      NeighbourSimulation.compute
  Runtime.URP/
    ProceduralMeshRenderer.cs
  Samples~/Phase2/
    Scenes/
    Scripts/
  Tests/
    EditMode/
      NeighbourSimulationTests.cs
      SimulationLifecycleTests.cs
    PlayMode/
      NeighbourSimulationPlayModeTests.cs
      SimulationRenderingTests.cs
```

Interactive developer scenes live in the project at:

```text
ReactiveGeometry/Assets/TestScenes/Phase2/
```

Package samples stay importable and dependency-light. Project test scenes may use local materials, profiling labels and on-screen controls.

---

## 5. Compute pipeline

### 5.1 Initialisation

On generation or reset, dispatch `InitializeState`:

1. Copy `RestPosition` into both position buffers.
2. Clear both velocity buffers.
3. Reset simulation status and counters.
4. Publish rest positions and normals to the mesh stream.
5. Set the read index to 0 and step sequence to 0.

This dispatch is required after every resize or regeneration; stale state must never be accepted from a differently sized geometry.

### 5.2 Fixed-step scheduler

`ProceduralMeshRenderer.Update` or a simulation component owns an accumulator:

1. Add `Time.deltaTime`.
2. Clamp catch-up time to `maximumCatchUpTime`.
3. While the accumulator permits and `substeps < maximumSubsteps`, execute one fixed step.
4. Subtract `fixedDeltaTime`.
5. If the cap is hit, retain at most one bounded remainder or explicitly discard excess time; increment a dropped-step diagnostic.
6. Publish normals and mesh positions once after all substeps, not after every one.

The GPU simulation itself contains no wall-clock time and receives only validated scalar parameters. This makes EditMode tests deterministic.

### 5.3 Force integration

Use one thread per vertex. Each thread:

1. Reads its own current position and velocity.
2. Iterates from `NeighbourOffsets[vertex]` to `NeighbourOffsets[vertex + 1]`.
3. Reads the neighbour and edge ID.
4. Skips edges that are not structural.
5. Accumulates the neighbour spring force.

For edge rest length `L`, current edge length `d`, and coefficient `k_s`:

```text
direction = (Pj - Pi) / max(d, epsilon)
force += k_s * (d - L) * direction
```

Add:

```text
force += restoringStiffness * (RestPosition - Pi)
force += -damping * Vi
acceleration = force / max(mass, epsilon)
Vnext = Vi + acceleration * dt
Pnext = Pi + Vnext * dt
```

Each thread writes only `positions[next][vertex]` and `velocities[next][vertex]`. No thread writes a neighbour's state.

If `d <= epsilon`, record an invalid/degenerate value and use a deterministic fallback direction based on the edge's rest direction rather than normalising a zero vector.

### 5.4 Clamping and finite-value policy

After integration:

- reject or clamp NaN and infinite values before they reach the render buffers;
- clamp speed to `maximumVelocity`;
- clamp `(Pnext - RestPosition).magnitude` to `maximumDisplacement`;
- increment the status counters atomically.

Clamping is a last safety rail, not the source of stability. Tests must also find configurations that remain valid without repeatedly hitting clamps.

### 5.5 Impulses and reset

Provide an impulse kernel rather than mutating positions from CPU:

```text
ApplyImpulse(vertex, direction, magnitude)
Reset()
```

For deterministic tests, `ApplyImpulse` targets one vertex ID and writes the next/current appropriate velocity buffer. The public component may resolve an inspector-selected vertex or a normalised local direction to a vertex.

Interactive scenes expose:

- apply impulse;
- reset to rest;
- pause/resume;
- single-step;
- solver parameter sliders.

Future interaction code should call this GPU input boundary, not access internal buffers.

### 5.6 Ping-pong and command order

For each fixed step, record in order:

1. `NeighbourIntegrate` reading `positions[read]` and `velocities[read]`.
2. validation/status dispatch using the newly written state if required.
3. swap C# read/write indices.

After the final step:

4. recompute normals from `positions[read]`.
5. copy the current positions/normals into the mesh dynamic stream.
6. `GeometryResource.MarkUpdated(bounds)` increments the version using conservative bounds.

The C# indices and actual executed command-buffer order must match. A test with an impulse on one vertex and two explicitly recorded steps verifies the state alternates and does not get overwritten.

### 5.7 Dynamic normals

Extend the existing normals path to accept live positions rather than always reading generated positions:

1. clear per-vertex normal accumulator;
2. accumulate area-weighted triangle normals from `LiveState.Positions[read]`;
3. normalise;
4. publish to the dynamic mesh normal stream.

Normals are dispatched once after all substeps. A degenerate face must produce an explicit finite fallback (for example its rest normal), not NaN.

### 5.8 Bounds

Phase 2 uses the conservative bound:

```text
rest sphere/cached generation bounds expanded by maximumDisplacement
```

No per-frame position readback is permitted. The bound is deliberately overestimated for stability and culling correctness.

---

## 6. Integration with the renderer

Refactor `ProceduralMeshRenderer` so generation and simulation are separate services:

```text
OnEnable:
  generate if needed
  initialise simulation

Update:
  regenerate only when pending
  schedule/execute fixed simulation work
  publish once per rendered frame

OnDisable:
  dispose scheduler, simulation, buffers and owned material
```

Keep `Generate()` and Phase 1 behaviour compatible. A component option enables simulation; with it disabled, the renderer behaves exactly as Phase 1.

The existing one-shot generation readbacks for topology and validation remain acceptable. Move no validation or bounds readback into the per-frame path.

Prefer direct dispatch through the existing scheduler. If a command buffer is reused, verify it is rebuilt or reset correctly when parameter state changes and after reset.

---

## 7. Interactive test-scene folder

Create and keep:

```text
Assets/TestScenes/Phase2/
  README.md
  01_RestAndRecovery.unity
  02_NeighbourPropagation.unity
  03_StabilityStress.unity
```

Every scene should share the same operator controls, camera framing and reset behaviour.

### 7.1 `01_RestAndRecovery`

Purpose: prove rest-state equilibrium and elastic recovery.

- frequency 8 or 16 icosphere;
- single vertex impulse;
- visible live deformation and dynamic lighting;
- pause, reset, impulse and single-step controls;
- current/final speed and displacement diagnostics.

Acceptance:

- no movement before interaction;
- impulse is visible;
- damping returns the sphere to rest tolerance;
- reset restores the same rest state.

### 7.2 `02_NeighbourPropagation`

Purpose: demonstrate that force propagation follows graph topology.

- frequency 4 for visible vertex/edge scale;
- selectable impulse vertex and direction;
- optional rest-wireframe overlay if it can be implemented without Phase 3's picking system;
- adjustable stiffness and damping;
- no Euclidean neighbour search.

Acceptance:

- a local impulse affects adjacent topology before distant vertices;
- zero stiffness only permits restoration/damping according to their settings;
- changing impulse vertex changes the propagation origin.

### 7.3 `03_StabilityStress`

Purpose: make documented limits and failure rails visible.

- frequency choices 4, 8, 16 and 32;
- presets for gentle, nominal and maximum documented values;
- repeated/randomised impulses;
- invalid-value, clamp, maximum-speed and dropped-step indicators;
- pause after 10 seconds without an option to silently continue invalid state.

Acceptance:

- preset extremes stay finite and bounded;
- the nominal preset recovers;
- resizing/regeneration does not leak GPU resources;
- the displayed settings match the values sent to the simulation.

Scene README instructions must not imply that a scene proves Phase 2 before its automated test suite passes.

---

## 8. Automated testing

### 8.1 EditMode GPU tests

Add focused tests for:

| Test | Expected result |
| --- | --- |
| initialise from rest | both position buffers equal rest position; velocities are zero |
| stable rest | N fixed steps produce no movement above epsilon |
| single impulse | targeted velocity or position changes while unrelated state is initially unchanged |
| topology propagation | first-hop neighbours respond before graph-distant vertices for the chosen fixed sequence |
| damping recovery | energy decreases and state approaches rest |
| symmetric graph | each structural neighbour relation contributes the expected reciprocal edge |
| ping-pong | two explicit dispatches match a known reference sequence and do not alias writes |
| clamps | velocity and displacement do not exceed limits |
| degenerate edge | finite fallback is produced |
| finite state | nominal and maximum documented presets contain no NaN/Inf |
| dynamic normals | rest normals match, deformed normals are finite and unit length |
| reset | state and counters return to the initial values |
| lifecycle | repeated regenerate/resize/disable/dispose leaves `GpuBufferRegistry` empty |

One-off `GetData` in tests is expected and must remain absent from production frame code.

### 8.2 CPU reference model

Implement a small EditMode-only CPU solver for a low frequency icosphere. The GPU and reference model use:

- the same CSR topology;
- the same accumulation order for each vertex;
- the same fixed timestep and impulse;
- tolerances documented per position, velocity and energy measurement.

Do not assert cross-API bit identity. Float comparisons should account for known GPU summation and API differences.

### 8.3 PlayMode tests

Cover:

- command scheduling over multiple frames;
- no drift at rest;
- impulse and recovery while rendering;
- regeneration and disable/enable transitions;
- finite mesh vertices/normals after stress;
- a graphics smoke capture only after the scene output is stable.

Use explicit frame stepping or fixed-state waits where possible. Avoid tests that depend on an unfocused Editor producing real-time frame progress.

### 8.4 Acceptance matrix

Record at least:

| Case | Frequency | Rest | Nominal | Maximum |
| --- | ---: | --- | --- | --- |
| unit sphere | 4 | exact tolerance | stable/recovering | finite/bounded |
| medium sphere | 8 | exact tolerance | stable/recovering | finite/bounded |
| dense sphere | 16 | exact tolerance | stable/recovering | finite/bounded |
| stress sphere | 32 | exact tolerance | finite | finite/bounded if supported |

The maximum documented preset is not arbitrary: each value must be reproducible in automated tests and in `03_StabilityStress`.

---

## 9. Milestones

### M0 - Contract and buffer spike

- Confirm shader access to all required structured buffers.
- Prototype one ping-pong vector update.
- Verify the dynamic mesh stream can be published after the simulation pass.
- Record platform/API, timings and any stride restrictions.

**Gate:** no per-frame readback and no buffer aliasing; spike artefacts are saved under `Assets/Spikes`.

### M1 - Live state and lifecycle

- Add simulation buffers, validation and settings.
- Initialise/reset from rest.
- Register and release every allocation.

**Gate:** initialise/reset and lifecycle tests pass.

### M2 - Structural solver

- Implement fixed-step spring integration, damping and clamping.
- Add one-shot impulse and ping-pong tests.
- Validate against the CPU reference model.

**Gate:** rest, impulse, propagation, damping, ping-pong and finite-value suites pass.

### M3 - Render integration

- Publish live positions and dynamic normals.
- Add conservative bounds.
- Preserve Phase 1 rendering when simulation is disabled.

**Gate:** rendered deformation and recovery are stable in PlayMode; normals and mesh state remain finite.

### M4 - Interactive test scenes

- Add the shared scene controller and presets.
- Complete the three Phase 2 scenes.
- Capture a reference image or short evidence clip for each scene.

**Gate:** each scene has a manual acceptance checklist, useful diagnostics and a reset path.

### M5 - Cross-backend and performance gate

- Run the targeted suites on supported graphics backends.
- Profile small, medium and dense icospheres.
- Record dispatch count, GPU time and memory per vertex.

**Gate:** the main documented preset passes all claimed backends with complete performance and memory notes.

---

## 10. Validation commands

Use the same Unity CLI loop established in Phase 1:

```powershell
unity command script recompile --project-path ReactiveGeometry
unity command console get --project-path ReactiveGeometry
unity test ReactiveGeometry --mode EditMode --filter "Simulation;Lifecycle"
unity test ReactiveGeometry --mode PlayMode --filter "Simulation;Rendering"
```

For rendering work, open each Phase 2 test scene, enter PlayMode, exercise its controls and capture both Game and Scene views. Inspect console output before and after play.

---

## 11. Performance and observability

Measure for each supported frequency:

- vertices, edges and triangles;
- GPU bytes per vertex and total simulation bytes;
- integration dispatch count per frame;
- simulation, normals and publish time;
- fixed steps executed and dropped;
- invalid/clamped counts for the stress preset;
- CPU frame cost and allocation delta;
- repeated regeneration peak and steady-state registry size.

Use `ProfilingSampler` labels with stable names, such as:

- `ProcGeo.Simulation.Step`
- `ProcGeo.Simulation.Normals`
- `ProcGeo.Simulation.Publish`

Do not add expensive diagnostics to the normal path. Detailed status readback is an explicit editor/test action.

---

## 12. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| ping-pong indices do not match dispatched commands | centralise swap in one scheduler and test two-step sequences |
| explicit Euler becomes unstable | use semi-implicit Euler, bounded fixed steps and tested parameter ranges |
| zero-length edges generate NaN | epsilon handling plus deterministic rest-direction fallback |
| renderer and simulation write the same buffer | keep simulation buffers separate; publish only at the end |
| normals are updated before the final substep | publish normals after the last recorded step |
| excessive frame catch-up causes stalls | cap elapsed time and substeps; report dropped simulation time |
| per-frame readback hides GPU issues | no production readback; test/diagnostic readback is explicit |
| lifecycle leaks after regeneration | all buffers owned and registered through one `LiveStateBuffers` lifecycle |
| stress preset passes only through clamping | assert unclamped nominal behaviour and track clamp counts |

---

## 13. Definition of done

Phase 2 is complete only when:

1. the structural neighbour solver is GPU-resident and generator-independent;
2. live position/velocity state ping-pongs correctly;
3. rest, impulse, propagation, damping, clamp and invalid-input tests pass;
4. dynamic normals and conservative bounds update after simulation;
5. no production frame performs CPU readback;
6. generation, resize, reset and disposal leave no registered GPU buffers behind;
7. all three interactive test scenes are usable and reset cleanly;
8. the documented nominal and maximum presets have reproducible results;
9. supported frequencies, backends and performance measurements are recorded;
10. existing Phase 1 generation, topology and rendering tests still pass.
