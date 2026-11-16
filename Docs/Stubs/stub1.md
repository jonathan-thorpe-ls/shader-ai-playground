GPU Audio-Reactive Procedural Geometry
Unity Implementation Specification

Project type: Fully GPU-driven procedural geometry and audio-reactive deformation
Engine: Unity
Primary technology: HLSL Compute Shaders, GraphicsBuffer, GPU procedural rendering
Objective: Build a modular framework for generating procedural meshes and simulating neighbour-based vertex interactions, controlled by real-time audio analysis.

1. Project Overview

The objective is to create a reusable GPU-driven procedural geometry system in Unity.

The system should generate different types of base geometry, establish vertex connectivity, simulate interactions between neighbouring vertices, and render the resulting deformed geometry without transferring vertex data back to the CPU every frame.

Audio analysis should influence the simulation through multiple independent parameters, allowing music to control physical and procedural properties rather than simply displacing vertices.

The architecture must be modular.

Three systems should operate independently:

Geometry generation.
Vertex interaction and simulation.
Audio analysis and control.

A fourth system handles GPU rendering.

The geometry generator should be interchangeable without requiring changes to the simulation solver.

Likewise, the simulation should be reusable across different geometry types.

Core objectives
Generate procedural geometry using compute shaders.
Store geometry and simulation data in GPU buffers.
Establish vertex-to-vertex connectivity.
Implement neighbour-based spring interactions.
Support attraction, repulsion, damping and localised excitation.
Support waves propagating through mesh connectivity.
Integrate real-time audio analysis.
Render deformed geometry directly from GPU buffers.
Avoid per-frame GPU-to-CPU vertex readback.
Support multiple procedural geometry generators.
Allow multiple simulation behaviours to be combined.
2. System Architecture

The intended data flow:

text
                AUDIO SOURCE
                     |
                     v
              AUDIO ANALYSIS
                     |
          +----------+----------+
          |          |          |
         Bass       Mids       Highs
          |          |          |
          +----------+----------+
                     |
                     v
              AUDIO PARAMETERS
                     |
                     v
              GPU SIMULATION
                     |
          +----------+----------+
          |          |          |
       Springs    Repulsion   Excitation
          |          |          |
          +----------+----------+
                     |
                     v
              POSITION BUFFERS
                     |
                     v
               GPU RENDERING
                     |
                     v
              PROCEDURAL MESH


Geometry generation is an independent initialisation process.

text
             GEOMETRY GENERATOR
                     |
          +----------+----------+
          |          |          |
       Positions  Neighbours  Indices
          |          |          |
          +----------+----------+
                     |
                     v
              GPU SIMULATION


The CPU should primarily manage:

Audio capture and FFT analysis.
Compute shader dispatches.
Simulation parameters.
Geometry initialisation.
GPU buffer allocation and disposal.
Rendering configuration.

The GPU should manage:

Vertex positions.
Vertex velocities.
Neighbour interactions.
Simulation integration.
Energy propagation.
Geometry generation.
Normal calculation.
Procedural rendering.
3. GPU Data Architecture

Use GraphicsBuffer or ComputeBuffer resources to store simulation and geometry data.

GraphicsBuffer is preferred where appropriate because it supports structured, vertex, index and indirect argument use cases.

3.1 Core buffers
Buffer	Type	Purpose
Position A	float3	Current vertex positions
Position B	float3	Next vertex positions
Velocity A	float3	Current vertex velocities
Velocity B	float3	Next vertex velocities
Original Positions	float3	Equilibrium positions
Neighbours	uint	Connected vertex indices
Neighbour Counts	uint	Number of neighbours per vertex
Rest Lengths	float	Original edge lengths
Triangle Indices	uint	Rendering topology
Vertex Normals	float3	Calculated surface normals
Vertex Excitation	float	Local simulation energy
Audio Parameters	float values	Audio-driven simulation controls

Use ping-pong buffers for position and velocity updates.

The simulation should read from one set of buffers and write to another.

After each simulation step, swap the buffers.

This prevents threads from reading partially updated simulation data.

3.2 Neighbour storage

Initially, use a fixed maximum number of neighbours per vertex.

For example:

hlsl
#define MAX_NEIGHBOURS 8

StructuredBuffer<uint> _Neighbours;
StructuredBuffer<float> _RestLengths;
StructuredBuffer<uint> _NeighbourCounts;


Use flattened indexing:

hlsl
uint index = vertexIndex * MAX_NEIGHBOURS + slot;


Unused neighbour slots should contain an invalid index such as 0xFFFFFFFF.

For irregular topology, investigate compressed sparse row (CSR) storage:

Neighbour offsets.
Flattened neighbour indices.
Flattened rest lengths.

CSR allows vertices to have different numbers of neighbours without wasting memory on unused slots.

The neighbour representation should be independent of the geometry generator.

4. Procedural Geometry Generators

Implement geometry generators as independent compute shader modules.

Every generator must output a standardised geometry representation.

Required outputs:

Vertex positions.
Neighbour connectivity.
Neighbour counts.
Rest lengths.
Triangle indices.
Optional vertex attributes.

The same simulation solver should work with every generator.

4.1 Icosphere Generator

Priority: First implementation

Generate an icosphere using an icosahedron and recursive triangular subdivision.

Process:

Generate the initial icosahedron.
Subdivide triangular faces.
Create midpoint vertices.
Normalise midpoint positions to the desired radius.
Generate triangle indices.
Establish vertex connectivity.
Calculate rest lengths.

Potential applications:

Organic spherical structures.
Radial deformation.
Elastic surfaces.
Localised excitation.
Wave propagation.

Use the original spherical positions as equilibrium positions.

Important: midpoint vertices shared by adjacent triangles must be deduplicated. Maintain consistent vertex indexing and avoid generating disconnected duplicates along shared edges.

4.2 Parametric Surface Generator

Generate geometry from mathematical functions.

Use:

text
P(u,v) = (x(u,v), y(u,v), z(u,v))


Initial geometry types:

Plane.
Sine wave surface.
Twisted surface.
Saddle.
Torus.
Cylindrical surface.

For a regular grid, each vertex can calculate its neighbours using row and column coordinates.

Four-way connectivity:

Left.
Right.
Up.
Down.

Optional eight-way connectivity includes diagonal neighbours.

Potential applications:

Travelling waves.
Surface tension.
Twisting.
Directional deformation.
Audio-reactive landscapes.
4.3 Branching Network Generator

Generate connected graph structures.

Initial examples:

Trees.
Roots.
Coral.
Fractal branching.
Neural-network-like structures.

Represent the underlying geometry as a graph:

text
G = (V, E)


Where V represents vertices and E represents connections.

Generate a graph first, then construct visible geometry around it.

Potential applications:

Energy propagation through branches.
Elastic networks.
Variable stiffness.
Branch-specific excitation.
4.4 Voronoi Generator

Generate irregular cellular geometry.

Potential applications:

Cellular structures.
Porous surfaces.
Irregular lattices.
Organic networks.

This is an advanced generator.

Investigate GPU Voronoi construction, spatial partitioning and connectivity generation.

The generator should output a usable neighbour graph and, where required, a closed triangle mesh.

4.5 Tube and Curve Generator

Generate curves and construct tubes around them.

Initial curve types:

Helix.
Spiral.
Torus knot.
Lissajous curve.
Procedural spline.

Each tube consists of rings of vertices.

Establish connectivity in two directions:

Longitudinal connectivity along the tube.
Radial connectivity around each ring.

Potential applications:

Tube expansion.
Travelling deformations.
Radial oscillation.
Twisting.
Elastic tubular structures.
4.6 Particle Network Generator

Generate points in 3D space and connect them using configurable rules.

Possible connection strategies:

Nearest neighbours.
Radius-based connections.
Procedural graph generation.
Spatial-grid connections.

Potential applications:

Organic structures.
Interconnected particle systems.
Attraction and repulsion.
Dynamic graph simulations.

For larger point counts, use a spatial hash or GPU spatial grid to avoid naive all-pairs neighbour searches.

5. Neighbour-Based Simulation

This is the central simulation system.

Each vertex interacts with its connected neighbours.

The primary simulation model is a mass-spring system.

Each vertex stores:

Position.
Velocity.
Mass.
Original position.
Neighbour indices.
Neighbour rest lengths.
5.1 Spring forces

For vertices i and j:

text
delta = position[j] - position[i]

distance = length(delta)

direction = delta / distance

stretch = distance - restLength

force = direction * stretch * stiffness


Mathematically:

𝐹
𝑖
𝑗
=
𝑘
𝑠
(
𝑑
𝑖
𝑗
−
𝐿
𝑖
𝑗
)
𝑃
𝑗
−
𝑃
𝑖
𝑑
𝑖
𝑗

Where:

𝑘
𝑠
 = spring stiffness.
𝑑
𝑖
𝑗
 = current distance.
𝐿
𝑖
𝑗
 = rest distance.
𝑃
𝑖
 and 
𝑃
𝑗
 = vertex positions.

Accumulate the spring forces for each vertex.

Each compute thread should calculate the forces acting on one vertex by reading its neighbours.

Each thread writes only to its own output position and velocity.

Avoid multiple threads writing to the same simulation vertex.

5.2 Restoring forces

Add a restoring force towards the original position.

text
restoringForce =
    (originalPosition - currentPosition) * restoringStrength


This helps maintain the overall shape.

Make restoring strength configurable.

5.3 Velocity integration

Use a semi-implicit Euler integration scheme.

𝑉
𝑛
𝑒
𝑤
=
𝑉
𝑜
𝑙
𝑑
+
(
𝐹
𝑚
−
𝑐
𝑉
𝑜
𝑙
𝑑
)
Δ
𝑡
𝑃
𝑛
𝑒
𝑤
=
𝑃
𝑜
𝑙
𝑑
+
𝑉
𝑛
𝑒
𝑤
Δ
𝑡

Where:

F = total force.
m = mass.
c = damping.
Δt = timestep.

Use a fixed simulation timestep.

Support multiple simulation substeps per rendered frame when necessary.

Investigate stability as a function of stiffness, vertex spacing and timestep.

5.4 Simulation constraints

Implement configurable constraints:

Maximum displacement.
Maximum velocity.
Minimum and maximum edge length.
Optional volume preservation.
Optional collision constraints.

These should be optional modules rather than hardcoded into the basic spring solver.

For more stable and controllable elastic systems, investigate Position-Based Dynamics (PBD) or Extended Position-Based Dynamics (XPBD) as future solver alternatives.

6. Additional Interaction Models

The simulation architecture should support multiple independent interaction types.

6.1 Attraction and repulsion

Introduce additional forces between vertices.

Potential behaviours:

Local attraction.
Local repulsion.
Global expansion.
Localised compression.
Audio-triggered separation.

Allow attraction and repulsion strength to be controlled independently.

Avoid unnecessary all-pairs calculations. Use the existing graph or spatial neighbourhood where possible.

6.2 Connectivity-Based Wave Propagation

Implement energy propagation through vertex connectivity.

Each vertex stores an excitation value.

Energy spreads between neighbouring vertices.

Conceptually:

𝐸
𝑖
𝑡
+
1
=
𝐸
𝑖
𝑡
+
𝛼
∑
𝑗
(
𝐸
𝑗
𝑡
−
𝐸
𝑖
𝑡
)

This is a simplified diffusion model.

For more wave-like behaviour, implement a damped second-order wave simulation using current and previous excitation buffers.

The resulting energy can influence:

Vertex displacement.
Spring stiffness.
Local vibration.
Colour.
Emission.
Secondary procedural effects.

The objective is to create waves that travel through topology rather than simply through 3D spatial distance.

6.3 Localised Excitation

Allow specific regions of the geometry to receive external forces.

For example:

𝐹
𝑎
𝑢
𝑑
𝑖
𝑜
=
𝐴
(
𝑡
)
𝑒
−
𝜆
𝑟
𝑁
⃗

Where:

A(t) = audio amplitude.
λ = falloff coefficient.
r = distance from excitation point.
N = excitation direction.

Support different excitation modes:

Radial.
Directional.
Normal-based.
Randomised.
Vertex-index-based.

Excitation points should be configurable and potentially animated.

6.4 Multiple Spring Types

Support multiple connection types.

For example:

Spring type	Purpose
Structural	Maintains geometry shape
Shear	Controls diagonal deformation
Bending	Controls resistance to curvature
Long-range	Connects distant regions
Secondary	Adds local surface behaviour

Each spring type should have independent stiffness and damping parameters.

This allows different physical behaviours to be combined.

7. Audio Analysis

Use Unity's AudioSource and FFT spectrum analysis.

The CPU performs audio analysis and sends a compact parameter set to the GPU.

Do not send the entire audio spectrum to the GPU unless required by a specific effect.

Initially, calculate:

Bass amplitude.
Mid-frequency amplitude.
High-frequency amplitude.
Overall RMS amplitude.
Onset or beat strength.

Apply attack/release smoothing to the audio values.

Use separate attack and release coefficients to prevent unstable visual behaviour.

7.1 Suggested audio mapping
Audio parameter	Simulation influence
Bass	Overall excitation
Mids	Spring stiffness
Highs	Local vibration
RMS	Global deformation intensity
Onset strength	Impulse forces

All mappings should be configurable.

Avoid hardcoding audio behaviour into the simulation kernel.

7.2 Audio-driven simulation parameters

Examples:

text
springStiffness =
    baseStiffness + bass * bassStiffnessMultiplier

excitationStrength =
    baseExcitation + rms * excitationMultiplier

vibrationStrength =
    highFrequency * vibrationMultiplier


The audio analyser should output normalised values.

The GPU simulation should be responsible for interpreting those values.

8. GPU Rendering

The rendering system should consume the simulation buffers directly.

Avoid copying deformed vertex data back to CPU memory every frame.

8.1 Rendering approach

Use procedural or indirect rendering.

Investigate Unity APIs:

GraphicsBuffer.
GraphicsBuffer.Target.Structured.
GraphicsBuffer.Target.Vertex.
GraphicsBuffer.Target.Index.
GraphicsBuffer.Target.IndirectArguments.
ComputeShader.Dispatch().
Graphics.RenderPrimitivesIndirect().
Graphics.RenderPrimitives() where appropriate.

Choose the drawing API according to the Unity version and rendering pipeline.

8.2 Rendering pipeline
text
POSITION BUFFERS
       |
       v
COMPUTE SIMULATION
       |
       v
DEFORMED POSITIONS
       |
       v
NORMAL CALCULATION
       |
       v
VERTEX SHADER
       |
       v
FRAGMENT SHADER
       |
       v
RENDERED GEOMETRY


The vertex shader should read the deformed positions directly from the GPU buffer.

Triangle indices should also remain GPU-resident.

8.3 Normal calculation

Deformation changes surface orientation.

Implement a separate normal calculation pass.

Possible approaches:

Calculate face normals and accumulate them per vertex.
Calculate normals from neighbouring positions for regular parametric surfaces.
Use analytical normals for suitable mathematical surfaces.

For irregular triangular meshes, use a safe accumulation strategy.

Avoid writing shared vertex normals from multiple threads without a suitable reduction strategy.

One straightforward approach is to calculate each vertex normal by iterating through its adjacent faces.

9. Recommended Compute Shader Organisation

Keep individual responsibilities separate.

Suggested files:

text
Shaders/
    Geometry/
        GenerateIcosphere.compute
        GenerateGrid.compute
        GenerateBranching.compute
        GenerateTube.compute
        GenerateParticleNetwork.compute

    Simulation/
        SimulateSprings.compute
        SimulateAttraction.compute
        SimulateRepulsion.compute
        PropagateEnergy.compute
        ApplyAudioForces.compute
        CalculateNormals.compute

    Rendering/
        ProceduralGeometry.shader
        ProceduralGeometry.hlsl


Suggested C# scripts:

text
Scripts/
    Core/
        GeometryManager.cs
        SimulationManager.cs
        GpuBufferManager.cs
        RenderingManager.cs

    Geometry/
        IcosphereGenerator.cs
        ParametricSurfaceGenerator.cs
        BranchingGenerator.cs
        TubeGenerator.cs
        ParticleNetworkGenerator.cs

    Simulation/
        SimulationSettings.cs
        SimulationParameters.cs

    Audio/
        AudioAnalyser.cs
        AudioParameters.cs


The C# classes should manage resources and dispatches rather than performing vertex simulation.

10. Implementation Roadmap

Implement incrementally.

Do not attempt all geometry generators and simulation types simultaneously.

Phase 1: GPU Geometry Generation

Objective: Generate an icosphere entirely on the GPU.

Tasks:

Allocate GPU buffers.
Generate initial icosahedron data.
Implement subdivision.
Generate unique vertices.
Generate triangle indices.
Generate neighbour connectivity.
Calculate rest lengths.
Validate topology.
Render the undeformed geometry.

Acceptance criteria:

Icosphere is correctly generated.
Triangle topology is valid.
Neighbour relationships are correct.
No CPU vertex readback is needed for rendering.
Phase 2: Neighbour Simulation

Objective: Implement a basic GPU mass-spring system.

Tasks:

Create position ping-pong buffers.
Create velocity ping-pong buffers.
Implement neighbour force calculations.
Implement restoring forces.
Implement damping.
Implement velocity integration.
Implement buffer swapping.
Add fixed timestep and substeps.
Expose simulation parameters.

Acceptance criteria:

Geometry responds to external forces.
Neighbour interactions work correctly.
The system returns towards equilibrium.
No GPU thread writes to another vertex's simulation state.
Simulation remains stable under tested conditions.
Phase 3: GPU Rendering

Objective: Render directly from GPU simulation buffers.

Tasks:

Implement a procedural vertex shader.
Read positions from the GPU buffer.
Read triangle indices.
Implement normal calculation.
Add a basic material.
Validate indirect or procedural rendering.

Acceptance criteria:

Deformation is visible.
No per-frame CPU vertex readback.
Normals update correctly.
Rendering and simulation use consistent buffer indexing.
Phase 4: Audio Integration

Objective: Connect real-time audio analysis to simulation parameters.

Tasks:

Implement FFT analysis.
Extract bass, mid and high bands.
Calculate RMS.
Implement onset detection.
Add attack/release smoothing.
Upload compact audio parameters to the GPU.
Connect audio parameters to simulation forces.

Acceptance criteria:

Different frequency bands create distinguishable behaviours.
Beat impulses generate visible localised deformation.
Audio input remains stable.
Simulation does not become numerically unstable at high audio amplitudes.
Phase 5: Additional Interaction Models

Implement:

Attraction and repulsion.
Connectivity-based wave propagation.
Localised excitation.
Multiple spring types.
Secondary procedural deformation.

Each interaction should be independently configurable.

Phase 6: Additional Geometry Generators

Implement:

Parametric grid.
Tube generator.
Branching structures.
Particle network.
Voronoi geometry.

Every generator must conform to the standardised geometry buffer interface.

Phase 7: Performance and Visual Development

Investigate:

Different vertex counts.
Neighbour counts.
Simulation substeps.
Thread group sizes.
Buffer memory usage.
Multiple simulation passes.
GPU profiling.
Normal calculation performance.
Rendering overhead.

Build debugging visualisations for:

Neighbour connectivity.
Vertex positions.
Vertex velocities.
Spring forces.
Excitation energy.
Original versus deformed geometry.
11. Important Technical Considerations
GPU race conditions

Each vertex thread should calculate its own forces using read-only neighbour data.

Write results to separate output buffers.

Avoid multiple threads modifying shared simulation values.

Ping-pong buffers

Do not update positions in place while other threads are reading them.

Use separate input and output buffers.

Swap them after each simulation step.

Numerical stability

Spring simulations can become unstable when stiffness is too high or timesteps are too large.

Use:

Fixed timesteps.
Simulation substeps.
Damping.
Velocity limits where appropriate.
Position constraints.
XPBD as a possible future solver.
Topology validation

Every geometry generator should validate:

Vertex indices.
Neighbour counts.
Neighbour symmetry where required.
Rest lengths.
Triangle winding.
Degenerate triangles.
Buffer bounds.
GPU memory

Estimate memory usage before increasing vertex counts.

For example, position and velocity ping-pong buffers each require two float3 arrays.

Use appropriate buffer formats and avoid unnecessary duplicate data.

Unity compatibility

Confirm the target Unity version and rendering pipeline before choosing the final rendering implementation.

Check:

Compute shader support.
GraphicsBuffer support.
Indirect rendering support.
Shader model requirements.
GPU backend compatibility.
Platform-specific limitations.

Do not assume every rendering API behaves identically across DirectX, Vulkan and Metal.

12. Initial Deliverable

The first implementation should be deliberately small.

Build a working prototype containing:

GPU-generated icosphere.
GPU-generated neighbour connectivity.
GPU mass-spring simulation.
Position and velocity ping-pong buffers.
GPU procedural rendering.
Basic bass and mid-frequency analysis.
Audio-controlled excitation.

The prototype should demonstrate:

A sphere that can be deformed through neighbour interactions.
Localised excitation spreading through the mesh.
Elastic recovery.
Audio-controlled spring behaviour.
Fully GPU-resident geometry during runtime.

Only after this prototype works should additional geometry generators and simulation models be introduced.

13. Design Principles

The following principles should guide all future development.

1. Geometry and simulation must remain independent.

A new geometry generator should not require a new simulation solver.

2. The GPU is the source of truth.

Simulation positions and velocities should remain GPU-resident.

3. Topology should be reusable.

All generators should output a standardised connectivity representation.

4. Interactions should be modular.

Spring forces, repulsion, excitation and wave propagation should be independently configurable.

5. Audio should control behaviour, not dictate geometry.

Audio should influence physical properties, simulation forces and procedural parameters.

6. Prefer composable systems.

Different interaction models should be capable of operating together.

7. Prioritise correctness before complexity.

Establish a stable and validated simulation before implementing complex geometry and visual effects.

14. Instructions for the Implementing AI Model

Treat this document as the project specification.

Work incrementally and produce working Unity C# and HLSL implementations.

For each implementation phase:

Explain the proposed architecture.
Identify the required GPU buffers and their formats.
Implement the compute shader.
Implement the corresponding C# resource management and dispatch logic.
Explain buffer binding and buffer swapping.
Explain how to test the implementation.
Identify performance and compatibility considerations.
Do not proceed to the next phase until the current phase works.

Do not replace the GPU simulation with CPU-side mesh manipulation.

Do not introduce per-frame GPU-to-CPU vertex readback.

Avoid implementing every feature in one large compute shader.

Keep the code modular, documented and reusable.

The immediate task is Phase 1: GPU-generated icosphere with valid triangle topology and neighbour connectivity, followed by a minimal GPU rendering test.

The ultimate goal is a reusable, fully GPU-driven, procedural geometry and audio-reactive deformation framework for Unity.