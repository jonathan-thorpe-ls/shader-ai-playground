# Phase 6 - Branching Graphs

Implementation plan for deterministic, deformable branching geometry in the
GPU procedural-geometry framework described in
[ProceduralGeometryFramework.md](../ProceduralGeometryFramework.md).

| Item | Decision |
| --- | --- |
| Engine / pipeline | Unity 6.6 / URP 17.6 |
| Target project | `ReactiveGeometry/` |
| Primary example | A rooted, tree-like graph with variable branch length and radius |
| Runtime source of truth | Immutable branch graph plus GPU rest/live branch-path state |
| First deformation primitives | Tip-weighted turbulence and spatial attraction |
| Phase gate | Root and thick lower branches move less than thin terminal tips; child branches remain attached at every junction |
| Rendering slice | Line/glyph graph first, then independent tube skins per branch |
| UI | Simple functional UI Toolkit component inspectors |
| Testing | deterministic plans, GPU invariants, deformation tests, lifecycle and PlayMode rendering |

## Implementation status

The Phase 6 branching vertical slice is implemented in `ReactiveGeometry/`:

- deterministic tree planning with stable branch identity, topology and
  root/junction/tip metadata;
- packed branch-path GPU resources with Phase 5-compatible rest/live frames and
  branch-local tube bindings;
- depth-ordered hierarchy attachment that keeps child starts on live parent
  junctions;
- radius/depth/local-tip flexibility with bounded direct path deformation;
- GPU wind-like `BranchTurbulenceDeformer` and
  transform/local-target `BranchAttractorDeformer`;
- independently skinned branch tubes with line/glyph and one-shot live graph
  overlays;
- a functional `Phase6/01_BranchGraphs` scene and UI Toolkit component
  inspectors;
- EditMode topology, attachment, deformation and lifecycle tests plus a
  PlayMode rendering smoke test.

The first implementation deliberately keeps branch tubes overlapping at
junctions rather than producing one watertight surface. Dynamic path forces,
particle networks, arbitrary cyclic graphs and Voronoi remain outside the
branching phase gate.

## 1. Purpose

Phase 6 turns Phase 5's single-path subsystem into a reusable collection of
connected branch paths. A branch generator emits topology, hierarchy and
semantic metadata; Phase 5-compatible paths deform and skin each branch; a
small hierarchy stage keeps child roots attached to their parent junctions.

The first useful result is a tree-like structure whose thick trunk and lower
branches resist motion while thinner outer branches and terminal tips bend in
wind-like turbulence or towards an attractor. The implementation must not be a
tree-only special case. Roots, veins, coral-like branching, L-systems and other
abstract networks should be able to supply the same graph plan and reuse the
same deformation primitives.

## 2. Scope

### 2.1 In scope

1. A deterministic `BranchGraphPlan` independent of rendering.
2. Root, junction, terminal and branch identity.
3. Parent/child hierarchy, ordered branch samples and graph adjacency.
4. Global root distance, distance to tip, branch-local distance, generation
   depth, radius, stiffness and continuous flexibility metadata.
5. A packed `BranchGraphResource` and reusable `PathSetResource`.
6. Direct reuse of Phase 5 path positions, distances, frames and
   `IPathDeformer` execution semantics.
7. Hierarchical attachment propagation before live path-frame reconstruction.
8. A configurable deterministic tree generator.
9. Wind-like branch turbulence and transform-authored attractor deformation.
10. Line/glyph rendering for topology validation and independent tube skins for
    visual geometry.
11. UI Toolkit inspectors, focused scenes, automated tests, performance presets
    and buffer-lifecycle validation.

### 2.2 Explicitly out of scope

- Rebuilding branch topology, sample counts or radii during play.
- A single watertight surface across branch junctions, mesh booleans or
  subdivision surfaces.
- Physical collision, self-collision or full torsion/rod dynamics.
- Particle networks, spatial grids, Voronoi and cyclic arbitrary graphs.
- Audio mapping; Phase 7 supplies controls to the path deformers.
- XR/provider interaction; Phase 8 supplies spatial inputs and constraints.
- A dedicated runtime control panel. Test scenes use component inspectors.
- Per-frame GPU-to-CPU position, hierarchy or neighbour readback.

## 3. Design principles

1. **The graph is not the rendered mesh.** One graph plan can feed lines,
   points, branch tubes or later secondary geometry.
2. **Branches reuse paths.** A branch is an open ordered path with an optional
   parent attachment, not another copy of tube-frame reconstruction.
3. **Hierarchy is an attachment constraint.** Child roots follow their parent
   live attachment transform exactly; deformers cannot pull a child away from
   its junction.
4. **Metadata controls response.** A deformer reads radius, local path
   position, depth, stiffness and tip weight instead of checking whether the
   structure is a "tree".
5. **Weights are continuous.** IDs and topology are discrete and never
   interpolated as scalars; influence, stiffness and distance are continuous.
6. **Topology is immutable at runtime.** Generation plans all counts, offsets
   and connectivity once. Runtime work is bounded by the committed preset.
7. **Pack related path work.** Branch paths share packed resources and kernels
   where practical, avoiding one GPU buffer set and command schedule per small
   twig.
8. **Performance is correctness.** Every preset publishes sample, branch,
   vertex, dispatch, memory, frame-time and allocation measurements.

## 4. Reusable branch primitives

### 4.1 Graph plan

Add a generator-neutral plan that contains no Unity GPU resources:

```text
BranchGraphPlan
  branches[]
  nodes[]                     optional unique junction/tip identities
  totalPathSamples
  maximumGenerationDepth
  rest bounds

BranchPlan
  BranchId
  ParentBranchId
  ParentSampleIndex
  StartNodeId / EndNodeId
  Samples[]                   position, rest tangent, radius
  RestLength
  GenerationDepth
```

Branch generators implement a common contract:

```text
IBranchGraphGenerator
  BranchGraphPlan Plan(BranchGenerationSettings settings)
```

The Phase 6 built-in generator is deterministic `TreeBranchGenerator`. Its
branching rule remains separate from `BranchGraphPlan`, so later `LSystem`,
`Root`, `Vein` or authored-control-point generators only replace plan creation.
The deformer, resource, validation and rendering layers do not change.

### 4.2 Packed path representation

Generalise Phase 5 by introducing a path-set resource without changing the
single-path contract:

```text
PathSetResource
  PathHeader[]                count, sample offset, open flag, rest length
  rest/live positions
  rest/live frames
  rest distances
  per-sample PathId
```

A `PathResource` remains a valid one-path convenience view. Both single paths
and branch graph paths therefore execute the same frame, turbulence and
skinning kernels. Branch data is stored in branch-major arrays, with exact
offset and count records rather than sentinel-delimited arrays.

All buffers have a committed fixed capacity. Packing order is root first,
then increasing depth, then stable branch ID. This is also the topological
order required for parent-first hierarchy propagation.

### 4.3 Branch graph resource

`BranchGraphResource` owns:

- the immutable graph-plan relationships;
- the packed `PathSetResource`;
- branch/node records and attachment data;
- graph adjacency or topology edges through the existing topology contract;
- the static branch/path metadata attributes;
- lifecycle registration for every GPU buffer.

The graph owns branch topology and path packing. A renderer or deformer does not
allocate an ad-hoc buffer per branch and does not infer parentage from spatial
proximity.

## 5. Structural and physical metadata

Generate branch-level values once, then emit only the required per-sample
lookup or packed attribute:

| Domain | Attributes |
| --- | --- |
| Sample | `PathId`, `BranchId`, `DistanceFromRoot`, `DistanceToTip`, `LocalBranchDistance`, `LocalBranch01`, `GenerationDepth`, `Radius`, `TipWeight`, `Flexibility`, `Stiffness`, `ParentDirection`, rest path basis |
| Edge | Parent/child sample identity, rest length, hierarchy attachment flag |
| Branch | Parent branch, parent attachment sample, child offsets, branch length, terminal flag, generation depth, depth-normalised length, inherited stiffness |
| Geometry | Root IDs, maximum depth, maximum root distance, supported branch/path counts |

`DistanceFromRoot` is accumulated edge/path length, not graph-hop count.
`DistanceToTip` is the minimum accumulated length to a terminal for that
branch path. `LocalBranchDistance` restarts at each branch; `LocalBranch01` is
normalised to that branch length.

Compute a base flexibility mask during planning:

```text
tipInfluence = smoothstep(tipInfluenceStart, 1, LocalBranch01)
depthInfluence = 1 - exp(-depthResponse * GenerationDepth)
radiusInfluence = (smallestSupportedRadius / max(Radius,
                                                smallestSupportedRadius))
                   ^ radiusExponent
flexibility01 = clamp(baseFlexibility
                      * tipInfluence
                      * (0.5 * depthInfluence + 0.5 * radiusInfluence))
baseConstraint = 1 for an authored root or fixed junction
```

Names and exact defaults are authoring details, but the monotonic requirement
is not: equal-sized branches at greater depth may be more flexible, and
larger/lower branches must be no more sensitive than smaller/tip branches for
the same controls.

## 6. Tree generator

The initial `TreeBranchGenerator` uses an injected deterministic sequence or
seed. Its serializable settings include:

- maximum depth;
- branches per branching event;
- maximum branch, path-sample and tip counts;
- initial and per-generation length;
- samples per unit length plus bounded min/max samples;
- initial and per-generation radius;
- branch angle and azimuth spread;
- upward/directional growth bias;
- branch-length/radius variation with seed;
- stiffness and tip-flexibility curves;
- root constraint and branch-rule preset.

Generation proceeds breadth/root first:

1. Emit trunk samples and the root node.
2. At a branch endpoint, emit child branches from the authored rule.
3. Assign parent branch and exact parent attachment distance/sample.
4. Build immutable ordered samples, tangents and transport frames.
5. Calculate local/global distances, depth, radius and tip metadata.
6. Build adjacency, branch records and stable IDs.
7. Validate all counts, finite values and limits before allocating GPU
   resources.

The generator must reject configurations that can exceed fixed caps. Random
variation changes branch geometry only during planning; it never alters the
runtime buffer layout or path sample count.

## 7. Hierarchy propagation and path pipeline

The branch pipeline extends the Phase 5 order:

```text
1. Clear packed path contribution, twist and force buffers
2. Evaluate branch path deformers over all branch samples
3. Propagate branch attachment transforms in root-to-leaf order
4. Apply root/fixed constraints and bounded displacement
5. Reconstruct live path tangents and transported frames
6. Correct path-local frame continuity
7. Skin branch line/tube geometry from committed live paths
8. Recompute branch base normals
9. Evaluate optional vertex deformers
10. Compose/publish final geometry and render/debug state
```

Hierarchy propagation is performed as a bounded sequence of depth kernels, or
one dispatch with only independent nodes at each depth. Child branch samples
are transformed as a coherent authored offset from the live parent attachment,
including the parent-to-child tangent orientation:

```text
childLocalBase = childRestSample - childRestStart
attachmentDelta = parentLiveAttachmentFrame
                * inverse(parentRestAttachmentFrame)
childLiveSample = parentLiveAttachmentPosition
                + attachmentDelta * childLocalBase
                + childContribution
```

The delta frame rotates the whole child's authored branch angle with the
parent while retaining that angle. The child start sample receives zero
branch-local contribution, so a junction never disconnects. If fixed or
constrained authored joints are introduced later, their effect is resolved
between hierarchy propagation and frame construction. A single branch still
uses the Phase 5 direct and dynamic path rules unchanged.

### 7.1 Reusing the Phase 5 stage contracts

The existing `IPathDeformer` remains the preferred deformer boundary. Phase 6
adds a branch-aware context or compatible path-set view containing the current
path header and branch metadata. `IVertexDeformer` stays after branch skinning
and receives only the committed base pose.

Suggested extension, without duplicating single-path deformers:

```text
IPathDeformer
  Record(CommandBuffer, BranchPathDeformerContext)

BranchPathDeformerContext
  PathSet
  current path/sample range
  branch metadata
  deformation buffers
  scheduler inputs
```

If a concrete API change is awkward for the existing Phase 5 context, provide
an adapter that exposes one packed range at a time. The shader kernels and
deformation equations should still be shared.

## 8. Branch deformers

### 8.1 Influence model

All branch path deformers compose a bounded contribution:

```text
response = branch response mask
         * authoring weight
         * deformer weight
         * spatial falloff (when enabled)
```

`branch response mask` is generated from radius, generation, branch-local
position and terminal status. Deformer masks are multiplicative; two effects
cannot accidentally make the root more flexible. Roots and explicit
constraints clamp to zero response.

### 8.2 Turbulence / wind

Add `BranchTurbulenceDeformer`, based on the reusable Phase 5 path-noise model
rather than a separate per-vertex wind shader. Authoring controls include:

- wind direction and strength;
- turbulence amplitude;
- spatial frequency and speed;
- seed, octave count and normalised octave persistence;
- gust response and frequency;
- path contribution bound;
- optional Spatial Source Parameters falloff;
- response-curve overrides and minimum radius effect.

Evaluate smooth periodic noise in a stable path-local domain and project it
perpendicular to the rest/live branch tangent. A shared `flexibility01` field
scales the contribution. With identical controls:

- the trunk/root contribution remains zero or near zero;
- thick, lower generations receive less displacement;
- thin terminal branches receive the greatest allowed displacement;
- an attached child follows its parent's live junction;
- phase wraps continuously for deterministic replay.

Turbulence is a generic `PathNoise` primitive with a branch response mask, so
it also applies to tubes, cables, tendrils or separate unconnected paths.

### 8.3 Attractor

Add `BranchAttractorDeformer`. The target is a scene Transform converted to
renderer-local space before binding, or an explicitly local point. Controls
include strength, inner/outer influence radius, response power, maximum bend
angle/displacement and optional attraction falloff.

For each branch sample:

1. Compute the vector from the live/rest sample to the target.
2. Remove the component parallel to the branch tangent.
3. Scale the perpendicular direction by spatial influence and tip flexibility.
4. Clamp the contribution to the configured angular or displacement bound.
5. Resolve through hierarchy attachment so each child tip approaches the
   target without detaching from its parent.

The attraction equation is a reusable point-target path deformer. It must not
contain assumptions about tree growth or generation order and can later drive
tubes, tentacles, cables or other path collections.

### 8.4 Optional dynamic response

Direct target mode is implemented first for deterministic tests. Dynamic mode
can expose turbulence and attraction as bounded forces through the existing
`IDynamicPathDeformer` mechanism. It uses the branch stiffness/flexibility
metadata, bounded velocity and damping. Direct mode remains the reference for
GPU numeric and tip-direction tests.

## 9. Rendering

### 9.1 Topology view

Implement a branch graph line/glyph representation before surface skinning:

- path edges as lines;
- roots, junctions and tips as glyphs;
- colour by branch, generation, distance, flexibility or selected deformer
  contribution;
- rest/live overlays;
- parent attachment and path tangent/frame gizmos.

This is a diagnostic output and a valid first sample, not the final visual
surface.

### 9.2 Tube skins

Reuse Phase 5 path frame and tube skinning for each branch. Each branch owns
immutable ring/cross-section bindings and radius-over-distance data. Generate
the branch tubes in a packed vertex/index range and skin them from the packed
path set.

The initial junction policy is explicit visual overlap: a child tube starts at
its parent attachment and may overlap/interpenetrate the parent. Watertight
junction topology is a later rendering enhancement. This keeps branch path
deformation testable without coupling it to remeshing.

Branch normals are recomputed in the same downstream path-skinning stage.
Tube ring radius is immutable before the optional vertex stage, matching the
Phase 5 cross-section invariant.

## 10. Authoring and test scenes

Use UI Toolkit custom inspectors and component separators rather than one
monolithic authoring class:

- `BranchGraphGenerator` or `ProceduralBranchRenderer`;
- `BranchTurbulenceDeformer`;
- `BranchAttractorDeformer`;
- optional shared branch/path debug component.

Required inspector functionality:

- choose a branch rule/preset and seed;
- display committed branch, sample and vertex counts;
- expose caps and show validation errors before GPU allocation;
- edit generation, length/radius, stiffness and flexibility curves;
- enable/reset/regenerate deterministic plans;
- edit deformer strength and bounding controls;
- author one attractor Transform and wind/turbulence direction;
- draw source gizmos, branch IDs, roots, junctions and tips.

Recommended scenes:

```text
Phase6/01_BranchGraphTopology
Phase6/02_BranchPathTurbulence
Phase6/03_BranchPathAttractor
Phase6/04_BranchTubeSkins
```

## 11. Proposed package layout

```text
Runtime/
  Branches/
    BranchGraphPlan.cs
    BranchPlan.cs
    BranchMetadataBuilder.cs
    BranchGraphResource.cs
    BranchTopologyBuilder.cs
    IBranchGraphGenerator.cs
    TreeBranchGenerator.cs
  Paths/
    PathSetResource.cs
    PathSetDeformationPipeline.cs
    BranchPathAttachment.cs
  Rendering/
    BranchLineRenderer.cs
    BranchTubeBindingBuilder.cs
  Shaders/Resources/ProcGeo/
    BranchHierarchy.compute
    BranchTurbulence.compute
    BranchAttractor.compute
    BranchGraphLines.compute

Runtime.URP/
  Authoring/
    ProceduralBranchRenderer.cs
    BranchTurbulenceDeformer.cs
    BranchAttractorDeformer.cs

Editor/
  Branches/
    BranchGeneratorEditor.cs
    BranchDeformerEditors.cs

Tests/
  EditMode/
    BranchGraphPlanTests.cs
    BranchPathResourceTests.cs
    BranchAttachmentTests.cs
    BranchTurbulenceTests.cs
    BranchAttractorTests.cs
  PlayMode/
    BranchGraphRenderingTests.cs
```

Names should follow the package's existing conventions after the runtime spike
confirms the smallest compatible API change.

## 12. Validation

### 12.1 Topology and metadata

- The same seed and settings produce the same node, branch and sample arrays.
- Every edge and parent relationship references valid committed IDs.
- Exactly one branch-root is unparented unless a multi-root preset is explicitly
  introduced.
- Parent-first branch order satisfies depth constraints.
- Child start position equals its parent attachment position in the rest pose.
- `DistanceFromRoot` equals accumulated edge/sample length within tolerance.
- Distances, radii, stiffness and flexibility are finite and within configured
  ranges.
- Stable IDs are not smoothly interpolated.

### 12.2 Deformation and attachment

- At zero controls, live paths reproduce the rest pose.
- Every child start remains equal to its live parent attachment.
- A root sample stays fixed under turbulence and attraction unless explicitly
  configured otherwise.
- Thick/lower branches have no greater flexibility response than thin/terminal
  branches under the same target response curve.
- Turbulence is finite, bounded and deterministic for fixed time, controls and
  seed.
- Attraction bends tip tangents towards a target and reduces tip-to-target
  distance without detaching branch starts.
- Increasing attraction strength monotonically increases the response up to
  the configured bound.
- Direct and dynamic zero-velocity modes remain within documented tolerance.
- Reset restores the whole graph within bounded time and without topology
  mutation.

### 12.3 Path and rendering

- Identity skinning reproduces Phase 5 path rest positions.
- Branch tube rings preserve authored radius, ordering and winding before the
  vertex stage.
- Frame reconstruction remains finite at branch ends.
- Final normals follow the branch-skinned base pose.
- Enabling only hierarchy attachment does not produce per-branch drift.
- Rest/live and metadata debug views identify the correct IDs and values.

### 12.4 Lifecycle and performance

- Regeneration disposes prior resources once and never leaks tracked buffers.
- No branch update performs per-frame GPU-to-CPU readback.
- No branch update performs per-branch managed allocation or topology rebuild.
- Dispatch count is bounded by path work plus configured maximum generation
  depth, and is published per preset.
- Record branch, path sample, node, edge, tube vertex and primitive counts.
- Record path deformation, hierarchy, frames, skinning, normals and draw cost.
- Record GPU bytes, allocation churn and buffer reuse.
- Establish small/medium/large presets on the same target graphics API and
  hardware; fail acceptance when the documented whole-frame budget is
  exceeded.

## 13. Milestones

### M1 - Branch plan and deterministic tree

- Introduce branch plan types, generator contract and deterministic tree rule.
- Validate topology, ordering, counts, distances and parent attachments.

**Gate:** the same seed/settings produce equal plans; all plans are finite and
within caps.

### M2 - Packed graph and path-set resources

- Add `PathSetResource`, branch records and lifecycle ownership.
- Expose a packed equivalent of a Phase 5 path without changing single-path
  behaviour.
- Emit line graph and semantic metadata.

**Gate:** line rendering and all existing Phase 5 path tests remain correct
without per-branch GPU allocations.

### M3 - Hierarchy attachment

- Add depth-ordered attachment propagation.
- Keep child roots attached while parent paths deform.
- Add root/fixed constraints and reset.

**Gate:** child/parent attachment error remains below documented tolerance for
all samples in stress presets.

### M4 - Turbulence

- Generalise Phase 5 path noise into bounded branch/path turbulence.
- Add radius/depth/tip response, wind direction and deterministic gust controls.
- Validate trunk-to-tip response monotonicity for a representative tree.

**Gate:** supported controls are finite, repeatable and bounded; terminal
branches move more than lower/larger branches.

### M5 - Attraction

- Add transform-local target binding, spatial falloff and bounded tip-directed
  bend.
- Compose attraction with turbulence.

**Gate:** branch tips move towards the target with intact attachments and the
root remains fixed.

### M6 - Tube skins and authoring

- Build packed branch-tube bindings from Phase 5 path data.
- Add line, glyph, rest/live, ID and metadata views.
- Add simple UI Toolkit component inspectors and four listed scenes.

**Gate:** tree tube skins preserve every branch ring before optional vertex
deformation and the scenes require no runtime control panel.

### M7 - Performance and regression

- Define supported presets and baseline hardware/API.
- Measure dispatch, frame, memory, allocation, recovery and attachment error.
- Run Phase 1-6 EditMode and PlayMode suites and lifecycle checks.

**Gate:** published presets meet real-time budgets and all regression/lifecycle
tests pass.

## 14. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| One dispatch per twig destroys scalability | pack paths and branches, dispatch by path/sample count, and use only depth-ordered hierarchy passes |
| Child junctions detach during independent path deformation | zero child-start local contribution and resolve parent attachment before frame construction |
| Lower branches move as strongly as tips | derive a continuous monotonic flexibility mask from radius, depth and local branch position; add monotonicity tests |
| Turbulence appears as vertex noise rather than branch bending | apply it at the path domain before frame reconstruction and skinning |
| Attractor shortens or translates whole branches | use perpendicular path contribution, bounded bend/displacement and hierarchy attachment |
| Parallel transport flips at sharp branch curves | use Phase 5 finite-frame rules and deterministic fallback plus stress tests |
| Branch tubes expose gaps at junctions | document independent overlapping tube skins for Phase 6; defer watertight junctions |
| Branch IDs interpolated as floats | keep all IDs discrete in their own integer attributes |
| Exponential generators exceed GPU caps | validate plan counts before resource allocation and reject over-limit settings |
| Graph-specific deformers prevent reuse | keep deformation contracts path/metadata based and validate on an isolated branch and cable |
| Parent transform composition creates excessive passes | cap generation depth, order by depth and profile the packed hierarchy passes |
| Dynamic mode diverges from deterministic direct mode | use shared contribution kernels and compare zero-velocity cases |

## 15. Definition of done

Phase 6 is complete when:

1. a deterministic graph plan represents rooted branches, paths, hierarchy and
   stable identities without depending on a renderer;
2. the same tree plan supports line/glyph and tube representations;
3. packed branch paths reuse Phase 5 sampling, frame reconstruction and
   path-skinning rules;
4. every child branch remains attached to its parent live junction;
5. required branch and response metadata is generated once and available on GPU;
6. turbulence and attraction act at the path domain, not as final-vertex
   shortcuts;
7. lower/larger branches respond less than terminal/thinner branches under the
   tested authoring model;
8. isolated paths and branch paths can use the same deformer implementations;
9. direct controls are deterministic, bounded and resettable;
10. UI Toolkit component inspectors and four functional test scenes are present;
11. topology, attachment, deformation, rendering and lifecycle tests pass;
12. supported presets meet documented real-time dispatch, memory and allocation
    budgets;
13. no runtime branch update performs readback or topology rebuilding;
14. audio remains an input to branch path controls for Phase 7, while XR remains
    an input for Phase 8.
