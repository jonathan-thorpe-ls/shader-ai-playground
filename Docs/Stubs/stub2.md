# **Phase 6: Additional Geometry Generators — Detailed Implementation Plan**

## **1. Phase objective**

Expand the procedural geometry framework beyond the initial icosphere so that it can generate multiple geometry types while reusing the existing GPU simulation and rendering architecture.

The key architectural principle is:

**Geometry generators define topology and initial geometry; the simulation system deforms that geometry without needing to know how it was generated.**

Each generator should produce a consistent set of GPU-ready data:

- Vertex positions
- Triangle or primitive indices
- Neighbour connectivity
- Rest lengths
- Vertex metadata required by the simulation
- Geometry-specific parameters, where needed

The simulation system should consume this data through a common interface rather than requiring a separate simulation implementation for each geometry type.

## **2. Scope and planned generators**

Implement the generators in the following order:

| Priority | Generator           | Geometry type                | Main purpose                                     |
| -------- | ------------------- | ---------------------------- | ------------------------------------------------ |
| 1        | Parametric grid     | Regular 2D surface           | Flexible surfaces and wave propagation           |
| 2        | Tube                | Swept 3D surface             | Cables, organic tubes and audio-reactive strands |
| 3        | Branching structure | Connected graph or mesh      | Organic structures and branching networks        |
| 4        | Particle network    | Point-based connected system | Abstract reactive structures                     |
| 5        | Voronoi structure   | Cell-based geometry          | Fragmented, cellular and crystalline forms       |

The parametric grid should be implemented first because it provides a relatively simple topology and is useful for validating the geometry-independent simulation architecture.

The later generators introduce progressively more complex topology and should build on the same data contract.

## **3. Establish a common geometry interface**

Before implementing additional generators, define a shared interface that every geometry generator must satisfy.

### **3.1 Geometry data contract**

Each generator should provide the following outputs:

| Output                 | Description                                   | Required              |
| ---------------------- | --------------------------------------------- | --------------------- |
| Vertex positions       | Initial or rest positions of vertices         | Yes                   |
| Triangle indices       | Defines the rendered surface                  | For mesh geometry     |
| Neighbour connectivity | Identifies connected vertices                 | For simulation        |
| Rest lengths           | Original distances between connected vertices | For spring simulation |
| Vertex count           | Number of generated vertices                  | Yes                   |
| Index count            | Number of rendering indices                   | For indexed geometry  |
| Geometry parameters    | Generator-specific configuration              | Yes                   |
| Vertex metadata        | Optional per-vertex properties                | Where needed          |

For geometries that do not naturally form triangle meshes, such as particle networks, the rendering representation may use points, lines or generated mesh elements.

The simulation data should remain separate from the rendering representation.

### **3.2 Common C# interface**

A possible interface is:

```
public interface IGeometryGenerator
{
    GeometryData Generate(GeometrySettings settings);
}
```

A shared data container could be:

```
public class GeometryData
{
    public Vector3[] RestPositions;
    public int[] TriangleIndices;

    public int[] NeighbourOffsets;
    public int[] NeighbourIndices;

    public float[] RestLengths;

    public int VertexCount;
    public int IndexCount;
}
```

This is a conceptual CPU-side representation of generated data. The final implementation should upload the data into GPU buffers and avoid using CPU-side mesh manipulation for simulation.

### **3.3 Common GPU buffers**

All generators should use a consistent GPU buffer layout wherever possible:

- `RestPositions`
- `PositionBufferA`
- `PositionBufferB`
- `VelocityBufferA`
- `VelocityBufferB`
- `NeighbourOffsets`
- `NeighbourIndices`
- `RestLengths`
- `TriangleIndexBuffer`

Geometry-specific buffers can be added when required.

For example, a tube generator may need tangent or frame information, while a branching generator may need parent-child relationships.

The simulation system should only depend on the buffers it actually needs.

## **4. Generator 1 — Parametric Grid**

First implementation priority

[TriTerrain - Triangle Terrain - Community Resources - Developer Forum | Roblox](https://images.openai.com/static-rsc-4/1ZSBpnqudsjhtVPSdWmMs0GwMUs1W0sC6dfMM-AtoKEN021sMijgRRMmX21hk4eQHRs5ZrOwcaoi4GMDBAa3--R_tGgFH_7drA3BKHqYdcIfNaD_YEdBqTBYT8968lcBlHuO8WM8a932sCW8GZn7XaouhSsb6WcAX4tO-kE0I_M?purpose=inline)

[Anmol Agarwal](https://images.openai.com/static-rsc-4/bug36CqfNYHfKUKQZMV29ke0SBIObdz41yrkDCk326yBCS8cB4zhVEi2ifgv6_eJx9U7MFt5CEhLo-Telfc3w-UKCiPKm2gKfPPWBEZmBWO5OXeX9l6TJA4VmnRd2d1v7oeS4eegAYIn_R5gknouwpXh5zEwhIOc7mHJSgIYj_I?purpose=inline)

[Eevee wireframe rendering - Lighting and Rendering - Blender Artists Community](https://images.openai.com/static-rsc-4/feriRYACSV2z7EfQ1sddzN2M8afXtZtniSRmlItWd4aVWztRIxcQNTcCd-D2eGz7DtYjPofGquy-ltfXJXpctwSdUyEd7ODFE_schnHboQYviiULTfAvIrFA3t-m2f2Bk1lkvprW9CiVWu61ctSUPqgqGxMCyhzaji7He5EOo7M?purpose=inline)

8

### **4.1 Objective**

Generate a regular two-dimensional grid of vertices and triangles.

This is the simplest additional geometry generator and provides a useful test case for:

- Regular vertex connectivity
- Surface deformation
- Neighbour-based normal calculation
- Wave propagation
- Audio-reactive surface animation

### **4.2 Generator parameters**

```
[System.Serializable]
public class GridSettings
{
    public int ResolutionX = 64;
    public int ResolutionY = 64;

    public float SizeX = 10f;
    public float SizeY = 10f;

    public bool GenerateDiagonals = false;
    public bool GenerateBoundaryConstraints = true;
}
```

The grid should support configurable resolution and dimensions.

### **4.3 Vertex generation**

For a grid with resolution \\(N_x \times N_y\\):

\\[ V = N_x N_y \\]

Each vertex can be positioned using:

\\[ x(i,j) = \left(\frac{i}{N_x-1}-0.5\right)S_x \\]

\\[ z(i,j) = \left(\frac{j}{N_y-1}-0.5\right)S_y \\]

\\[ y(i,j)=0 \\]

Where:

- \\(i\\) is the horizontal grid coordinate.
- \\(j\\) is the vertical grid coordinate.
- \\(S_x\\) is the grid width.
- \\(S_y\\) is the grid depth.

The flattened vertex index is:

\\[ index(i,j)=jN_x+i \\]

### **4.4 Triangle generation**

Each grid cell contains two triangles.

For a cell with corners:

- \\(a = index(i,j)\\)
- \\(b = index(i+1,j)\\)
- \\(c = index(i,j+1)\\)
- \\(d = index(i+1,j+1)\\)

Generate:

```
Triangle 1: a, c, b
Triangle 2: b, c, d
```

The winding order should be verified against the desired surface orientation and Unity's rendering configuration.

### **4.5 Neighbour connectivity**

Each interior vertex can have up to eight neighbours:

- Left
- Right
- Up
- Down
- Upper-left
- Upper-right
- Lower-left
- Lower-right

Start with four-direction connectivity and introduce diagonal connections as an optional setting.

This allows the simulation to use different spring configurations.

### **4.6 Simulation behaviour**

The grid should support:

- Localised vertex excitation
- Wave propagation across the surface
- Spring-based restoring forces
- Damping
- Boundary constraints
- Audio-driven displacement

Boundary vertices should optionally be fixed or constrained to reduce unwanted movement.

### **4.7 Normal calculation**

For a regular grid, normals can be calculated using neighbouring deformed positions.

For example:

\\[ T_x=P\_{i+1,j}-P\_{i-1,j} \\]

\\[ T_z=P\_{i,j+1}-P\_{i,j-1} \\]

\\[ N=\operatorname{normalize}(T_z \times T_x) \\]

Edge vertices require one-sided differences or a suitable fallback.

### **4.8 Acceptance criteria**

- Grid resolution can be changed.
- Vertex positions are generated correctly.
- Triangle winding is consistent.
- Neighbour connectivity is valid.
- Rest lengths match the generated topology.
- The existing GPU simulation can deform the grid.
- Normals update correctly.
- Boundary constraints work as configured.

## **5. Generator 2 — Tube**

[Blender Adaptive and Optimize Spline Interpolation - Modeling - Blender Artists Community](https://images.openai.com/static-rsc-4/Oqowv4udZSA6eLpi4npJLyCjUNeiSYF4l0UTswS5aEzTBqCddqK9ZW1AluXLHuVzoeEHLQenoBm3hFu6oTNX78Dabw-aqXVBmuCAHv77bv4pdbpSj_lO_0KmtJR4d78BCDLKHRL9ZRbn3yGt4d1IUPqR06NUgiMVvlXBTjEdkTU?purpose=inline)

[\[ANSYS Meshing\] long pipe mesh -- CFD Online Discussion Forums](https://images.openai.com/static-rsc-4/cZRA8s_FT_hiIWHicVN0mqzplkZptfMGgadBjlw18JWw3s1AxWI2hCI8OdmatPs2udgd5MmkRFGhcNIvanmoT7zIB-iiPHx6NYzYXLw4Xjl4ncy8Heau4Ty8G0XRVQP_QL8NMbg4J20N_OSWHr1NSVivCitl9BwiZO9FoYLKyvM?purpose=inline)

[Tube extrusion along spline path - Math and Physics - GameDev.net](https://images.openai.com/static-rsc-4/xS0cwLZgc-X_labbVuJwOLZSiFCj2C5bWfKTVFOunCGnzK2wb0ZOSIKazW_SrbJAb71kLUth4LyPbQXE0Uf3zIb-g-lkrFbV3mig63NuyLnberXSYwFv0PKY0sOlNL30yxiRl-oVqOfZ5ijdRJbnEHn9xzJ_npxe2lR5BlkgukU?purpose=inline)

9

### **5.1 Objective**

Generate a tube by sweeping a circular cross-section along a path.

The generator should support straight, curved and dynamically deformable tubes. It can be used for cables, tendrils, audio-reactive strands and other elongated forms.

### **5.2 Generator parameters**

```
[System.Serializable]
public class TubeSettings
{
    public int SegmentCount = 64;
    public int RadialResolution = 12;

    public float Length = 10f;
    public float Radius = 0.5f;

    public bool Closed = false;
    public bool GenerateCaps = true;
}
```

The initial implementation should support a straight tube. Curved paths can be added once the basic topology is validated.

### **5.3 Vertex generation**

Use two coordinates:

- \\(i\\): position along the tube.
- \\(j\\): position around its circular cross-section.

For a straight tube aligned along the Y axis:

\\[ \theta_j = \frac{2\pi j}{N_r} \\]

\\[ x\_{i,j}=R\cos(\theta_j) \\]

\\[ y\_{i,j}=\frac{i}{N_s-1}L \\]

\\[ z\_{i,j}=R\sin(\theta_j) \\]

Where:

- \\(R\\) is the tube radius.
- \\(L\\) is its length.
- \\(N_s\\) is the segment count.
- \\(N_r\\) is the radial resolution.

The flattened vertex index is:

\\[ index(i,j)=iN_r+j \\]

The angular coordinate should wrap around so that the final radial vertex connects to the first.

### **5.4 Triangle generation**

Each quad between adjacent rings is divided into two triangles.

For each longitudinal segment and radial segment:

```
a = index(i, j)
b = index(i + 1, j)
c = index(i, (j + 1) % RadialResolution)
d = index(i + 1, (j + 1) % RadialResolution)

Triangle 1: a, b, c
Triangle 2: c, b, d
```

Verify winding order and correct it if the surface faces inward.

Caps, if enabled, require additional end-cap geometry and indices.

### **5.5 Neighbour connectivity**

Each tube vertex should connect to:

- The previous vertex along the tube.
- The next vertex along the tube.
- The previous vertex around the ring.
- The next vertex around the ring.

Optional diagonal connections can improve deformation stability.

This produces a structured surface topology that can use the existing spring simulation.

### **5.6 Curved tube extension**

After the straight tube works, add path-based generation.

A curved tube requires a local coordinate frame at each path sample:

1. Sample the path at regular intervals.
2. Calculate a tangent at each sample.
3. Construct two perpendicular vectors to the tangent.
4. Use those vectors to orient the circular cross-section.
5. Generate ring vertices around each path sample.

The local frame must avoid sudden twisting between adjacent rings. A parallel-transport frame is a suitable approach to investigate for this purpose.

### **5.7 Simulation behaviour**

Support:

- Bending along the tube.
- Radial expansion and compression.
- Localised impulses.
- Propagating waves along the tube.
- Audio-controlled radius or displacement.
- Optional fixed endpoints.

A useful initial test is to excite one vertex near one end and observe the deformation travelling along the tube.

### **5.8 Normal calculation**

For the straight tube, normals can initially be derived from the radial direction.

For curved and deformed tubes, calculate normals from the deformed surface topology or use neighbouring surface positions.

### **5.9 Acceptance criteria**

- Tube dimensions and resolution are configurable.
- Angular connectivity wraps correctly.
- Triangle winding is consistent.
- End caps work when enabled.
- Neighbour connectivity and rest lengths are valid.
- The tube deforms without breaking its topology.
- Curved tubes do not exhibit severe frame twisting.
- Normals remain correct during deformation.

## **6. Generator 3 — Branching Structure**

[Experiments - Houdini and CG tips](https://images.openai.com/static-rsc-4/fSlhu8K-J270x9i9iaFJwXzFKiio51K7HT951CgxQmB27QIZeFpxwpR-D0LW9NSUZTxMYXHhCp6Iwy1O6cmRrqLNp-TIjuswwtWL4KIcIxdUHsCVAOr42XQq16xj4vS0WNLPDBnJUpT-1KOLd1kxgg8EIHI4jM7RDgoyMkOiW6E?purpose=inline)

[Branching Subdivision Curves | procegen](https://images.openai.com/static-rsc-4/Ut9znbPUBoKgJPJ3_ccXgBdzaNP346sjglCGVOoXi5caX_jAfSO-1SDTwmIPRVO7wqmsULPh-RD9PHrrxgh7oZ2BWAdV2RBDjCq9cWPdRdLipk869RZh2jcOr5AqV-Jr8luhsdRQvHDYhT8kr5ger0dVK3l41M2b52Jz6CxRgq8?purpose=inline)

[Oak Tree Topology and Appearance - Works in Progress - Blender Artists Community](https://images.openai.com/static-rsc-4/PliVEDNExOG_12cgC99QIUYsvIODshOH4_xMQ2I1NS9i1DmiaCDWi5q66RhorrWZm0_FHHK_QuFqBLIwUrHT3DWJofZvSFbnbRrmv6nNOQdQ_jLQjaZWsUFtBbSEn3IoPhSMG8EbU_tfO35q3Rt2PYR30IDHeAQop21by5GOMro?purpose=inline)

10

### **6.1 Objective**

Generate connected branching structures that can represent trees, roots, veins, neural-like networks or abstract organic forms.

Unlike the grid and tube, a branching structure is not naturally represented by a simple rectangular topology. It should therefore be generated from a graph.

### **6.2 Graph representation**

Represent the structure as:

- Nodes: positions in 3D space.
- Edges: connections between nodes.
- Branch relationships: parent-child relationships.
- Optional branch attributes: radius, depth and generation level.

A conceptual node structure:

```
public struct BranchNode
{
    public Vector3 Position;
    public int ParentIndex;
    public int Generation;
    public float Radius;
}
```

Each edge connects two nodes.

The graph should be the source of truth for structural connectivity.

### **6.3 Generator parameters**

```
[System.Serializable]
public class BranchSettings
{
    public int MaxDepth = 5;
    public int BranchesPerNode = 2;

    public float InitialLength = 3f;
    public float LengthScale = 0.7f;

    public float InitialRadius = 0.3f;
    public float RadiusScale = 0.7f;

    public float BranchAngle = 30f;
}
```

These are initial configuration parameters. The exact generation algorithm and branching rules should be kept modular.

### **6.4 Generation process**

1. Create a root node.
2. Create one or more child nodes from the root.
3. Calculate child positions using branch direction and length.
4. Assign parent-child relationships.
5. Reduce branch length and radius according to generation depth.
6. Repeat until the configured depth is reached.
7. Generate graph edges from the parent-child relationships.
8. Build simulation connectivity from those edges.

The first version should generate a predictable branching pattern. Random variation can be introduced later through controlled parameters and seeds.

### **6.5 Rendering representation**

There are two possible representations:

**Option A: Line-based graph**

- Render edges as lines.
- Render nodes as points or small instances.
- Keep the graph directly connected to the simulation data.

This is the simpler initial implementation.

**Option B: Branching tubular mesh**

- Generate a tube along each edge.
- Vary tube radius according to branch depth.
- Connect branch intersections.
- Produce a continuous or visually connected surface.

This is more visually complete but introduces additional topology and mesh-junction complexity.

Implement Option A first. Treat Option B as an extension rather than a requirement for the initial branching generator.

### **6.6 Simulation behaviour**

The graph can use the existing spring system:

- Each edge becomes a spring connection.
- Rest lengths are calculated from original node positions.
- Node movement propagates through connected branches.
- Root nodes can be fixed or constrained.
- Branch stiffness can vary with generation depth.

Additional behaviour can include localised impulses, wave propagation and audio-driven excitation.

### **6.7 Acceptance criteria**

- Branch generation produces valid parent-child relationships.
- Every edge references valid nodes.
- Rest lengths are calculated correctly.
- The simulation can deform the graph.
- Root constraints work.
- Branches remain connected during simulation.
- The graph can be rendered without requiring a triangle mesh.
- The same graph can later be used as the basis for tubular rendering.

## **7. Generator 4 — Particle Network**

[X-Particles Experiments | COLECTION IV :: Behance](https://images.openai.com/static-rsc-4/FP1xKiIiM6L0hAs6CsBk4V7ZqSu6zFkqYFKrS2OggUTRUTlUfzTtRp1Wo1_zfb9EGZ4hMLuV1SezCqpILzzz8mYsA1ctpzaBAIBi4E8NNILCHwbxowaeiN-_e1aR7nmZflM8GUHds3IO8--ClK0VgSEX9zo-lIW9Uei9Jnte2F8?purpose=inline)

[High dimensional thinking. Part of a series of articles I’ll be… | by Memo Akten | Medium](https://images.openai.com/static-rsc-4/MKiTv9bFcq2ju7umrNV_qUn5IInMjVIwQxjSJUKdooqEsUwRbD3BzudEi8z2dN-p_nMzLZk_F5ELPoRVr1pvRTkyVZyA9Df-etzqEnmUx_JpWKQrD18b3D41rdlYCZr5JPAncUXPaCA-4eQpqRq6QB04QZcf0nx-4mHNYTNGZ0g?purpose=inline)

[NETWORKS :: Behance](https://images.openai.com/static-rsc-4/gJugLZgf5Nz7JbuS8TMVWPVZPq4aus-Zlz--SQcV7coZU3Tje-V8htvhDttw0_tOJndCSkVQKKc6YNOoUcaQhWwb1LAF2sJJYp9NkD6GP3B2QgGkzQPPWpqidzxyv1v13LJ0TpK-i6TWx4TWwyHFe1-4VNeIKh5Q7F2mwCejsKk?purpose=inline)

12

### **7.1 Objective**

Generate a point-based network in which particles are connected by edges.

Unlike the parametric grid or tube, this generator does not need to produce a conventional surface mesh. Its main purpose is to support abstract connected geometry and graph-based simulation.

### **7.2 Generator parameters**

```
[System.Serializable]
public class ParticleNetworkSettings
{
    public int ParticleCount = 256;

    public float DistributionRadius = 5f;
    public float ConnectionDistance = 1.5f;

    public int MaxNeighbours = 8;

    public int RandomSeed = 12345;
}
```

### **7.3 Particle generation**

Generate an initial set of particle positions using a configurable distribution.

Potential initial distributions include:

- Uniform random positions within a volume.
- Positions on a sphere.
- Positions within a box.
- Positions along a predefined path.

Start with one distribution and keep the distribution method replaceable.

### **7.4 Connection generation**

Build a graph by connecting particles according to a chosen rule.

Initial connection method:

1. For each particle, find nearby particles.
2. Filter candidates using the connection distance.
3. Select up to the configured maximum number of neighbours.
4. Store the connections.
5. Remove duplicate or invalid connections.
6. Calculate rest lengths.

The connection method should be deterministic when the same random seed and settings are used.

For larger networks, avoid an unrestricted all-pairs distance search if it becomes too expensive. A spatial partitioning approach can be considered as an optimisation.

### **7.5 GPU data representation**

The network should reuse the existing position, velocity and connectivity buffers.

Unlike a surface generator, it may not require triangle indices.

Its rendering data may instead contain:

- Particle positions.
- Connection indices.
- Optional particle sizes.
- Optional particle colours.
- Optional per-particle attributes.

### **7.6 Simulation behaviour**

Support:

- Spring forces between connected particles.
- Particle attraction and repulsion.
- Localised excitation.
- Wave propagation through graph connections.
- Audio-reactive displacement.
- Optional position constraints.

The network should be able to use the same simulation dispatch structure as other geometries.

### **7.7 Rendering**

Implement two basic rendering modes:

1. **Particle rendering:** render each particle as a point, billboard or small instanced primitive.
2. **Connection rendering:** render edges as lines or thin procedural geometry.

These modes should be independently configurable.

### **7.8 Acceptance criteria**

- Particle positions are generated reproducibly.
- Connections reference valid particle indices.
- Duplicate edges are handled consistently.
- Rest lengths are valid.
- The simulation can deform the network.
- Particle and connection rendering work independently.
- Increasing particle count does not require changes to the simulation architecture.

## **8. Generator 5 — Voronoi Structure**

[3d Model Voronoi Sphere](https://images.openai.com/static-rsc-4/1IJhzRklJnia4tjX3K8gaThiAE3bOqTztcrZXZCcvlSvD6dwD-ngQ1TjZyeheX5U8cbGwLEMBus0HZHGtxWwdd5HLh2dwa0sn8jyR-3k_o8pIVttaSGTaVfW_Lpb7OCpfnasuIS6u6Gd8fkD5ORJJC_W7p-oasdCyDDcOCOWEzc?purpose=inline)

[Voronoi Fracture Download • helloluxx](https://images.openai.com/static-rsc-4/rvUSt6kIbCKlnqB6pmHD8NWizFqT5I_nk-mYhFmMzNpNIMs3sY1V7tB0bRv_WUUJVOiMixeE28ayoe9gIcZsZ8_O_LFLCqvZGC_qrWv1SplsZR-7ftQgSR9Ez185PgxYkW9uCH8N--HDoB2kV9LTINb3OmzP7JJw2Imx0qU3k24?purpose=inline)

[I would like to add thickness in 3d voronoi :) - Grasshopper - McNeel Forum](https://images.openai.com/static-rsc-4/9b3uEZYiZjaoiS9ZYAs4mezmiQYAOgDAgvBjkPuoUTQ2_UcCLrKb2ePonnx5BufX1yR5Ys9ab7Q4KII7xQaCaIklD2YAd3KUB3U4r_M7bZnRcCKafrS2v7P9bclrjgSz47FIsfiwU4-MAtSuXPiQUmJqVceuy5uXLcU06fDdYeo?purpose=inline)

12

### **8.1 Objective**

Generate cellular geometry based on Voronoi regions.

This generator is intended for more complex forms such as cellular surfaces, crystalline structures, fragmented geometry and organic-looking patterns.

It is the most complex generator in this phase and should be implemented last.

### **8.2 Generator parameters**

```
[System.Serializable]
public class VoronoiSettings
{
    public int SeedCount = 64;

    public float BoundsSize = 10f;

    public int RandomSeed = 12345;

    public bool GenerateCellEdges = true;
    public bool GenerateCellSurfaces = true;
}
```

The initial implementation should focus on a clearly defined bounded region and a consistent set of cells.

### **8.3 Generation process**

1. Generate seed points inside the configured bounds.
2. Construct Voronoi regions from those seed points.
3. Generate cell boundaries.
4. Convert the boundaries into vertices and indices.
5. Identify shared boundaries and shared vertices where appropriate.
6. Build connectivity information for simulation.
7. Calculate rest lengths.
8. Upload the resulting geometry data to GPU buffers.

The precise Voronoi construction method is an implementation decision that should be made before development begins. The existing specification does not prescribe a particular algorithm or library.

### **8.4 Topology considerations**

Voronoi geometry introduces several challenges:

- Cells may have different numbers of vertices.
- Adjacent cells share boundaries.
- Boundary vertices must be represented consistently.
- A cell boundary may need to be triangulated for rendering.
- Shared topology must not accidentally create duplicate or disconnected simulation vertices.

A critical design decision is whether the generated structure represents:

- A collection of independently rendered cells.
- A connected surface made from shared cell boundaries.
- A volumetric cellular structure.

These representations have different topology and rendering requirements. Begin with a two-dimensional or surface-based representation before attempting fully volumetric cells.

### **8.5 Simulation behaviour**

The first implementation should use the common spring system wherever a valid shared connectivity graph exists.

Potential extensions include:

- Cell expansion and compression.
- Localised cell excitation.
- Boundary deformation.
- Neighbouring-cell wave propagation.
- Audio-reactive cell displacement.
- Fragmentation-like visual effects.

Do not introduce fracture or topology-changing simulation as a requirement for the initial generator. It would significantly expand the scope beyond generating and deforming a stable topology.

### **8.6 Rendering**

Support:

- Cell boundary rendering.
- Triangulated cell surfaces.
- Optional per-cell visual attributes.
- Optional variation based on cell size or neighbourhood.

### **8.7 Acceptance criteria**

- Seed generation is reproducible.
- Cell boundaries are valid.
- Adjacent cells share topology correctly where intended.
- Triangulation produces valid indices.
- Connectivity and rest lengths are valid.
- The generated geometry can be uploaded to GPU buffers.
- The common simulation can deform the resulting structure.
- Cell boundaries remain visually coherent during deformation.

## **9. Shared implementation requirements**

All five generators should integrate with the same geometry and simulation architecture.

### **9.1 Generator registration**

Create a central geometry factory or registry that selects the requested generator.

Conceptual example:

```
public enum GeometryType
{
    Icosphere,
    ParametricGrid,
    Tube,
    Branching,
    ParticleNetwork,
    Voronoi
}
```

A geometry factory can then create the requested implementation:

```
public static IGeometryGenerator Create(GeometryType type)
{
    switch (type)
    {
        case GeometryType.ParametricGrid:
            return new GridGenerator();

        case GeometryType.Tube:
            return new TubeGenerator();

        case GeometryType.Branching:
            return new BranchGenerator();

        case GeometryType.ParticleNetwork:
            return new ParticleNetworkGenerator();

        case GeometryType.Voronoi:
            return new VoronoiGenerator();

        default:
            throw new System.NotSupportedException();
    }
}
```

This is illustrative C# architecture, not a requirement to use these exact class names.

### **9.2 Separate geometry from simulation**

The geometry generator should not contain geometry-specific simulation logic.

For example:

- `GridGenerator` creates grid topology.
- `TubeGenerator` creates tube topology.
- `BranchGenerator` creates graph connectivity.
- The shared simulation system applies forces and integrates positions.

The simulation should receive the topology and buffer references it needs, not inspect which generator produced them.

### **9.3 Support different connectivity types**

Not every generator produces the same kind of geometry.

| Geometry         | Rendering representation     | Simulation connectivity            |
| ---------------- | ---------------------------- | ---------------------------------- |
| Grid             | Triangles                    | Regular vertex neighbours          |
| Tube             | Triangles                    | Longitudinal and radial neighbours |
| Branching        | Lines initially; tubes later | Graph edges                        |
| Particle network | Points and lines             | Graph edges                        |
| Voronoi          | Cell surfaces or boundaries  | Shared cell topology               |

The implementation should distinguish rendering indices from simulation connectivity.

For example, triangle indices describe how to render a surface. They should not automatically be treated as the complete spring connectivity list.

### **9.4 Buffer lifecycle**

Every generator must integrate with the existing GPU resource management system.

The common lifecycle should be:

1. Receive geometry settings.
2. Generate or prepare the geometry data.
3. Allocate the required GPU buffers.
4. Upload or generate initial positions and topology.
5. Initialise simulation buffers.
6. Bind the buffers to the simulation and rendering shaders.
7. Dispatch the appropriate compute shaders.
8. Release buffers when the geometry is replaced or destroyed.

Avoid creating and destroying buffers every frame. Reuse allocations when the geometry settings have not changed.

## **10. Recommended implementation roadmap**

Implement Phase 6 incrementally. Do not attempt all five generators simultaneously.

1. **Step 1 — Common geometry interface**

   Define the shared geometry data contract, generator interface, buffer requirements and registration mechanism.

   *Deliverable:* A geometry architecture that can accommodate multiple generator implementations.
2. **Step 2 — Parametric grid**

   Implement regular vertex generation, triangle indices, neighbour connectivity, rest lengths and GPU simulation integration.

   *Deliverable:* A working deformable grid.
3. **Step 3 — Tube**

   Implement ring-based vertex generation, circular connectivity, caps and tube deformation.

   *Deliverable:* A working deformable tube.
4. **Step 4 — Branching structure**

   Implement graph generation, parent-child relationships, edge connectivity and line rendering.

   *Deliverable:* A working deformable branching network.
5. **Step 5 — Particle network**

   Implement particle distributions, connection generation, graph simulation and point/line rendering.

   *Deliverable:* A working deformable particle network.
6. **Step 6 — Voronoi structure**

   Select the Voronoi construction approach, generate cells, build shared topology and integrate GPU simulation.

   *Deliverable:* A working deformable cellular geometry.
7. **Step 7 — Integration and optimisation**

   Test all generators through the same geometry selection, simulation, audio and rendering systems.

   *Deliverable:* A unified multi-geometry procedural framework.

## **11. Testing and validation**

Every generator should pass a common validation suite before being considered complete.

### **11.1 Topology validation**

Check:

- Every index references an existing vertex.
- No unintended duplicate connections exist.
- Neighbour relationships are consistent.
- Rest lengths are finite and non-negative.
- No unintended zero-length edges exist.
- Triangle winding is consistent for surface geometries.
- Boundary conditions are valid.
- Shared vertices are handled consistently.

### **11.2 Simulation validation**

Test each geometry with:

1. No forces applied — geometry should remain stable.
2. A single localised impulse — deformation should propagate through the topology.
3. Continuous excitation — deformation should remain numerically stable.
4. Damping enabled — motion should gradually settle.
5. Audio input — geometry should respond to the selected audio bands.
6. High excitation — geometry should not produce invalid positions or unstable simulation values.

### **11.3 Rendering validation**

Check:

- Correct vertex positions.
- Correct indices and winding.
- Correct normals.
- No unexpected holes or disconnected surfaces.
- No visible tearing under normal simulation conditions.
- Consistent results across supported graphics APIs.

### **11.4 Performance validation**

Record the following for each generator:

| Metric                   | Purpose                             |
| ------------------------ | ----------------------------------- |
| Vertex count             | Measures geometry size              |
| Connection count         | Measures simulation workload        |
| Triangle count           | Measures surface rendering workload |
| Buffer memory            | Measures GPU resource consumption   |
| Geometry generation time | Measures generator cost             |
| Simulation dispatch time | Measures deformation cost           |
| Rendering time           | Measures rendering cost             |
| Frame rate               | Measures overall performance        |

Test multiple resolutions and particle counts. In particular, verify that the more complex generators do not introduce unnecessary costs into the shared simulation pipeline.

## **12. Suggested code organisation**

One possible organisation is:

```
Scripts/
└── Geometry/
    ├── Core/
    │   ├── IGeometryGenerator.cs
    │   ├── GeometryData.cs
    │   ├── GeometrySettings.cs
    │   ├── GeometryFactory.cs
    │   └── GeometryType.cs
    │
    ├── Generators/
    │   ├── GridGenerator.cs
    │   ├── TubeGenerator.cs
    │   ├── BranchGenerator.cs
    │   ├── ParticleNetworkGenerator.cs
    │   └── VoronoiGenerator.cs
    │
    ├── Settings/
    │   ├── GridSettings.cs
    │   ├── TubeSettings.cs
    │   ├── BranchSettings.cs
    │   ├── ParticleNetworkSettings.cs
    │   └── VoronoiSettings.cs
    │
    └── Validation/
        ├── GeometryValidator.cs
        └── TopologyValidator.cs

Shaders/
└── Geometry/
    ├── GridGeneration.compute
    ├── TubeGeneration.compute
    ├── BranchGeneration.compute
    ├── ParticleNetworkGeneration.compute
    └── VoronoiGeneration.compute
```

The shader files are a suggested organisation. Generation can be split into additional compute passes where necessary.

Keep the existing simulation and rendering shader organisation separate from these generator-specific files.

## **13. Final Phase 6 deliverable**

At the end of Phase 6, the project should contain:

- A common geometry generator interface.
- A shared geometry data contract.
- A geometry selection and creation system.
- A parametric grid generator.
- A tube generator.
- A branching structure generator.
- A particle network generator.
- A Voronoi structure generator.
- Consistent topology and connectivity generation.
- Integration with the existing GPU simulation buffers.
- Integration with the existing procedural rendering pipeline.
- Validation and performance testing for all generators.

**Expected final behaviour**

The user should be able to select a geometry type, configure its parameters, generate it and run it through the same GPU simulation and rendering pipeline.

All supported geometries should respond to the same general simulation controls and audio-reactive system without requiring a separate simulation implementation for each geometry.

## **14. Instructions for the implementing AI model**

Implement Phase 6 in the following order:

1. Inspect the existing Phase 1–5 implementation before changing anything.
2. Identify the existing GPU buffer formats, simulation interface, rendering interface and resource lifecycle.
3. Define the common geometry generator interface without breaking the existing icosphere implementation.
4. Implement the parametric grid first and validate it completely.
5. Implement the tube using the same interface.
6. Implement branching structures using graph-based connectivity.
7. Implement particle networks using the same graph-compatible simulation architecture.
8. Implement Voronoi geometry after the simpler generators are stable.
9. Test each generator independently and then through the complete audio-reactive pipeline.
10. Document the data formats, generation process, topology rules and any geometry-specific limitations.

**Important implementation constraints:**

- Do not create a separate simulation system for every geometry.
- Do not make the CPU responsible for per-frame vertex deformation.
- Keep GPU buffers as the source of truth for simulated positions.
- Keep geometry generation, simulation and rendering separate.
- Do not assume every geometry is a triangle mesh.
- Do not introduce unnecessary CPU readbacks.
- Validate topology before passing it to simulation.
- Preserve the existing Phase 1–5 functionality.
- Prioritise correctness and architectural consistency before performance optimisation.

### **Immediate task**

Begin with the common geometry interface, then implement the parametric grid generator.

Do not proceed to the tube generator until the grid has passed topology validation, GPU simulation testing, normal calculation testing and rendering validation.

**Ultimate goal:** Build a reusable geometry-generation framework in which new procedural shapes can be introduced by implementing their topology and geometry generation logic, while automatically benefiting from the existing GPU simulation, rendering and audio-reactive systems.