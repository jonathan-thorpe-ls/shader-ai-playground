# Phase 1 — GPU Geometry Generators, Metadata and Visualisation

Implementation plan for the first phase of the GPU audio-reactive procedural geometry framework described in [Docs/Stubs](../Stubs/).

| Item | Decision |
| --- | --- |
| Engine | Unity 6.6 |
| Pipeline | URP 17.6 (render graph) — the only supported pipeline |
| Target project | `ReactiveGeometry/` |
| Testing | Unity Test Framework run via the Unity CLI (`unity test`) locally and in CI |
| Agentic tooling | Unity CLI (`unity`) + Pipeline package (`com.unity.pipeline`) for live Editor control — see §12.1 |
| Primary tech | Compute shaders, `GraphicsBuffer`, GPU-writable `Mesh` buffers, `Graphics.RenderPrimitives*` |

---

## 1. Scope

### 1.1 In scope

1. A **geometry data contract** shared by all generators (geometry, topology, metadata, live state slots).
2. **Compute-shader generators**: icosphere (geodesic), parametric surfaces (plane, sine, saddle, twist, cylinder, torus), tube along a curve. Branching graph as a stretch milestone to prove non-triangle topology.
3. A **generic GPU topology builder** producing deterministic CSR neighbour connectivity, an edge list, rest lengths and vertex→face incidence from any triangle or line primitive buffer.
4. A **metadata (attribute) system**: discoverable, typed, domain-aware attributes stored in GPU buffers, plus reusable GPU *attribute operators* (graph distance, normalise, remap, curvature…) to derive them.
5. **Rendering in URP** directly from GPU buffers with no per-frame CPU readback, compatible with URP Lit / Shader Graph, shadows, depth/normals prepasses and XR single-pass instanced.
6. **Visualisation and debug tools** for geometry, topology and metadata.
7. **Automated validation** (EditMode GPU tests, graphics tests, performance tests) runnable via Unity CLI.

### 1.2 Out of scope (designed for, not implemented)

Mass-spring / XPBD simulation, wave propagation, audio analysis, VR interaction, particle networks, Voronoi. Phase 1 must, however, **produce every input those systems need** (§7) and reserve the live-state buffers they will write to.

### 1.3 Guiding principles (from the stubs)

- Geometry generation, simulation, interaction, audio and rendering are independent modules communicating through GPU buffers.
- The GPU is the source of truth. CPU readback is allowed only for one-off generation metadata (bounds/counts), editor tooling and tests — never per frame.
- Geometry is **self-describing**: generators emit structure *and* semantics.
- Rendering indices ≠ simulation connectivity. Both are produced, separately.
- Determinism: same settings + seed ⇒ bit-identical buffers.

---

## 2. Repository and package layout

The framework lives in an embedded package inside the Unity project, keeping it reusable and its tests isolated from sample content.

```
ReactiveGeometry/Packages/
  com.sonosthesia.procgeo/
    package.json
    Runtime/
      Core/            GeometryResource, GeometryLayout, GpuBufferRegistry, GeometryHeader
      Attributes/      AttributeDescriptor, AttributeSet, AttributeIds, operators (C# side)
      Topology/        TopologyBuilder (C# dispatch)
      Generators/      IGeometryGenerator, generator assets + settings
      Scheduling/      GeometryCommandScheduler
      Shaders/
        Include/       ProcGeoCommon.hlsl, ProcGeoAttributes.hlsl, ProcGeoTopology.hlsl, Random.hlsl
        Generators/    Icosphere.compute, ParametricSurface.compute, Tube.compute, Branching.compute
        Topology/      TopologyBuild.compute, Scan.compute, Reduce.compute
        Attributes/    AttributeOps.compute, Normals.compute
    Runtime.URP/
      ProceduralMeshRenderer.cs   (Mesh bridge path)
      ProceduralDrawRenderer.cs   (RenderPrimitives path)
      Shaders/ ProcGeoLit.shader, ProcGeoDebug.shader, ProcGeoLines.shader, ProcGeoGlyphs.shader
      ShaderGraph/ SampleGeometryAttribute.shadersubgraph + .hlsl
    Debug/              runtime debug overlays (usable in player builds / XR)
    Editor/             Geometry Lab window, inspectors, gizmos, mesh export
    Tests/
      EditMode/         GPU generator + topology + attribute tests
      PlayMode/         graphics (image) tests, performance tests
    Samples~/           demo scenes
```

Embedded packages are picked up automatically. Already installed: `com.unity.pipeline` 1.0.0 and `com.unity.test-framework` 1.8.0. Still to add: `com.unity.shadergraph` if not pulled in by URP, the Graphics Test Framework and Performance Testing packages, and `com.sonosthesia.procgeo` under `"testables"`.

Assemblies: `Sonosthesia.ProcGeo.Runtime` (generation, topology, attributes — no rendering dependency, so it can be tested in isolation), `.Runtime.URP`, `.Debug`, `.Editor`, `.Tests.EditMode`, `.Tests.PlayMode`.

A small CPU reference icosphere builder (recursive midpoint subdivision with a midpoint cache) lives in the EditMode test assembly to cross-check the GPU generator.

---

## 3. Milestone 0 — Platform spikes

Each spike is a throwaway scene/test that de-risks an API assumption on Unity 6.6. Results are recorded in this document before Milestone 1 locks the design.

| # | Spike | Question answered |
| --- | --- | --- |
| S0 | Baseline `ReactiveGeometry/` (Unity 6.6, URP 17.6): confirm URP asset/renderer settings, enabled graphics APIs (D3D12/Vulkan), XR plug-in setup | Project builds from CLI; render graph only; settings documented |
| S1 | Compute writes into `Mesh` vertex/index buffers (`mesh.vertexBufferTarget \|= GraphicsBuffer.Target.Raw`, `indexBufferTarget` likewise) rendered by `MeshRenderer` + URP Lit | Shadows, depth prepass, SSAO, SRP Batcher, GPU Resident Drawer interaction (expect: exclude GRD) |
| S2 | `Graphics.RenderPrimitivesIndexed` / `RenderPrimitivesIndexedIndirect` with a custom URP shader | Works in Scene + Game view, render graph, correct pass tags |
| S3 | XR single-pass instanced (Mock HMD / OpenXR) for S1 and S2 | Procedural draws need stereo instancing macros + instance-count handling |
| S4 | `GraphicsBuffer` created with `Target.Structured \| Target.Raw` | Enables generic `ByteAddressBuffer` debug reads of any attribute; fallback is a debug copy kernel |
| S5 | Shader Graph Custom Function reading `StructuredBuffer` by Vertex ID | Metadata available to artist-authored materials on the mesh-bridge path |
| S6 | CLI test run on dev machine and CI agent with a real graphics device (no `-nographics`) | `SystemInfo.supportsComputeShaders` true, compute dispatch + readback work headless. *Dev machine partially confirmed via Pipeline `script eval`: Unity 6000.6.9f1, Direct3D12, NVIDIA RTX 3060 Laptop, compute supported, URP asset `PC_RPAsset` active* |
| S7 | Motion vectors for GPU-deformed meshes | How to supply previous positions (custom `MotionVectors` pass) — needed later for TAA/STP and XR spacewarp; Phase 1 only documents the approach |

---

## 4. Data contract

### 4.1 `GeometryResource`

A `GeometryResource` (C#, `IDisposable`) owns all GPU buffers for one generated object. It is created from a `GeometryLayout` (capacities computed on the CPU from settings) and filled entirely by GPU passes.

```csharp
public enum PrimitiveTopology { Triangles, Lines, Points }

public sealed class GeometryResource : IDisposable
{
    public GeometryLayout Layout { get; }          // capacities, topology, flags
    public GraphicsBuffer Header { get; }          // GPU-written counts + bounds
    public GraphicsBuffer Indices { get; }         // render primitives
    public GraphicsBuffer DrawArgs { get; }        // indirect args, GPU-written
    public TopologyBuffers Topology { get; }       // CSR, edges, incidence
    public AttributeSet Attributes { get; }        // all metadata incl. rest geometry
    public LiveStateBuffers Live { get; }          // positions/normals consumed by renderers
    public Bounds LocalBounds { get; }             // from one-off header readback + margin
    public int Version { get; }                    // bumped on regeneration
}
```

**Capacities vs. counts.** Analytic generators know exact counts. Future generators (particle networks, Voronoi) only know upper bounds, so actual counts are written by the GPU into `Header`, and all draws/dispatches that depend on them use `DrawArgs` / indirect dispatch arguments. Phase 1 builds this path even though its generators are exact.

```hlsl
struct GeometryHeader
{
    uint vertexCount; uint primitiveCount; uint edgeCount; uint regionCount;
    float3 boundsMin; uint flags;      // flags: manifold, closed, orderedOneRing, hasBoundary, validationFailed
    float3 boundsMax; uint maxDegree;
};
```

### 4.2 Topology buffers (simulation connectivity)

Compressed sparse row, undirected graph, deterministic ordering.

| Buffer | Type | Size | Notes |
| --- | --- | --- | --- |
| `_NeighbourOffsets` | `uint` | V+1 | CSR offsets |
| `_Neighbours` | `uint` | 2E | Sorted ascending, then cyclically ordered where `orderedOneRing` |
| `_NeighbourEdge` | `uint` | 2E | Edge id for each neighbour slot → reach per-edge data from the vertex view |
| `_Edges` | `uint2` | E | `x < y`, sorted |
| `_EdgeRestLength` | `float` | E | From rest positions |
| `_EdgeType` | `uint` | E | Structural / shear / bend / long-range (Phase 1: structural only; enum fixed now) |
| `_VertexFaceOffsets`, `_VertexFaces` | `uint` | V+1, 3F | Vertex→face incidence for normals and one-ring ordering |

A fixed-`MAX_NEIGHBOURS` view is **not** stored; it can be produced from CSR by a conversion kernel later if profiling favours it.

### 4.3 Live state (renderer input, future simulation output)

| Buffer | Type | Phase 1 writer | Future writer |
| --- | --- | --- | --- |
| `_LivePositions` | `float3` | Rest copy / test deformer | Simulation final substep |
| `_LiveNormals` | `float3` (or oct-encoded) | Normals pass | Normals pass |
| `_PrevPositions` | `float3` | Copy before update | Same (motion vectors) |

On the mesh-bridge path, `_LivePositions`/`_LiveNormals` **are** the mesh's dynamic vertex stream (§6.2) so no extra copy is needed. Simulation ping-pong buffers are owned by the future simulation module, not by `GeometryResource`.

### 4.4 Element domains

Every attribute belongs to one domain: `Vertex`, `Edge`, `Primitive` (face/segment), `Region`, `Geometry`. Region-level data (e.g. per-branch stiffness) is stored once and referenced via a `RegionId` vertex attribute.

---

## 5. Metadata / attribute system

### 5.1 Descriptors

```csharp
public enum AttributeDomain { Vertex, Edge, Primitive, Region, Geometry }
public enum AttributeFormat { Float, Float2, Float3, Float4, UInt, UInt2 }
public enum AttributeInterpolation { Smooth, Flat }        // IDs are Flat
public enum AttributeLifetime { Static, Dynamic }

public sealed class AttributeDescriptor
{
    public AttributeId Id;            // stable, interned (e.g. "DistanceFromRoot")
    public AttributeDomain Domain;
    public AttributeFormat Format;
    public AttributeInterpolation Interpolation;
    public AttributeLifetime Lifetime;
    public string Units;              // "m", "normalised", "rad", "id", "s"...
    public Vector2 NominalRange;      // display hint; actual range from GPU reduction
    public string Description;
}
```

- `AttributeSet` maps `AttributeId → (descriptor, GraphicsBuffer)` and exposes `TryGet`, `Require` (throws with a clear message if missing — never silently binds a mismatched buffer), and binding helpers for `ComputeShader`, `CommandBuffer`, `MaterialPropertyBlock`.
- Shader binding convention: `_Attr_<Name>` (e.g. `_Attr_DistanceFromRoot`), plus `_AttrPresent` bitmask for optional attributes.
- Consumers (future deformation fields, interaction profiles, shaders) declare `Requires` / `Produces` lists of `AttributeId`, validated at bind time — this is the hook for the "deformation field" and "geometry response profile" concepts in stubs 3 and 4.

### 5.2 Phase 1 attribute set

Core (every surface generator):

| Attribute | Domain | Format | Source |
| --- | --- | --- | --- |
| `RestPosition` | Vertex | Float3 | Generator |
| `RestNormal` | Vertex | Float3 | Generator (analytic) or normals pass |
| `RestTangent` | Vertex | Float4 | Generator (w = handedness) |
| `LocalCoord` | Vertex | Float2/3 | Generator (UV, (u,θ) on tube, barycentric-face on icosphere) |
| `RegionId` | Vertex | UInt, Flat | Generator (icosahedron face, grid tile, tube segment, branch) |
| `BoundaryWeight` | Vertex | Float | Topology builder (boundary flag) → operator |
| `NeighbourCount` | Vertex | UInt | Topology builder |
| `Curvature` | Vertex | Float | Operator (umbrella / cotan Laplacian on rest positions) |
| `EdgeRestLength` | Edge | Float | Topology builder |

Structural (when applicable):

| Attribute | Generators |
| --- | --- |
| `DistanceFromRoot` / `DistanceFromRoot01` | Tube (arc length), Branching; any generator via graph-distance operator from a source set |
| `LocalDistance` | Tube, Branching (branch-local arc length) |
| `GenerationDepth`, `ParentId`, `BranchId` | Branching |
| `Direction` | Tube/Branching tangent, grid u-axis |
| `DistanceToBoundary` | Grid, open tube (graph distance from boundary) |

Physical / interaction defaults (filled by recipes, consumed in later phases):

| Attribute | Format | Notes |
| --- | --- | --- |
| `Mass`, `Stiffness`, `Damping`, `ConstraintWeight` | Float | Simulation phase |
| `AudioSensitivity` | Float4 | Bass, mid, high, onset weights |
| `WaveSpeed`, `WaveAttenuation` | Float | Propagation phase |
| `InteractionWeight`, `GrabWeight` | Float | VR phase |

Dynamic attribute *slots* (`Excitation`, `WaveValue`, `DeformationWeight`) are declared in the registry with `Lifetime = Dynamic` but not allocated until a consumer requests them.

### 5.3 Attribute operators (GPU, generic)

Small reusable kernels in `AttributeOps.compute`, configured per generator by an **attribute recipe** (serialised list of operations on the generator asset):

| Operator | Description |
| --- | --- |
| `Fill(constant)` | Constant value |
| `GraphDistance(sources)` | Edge-length-weighted shortest path over CSR (iterative relaxation; GPU "changed" flag makes trailing iterations no-ops, no readback) |
| `Normalise` | Min/max via GPU reduction → `*01` attribute; range also written to a small stats buffer used by debug views |
| `Remap(curve)` | `AnimationCurve` baked to a 256-sample buffer |
| `Combine(a, b, op)` | Mul / add / lerp |
| `Curvature` | Signed mean-curvature estimate |
| `HashToRegion` | Deterministic region assignment (PCG hash) |

Example recipe: `Stiffness = Remap(DistanceFromRoot01, stiffCurve)`; `AudioSensitivity.z = Remap(Curvature, highFreqCurve)`. This lets geometry carry its own reactive character without generator-specific simulation code.

---

## 6. Rendering in URP

### 6.1 Two complementary paths

| Path | Mechanism | Used for |
| --- | --- | --- |
| **Mesh bridge** (primary surface path) | `Mesh` with GPU-writable vertex/index buffers, written by compute, drawn by `MeshRenderer` (or `Graphics.RenderMesh`) | Production surfaces: URP Lit, Shader Graph, shadows, depth/normals, SSAO, decals, XR — all for free |
| **Procedural draw** | `Graphics.RenderPrimitivesIndexed(Indirect)` with `RenderParams` and custom shaders reading `StructuredBuffer`s via `SV_VertexID` | Debug overlays, wireframe, points, glyphs, line/graph geometry (branching, future particle networks) |

### 6.2 Mesh bridge details

- Vertex layout, two streams:
  - Stream 0 (dynamic, written per update): `Position float3`, `Normal float3` (later `Tangent` if needed).
  - Stream 1 (static, written once): `Tangent float4`, `TexCoord0 = LocalCoord`, `TexCoord1..3` = packed hot metadata selected by the recipe (e.g. `DistanceFromRoot01`, `RegionId` as float, `AudioSensitivity`). This makes the most common metadata available to stock Shader Graph without custom nodes.
- `mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw`, `mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw`; buffers obtained with `GetVertexBuffer(stream)` / `GetIndexBuffer()` and written by a `MeshExport.compute` kernel using `RWByteAddressBuffer` (with stride/offset constants so layout changes don't require kernel changes).
- `mesh.bounds` set from the GPU header (one-off async readback after generation) plus a configurable deformation margin. No per-frame bounds readback.
- Vertex ids in the mesh equal attribute buffer indices (no remapping, no seam splitting in Phase 1), so any metadata buffer can also be sampled by Vertex ID.
- Seams: wrapped surfaces (torus, cylinder, tube) are welded for simulation; `LocalCoord` is stored periodic. Shaders that need seam-free UVs use the analytic coordinate (angle) rather than interpolated UVs. Optional render-only seam split is deferred.
- `MaterialPropertyBlock` breaks SRP Batcher compatibility for that renderer; acceptable for a handful of objects. Alternative: per-object material instance with `SetBuffer`.
- Exclude from GPU Resident Drawer (dynamic mesh), confirmed in S1.

### 6.3 Shader access to metadata

- `ProcGeoAttributes.hlsl`: declarations + accessors (`ProcGeo_LoadFloat(attrBuffer, vertexId)`), guarded by `_AttrPresent` bits.
- Shader Graph sub-graph **Sample Geometry Attribute** (Custom Function + Vertex ID node) for attributes not packed into vertex streams.
- Flat attributes (IDs) use `nointerpolation` in hand-written shaders; Shader Graph reads them per-fragment via primitive lookup or treats them as per-vertex in the vertex stage only.

### 6.4 Procedural draw shaders

`ProcGeoLit.shader` (reference lit shader for procedural draws) implements `UniversalForward`, `ShadowCaster`, `DepthOnly`, `DepthNormals`, and is XR single-pass-instanced aware (`UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_VERTEX_OUTPUT_STEREO`, instance id ⇒ eye index handling as established in S3). Debug shaders are unlit, depth-tested with optional x-ray mode.

### 6.5 Update scheduling

- `GeometryCommandScheduler` records generation and per-frame passes into a `CommandBuffer` executed before camera rendering (`RenderPipelineManager.beginContextRendering`), so all cameras (incl. both XR eyes and Scene view) see the same state.
- Generation is event-driven (settings change / explicit request). Per-frame work in Phase 1 is only the optional test deformer + normals + mesh export.
- Optional later: move per-frame passes into a render graph compute pass via a `ScriptableRendererFeature`, and evaluate async compute for simulation.
- Every pass is wrapped in a `ProfilingSampler` so it appears in the Profiler (GPU module), Frame Debugger and RenderDoc captures.

### 6.6 Normals pass (generic)

Per vertex, area-weighted sum of incident face normals using `_VertexFaceOffsets/_VertexFaces` — no atomics, no inter-thread writes. For lines/points topology, normals are not computed (glyph shaders use `Direction`). Analytic generators also write `RestNormal` directly, which the tests compare against the generic pass.

### 6.7 Test deformer (debug only)

A tiny kernel that displaces `_LivePositions` along `RestNormal` by `amplitude * sin(k * attr - ωt)` where `attr` is any selected scalar attribute. It is not the simulation; it exists to validate the dynamic rendering path (stream writes, normals recompute, bounds margin, prev positions) and to visually prove that metadata is meaningful (e.g. a wave following `DistanceFromRoot`).

---

## 7. Generators

All generators implement:

```csharp
public interface IGeometryGenerator
{
    GeometryLayout Plan();                                       // CPU: capacities, topology, attribute list
    void Record(CommandBuffer cmd, GeometryResource target);     // GPU: positions, indices, generator attributes
}
```

Generators are `ScriptableObject` assets (`GeometryGeneratorAsset`) holding serialised settings + attribute recipe. After `Record`, the shared pipeline runs: **TopologyBuilder → Normals (if needed) → Attribute recipe → Validation (optional) → Mesh export**.

Randomness uses a PCG hash of `(seed, elementId)`; no order-dependent RNG.

### 7.1 Icosphere (geodesic, frequency *n*)

GPU-friendly deduplication **by construction** rather than hashing:

- Use geodesic frequency `n ≥ 1` (`n = 2^s` matches recursive subdivision topology).
- Canonical vertex enumeration:
  - corners `[0, 12)`
  - edge interiors: `12 + e·(n−1) + k` for the 30 icosahedron edges, `k` measured from the lower-index corner
  - face interiors: `12 + 30(n−1) + f·(n−1)(n−2)/2 + tri(i, j)`
- `ResolveVertex(face, i, j)` maps any face-lattice point to its canonical id — the single function that guarantees shared midpoints are never duplicated.
- Kernels: `IcoVertices` (one thread per unique vertex, projects barycentric point to sphere), `IcoTriangles` (one thread per output triangle, `20n²` total).
- Projection modes: normalised (gnomonic) and slerp-based (more uniform); recursive-midpoint-equivalent positions optional.
- Attributes: `RegionId` = icosahedron face, `LocalCoord` = face barycentric, analytic `RestNormal`/`RestTangent`.
- Invariants: `V = 10n² + 2`, `E = 30n²`, `F = 20n²`, `V − E + F = 2`, 12 vertices of degree 5, rest degree 6.

### 7.2 Parametric surface

One kernel family, surface function selected by `multi_compile` keyword: `PLANE`, `SINE`, `SADDLE`, `TWIST`, `CYLINDER`, `TORUS`.

- Settings: `ResolutionU/V`, `SizeU/V`, `WrapU/WrapV` (cylinder: U; torus: U+V), shape parameters, `Diagonals` (grid shear edges as `EdgeType.Shear`).
- Wrapped directions share vertices (closed topology for simulation).
- Analytic normals/tangents from partial derivatives (central differences of the function, not of neighbours).
- Attributes: `LocalCoord = (u, v)`, `GridCoordinates` (uint2), `RegionId` = tile id, `DistanceToBoundary`, `BoundaryWeight`.

### 7.3 Tube along a curve

- Curve sources: analytic on GPU (line, helix, torus knot, Lissajous) or a control-point buffer uploaded from the CPU (Catmull-Rom).
- Frames: rotation-minimising frames via the double-reflection method. This is sequential along the curve, so one thread per curve computes `SegmentCount` frames into a frame buffer (generation-time only, cost negligible); rings are then generated fully in parallel.
- Settings: `SegmentCount`, `RadialResolution`, `Radius` (+ radius-over-length curve), `ClosedLoop`, `Caps`.
- Attributes: `DistanceFromRoot` (arc length), `LocalCoord = (s, θ)`, `Direction` (tangent), `RadialDirection`, `RestRadius`, `TwistAngle`, `RegionId` = segment.
- Caps add a centre vertex per end, flagged as their own region.

### 7.4 Stretch — Branching graph (line topology)

Proves the contract for non-surface geometry and is the richest metadata producer.

- Deterministic L-system-like expansion on the GPU, one generation per dispatch (node count per generation is known from `BranchesPerNode^depth`, so layout is exact).
- `PrimitiveTopology.Lines`; rendered via the procedural line/glyph path; no faces, no normals.
- Attributes: `DistanceFromRoot`, `LocalDistance`, `GenerationDepth`, `BranchId`, `ParentId`, `Direction`, `TipWeight`, `JunctionWeight`, region table with per-branch length/radius.
- Tubular skinning around the graph is deferred.

---

## 8. Generic GPU topology builder

Input: primitive index buffer (triangles or lines) + vertex count. Output: §4.2 buffers. Deterministic regardless of thread scheduling.

1. **Count** — per primitive edge `(a,b)`: `InterlockedAdd` degree for both `a` and `b`; per triangle: incidence count per corner.
2. **Scan** — exclusive prefix sum (`Scan.compute`, multi-level, supports millions of elements) → provisional offsets.
3. **Scatter** — write neighbours via atomic cursors (order nondeterministic at this stage).
4. **Sort + dedupe per vertex** — insertion sort within each vertex range (degrees are small; vertices above `MAX_SORT_DEGREE` set a header flag and fail validation).
5. **Compact** — second scan over unique counts → final `_NeighbourOffsets`, `_Neighbours`.
6. **Edges** — each vertex emits edges to neighbours with larger id; scan gives deterministic edge ids; fill `_NeighbourEdge` by lookup; compute `_EdgeRestLength`.
7. **Incidence** — same count/scan/scatter/sort pattern for vertex→face.
8. **One-ring ordering** (manifold triangle meshes) — per vertex, walk incident faces to order neighbours cyclically; boundary vertices start at their boundary edge; sets `orderedOneRing`. Enables cheap one-ring normals, Laplacians and curvature later.
9. **Boundary detection** — an edge used by exactly one triangle marks both vertices as boundary.

Analytic connectivity fast paths for grid/tube can be added later if profiling shows the builder matters; generation is not per-frame, so correctness and reuse take priority.

---

## 9. Validation (GPU, reusable at runtime)

`Validate.compute` writes error counters + first-offending element ids to a small result buffer (read back only by the editor tool and tests):

- All indices `< vertexCount`; no degenerate primitives.
- Adjacency symmetric; no self loops; no duplicate edges.
- Rest lengths finite and `> ε`.
- Manifoldness: each edge used by ≤ 2 triangles, consistent orientation (opposite directions in the two faces).
- Closed meshes: no boundary edges; Euler characteristic matches expectation from the layout.
- Winding: for closed meshes, signed volume > 0.
- Attributes: no NaN/Inf; normals unit length; IDs within region count.
- A C# mirror (`TopologyValidator`) operating on read-back arrays is used in tests so failures produce readable diagnostics.

---

## 10. Visualisation and debug tools

### 10.1 Runtime overlays (`GeometryDebugView` component, works in Play mode and XR)

| View | Implementation |
| --- | --- |
| Attribute colouring | Debug shader reads any attribute via generic `ByteAddressBuffer` binding + format/component/stride constants; scalar → selectable ramp with range from GPU reduction (or manual); vector → RGB or glyph; IDs → hashed flat colour |
| Wireframe | Edge buffer drawn as lines; optional screen-space thick lines (quad expansion) for XR legibility; colour by `EdgeType` or edge attribute |
| Vertices | Billboard points, size/colour by attribute |
| Glyphs | Arrows for `RestNormal`, live normal, `Direction`, tangent frames (later velocity/force) |
| Boundary / validation | Boundary vertices, non-manifold edges, flagged elements highlighted from validation output |
| Rest vs live | Ghost rest surface/wireframe overlaid on live geometry |
| Neighbourhood probe | GPU picking: ray → nearest vertex via compute (`InterlockedMin` on packed distance/id); highlights picked vertex, its CSR neighbours and edges, k-ring rings, and graph-distance field from it |

The GPU picking kernel doubles as the prototype for VR interaction broad/narrow-phase queries.

### 10.2 Editor tooling

- **Geometry Lab** window: pick generator asset, edit settings, regenerate, select debug view and attribute, run validation, view stats (V/E/F, max degree, memory per buffer from `GpuBufferRegistry`, last generation GPU time from `ProfilingSampler` recorders).
- **Inspector of picked element**: one-off readback of all attributes for the picked vertex/edge/region.
- Scene-view labels (vertex ids, attribute values) for small meshes only (readback, editor-only, capped count).
- **Export to Mesh asset**: readback to a `Mesh` asset for diffing, external inspection or baking.
- Attribute dependency report: what a generator provides vs. what attached consumers require.

### 10.3 Leak and memory tracking

All buffers are allocated through `GpuBufferRegistry` (name, owner, size). Exposed in the Geometry Lab and asserted empty after disposal in tests. Buffers are reused when settings change but capacity is sufficient.

---

## 11. Forward compatibility map

What later phases need, and where Phase 1 provides it:

| Future system | Needs | Provided by Phase 1 |
| --- | --- | --- |
| Mass-spring / XPBD | CSR neighbours, edge rest lengths, edge types, mass/stiffness/damping/constraint weights, live position output, prev positions | §4.2, §4.3, §5.2 recipes |
| Bending / shear springs | 2-ring or diagonal edges | `EdgeType` enum, grid diagonals; 2-ring edges added later as extra `EdgeType` |
| Wave / energy propagation | Graph, edge lengths, wave speed/attenuation, source distances | CSR, `GraphDistance` operator, `WaveSpeed` attribute |
| Deformation fields | Declared attribute dependencies, rest normals/directions | `AttributeDescriptor` + `Requires/Produces` validation |
| Audio reactivity | Per-vertex band weights, region routing | `AudioSensitivity` (float4), `RegionId` + region table |
| VR interaction | Spatial queries, interaction weights, region selection | Picking kernel, `InteractionWeight`/`GrabWeight`, region table with bounds |
| Topology-varying generators (particles, Voronoi) | GPU-determined counts | `GeometryHeader` + indirect draw/dispatch args |
| Rendering effects | LocalCoord, curvature, IDs, prev positions for motion vectors | §5.2, §6.2 streams, S7 findings |
| VR performance | Stereo-correct draws, minimal per-frame passes | S3, mesh-bridge path, event-driven generation |

---

## 12. Testing and agentic development workflow

### 12.1 Available tooling

Development is expected to be largely agent-driven. The following are installed and verified on the dev machine:

| Tool | Location / version | Purpose |
| --- | --- | --- |
| Unity Editor | 6000.6.9f1 (Unity Hub install) | Matches `ReactiveGeometry/ProjectSettings/ProjectVersion.txt` |
| Unity CLI | `unity` 1.0.0, `%LOCALAPPDATA%\Unity\bin\unity.exe` | Open/close projects, run tests, build, manage packages, drive a running Editor |
| Pipeline package | `com.unity.pipeline` 1.0.0 in `ReactiveGeometry` | Live Editor control over a local connection (port 7800 by default) |
| Test framework | `com.unity.test-framework` 1.8.0 | EditMode / PlayMode tests |

Key commands (always pass `--project-path ReactiveGeometry`; discover others with `unity command --query <term>`):

| Task | Command |
| --- | --- |
| Open Editor / wait until connected | `unity open ReactiveGeometry --until-ready`, `unity status --until-ready` |
| Recompile and report C# errors | `unity recompile`, or `unity command script recompile` + `script recompile_status` |
| Read console (compile/shader errors, logs) | `unity command console get`, `console status`, `console clear` |
| Run C# in the Editor (GPU capability probes, quick dispatch experiments, readback checks) | `unity command script eval --code '...'` or `--file <path.cs>` |
| Run tests in the live Editor | `unity command test run` (async; `test list`, `test cancel`) |
| Run tests and write a report | `unity test ReactiveGeometry --mode EditMode -o Build/TestResults/editmode.xml` |
| Re-run affected / failed tests | `unity test --affected`, `unity test --rerun-failed`, `unity watch test` |
| Visual checks | `unity command capture game_view` / `capture scene_view` / `capture editor_window` (PNG; not in batch mode) |
| Play mode | `unity command editor.playmode start` / `stop` |
| Packages | `unity command package add --name <id> --confirm true --wait true`, `package list` |
| Profiling / memory | `unity command diagnostics memory_snapshot` (+ `memory_snapshot_status`) |

Practical notes for agents:

- Terminals opened before the CLI was installed need PATH reloaded from the registry before `unity` resolves.
- PowerShell 5.1 mangles embedded double quotes in native arguments: keep `script eval` code free of `"` or use `--file`. Append `2>&1 | Out-String` to reliably capture output.
- After package or script changes, the Editor recompiles and `unity status` reports `starting` / blocked by a "Compiling Scripts" dialog; wait with `unity status --until-ready` before issuing commands. If the Editor enters Safe Mode (compile errors), the Pipeline is unavailable: read errors from `unity status`, fix, then retry.
- An unfocused Editor may defer imports/play-mode updates; `script recompile` works while unfocused. Confirm visual results with captures rather than assuming state.

**Agentic inner loop per change:** edit code/shaders → `unity recompile` → `console get` (errors / shader warnings) → `unity test --affected` (or `command test run` with a filter) → for rendering work, open the sample scene, `capture scene_view`/`game_view` and inspect the image → iterate. Milestone completion still requires the full CLI suite (§12.4).

### 12.2 Test suites

| Suite | Type | Content |
| --- | --- | --- |
| `Generators.*` | EditMode, GPU | Each generator × parameter matrix (e.g. icosphere n ∈ {1,2,3,4,8,16,64}; grids with/without wrap/diagonals; tubes open/closed/caps): counts, invariants, validator clean |
| `Topology.*` | EditMode, GPU | Builder on hand-made fixtures (single triangle, quad, tetrahedron, non-manifold bowtie, disconnected parts, lines); determinism (two runs ⇒ identical bytes) |
| `Attributes.*` | EditMode, GPU | Graph distance vs. CPU Dijkstra; normalise range; remap; curvature sign on sphere; analytic vs. generic normals within tolerance |
| `Reference.*` | EditMode | Icosphere with `n = 2^s` vs. CPU reference builder: same V/E/F and degree histogram; positions match as point sets (in recursive-equivalent projection mode) |
| `Lifecycle.*` | EditMode | Regenerate / resize / dispose loops; `GpuBufferRegistry` empty afterwards |
| `Rendering.*` | PlayMode, Graphics Test Framework (`com.unity.testframework.graphics`) | Reference images per generator × debug view (attribute colouring, wireframe, glyphs) and mesh-bridge Lit rendering; per graphics API references |
| `Performance.*` | PlayMode, Performance Testing package (`com.unity.test-framework.performance`) | GPU time of generation, topology build, normals, mesh export at several sizes; memory per vertex |

GPU tests read back with `GraphicsBuffer.GetData` / `AsyncGPUReadback.WaitAllRequests()` — allowed in tests only. Tests that need compute call `Assume.That(SystemInfo.supportsComputeShaders)` so misconfigured agents report *inconclusive* rather than false passes.

### 12.3 Commands

Preferred: the Unity CLI resolves the correct Editor automatically.

```powershell
unity test ReactiveGeometry --mode EditMode -o Build/TestResults/editmode.xml --report-format nunit,junit
unity test ReactiveGeometry --mode PlayMode --filter Rendering -o Build/TestResults/playmode.xml
```

Fallback (no CLI, e.g. minimal CI images): `Tools/ci/run-tests.ps1` wraps the raw editor executable (path from `UNITY_EDITOR_PATH`):

```powershell
# EditMode (GPU required: do NOT pass -nographics)
& $env:UNITY_EDITOR_PATH -batchmode -projectPath ReactiveGeometry -runTests -testPlatform EditMode `
    -testResults Build/TestResults/editmode.xml -logFile Build/Logs/editmode.log

# PlayMode graphics + performance
& $env:UNITY_EDITOR_PATH -batchmode -projectPath ReactiveGeometry -runTests -testPlatform PlayMode `
    -testCategory "Rendering;Performance" `
    -testResults Build/TestResults/playmode.xml -logFile Build/Logs/playmode.log

# Force a specific API for cross-backend checks
... -force-d3d12 | -force-vulkan
```

Notes: do not combine `-quit` with `-runTests`; parse the NUnit XML for results; graphics test reference images are stored per platform/API and updated via the Graphics Test Framework's result-collection workflow.

### 12.4 CI pipeline

`unity ci init` can scaffold the CI configuration; `unity test --shard n/m` splits long suites across agents and `--retries` flags flaky GPU tests.

1. Compile check (open project in batch mode, fail on compile errors / shader compile errors in log).
2. EditMode GPU suites (every change).
3. PlayMode Rendering suite (every change, on a GPU agent).
4. Performance suite (nightly; results archived and compared against a baseline with tolerance).
5. Artefacts: NUnit XML, editor logs, failed-image diffs, performance JSON.

---

## 13. Milestones and acceptance criteria

Each milestone is complete only when its tests pass via the CLI pipeline.

**M0 — Spikes (§3).** `ReactiveGeometry` baseline confirmed; Unity CLI + Pipeline + Test Framework already in place (§12.1); remaining test packages added; spike results recorded here; CI agent runs a trivial compute test headless.

**M1 — Foundations.** Package skeleton, assemblies, `GpuBufferRegistry`, `GeometryResource`, `GeometryHeader`, attribute registry/descriptors, scan + reduce kernels with tests, CLI test script.
*Accept:* scan/reduce match CPU for sizes 1…2²⁰; registry reports zero leaks.

**M2 — Icosphere + topology builder.** Canonical-id icosphere kernels, generic topology builder, validator (GPU + C#).
*Accept:* invariants for all tested `n`; validator clean; deterministic bytes; `Reference.*` passes.

**M3 — URP rendering.** Mesh bridge (streams, raw buffers, bounds), procedural draw path, `ProcGeoLit`, XR single-pass instanced check, scheduler + profiling samplers.
*Accept:* icosphere rendered with URP Lit and a Shader Graph material, casting/receiving shadows, in Game/Scene view and Mock HMD; no per-frame readback (verified by profiler/Frame Debugger); `Rendering.*` reference images pass.

**M4 — Debug tools.** Attribute colouring, wireframe, points, glyphs, rest-vs-live, picking + neighbourhood probe, Geometry Lab window, stats, mesh export, test deformer + normals pass.
*Accept:* every attribute of the icosphere viewable; picked vertex shows correct 5/6 neighbours; deformer animates with correct lighting.

**M5 — Attribute operators and recipes.** Graph distance, normalise, remap, combine, curvature, boundary; recipe asset UI; Shader Graph sub-graph.
*Accept:* `Attributes.*` passes; a Shader Graph material colours by `DistanceFromRoot01` computed from a picked source.

**M6 — Parametric surfaces.** All six shapes, wrap and diagonals, analytic normals/tangents, surface attributes.
*Accept:* closed shapes have no boundary and correct Euler characteristic (torus 0, sphere 2); open plane boundary weights correct; analytic vs. generic normals within tolerance.

**M7 — Tube.** Curve sources, RMF frames, rings, caps, tube attributes.
*Accept:* no frame flips on helix/torus knot (adjacent frame angle below threshold); closed loop seam welded; arc length matches analytic length for helix within tolerance.

**M8 (stretch) — Branching graph.** Line topology through the whole pipeline, branch metadata, line/glyph rendering.
*Accept:* every node except root has exactly one parent; `DistanceFromRoot` equals summed edge lengths; validator handles line topology.

**M9 — Hardening.** Performance baselines at representative sizes (e.g. 10k / 100k / 1M vertices), memory report per generator, documentation of data formats and limitations, sample scenes.

---

## 14. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Unity 6.6 API/behaviour differences (render graph, mesh raw buffers, RenderPrimitives in XR) | M0 spikes before committing; keep both render paths |
| Headless CI without usable GPU | S6; `Assume` on compute support; dedicated GPU agent |
| Motion vectors / TAA artefacts on GPU-deformed meshes | Document approach in S7; `_PrevPositions` slot reserved; implement with simulation phase |
| Atomic ordering nondeterminism | Sort + compact stage in topology builder; determinism tests |
| High-degree vertices (future graphs) slow per-vertex sort | `MAX_SORT_DEGREE` flag; fall back to segmented GPU radix sort if needed |
| Buffer layout churn affecting shaders | Layouts expressed as constants shared via include files; mesh export kernel driven by stride/offset parameters |
| Metadata explosion / bandwidth | Optional, per-generator attributes; hot attributes packed into mesh stream 1; profile before repacking |
| Graphics API differences (D3D12 / Vulkan / Metal) | Rendering + generator tests run per API on CI where available |
