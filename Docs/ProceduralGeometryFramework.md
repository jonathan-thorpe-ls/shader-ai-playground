# GPU Audio-Reactive Procedural Geometry Framework

Unified design and development roadmap for the `ReactiveGeometry` Unity project.

This document combines the intent of:

- [stub1.md](../Stubs/stub1.md): core GPU generation, simulation, audio and rendering architecture.
- [stub2.md](../Stubs/stub2.md): additional geometry generators and their shared contract.
- [stub3.md](../Stubs/stub3.md): geometry metadata, attributes and reusable deformation fields.
- [stub4.md](../Stubs/stub4.md): provider-independent VR interaction and geometry response profiles.
- `stub5.md`: empty at the time of consolidation, so it contributes no requirements.

Where source stubs overlap or propose incompatible layouts, this document is authoritative. The detailed completed Phase 1 implementation remains in [Phase1-GeometryGenerators.md](Phase1-GeometryGenerators.md).

## 1. Vision

Build a reusable, GPU-resident framework for generating procedural structures, simulating their connected elements, and rendering or interacting with them in real time.

Each generated structure is more than a mesh. It is a **self-describing procedural structure** containing:

- geometry and rendering primitives;
- simulation topology;
- rest-state geometry;
- typed semantic metadata;
- physical and interaction response properties;
- runtime state owned by simulation and interaction modules.

Geometry generation, simulation, audio analysis, interaction, and rendering remain independent modules. They communicate through explicit GPU-buffer contracts rather than geometry-specific code paths.

## 2. Scope and non-goals

### In scope

- Compute-shader geometry generation.
- Shared GPU resource ownership and disposal.
- Triangle, line, point and future cell-based geometry.
- Deterministic simulation connectivity independent of rendering indices.
- Neighbour-based spring and wave simulation.
- Audio analysis and configurable response mapping.
- Typed geometry attributes across multiple domains.
- Reusable deformation and interaction fields.
- URP mesh-bridge and procedural/indirect rendering.
- Runtime visualisation, validation, profiling and leak checks.
- Provider-independent XR hand/controller interaction.

### Explicitly separated

- Audio is not the geometry generator.
- Gesture recognition is not simulation.
- Rendering indices are not automatically spring connectivity.
- CPU generation is permitted for one-off topology preparation or complex algorithms, but not for per-frame deformation.
- Simulation should not perform per-frame GPU-to-CPU vertex readback.
- A deformation field should not be tied to a particular generator when the required attributes can describe it generically.
- Optional attributes and simulation modules should allocate buffers only when required.

## 3. Design principles

1. **The GPU is the runtime source of truth.** Current positions, velocities and dynamic interaction state remain GPU-resident.
2. **Generation and simulation are independent.** A new generator should not require a new simulation solver.
3. **Rendering and simulation topology are separate.** One may use triangles while the other uses spring or graph edges.
4. **Generated structures are self-describing.** They declare typed attributes, domains, formats, units and lifetimes.
5. **Dependencies are explicit.** A deformation field declares its required attributes and fails clearly when they are unavailable.
6. **Physical and visual deformation are distinct.** Physics writes simulation state; optional render-only effects may add bounded secondary displacement.
7. **Updates are race-safe.** Each compute thread writes only its own output, or uses an explicitly designed reduction.
8. **Simulation is composable.** Structural springs, shear, bending, wave propagation, constraints and external fields are independent contributions.
9. **Determinism is defined narrowly.** Integer/topology output should match on the same tested GPU/API; floating-point output uses documented tolerances.
10. **Correctness precedes scale.** Establish validation and stability before increasing topology complexity or vertex count.

## 4. System architecture

```text
                       Geometry Generator
                              |
              +---------------+----------------+
              |                                |
       Render primitives                 Simulation topology
       and rest geometry                 and rest lengths
              |                                |
              +----------- Attributes --------+
                              |
          +-------------------+-------------------+
          |                   |                   |
     Static metadata     Dynamic state        Runtime inputs
     and properties      buffers              (audio / XR)
          |                   |                   |
          +-------------------+-------------------+
                              |
                     Compute simulation
                   forces / constraints /
                   propagation / normals
                              |
                 +------------+-------------+
                 |                          |
          Mesh bridge draw          Procedural / indirect draw
                 |                          |
                 +------------+-------------+
                              |
                           URP output
```

### Module boundaries

| Module | Responsibility | Must not own |
| --- | --- | --- |
| Generator | Rest geometry, primitives, topology and generated metadata | Per-frame simulation, audio capture or input gestures |
| Resource core | Buffer capacities, aliases, ownership and disposal | Generator-specific behaviour |
| Attribute system | Typed descriptors, availability and shader binding | Implicit interpretation of same-format data |
| Simulation | Forces, integration, constraints, propagation and normals | Knowledge of how geometry was generated |
| Audio | FFT bands, RMS, onset and smoothing | Direct geometry mutation |
| Interaction | Tracking interpretation, sources, volumes and gestures | Generator-specific force laws |
| Rendering | Material access, draw scheduling and view-dependent work | Simulation decisions |
| Validation/debug | Topology checks, visualisation, profiling and diagnostics | Production-state mutation except explicit debug tools |

## 5. Canonical geometry contract

Every generator plans capacities on the CPU and records its GPU generation commands against one shared resource. Phase 1 established `GeometryLayout`, `GeometryResource`, topology buffers, attributes, live mesh backing and validation.

Conceptual interface:

```csharp
public interface IGeometryGenerator
{
    GeometryLayout Plan();
    void Record(CommandBuffer command, GeometryResource target);
}
```

### Required generator outputs

| Output | Required for |
| --- | --- |
| Rest positions | Every generator |
| Render primitives or indirect draw representation | Visible geometry |
| Simulation connectivity | Simulated geometry |
| Rest lengths and edge types | Spring/constraint systems |
| Counts and capacities | Resource allocation and indirect work |
| Bounds and orientation | Culling and validation |
| Attribute definitions and generated values | Self-description |

### Core buffers

Core runtime storage includes:

- `RestPositions`
- CSR `NeighbourOffsets`, `Neighbours`, and `NeighbourEdge`
- `Edges`, `EdgeRestLength`, and `EdgeType`
- rendering indices or procedural primitive buffers
- vertex-to-face incidence
- dynamic position/normal backing
- position/velocity ping-pong buffers owned by the future simulation module
- optional previous positions
- draw and dispatch arguments for variable-count geometry
- validation counters and first-offending element IDs

Do not create a fixed-neighbour representation as the source of truth. CSR is canonical; a fixed-width view may be generated only when profiling justifies it.

## 6. Geometry attributes and metadata

### Attribute contract

Every attribute definition states:

- stable ID;
- domain;
- format and shader stride;
- smooth or flat interpolation;
- static or dynamic lifetime;
- unit and interpretation;
- nominal range where useful;
- description;
- producer/generator;
- optional shader binding name.

Attribute domains:

| Domain | Meaning |
| --- | --- |
| Vertex | One value per welded simulation vertex |
| Corner | One value per primitive corner |
| Edge | One value per committed topology edge |
| Primitive | One value per triangle, line segment or other primitive |
| Region | One value per branch, cell, segment or structural region |
| Geometry | One global value or small geometry-level record |

IDs such as `RegionId`, `BranchId` and `CellId` are discrete values and must not be smoothly interpolated.

### Initial attribute set

Core attributes:

- `RestPosition`
- `RestNormal`
- `RestTangent` / structural `Direction`
- `LocalCoord`
- `RegionId`
- `BoundaryWeight`
- `NeighbourCount`
- `Curvature`
- `EdgeRestLength`

Structural attributes:

- `DistanceFromRoot`
- `LocalDistance`
- `DistanceToTip`
- `DistanceToBoundary`
- `GenerationDepth`
- `ParentId`
- `BranchId`
- `Direction`
- `TipWeight`
- `JunctionWeight`

Physical attributes:

- `Mass`
- `Stiffness`
- `Damping`
- `ConstraintWeight`
- `WaveSpeed`
- `WaveAttenuation`

Audio and interaction attributes:

- `AudioSensitivity` for bass, mid, high and onset
- `InteractionWeight`
- `TouchSensitivity`
- `PushSensitivity`
- `PinchSensitivity`
- `GrabWeight`
- `ImpactSensitivity`
- `InteractionConstraint`

Dynamic attributes:

- `Excitation`
- `WaveValue`
- `DeformationWeight`
- `LocalEffectMask`

### Storage rules

- Separate cold/static attributes from frequently updated simulation state.
- Use one buffer per reusable attribute unless profiling shows a packed structure is better.
- Dynamic attributes are allocated only when a consumer requests them.
- Attribute buffers have explicit domain cardinality.
- Consumers validate attributes by ID, domain, format and lifetime; matching stride alone is insufficient.
- Regions are stored once and referenced by IDs from vertices, corners, primitives or edges.

## 7. Reusable deformation fields

A deformation field reads geometry metadata, simulation state and external inputs, then produces a force, displacement or dynamic attribute.

Representative fields:

| Field | Typical inputs |
| --- | --- |
| Root wave | `DistanceFromRoot`, time, wave speed, attenuation |
| Surface ripple | local coordinate, source location, decay |
| Radial pulse | `RadialDirection`, radius, phase |
| Branch bend | `Direction`, stiffness, external force |
| Graph propagation | CSR topology and dynamic excitation |
| Curvature response | curvature and sensitivity |
| Audio band response | audio values and generated sensitivity weights |
| Interaction field | XR source volumes and geometry response metadata |

Each field declares:

```text
Requires:
  attributes and external inputs
Produces:
  force, constraint, displacement, or dynamic attribute
Blend:
  additive, weighted, minimum, maximum, or explicit precedence
```

Physical fields should normally contribute acceleration or constraints. Direct position offsets are reserved for controlled visual-only effects.

## 8. Geometry generators

All generators use the canonical resource contract. Rendering primitives and simulation connectivity may differ.

### 8.1 Icosphere

Completed in Phase 1.

- Canonical geodesic vertex indexing.
- Direct and recursive-equivalent power-of-two projection.
- Shared edges and vertices without duplicate simulation points.
- Triangle rendering, CSR connectivity, normals and validation.
- URP mesh-bridge rendering without per-frame readback.

### 8.2 Parametric surfaces

Implement first after the simulation foundation.

Support:

- plane;
- sine;
- saddle;
- twist;
- cylinder;
- torus.

Provide `u/v` coordinates, wrapped topology, optional diagonal/shear edges, analytic normals/tangents, primitive region IDs and boundary attributes.

### 8.3 Tube and curve

Generate rings along analytic or sampled curves:

- line;
- helix;
- torus knot;
- Lissajous;
- Catmull-Rom control-point curve.

Use rotation-minimising frames, closed-loop welding, optional caps and radius-over-length attributes. Emit longitudinal and radial connectivity.

### 8.4 Branching graph

Start with deterministic graph/line topology and line or glyph rendering. Add optional tubular skinning later.

Required structure:

- parent/child relationships;
- branch and node IDs;
- accumulated `DistanceFromRoot`;
- local branch distance;
- generation depth;
- branch radius;
- junction and tip weights;
- region bounds and properties.

### 8.5 Particle network

Generate points and connections using radius or nearest-neighbour rules backed by a GPU spatial grid. Avoid all-pairs search at scale.

Render particles as points/billboards and connections as lines or procedural geometry. Support attraction, repulsion, graph excitation and dynamic graph-density parameters where practical.

### 8.6 Voronoi structure

Treat this as the most advanced generator. A selected approach must prove deterministic region identity, shared boundaries, usable adjacency and bounded memory before simulation integration.

Support:

- seed generation;
- cell or boundary construction;
- region identity;
- shared-edge topology;
- optional surface/cell meshes;
- graph or surface-based simulation connectivity.

## 9. Simulation

### 9.1 Baseline mass-spring model

Maintain:

- position and velocity ping-pong buffers;
- rest positions;
- mass, stiffness and damping attributes;
- structural spring connectivity and rest lengths.

For a connected pair:

\[
F_{ij}=k_s(d_{ij}-L_{ij})\frac{P_j-P_i}{d_{ij}}
\]

Each vertex thread reads neighbours and writes only its own output. Use semi-implicit Euler with a fixed timestep and configurable substeps.

Add optional:

- restoring-to-rest force;
- maximum velocity and displacement;
- edge-length constraints;
- collision constraints;
- volume/area constraints where justified.

XPBD should remain a planned alternative or extension if explicit force integration is insufficient.

### 9.2 Additional spring types

Use `EdgeType` rather than a separate solver for:

- structural;
- shear;
- bending;
- long-range;
- secondary/local constraints.

Each type has independent stiffness, damping and optional directional rules.

### 9.3 Connectivity propagation

Support two modes:

- diffusive excitation for energy spreading;
- damped second-order wave propagation for oscillatory motion.

Both operate over committed graph topology, not Euclidean neighbour search. Region and distance attributes provide artistic controls but do not replace actual graph propagation.

### 9.4 Stability requirements

No implementation is complete until it demonstrates:

- stability at rest;
- elastic recovery;
- bounded values under maximum documented input;
- correct ping-pong behaviour;
- no thread writing another vertex's position;
- stable behaviour across tested timestep, stiffness and spacing ranges;
- explicit failure or clamping for invalid numeric values.

## 10. Audio system

Audio analysis runs on the CPU and uploads only compact controls to GPU buffers.

Initial values:

- bass;
- mids;
- highs;
- RMS;
- onset/beat strength;
- smoothed variants where required.

Use separate attack/release smoothing. Audio mapping remains configurable and may affect force strength, stiffness, damping, excitation, visual intensity or field parameters.

The analyser does not decide geometry behaviour directly. It supplies normalised signals interpreted by generic deformation fields and geometry response profiles.

## 11. Rendering

### Mesh bridge

Use GPU-writable `Mesh` buffers for production surfaces needing stock URP or Shader Graph materials, depth/shadow passes and other standard rendering behaviour. Write positions and normals directly into the dynamic stream.

### Procedural and indirect drawing

Use `Graphics.RenderPrimitives*` for:

- line/graph geometry;
- point and particle geometry;
- wireframes and debug overlays;
- glyphs;
- variable-count outputs.

### Normals and bounds

- Recompute normals after physical deformation.
- For triangle meshes, use vertex-to-face incidence and a race-safe per-vertex accumulation.
- Preserve analytic normals where useful as attributes and test them against the generic pass.
- Read back/update bounds only after generation or on a bounded scheduling event, never for every frame.
- Motion vectors and previous positions are reserved for a later dedicated spike.

## 12. VR and controller interaction

Keep tracked-input interpretation separate from geometry response.

### Interaction sources

Hands and controllers expose:

- position, velocity and orientation;
- palm or controller direction;
- pinch/grab/trigger strengths;
- optional fingertip or joint data;
- stable source ID, type and interaction state.

### Interaction volumes

Interpret gestures as reusable spatial volumes:

- point/sphere;
- capsule;
- cone;
- oriented box;
- two-point constraint;
- grab volume;
- transient impulse volume.

### Interaction fields

Prioritised gestures:

1. touch/proximity;
2. push;
3. pinch;
4. hit/impulse;
5. grab;
6. two-hand stretch/twist.

Continuous sources, discrete events and persistent grab constraints remain distinct GPU structures.

### Spatial queries

Use a two-stage query:

1. Broad phase with a spatial grid, cluster/region bounds or BVH.
2. Narrow phase against candidate vertices, edges, primitives or regions.

Geometry response profiles map interaction fields onto a structure's attributes. For example, a grab field may use `GrabWeight`, `ConstraintWeight`, `Stiffness` and graph topology without knowing whether the target is a branch, tube or cell.

## 13. Validation, debugging and performance

### Topology validation

For every generator:

- all indices are in range;
- no unintended duplicate or self edges;
- adjacency is symmetric where required;
- rest lengths are finite and positive;
- triangle winding is consistent;
- expected manifold/boundary properties are met;
- Euler characteristics are checked where the topology defines one;
- vertex, edge, primitive, corner and region cardinalities match their descriptors.

### Simulation validation

- rest state remains unchanged;
- a local impulse propagates through expected connectivity;
- damping settles the system;
- audio and interaction extremes stay bounded;
- regenerated and resized resources release correctly;
- repeated runs produce expected deterministic topology.

### Debug views

Provide:

- attribute colourisation;
- wireframe and edge views;
- vertex/normal/tangent glyphs;
- rest versus live overlay;
- topology and boundary highlighting;
- picked vertex/edge/region inspection;
- source/field volume visualisation;
- buffer memory and ownership report.

### Performance measures

Record:

- vertex, edge, primitive and region counts;
- GPU memory per geometry and per vertex;
- generation, topology, simulation, normals and draw cost;
- frame time and dispatch count;
- stability range and substep count;
- allocation churn and buffer reuse.

Profile representative small, medium and large cases before changing storage layout.

## 14. Next development phases

Phase 1 is complete: the project has GPU icosphere generation, deterministic triangle topology, metadata foundations, validation, lifecycle checks and URP mesh-bridge rendering.

### Phase 2 — Dynamic neighbour simulation

Implement the GPU simulation vertical slice on the icosphere:

- position/velocity ping-pong buffers;
- fixed timestep and substeps;
- structural forces, restoration and damping;
- dynamic normal recomputation;
- one-shot validation impulses and stable recovery;
- no per-frame CPU readback.

Completion means the existing rendered icosphere can deform, propagate local changes through topology, return to rest, and remain numerically valid under documented limits.

See the detailed implementation plan in [Phase2-DynamicNeighbourSimulation.md](Implementation/Phase2-DynamicNeighbourSimulation.md), including the GPU data contract, fixed-step pipeline, renderer integration, automated tests, milestones, and interactive Phase 2 test scenes.

### Phase 3 — Fields, operators and debugging

Add reusable metadata-driven operations and observability:

- graph distance and normalisation;
- attribute recipes;
- curvature and boundary operators;
- composable deformation fields;
- procedural debug draws and attribute colourisation;
- one-off picking/inspection and test deformer.

Completion means a field can be configured from declared attributes and exercised without geometry-specific solver code.

### Phase 4 — Audio reactivity

Add FFT/RMS/onset analysis, compact smoothed GPU parameters, and configurable audio mappings. Prove distinct bass, mid, high and onset responses while preserving simulation stability.

### Phase 5 — Additional surface generators

Extend the shared architecture to parametric surfaces, followed by tubes:

1. parametric grid and shapes;
2. wrapped cylinder/torus topology;
3. rotation-minimising tube frames, caps and longitudinal metadata.

Completion means every new generator passes shared topology, simulation, normal, rendering and metadata tests.

### Phase 6 — Graph and irregular generators

Add branching graphs, particle networks and eventually Voronoi:

- line/point rendering;
- deterministic graph metadata;
- spatial-grid neighbour generation;
- region and cell identity;
- graph-aware wave propagation;
- variable-count indirect work where required.

### Phase 7 — VR interaction

Introduce provider-independent interaction sources, fields, events and grab constraints. Start with one fingertip touching/pushing a grid, then add pinch, hit, grab and two-hand interaction, with a spatial broad phase and haptics/feedback.

### Phase 8 — Hardening and visual development

Complete cross-backend tests, performance baselines, memory reports, render-graph integration, motion vectors, CI/GPU-agent coverage, samples, reference captures and user-facing editor tools.

## 15. Phase-gate order

Dependencies are intentionally strict:

```text
Phase 1: Icosphere and topology foundation        [complete]
    |
Phase 2: Stable neighbour simulation
    |
Phase 3: Metadata operators, fields and debug tools
    |
Phase 4: Audio control
    |
Phase 5: Parametric surfaces and tubes
    |
Phase 6: Graphs, particles and Voronoi
    |
Phase 7: VR interaction
    |
Phase 8: Hardening, performance and polish
```

Do not add a later generator merely to provide a test for an unfinished solver. Use the simplest existing topology that proves the next architectural capability:

- icosphere for the first simulation;
- grid for surface waves and first XR touch;
- tube for frame/directional metadata;
- branching graph for root distance and graph interaction;
- particle network for spatial queries;
- Voronoi for regional/cell semantics.

## 16. Definition of done

A phase is complete only when:

1. the feature is covered by automated EditMode and/or PlayMode tests;
2. topology and numeric outputs are validated;
3. lifecycle tests leave no registered GPU leaks;
4. GPU state is not read back per frame;
5. the common interfaces remain reusable across generators;
6. the relevant scene or sample demonstrates the result;
7. console errors, shader errors and diagnostics are clean;
8. limitations, data formats and settings are documented;
9. performance is measured for the sizes claimed as supported.

## 17. Source design decisions retained

The consolidated direction retains these key ideas from all non-empty stubs:

- one reusable GPU simulation should serve multiple geometries;
- geometry emits semantic structure as well as positions and indices;
- audio influences physical and procedural controls rather than dictating geometry;
- graph connectivity supports waves, propagation and interaction;
- generators are added only after the preceding shared contract is stable;
- VR operates through interaction sources, fields and geometry response profiles;
- provider-independent XR abstractions keep gesture and geometry concerns separate;
- debugging and validation are first-class because GPU state is otherwise difficult to inspect.
