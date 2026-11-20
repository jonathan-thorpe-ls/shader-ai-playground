# Phase 5 - Fundamental Path Deformation

Implementation plan for path-based deformation in the GPU procedural-geometry
framework described in [ProceduralGeometryFramework.md](../ProceduralGeometryFramework.md).

| Item | Decision |
| --- | --- |
| Engine | Unity 6.6 |
| Pipeline | URP 17.6 |
| Target project | `ReactiveGeometry/` |
| Phase 5 gate | A closed torus or knot deforms its centreline while its path-skinned base pose retains every authored ring; optional vertex detail then follows the live base normals |
| Runtime source of truth | Rest and live GPU path samples plus transported frames |
| Primary output | Path-skinned base pose followed by optional vertex-level deformation in the resulting normal basis |
| UI for example/test scenes | UI Toolkit only, through simple component inspectors |
| Testing | deterministic path/frame GPU tests, deformation invariants and PlayMode rendering |

## Implementation status

The Phase 5 vertical slice is implemented in `ReactiveGeometry/`:

- immutable tube-to-path bindings generated from Phase 4 path frames;
- GPU-owned rest/live path resources and arc-length data;
- direct and bounded dynamic path deformation;
- transported live frames and closed-path seam correction;
- path-skinned base positions, normals and tangents;
- separate `IPathDeformer` and `IVertexDeformer` scheduler stages;
- path offset, bend, twist, dynamic wave and vertex-normal authoring components;
- periodic 2D value-noise path deformation sampled through a circular
  normalized-arc-length domain, with bounded fBM controls;
- optional Phase 4-compatible Constant/Inverse/Smoothstep spatial falloff for
  every path and vertex deformer, with the component transform as source centre;
- UI Toolkit component inspectors;
- `Phase5/01_PathAndVertexDeformers` and
  `Phase5/02_SpatialDeformerSources` functional test scenes;
- EditMode invariant/lifecycle tests and a PlayMode renderer integration test.

The first implementation intentionally limits the vertex stage to bounded
normal-relative offsets. More advanced vertex solvers and additional authored
stress scenes can extend the committed `IVertexDeformer` contract without
changing path ownership or stage order.

## 1. Purpose

Phase 4 generated tube surfaces around analytic and sampled curves. Phase 5
separates a tube into two independently meaningful parts:

1. a deformable one-dimensional **path**;
2. a constant **extruded shape** stored in path-local coordinates.

Deforming the path moves, bends and twists the extrusion without requiring every
tube vertex to become an independently simulated spring point. A circle remains
a circle around each deformed path sample before optional vertex detail; a torus
becomes a path-deformed torus; a knot retains the same ring offsets while its
centreline changes.

The phase is complete when path deformation can be configured without
knowledge of the original curve definition. Analytic, sampled, open and closed
paths must all use the same runtime path resource and deformation pipeline.

## 2. Scope

### 2.1 In scope

1. A canonical GPU path resource independent of tube generation.
2. Open and closed path semantics, arc-length parameterisation and finite
   tangents.
3. Rest and live path position/frame buffers with bounded ownership and reuse.
4. Immutable path-local cross-section coordinates for extruded geometry.
5. Reconstructing tube positions and normals from live path samples and
   frames as a dynamic base pose.
6. Parallel-transport frame reconstruction, including closed-loop seam
   alignment.
7. Generic bend, offset, twist and longitudinal-wave path deformers.
8. Integration with the existing field composition and simulation scheduler.
9. Reset and recovery toward the rest path.
10. Applying bounded vertex-level deformation after skinning, using normals and
    tangent directions from the resulting path-skinned base pose.
11. UI Toolkit inspectors, focused EditMode/PlayMode tests and test scenes.

### 2.2 Explicitly out of scope

- Audio analysis or mappings; Phase 7 consumes path controls.
- XR/provider interaction; Phase 8 supplies controls and constraints.
- Branching junction deformation; Phase 6 generalises the resource to a
  graph-of-paths.
- Regenerating path topology, sample count or tube connectivity per frame.
- Remeshing, adaptive tessellation or mesh-boolean operations.
- Length-preserving physical rod simulation with full torsion dynamics.
- Authoring permanent changes to tube radius or scaling the generated
  cross-section. Bounded vertex-level effects may change final surface points
  after skinning, but do not mutate the immutable cross-section binding.
- Per-frame GPU-to-CPU readback.

## 3. Immutable extrusion skinned to a live path

Store each vertex as a path-local cross-section coordinate and reconstruct it
from the nearest/current path sample and transported frame:

```text
P' = C'(u) + N'(u) * x + B'(u) * y
```

Where:

- `C'(u)` is the deformed path centre;
- `N'(u)` and `B'(u)` are the deformed path frame axes;
- `(x, y)` are immutable cross-section offsets;
- `u` is immutable path distance.

Advantages:

- preserves the authored extrusion by construction;
- works identically for line, circle, helix, knot and sampled paths;
- keeps topology and sample count fixed;
- lets path deformers act at the lower-dimensional path domain;
- provides a natural base for future complex deformation.

Costs:

- requires a path resource, sampling and frame reconstruction;
- introduces skinning seams and interpolation rules that must be tested;
- requires a deliberate relationship with vertex simulation.

This is the Phase 5 implementation architecture.

## 4. Path resource contract

Introduce a path domain alongside vertex, corner, edge and primitive domains.
It is a committed set of ordered samples, not another geometry mesh.

```text
PathResource
  header:
    sample count
    closed/open flag
    rest length
    maximum live bounds
  rest:
    positions
    cumulative arc lengths
    tangents
    parallel-transport normals
    parallel-transport binormals
  live:
    positions
    tangents
    normals
    binormals
    optional velocity / force
  topology:
    previous / next sample identity
    fixed segment rest lengths
```

`PathResource` owns all buffers, validates capacities and participates in the
existing GPU lifecycle tracker. It must not alias buffers owned by a particular
`TubeGenerator` if the path is intended to drive multiple extrusion skins.

Recommended resource relationships:

```text
GeometryResource
  +-- Geometry topology and vertex attributes
  +-- optional PathBinding
        +-- PathResource
        +-- vertex PathIndexA/PathIndexB
        +-- vertex PathBlend
        +-- vertex SectionU/SectionV
```

One path may initially bind to one extrusion. Supporting several skins is a
useful direction for reuse and can be introduced without changing the path
contract.

## 5. Tube binding attributes

Add typed path bindings rather than relying on matching `LocalCoord.x` semantics.
Required attributes for path-skinned geometry:

| Attribute | Domain | Meaning |
| --- | --- | --- |
| `PathIndex` or compact index pair | Vertex | sample span containing the vertex |
| `PathBlend` | Vertex | interpolation factor within that span |
| `CrossSectionPosition` | Vertex | immutable authored `(normal, binormal)` offset |
| `PathDistance` | Vertex | cumulative or normalised distance along the path |
| `PathFrameTwist` | Vertex | optional fixed roll about the longitudinal axis |
| `PathId` | Vertex/region | identifies the bound path, initially zero |

The path binding must retain closure policy explicitly. A closed path interpolates
the last sample toward the first; an open path clamps to its endpoint samples.
Integer wraparound and interpolation use exact, tested formulas rather than
shader modulo shortcuts with undefined negative-input behaviour.

## 6. Update pipeline

The frame update order is:

```text
1. Clear path force/control contributions
2. Evaluate path deformers and external controls
3. Integrate or project live path samples
4. Reconstruct live path tangents
5. Rebuild/transport live path frames
6. Correct closed-loop frame alignment
7. Skin all bound extrusion vertices
8. Recompute path-skinned base normals
9. Clear vertex-level deformation contributions
10. Evaluate vertex deformers against the path-skinned base pose
11. Apply vertex offsets in the resulting normal/tangent basis
12. Recompute final normals and feed/render resulting geometry
```

All dispatches use fixed-size buffers and bounded thread counts. Path topology,
sample allocation and tube-to-path bindings are immutable after generation.
The path-skinned position and normal buffers are intermediate GPU state, not
aliases of the final deformed vertex buffers. Vertex-level deformation consumes
that state explicitly; it must not read static `RestPosition` or `RestNormal`
when computing normal-relative offsets.

There are two supported path-state modes:

1. **direct control mode**: path deformers directly write the live path target
   each update from rest state and controls;
2. **dynamic mode**: path deformers contribute forces, while a bounded path
   integrator updates position and velocity.

Direct mode is implemented first because it is deterministic and is the best
reference for bend and wave tests. Dynamic mode is enabled in the same contract
so later interaction and physical constraints do not require a second pipeline.
Both modes feed the same path-skinning and subsequent vertex-deformation stages.

Vertex-level deformation is an optional, composable second stage. For an
offset scalar `d` it writes:

```text
P-final = P-path + N-path * d
```

Vector contributions may use the complete local basis:

```text
P-final = P-path + T-path * x + N-path * y + B-path * z
```

Here `P-path` and the live basis are reconstructed in the immediately
preceding stage. This is the defining order: a path bend rotates the normals
first, and all subsequent normal-displacement effects follow that new
orientation rather than the original tube frame.

For a vertex, `N-path` is the normal of the committed path-skinned surface.
`T-path` is the path tangent associated with that vertex (interpolated through
the same path binding), and:

```text
B-path = normalise(cross(T-path, N-path))
N-path = normalise(cross(B-path, T-path))
```

Normal-only fields require only `N-path`. Tangential or full-local-space fields
require the complete orthonormal basis and must be evaluated after the basis is
committed. They may not approximate the result with the original path frame.

## 7. Frame reconstruction

Tangents come from neighbouring live path samples:

```text
T[i] = normalise(C'[i + 1] - C'[i - 1])
```

Endpoints use one-sided differences and open curves use clamped neighbours.
Degenerate segments fail validation or use a documented, state-independent
fallback frame; they must not silently propagate NaNs.

Transport one reference normal along the ordered samples using the minimal
rotation between consecutive tangents. For a closed path:

1. transport through the last sample;
2. measure the signed seam rotation;
3. distribute the inverse correction across the samples;
4. validate last-to-first continuity.

Normal and binormal interpolation should never normalise a linearly interpolated
frame as the primary method because large angular differences can collapse or
flip the frame. Sample or interpolate from the transported live frame buffer
with the tested seam-aware rule.

## 8. Cross-section invariant

The primary path-skinning acceptance invariant is geometric:

```text
distance(P-path[ring, j], C'[u]) == distance(CrossSectionPosition[j], 0)
```

within documented tolerance for every valid ring and frame, before optional
vertex-level deformation.

Additional invariants:

- cross-section arc adjacency lengths remain constant;
- ring area remains constant for planar cross-sections;
- closed-ring vertex ordering and winding remain unchanged;
- deformation moves the longitudinal mapping and does not alter vertex count;
- the deformed cross-section has no additional stretch from frame blending.

Tube radius and cross-section shape therefore remain immutable generation
attributes of the path-skinned base pose. A later vertex stage may intentionally
displace final surface points along the base-pose normal or local basis, but
that effect must be represented as a separate offset; it must not overwrite the
cross-section coordinates or make the path stage appear to stretch the tube.

## 9. Deformer component types

Path deformation and vertex deformation are separate component classes with
separate inputs, outputs and execution stages.

### 9.1 Path deformers

A path deformer:

- targets a `PathResource`;
- reads rest/live path state and path metadata;
- writes a bounded path position, frame/twist or path-force contribution;
- executes before path skinning;
- cannot read or write tube vertex buffers;
- is reusable for any geometry bound to the path;
- must not change sample count, closure, segment topology or cross-section
  coordinates.

Path deformers compose through a dedicated path contribution buffer and
path-controller scheduler. Their output becomes authoritative only after the
path integration/constraint stage and frame reconstruction complete.

### 9.2 Vertex deformers

A vertex deformer:

- targets the path-skinned `GeometryResource`;
- reads committed base positions, base normals and the live local basis;
- writes a bounded vertex offset or vertex-space force;
- executes only after path skinning and base-normal reconstruction;
- cannot modify `PathResource` samples or frames;
- is reusable with non-path geometry when it declares an equivalent dynamic
  base-pose contract;
- must not replace or mutate immutable rest metadata used by the path binding.

Path and vertex deformers may optionally declare a
`SpatialSourceParameters` target. When enabled, the component transform is the
world-space source centre, converted to the renderer's local space for the GPU
dispatch. Constant, inverse and smoothstep falloff match the Phase 4 spatial
field semantics. Disabled spatial source means the deformer applies uniformly.

Normal, tangent and binormal vertex effects are evaluated from the
path-skinned basis. A component that needs to deform both the path and its
surface is two explicitly ordered components: one path deformer followed by
one vertex deformer. No single component owns both stages.

### 9.3 Component and scheduler contracts

```text
IPathDeformer
  PathRequirements Requires
  void AddContribution(PathDeformerContext context)

IVertexDeformer
  GeometryRequirements Requires
  void AddContribution(VertexDeformerContext context)
```

The scheduler clears each contribution type once, evaluates all registered
components in its stage, resolves the result, then advances to the dependent
stage. A direct path deformer cannot skip ahead to vertex skinning, and a
vertex deformer cannot run early using stale rest normals.

Recommended component naming distinguishes the stages:

- `PathOffsetDeformer`
- `PathBendDeformer`
- `PathTwistDeformer`
- `PathWaveDeformer`
- `PathConstraintDeformer`
- `VertexNormalOffsetDeformer`
- `VertexNoiseDeformer`
- `VertexSpringDeformer`

## 10. Path deformer types

Every path deformer reads declared path inputs and writes a single bounded
contribution type.

### 10.1 Offset

Applies a path-space or world-space displacement, optionally masked by
`PathDistance`:

```text
offset(path sample, control)
```

### 10.2 Bend

Rotates the rest path around an authored pivot and axis with a distance falloff.
The contribution must be a transform target or force, not a per-vertex shortcut.

### 10.3 Twist

Rotates the transported frame around the path tangent. Twist is distinct from
cross-section deformation: it changes ring orientation, not the ring shape.
Closed-loop total twist must follow an explicit endpoint/seam policy.

### 10.4 Longitudinal wave

Maps phase and distance to a path-space offset:

```text
phase = time * speed - distance * spatialFrequency
```

For open paths, `spatialFrequency` is radians per local distance unit. For
closed paths, it is cycles per complete loop:

```text
spatialPhase = (pathDistance / pathRestLength) *
               spatialFrequency * 2 * PI
```

This closed-path interpretation keeps the final-to-first sample interval
continuous for integer and non-integer cycle settings instead of ending at an
unrelated phase.

Path noise uses the normalized arc-length parameter in a circular 2D noise
domain:

```text
angle = normalizedPathDistance * 2 * PI
noiseCoordinate = (cos(angle), sin(angle)) * spatialFrequency + timeOffset
```

The first and last theoretical parameter values therefore map to the same
coordinate, so the noise is seamless without requiring an integer frequency.
Open paths use a linear 2D coordinate. fBM octaves are normalized and bounded
before projection through the path tangent/normal/binormal basis.

The deformer controls path sample displacement and, indirectly, the transported
frame. Later audio controls modulate amplitude, speed or band weights rather
than implementing their own skinning formula.

### 10.5 Constraint and reset

Fixed, axis-constrained, grab and rest-distance constraints operate on path
samples. Reset restores live path state to rest in one bounded operation.

### 10.6 Vertex deformer types

Vertex deformers begin only with the committed path-skinned base pose:

- `VertexNormalOffsetDeformer` applies scalar offsets along the live base
  normal;
- `VertexNoiseDeformer` evaluates noise and maps it into the live local basis;
- `VertexSpringDeformer` integrates secondary offsets from rest-offset targets
  and bounded forces relative to the current base pose;
- future vertex deformers may use colour, region, distance or material metadata,
  but still return an offset/contribution in the vertex stage.

A vertex deformer does not need to know how the path was generated. It only
declares which base buffers and metadata it requires.

## 11. Relationship with neighbour simulation

The existing geometry simulation is not reused implicitly at the path domain.
Define a clear mode:

### Kinematic path with optional vertex detail

The path is authoritative for the base pose. It skins intermediate positions
and normals, and the regular vertex solver may then apply bounded secondary
offsets. With no vertex deformers enabled, the final pose equals the
path-skinned base pose.

### Dynamic path and secondary-surface mode

The path skin always provides a dynamic base pose. A vertex deformer operates
on offsets, velocities or forces relative to that base pose, never on absolute
positions relative to the original `RestPosition`.

The existing neighbour simulation therefore requires an explicit base-pose
binding when attached to path-skinned geometry:

```text
absolute vertex position = dynamic path base position + vertex offset
spring/rest target         = zero vertex offset
normal-relative field      = current path-skinned normal * contribution
```

If the existing solver cannot consume a dynamic base-pose buffer directly,
Phase 5 adapts the integration contract before reusing it; it must not keep
two independent absolute-position authorities or apply a path transform as a
post-correction to an already simulated tube.

Physical path samples may later use the existing scheduling, force accumulation
and fixed-step conventions. They do not have to use the same buffers or
vertex-connectivity representation.

## 12. Proposed package layout

```text
Runtime/
  Paths/
    PathHeader.cs
    PathResource.cs
    PathLifecycle.cs
    PathBinding.cs
    PathSampler.cs
  Paths/Deformers/
    IPathDeformer.cs
    PathOffsetDeformer.cs
    PathBendDeformer.cs
    PathTwistDeformer.cs
    PathWaveDeformer.cs
  Paths/Simulation/
    PathIntegrator.cs
    PathConstraint.cs
  Paths/Rendering/
    PathSkinning.cs
  Deformers/
    IVertexDeformer.cs
    VertexNormalOffsetDeformer.cs
    VertexNoiseDeformer.cs
    VertexSpringDeformer.cs
  Shaders/Resources/ProcGeo/
    PathDeformers.compute
    PathFrames.compute
    PathSkinning.compute
    PathRelativeVertexDeformation.compute
  Generators/
    TubePathBindingBuilder.cs
Tests/
  EditMode/
    PathResourceTests.cs
    PathFrameTests.cs
    PathDeformerTests.cs
    VertexDeformerTests.cs
    PathSkinningTests.cs
  PlayMode/
    PathDeformationRenderingTests.cs
```

Keep `PathResource` and `PathBinding` independent from `TubeGenerator`. A tube
generator creates both or connects them at authoring time; it must not own the
path update pipeline.

## 13. Inspector and scene proposal

Use simple UI Toolkit component inspectors on GameObjects:

- select path binding and display sample count, closure and length;
- choose direct or dynamic mode;
- list path deformers and vertex deformers in separate inspector sections;
- expose bend angle, pivot, axis, falloff and mask;
- expose wave amplitude, frequency, speed and path direction;
- expose optional vertex-normal offset fields and clearly distinguish them from
  path deformation;
- expose spatial falloff enablement, inner/outer radius, weight and mode;
- draw source gizmos for inner and outer radii;
- expose twist, fixed constraints and reset;
- provide one-shot apply, pause and reset actions;
- display validation messages for missing path metadata or incompatible
  cross-section bindings.

Implemented core scenes:

```text
01_PathAndVertexDeformers.unity
02_SpatialDeformerSources.unity
```

The second scene keeps the renderer and every deformer on separate GameObjects.
Each deformer targets the shared renderer and uses its own transform position as
the spatial falloff centre. Additional open-path, stress and secondary-surface
scenes remain later authoring additions.

Scene view visualisation should draw rest path, live path, tangents and the
selected frame basis. It may sample a one-off GPU snapshot explicitly for
inspection but must not perform per-frame readback.

## 14. Milestones

### M1 - Path contract and ownership

- Add `PathResource`, rest/live buffers, path IDs and lifecycle registration.
- Build a path from Phase 4 tube generation data.
- Validate sample order, arc lengths, endpoint and closure semantics.

**Gate:** a line, circle and knot produce deterministic path resources with no
buffer leaks.

### M2 - Frame reconstruction

- Add live tangents and parallel-transport frames.
- Add closed-loop correction and degenerate-segment diagnostics.
- Compare finite-frame invariants against CPU reference vectors.

**Gate:** adjacent frames rotate minimally and the first/last frames join within
tolerance for a closed knot; each path-skinned vertex receives a finite normal
and tangent basis in the deformed orientation.

### M3 - Immutable tube skinning

- Emit path bindings and cross-section coordinates from tube generation.
- Skin geometry from the live path.
- Recompute path-skinned normals without changing topology.
- Expose the path-skinned position and normal buffers as the base pose consumed
  by subsequent vertex deformation.

**Gate:** an identity path reproduces rest tube positions; deformed paths preserve
ring radius, ring edge lengths and winding before the optional vertex stage.

### M4 - Generic path and vertex deformers

- Implement offset, bend, twist and longitudinal wave.
- Add blend and bounded mask composition.
- Add deterministic reset.
- Add a path-relative `IVertexDeformer` and validate that it follows the
  deformed surface normal after bend and twist.

**Gate:** path deformers run on line, circle and knot resources without
path-type branches. Vertex deformers receive only the committed path-skinned
base pose. A positive scalar vertex offset moves outward along the live base
normal, including on heavily bent segments. Disabling the vertex stage restores
the path-skinned base pose exactly.

### M5 - Dynamic path controls

- Add bounded path force accumulation and optional integration/constraints.
- Keep the direct mode as a deterministic reference.
- Validate finite values and recovery.

**Gate:** maximum documented controls remain finite and dynamic paths recover
within the required tolerance and time.

### M6 - Authoring, tests and performance

- Add UI Toolkit inspectors and the listed test scenes.
- Measure small/medium/large path and tube presets.
- Document supported sample counts, tube sizes and dispatch budgets.

**Gate:** all acceptance tests, existing suites and performance budgets pass.

## 15. Validation and performance

Suggested editor commands:

```text
Unity -batchmode -projectPath ReactiveGeometry -runTests -testPlatform EditMode
Unity -batchmode -projectPath ReactiveGeometry -runTests -testPlatform PlayMode
```

Required measurements:

- path sample and tube vertex counts;
- path/frame/skinning dispatch count;
- path, frame, skinning and normal recomputation cost;
- path-relative vertex deformer/integration and final-normal cost;
- GPU memory per path sample and skinned vertex;
- allocation count and buffer reuse;
- maximum frame angular step;
- base-pose cross-section radius, ring-length and area error;
- final vertex offset direction error relative to the path-skinned normal;
- recovery time and maximum finite displacement;
- closed-seam position and frame error.

Do not include CPU/GPU transfers in the path update loop. Test readback and CPU
reference calculations are permitted only as one-off tests or inspector
diagnostics.

## 16. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Frame flips near inflections or stationary tangents | validate tangent thresholds and use deterministic fallback/up-vector logic |
| Closed paths have visible twist at the seam | apply explicit closed-loop correction and test ring orientation at the seam |
| Cross-section shifts because of frame interpolation | interpolate path-space frame data with the seam-aware rule and validate ring distances |
| Path and vertex solvers both write final positions | make one system authoritative per mode and define the base-pose/offset contract |
| Vertex detail uses stale rest normals | bind path-skinned positions/normals explicitly and enforce stage dependencies before vertex dispatch |
| Vertex offsets accumulate as absolute positions | store and integrate secondary offsets relative to the current path-skinned base pose |
| Vertex normal recomputation changes the direction mid-dispatch | separate base-normal reconstruction from final-normal recomputation and read each from its committed buffer |
| Dynamic path controls diverge from direct reference mode | share deformer logic and test equivalent controls at zero velocity with identical bounds |
| Tube metadata is tied to a specific analytic curve | bind by path sample, distance and cross-section only |
| A component crosses the path/vertex stage boundary | require separate `IPathDeformer` and `IVertexDeformer` registrations with scheduler-enforced stage order |
| One path deformer becomes tube-specific | validate `IPathDeformer` on at least open and closed paths |
| Interaction with highly curved paths causes self-intersection | treat self-collision as unsupported until a spatial broad phase is implemented |
| Path sample count limits visual resolution | publish presets and consider remappable subpaths, not per-frame topology rebuilding |

## 17. Definition of done

Phase 5 is complete when:

1. a shared `PathResource` represents open and closed sampled paths without
   depending on a tube generator;
2. line, circle, helix and knot tubes bind to the path contract;
3. identity skinning reproduces Phase 4 rest positions;
4. path deformation preserves every ring's cross-section within tolerance;
5. transported frames remain finite and pass closed-loop seam tests;
6. bend, twist, offset and longitudinal-wave controls run through generic
   `IPathDeformer` components;
7. path deformation is committed to a GPU base pose before any vertex deformer
   executes;
8. normal-relative vertex deformation runs through separate `IVertexDeformer`
   components and follows the path-skinned live normals and tangent basis, not
   the original rest normals;
9. live path and final vertex positions update without topology rebuilding,
   per-frame readback or unbounded allocation;
10. path deformers cannot write vertex buffers and vertex deformers cannot
     write path buffers;
11. direct and optional dynamic path modes have explicit authority and reset
   semantics;
12. UI Toolkit component inspectors expose authoring and one-shot actions;
13. Phase 1-5 automated suites pass with no registered GPU leaks;
14. small, medium and large presets satisfy documented dispatch, memory and
    frame budgets;
15. audio and XR remain inputs to Phase 5 controls in later phases rather than
    duplicating path deformation.
