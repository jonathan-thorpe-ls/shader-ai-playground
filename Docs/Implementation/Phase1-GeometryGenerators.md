# Phase 1 — GPU Geometry Generators, Metadata and Visualisation

Implementation plan for the first phase of the GPU audio-reactive procedural geometry framework described in [Docs/Stubs](../Stubs/).

| Item | Decision |
| --- | --- |
| Engine | Unity 6.6 |
| Pipeline | URP 17.6 (render graph) — the only supported pipeline |
| Target project | `ReactiveGeometry/` |
| Phase 1 gate | One complete icosphere vertical slice: GPU generation, topology, metadata, validation and URP rendering |
| Testing | Unity Test Framework run via the Unity CLI (`unity test`) locally and in CI |
| Agentic tooling | Unity CLI (`unity`) + Pipeline package (`com.unity.pipeline`) for live Editor control — see §12.1 |
| Primary tech | Compute shaders, `GraphicsBuffer`, GPU-writable `Mesh` buffers, `Graphics.RenderPrimitives*` |

---

## 1. Scope

### 1.1 In scope

1. A **geometry data contract** designed for shared use by later generators, implemented first for an icosphere.
2. A **compute-shader geodesic icosphere generator** with canonical vertex deduplication.
3. A **generic triangle-topology GPU builder** producing deterministic CSR neighbour connectivity, edges, rest lengths and vertex→face incidence. Line topology is a planned interface capability, not a Phase 1 acceptance requirement.
4. Core metadata: rest geometry, local coordinates, primitive/corner region identity, degrees, boundary weights and curvature.
5. URP rendering through the **mesh-bridge path** with no per-frame CPU readback.
6. Minimal debug visualisation needed to verify the generated icosphere and topology.
7. Automated EditMode GPU validation and one PlayMode graphics smoke test runnable via Unity CLI.

### 1.2 Out of scope (designed for, not implemented)

Mass-spring / XPBD simulation, wave propagation, audio analysis, VR interaction, particle networks, Voronoi, parametric surfaces, tubes, branching graphs, production editor tooling, and the procedural-draw path. Phase 1 reserves forward-compatible domains and live-state storage without implementing those consumers.

### 1.3 Guiding principles (from the stubs)

- Geometry generation, simulation, interaction, audio and rendering are independent modules communicating through GPU buffers.
- The GPU is the source of truth. CPU readback is allowed only for one-off generation metadata (bounds/counts), editor tooling and tests — never per frame.
- Geometry is **self-describing**: generators emit structure *and* semantics.
- Rendering indices ≠ simulation connectivity. Both are produced, separately.
- Determinism: same settings + seed produce bit-identical integer and topology buffers on the same tested GPU/API. Floating-point buffers are compared with a documented per-field tolerance. Cross-driver bit identity is not required.

---

## 2. Repository and package layout

The framework lives in an embedded package inside the Unity project, keeping it reusable and its tests isolated from sample content. The complete layout below is the eventual package shape; Phase 1 creates only the directories and files needed by M0–M3.

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

Embedded packages are picked up automatically. Already installed: `com.unity.pipeline` 1.0.0, `com.unity.test-framework` 1.8.0, `com.unity.shadergraph` 17.6.0 (via URP), and `com.unity.test-framework.performance` 6.6.0. Still to add for the later visual-test milestone: the Graphics Test Framework. During development, `com.sonosthesia.procgeo` must also be listed under `"testables"`.

Assemblies: `Sonosthesia.ProcGeo.Runtime` (generation, topology, attributes — no rendering dependency, so it can be tested in isolation), `.Runtime.URP`, `.Debug`, `.Editor`, `.Tests.EditMode`, `.Tests.PlayMode`.

A small CPU reference icosphere builder (recursive midpoint subdivision with a midpoint cache) lives in the EditMode test assembly to cross-check the GPU generator.

---

## 3. Milestone 0 — Platform spikes

Each spike is a small, self-contained scene plus code that de-risks an API assumption on Unity 6.6. Spikes are **kept, not thrown away**: they are the reference implementation for each API pattern, a record of what worked (and what didn't), and a quick regression check when Unity or URP is upgraded. Results are recorded in this document before Milestone 1 locks the design.

S0, S1, S4 and S6 are required before M1. S2, S3, S5 and S7 are retained as post-Phase-1 spikes and do not gate implementation of the mesh-bridge icosphere.

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

### 3.1 Spike artefacts and layout

Spikes live in the Unity project, outside the framework package, so they never ship with it but stay versioned and runnable:

```
ReactiveGeometry/Assets/Spikes/
  Sonosthesia.Spikes.asmdef            # references URP + test framework; never referenced by the package
  Sonosthesia.Spikes.Tests.asmdef      # EditMode/PlayMode checks for spikes, category "Spike"
  S1_MeshRawBuffers/
    S1_MeshRawBuffers.unity            # scene demonstrating the pattern, opens and runs as-is
    *.cs, *.compute, *.shader, *.hlsl, *.shadergraph
    NOTES.md                           # see template below
    Captures/                          # scene/game view PNGs, Frame Debugger / RenderDoc notes, profiler screenshots
  S2_RenderPrimitives/
  ...
```

Rules:

- One folder per spike, named `S<n>_<ShortName>`, containing everything needed to reproduce it (scene, scripts, shaders, materials, settings assets).
- Scenes must open and run without manual setup; any required project setting is noted in `NOTES.md`.
- Each spike has at least one automated check in `Sonosthesia.Spikes.Tests` (category `Spike`) asserting its key claim, e.g. "compute-written mesh vertex buffer reads back expected positions" or "`Structured | Raw` buffer creation succeeds". Run with `unity test ReactiveGeometry --filter Spike`.
- Visual evidence is captured via the Pipeline (`unity command capture scene_view` / `capture game_view` with `output-path` into `Captures/`) so results are reproducible by agents.
- Spikes are frozen once the outcome is recorded; framework code may copy patterns from them but never references them. If a later Unity/URP upgrade breaks a spike, its test fails and the corresponding framework area is re-checked.
- Failed or abandoned approaches are kept with their notes (marked `Outcome: rejected`) to avoid re-investigating them.

`NOTES.md` template (one per spike):

```
# S<n> — <title>
Question:     <what this spike answers>
Environment:  Unity <version>, URP <version>, <graphics API>, <GPU>, XR <plug-in/mode if relevant>
Approach:     <APIs used, key code paths/files>
Outcome:      confirmed | confirmed with caveats | rejected
Findings:     <behaviour observed, limitations, gotchas, perf numbers>
Evidence:     <capture files, test names>
Design impact: <section(s) of this plan changed, and how>
```

### 3.2 Spike results

Summary kept in this document; details live in each spike's `NOTES.md`.

| # | Outcome | Date | Design impact | Folder |
| --- | --- | --- | --- | --- |
| S0 | pending | | | `Assets/Spikes/S0_Baseline/` |
| S1 | pending | | | `Assets/Spikes/S1_MeshRawBuffers/` |
| S2 | pending | | | `Assets/Spikes/S2_RenderPrimitives/` |
| S3 | pending | | | `Assets/Spikes/S3_XRSinglePass/` |
| S4 | pending | | | `Assets/Spikes/S4_StructuredRawBuffers/` |
| S5 | pending | | | `Assets/Spikes/S5_ShaderGraphBuffers/` |
| S6 | partial — dev machine capabilities confirmed | 2026-11-16 | none yet | `Assets/Spikes/S6_HeadlessCompute/` |
| S7 | pending | | | `Assets/Spikes/S7_MotionVectors/` |

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
    public TopologyBuffers Topology { get; }       // committed CSR, edges, incidence
    public TopologyScratchBuffers TopologyScratch { get; } // provisional count/scatter buffers
    public GeometryRegionBuffers Regions { get; }  // optional region table
    public AttributeSet Attributes { get; }        // all metadata incl. rest geometry
    public LiveStateBuffers Live { get; }          // positions/normals consumed by renderers
    public Mesh Mesh { get; }                      // mesh-bridge target, where applicable
    public Bounds LocalBounds { get; }             // from one-off header readback + margin
    public int Version { get; }                    // bumped on regeneration
}
```

`GeometryResource` owns the chosen render backing. On the mesh-bridge path, it owns the `Mesh` and uses that mesh's raw vertex/index buffers. `Indices` and the dynamic-vertex backing exposed through `Live` are logical aliases for those mesh buffers, not independently allocated copies.

**Capacities vs. counts.** Analytic generators know exact counts. Future generators (particle networks, Voronoi) only know upper bounds, so actual counts can be written by the GPU into `Header`, and dependent draws/dispatches can use `DrawArgs`. Phase 1 initialises this header/indirect path but does not require variable-count generation.

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
| `_Neighbours` | `uint` | 2E | Stored in ascending vertex-id order |
| `_NeighbourEdge` | `uint` | 2E | Edge id for each neighbour slot → reach per-edge data from the vertex view |
| `_Edges` | `uint2` | E | `x < y`, sorted |
| `_EdgeRestLength` | `float` | E | From rest positions |
| `_EdgeType` | `uint` | E | Structural / shear / bend / long-range (Phase 1: structural only; enum fixed now) |
| `_VertexFaceOffsets`, `_VertexFaces` | `uint` | V+1, 3F | Vertex→face incidence in deterministic primitive-id order |

The committed topology is always vertex-id ordered. If a cyclic one-ring view is later needed, store it separately; do not reinterpret the canonical CSR ordering. `MAX_SORT_DEGREE` is a layout-defined limit, and exceeding it is a generation failure rather than a partially valid result.

The builder also owns provisional buffers which are not part of the committed topology contract:

| Provisional buffer | Capacity | Purpose |
| --- | --- | --- |
| Degree/slot cursors | V | Count and scatter primitive-edge occurrences |
| Provisional neighbour list | 6F for triangles; 2L for lines | Pre-deduplication scatter destination |
| Raw incidence list | 3F for triangles | Face incidence before final compaction |
| Scan work | Derived from the corresponding input capacity | Prefix sums for all multi-level scans |

A fixed-`MAX_NEIGHBOURS` view is **not** stored; it can be produced from CSR by a conversion kernel later if profiling favours it.

### 4.3 Live state (renderer input, future simulation output)

| Logical buffer | Type | Phase 1 writer | Future writer |
| --- | --- | --- | --- |
| `_DynamicVertices` | `DynamicVertex { float3 position; float3 normal; }` | Generator/rest copy + normals pass | Simulation final substep + normals pass |
| `_PrevPositions` | `float3` | Copy once before an update | Same (motion vectors) |

`_DynamicVertices` is one backing buffer, not two independently owned position/normal buffers. Phase 1 binds it as a raw byte buffer and uses shared byte-offset constants. The later procedural-draw implementation may consume an equivalent structured `DynamicVertex` view where the API permits it.

On the mesh-bridge path, `_DynamicVertices` is the mesh stream-0 buffer obtained through `Mesh.GetVertexBuffer(0)`. There is no per-frame mesh-export copy. Generation, normals and any test deformation write the correct interleaved offsets directly. A static stream is used for attributes that are packed into the render mesh. Simulation ping-pong buffers remain owned by the future simulation module.

### 4.4 Element domains

Every attribute belongs to one domain: `Vertex`, `Corner`, `Edge`, `Primitive` (face/segment), `Region`, `Geometry`. Corner domain has one entry per primitive corner and is present to avoid assigning several incident face/region values to a single welded vertex. Region-level data is stored once. Refer to it with an attribute on the domain whose identity is unambiguous; Phase 1 icosahedron `RegionId` belongs to `Primitive`, with a `Corner` value available only if a render shader needs it.

---

## 5. Metadata / attribute system

### 5.1 Descriptors

```csharp
public enum AttributeDomain { Vertex, Corner, Edge, Primitive, Region, Geometry }
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

Core (required for the Phase 1 icosphere; later surface generators reuse these contracts):

| Attribute | Domain | Format | Source |
| --- | --- | --- | --- |
| `RestPosition` | Vertex | Float3 | Generator |
| `RestNormal` | Vertex | Float3 | Generator (analytic) or normals pass |
| `RestTangent` | Vertex | Float4 | Generator (w = handedness) |
| `LocalCoord` | Vertex or Corner | Float2/3 | Continuous vertex coordinate where unique; barycentric corner coordinates for icosphere rendering |
| `RegionId` | Primitive | UInt, Flat | Generator (icosahedron face, grid tile, tube segment, branch) |
| `BoundaryWeight` | Vertex | Float | Topology builder (boundary flag) → operator |
| `NeighbourCount` | Vertex | UInt | Topology builder |
| `Curvature` | Vertex | Float | Operator (umbrella / cotan Laplacian on rest positions) |
| `EdgeRestLength` | Edge | Float | Topology builder |

Reserved structural attributes (post-Phase-1):

| Attribute | Generators |
| --- | --- |
| `DistanceFromRoot` / `DistanceFromRoot01` | Tube (arc length), Branching; any generator via graph-distance operator from a source set |
| `LocalDistance` | Tube, Branching (branch-local arc length) |
| `GenerationDepth`, `ParentId`, `BranchId` | Branching |
| `Direction` | Tube/Branching tangent, grid u-axis |
| `DistanceToBoundary` | Grid, open tube (graph distance from boundary) |

Reserved physical / interaction defaults (filled by recipes and consumed in later phases):

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
| `GraphDistance(sources)` | Edge-length-weighted shortest path over CSR. A post-phase implementation must specify `MaxIterations`, an infinite-distance sentinel, floating-point tolerance, and an indirect-dispatch or asynchronous-readback termination strategy. |
| `Normalise` | Min/max via GPU reduction → `*01` attribute; range also written to a small stats buffer used by debug views |
| `Remap(curve)` | `AnimationCurve` baked to a 256-sample buffer |
| `Combine(a, b, op)` | Mul / add / lerp |
| `Curvature` | Signed mean-curvature estimate |
| `HashToRegion` | Deterministic region assignment (PCG hash) |

Example recipe: `Stiffness = Remap(DistanceFromRoot01, stiffCurve)`; `AudioSensitivity.z = Remap(Curvature, highFreqCurve)`. This lets geometry carry its own reactive character without generator-specific simulation code.

---

## 6. Rendering in URP

### 6.1 Two complementary paths

Only the mesh-bridge path is implemented and accepted in Phase 1. The procedural path remains a post-phase extension; keeping it in the design prevents the resource contract from becoming tied to `Mesh`.

| Path | Mechanism | Used for |
| --- | --- | --- |
| **Mesh bridge** (primary surface path) | `Mesh` with GPU-writable vertex/index buffers, written by compute, drawn by `MeshRenderer` (or `Graphics.RenderMesh`) | Production surfaces: URP Lit, Shader Graph, shadows, depth/normals, SSAO, decals, XR — all for free |
| **Procedural draw** | `Graphics.RenderPrimitivesIndexed(Indirect)` with `RenderParams` and custom shaders reading `StructuredBuffer`s via `SV_VertexID` | Debug overlays, wireframe, points, glyphs, line/graph geometry (branching, future particle networks) |

### 6.2 Mesh bridge details

- Vertex layout, two streams:
  - Stream 0 (dynamic, written per update): `Position float3`, `Normal float3` (later `Tangent` if needed).
  - Stream 1 (static, written once): `Tangent float4`, `TexCoord0 = LocalCoord`, `TexCoord1..3` = packed hot metadata selected by the recipe (e.g. `DistanceFromRoot01`, `RegionId` as float, `AudioSensitivity`). This makes the most common metadata available to stock Shader Graph without custom nodes.
- `mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw`, `mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw`; buffers obtained with `GetVertexBuffer(stream)` / `GetIndexBuffer()` and written directly by generator/normals kernels using `RWByteAddressBuffer` with shared stride/offset constants. Phase 1 does not maintain a separate live-position buffer followed by a redundant mesh-export copy.
- `mesh.bounds` set from the GPU header (one-off async readback after generation) plus a configurable deformation margin. No per-frame bounds readback.
- Vertex ids in the mesh equal vertex-attribute buffer indices (no remapping, no seam splitting in Phase 1), so vertex-domain metadata can be sampled by Vertex ID. Primitive/corner attributes require primitive/corner IDs and must not be addressed as vertex-domain values.
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

- `GeometryCommandScheduler` records generation into a `CommandBuffer`. Phase 1 does not execute mutable generation work once per camera callback; regeneration is requested explicitly and recorded once on the next update.
- Generation is event-driven (settings change / explicit request). No per-frame simulation is required for Phase 1 acceptance.
- Any future per-frame pass has one explicit update owner and a monotonic update version. Context callbacks may render many cameras, but a pass runs at most once per frame/version.
- Optional later: move per-frame passes into a render graph compute pass via a `ScriptableRendererFeature`, and evaluate async compute for simulation.
- Every pass is wrapped in a `ProfilingSampler` so it appears in the Profiler (GPU module), Frame Debugger and RenderDoc captures.

### 6.6 Normals pass (generic)

Per vertex, area-weighted sum of incident face normals using `_VertexFaceOffsets/_VertexFaces` — no atomics, no inter-thread writes. For lines/points topology, normals are not computed (glyph shaders use `Direction`). Analytic generators also write `RestNormal` directly, which the tests compare against the generic pass.

### 6.7 Test deformer (debug only)

A post-phase kernel may displace `_DynamicVertices.position` along `RestNormal` by `amplitude * sin(k * attr - ωt)`. It is not the simulation and is deferred because the first acceptance gate uses static generated geometry.

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

Phase 1 implements one `IcosphereGeneratorAsset`; the interface remains suitable for later generators. After `Record`, the shared pipeline runs: **TopologyBuilder → Normals → Phase-1 core attributes → Validation → bounds update**. Writes occur directly into the selected renderer backing; there is no final mesh-export copy.

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
- Attributes: primitive-domain `RegionId` = icosahedron face; corner-domain `LocalCoord` = face barycentric; analytic vertex-domain `RestNormal`/`RestTangent`.
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

1. **Count** — per primitive edge `(a,b)`: `InterlockedAdd` degree for both `a` and `b`; per triangle: incidence count per corner. Counts include provisional duplicate neighbour occurrences.
2. **Scan** — exclusive prefix sum (`Scan.compute`, multi-level, supports millions of elements) → provisional offsets.
3. **Scatter** — write into provisional arrays via atomic cursors (order nondeterministic at this stage). The provisional neighbour capacity is `6F` for triangles or `2L` for lines.
4. **Sort + dedupe per vertex** — sort by neighbour id, remove duplicates, and establish deterministic canonical ordering within each vertex range. Vertices above `MAX_SORT_DEGREE` fail generation.
5. **Compact** — second scan over unique counts → final `_NeighbourOffsets`, `_Neighbours`.
6. **Edges** — each vertex emits edges to neighbours with larger id; scan gives deterministic edge ids; fill `_NeighbourEdge` by lookup; compute `_EdgeRestLength`.
7. **Incidence** — same count/scan/scatter/sort pattern for vertex→face.
8. **One-ring ordering** (post-Phase-1 option) — build a separate cyclic ordering by walking incident faces. Boundary vertices start at their boundary edge and set `orderedOneRing`. It must never reorder committed canonical CSR storage.
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

All directly allocated buffers are allocated through `GpuBufferRegistry` (name, owner, size). Mesh-owned buffers are registered as non-owning aliases with their actual count and stride. Registry ownership and aliases are asserted clean after disposal in tests. Buffers may be reused when settings change and capacity is sufficient.

---

## 11. Forward compatibility map

What later phases need, and where Phase 1 provides it:

| Future system | Needs | Provided by Phase 1 |
| --- | --- | --- |
| Mass-spring / XPBD | CSR neighbours, edge rest lengths, edge types, live position output, prev positions | §4.2 buffers and one dynamic-vertex backing are reserved; simulation attributes are later additions |
| Bending / shear springs | 2-ring or diagonal edges | `EdgeType` is reserved; concrete edge recipes are later additions |
| Wave / energy propagation | Graph, edge lengths, source distances | CSR and edge lengths now; `GraphDistance` and `WaveSpeed` are post-Phase-1 |
| Deformation fields | Declared attribute dependencies, rest normals/directions | Domain-aware descriptors now; consumers are post-Phase-1 |
| Audio reactivity | Per-vertex band weights, region routing | Domain contract and primitive `RegionId` now; response attributes are post-Phase-1 |
| VR interaction | Spatial queries and region selection | Reserved domains and region storage now; picking is post-Phase-1 |
| Topology-varying generators (particles, Voronoi) | GPU-determined counts | Header and indirect-argument slots now; variable-count paths are post-Phase-1 |
| Rendering effects | Coordinates, curvature, IDs | Phase-1 core metadata layout; motion vectors remain deferred |
| VR performance | Stereo-correct draws | Mesh-bridge integration now; XR verification is deferred to S3 |

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
| `Generators.*` | EditMode, GPU | Phase 1: icosphere n ∈ {1,2,3,4,8,16,64}; counts, invariants and validator clean |
| `Topology.*` | EditMode, GPU | Phase 1 triangle fixtures: single triangle, quad, tetrahedron, non-manifold bowtie and disconnected parts; topology bytes deterministic across two runs |
| `Attributes.*` | EditMode, GPU | Phase 1 core attributes: curvature sign on sphere; analytic vs. generic normals within tolerance; primitive/corner domain cardinalities |
| `Reference.*` | EditMode | Icosphere with `n = 2^s` vs. CPU reference builder: same V/E/F and degree histogram; positions match as point sets (in recursive-equivalent projection mode) |
| `Lifecycle.*` | EditMode | Regenerate / resize / dispose loops; `GpuBufferRegistry` empty afterwards |
| `Rendering.*` | PlayMode | Phase 1 smoke test: non-empty mesh, valid bounds, URP Lit visibility, and one stable captured reference if the Graphics Test Framework is available |
| `Performance.*` | Post-Phase-1, Performance Testing package | GPU generation/topology/normals timings and memory per vertex |

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

Notes: do not combine `-quit` with `-runTests`; parse the NUnit XML for results. A single Phase 1 graphics smoke reference is optional until the Graphics Test Framework is added; later visual suites store reference images per platform/API.

### 12.4 CI pipeline

`unity ci init` can scaffold the CI configuration; `unity test --shard n/m` splits long suites across agents and `--retries` flags flaky GPU tests.

1. Compile check (open project in batch mode, fail on compile errors / shader compile errors in log).
2. Phase 1 EditMode GPU suites (every change).
3. Phase 1 PlayMode rendering smoke test (every change, on a GPU agent).
4. Post-phase: graphics reference suite and performance baseline jobs.
5. Artefacts: NUnit XML, editor logs, and later failed-image/performance reports.

---

## 13. Milestones and acceptance criteria

Each milestone is complete only when its tests pass via the CLI pipeline. Phase 1 ends at M3; M4 onward is the ordered post-phase backlog.

**M0 — Required spikes (§3).** Confirm the `ReactiveGeometry` baseline plus S1 mesh raw-buffer writes and S6 CLI GPU testing. S0, S1, S4 and S6 have reproducible artefacts and passing focused tests where applicable; S4 confirms the buffer binding capability selected by the final resource contract. S6 must record the actual graphics device and API; it must not claim a no-GPU/headless run when a real GPU was used.

**M1 — Foundations.** Package skeleton, assemblies, `GpuBufferRegistry`, `GeometryResource`, `GeometryHeader`, attribute registry/descriptors, scan + reduce kernels with tests, CLI test script.
*Accept:* scan/reduce match CPU for sizes 1…2²⁰; registry reports zero leaks.

**M2 — Icosphere + topology builder.** Canonical-id icosphere kernels, triangle topology builder, validator (GPU + C#).
*Accept:* invariants for all tested `n`; validator clean; index/topology bytes deterministic on the same GPU/API; floating-point values within documented tolerances; `Reference.*` passes.

**M3 — URP mesh-bridge rendering.** Mesh streams, direct raw-buffer writes, bounds, simple URP Lit/Shader Graph verification, event-driven generation and profiling samplers.
*Accept:* icosphere is visible in Game and Scene view, renders with URP Lit, has valid bounds, casts/receives the configured standard URP effects, and performs no per-frame readback or redundant mesh-export copy. `Rendering.*` smoke test passes. Procedural drawing and XR move out of this gate.

### Post-Phase-1 backlog

**M4 — Debug tools.** Attribute colouring, wireframe, points, glyphs, rest-vs-live, picking + neighbourhood probe, Geometry Lab window, stats, mesh export and test deformer.
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
| Unity 6.6 API/behaviour differences (render graph and mesh raw buffers) | Gate M1 on the required M0 spikes; defer XR and RenderPrimitives verification |
| Headless CI without usable GPU | S6; `Assume` on compute support; dedicated GPU agent |
| Motion vectors / TAA artefacts on GPU-deformed meshes | Document approach in S7; `_PrevPositions` slot reserved; implement with simulation phase |
| Atomic ordering nondeterminism | Sort + compact stage in topology builder; determinism tests |
| High-degree vertices (future graphs) slow per-vertex sort | `MAX_SORT_DEGREE` flag; fall back to segmented GPU radix sort if needed |
| Buffer layout churn affecting shaders | Layouts expressed as constants shared via include files; all generators write the declared dynamic-vertex offsets directly |
| Metadata explosion / bandwidth | Optional, per-generator attributes; hot attributes packed into mesh stream 1; profile before repacking |
| Graphics API differences (D3D12 / Vulkan / Metal) | Rendering + generator tests run per API on CI where available |
