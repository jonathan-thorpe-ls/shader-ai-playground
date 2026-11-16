# **VR interaction architecture for procedural geometry**

I would make VR interaction a first-class part of the geometry metadata system, rather than treating hands and controllers as separate objects that simply push vertices around.

The key idea is:

**Hands and controllers generate interaction events and influence fields. Geometry metadata determines how the procedural structure responds.**

That gives you a consistent interaction system across grids, tubes, branches, particle networks and Voronoi structures. The same hand gesture could produce different results depending on the geometry being touched.

For example:

- Touching a branch could bend it.
- Touching a grid could create a ripple.
- Pinching a tube could compress it.
- Grabbing a branch could let you pull and stretch it.
- Hitting a particle network could send a wave through its connections.
- Squeezing a Voronoi structure could compress cells around the hand.

I would build this around five interconnected systems:

1. VR input and gesture interpretation.
2. Hand/controller interaction volumes.
3. Geometry interaction metadata.
4. Interaction forces and deformation fields.
5. Feedback and interaction state.

## **1. Treat hands and controllers as interaction sources**

Rather than making every geometry generator understand VR input, create a common interaction-source representation.

A hand or controller becomes a source of spatial information and interaction state.

[Hand data model | XR Hands | 1.3.0](https://images.openai.com/static-rsc-4/eMSOjwMwyUKYnPwWvomZvQgMd25qtqb4ONiBCdKLTM_vseaHdXapwuSdc6lpz8nJZTNPMQVr58q2XkXp3mqt5xIrTh7Oj8BqJ80Zz4uG8FW9MAvikcZezY5r0PGTSaODhXAaRfwCxgYI46xVeT1FCve8Xdth5liurdoKQXl3t5w?purpose=inline)

**Tracked hand**

Position, orientation, individual finger positions, palm normal, pinch distance, finger curl and gesture confidence.

[Valve Index Hands-On: Steam’s Big Upgrade From Vive Takes A Crowbar To Rift S](https://images.openai.com/static-rsc-4/HbSUgm4hoU1nSIXl72syP4ecLkgnvjyeSh5MPt9gdvPXjBuJJSPgVGnqgHaKTj9ieXN7Onvbqr9MwSVTbRMBR-VPbUQF6l-vQh1SlnYwyEB8F78d3vybICUsiTlrZbBgbBLg1njBu6dsafjO2jD6ztjFjMPvOYtLSIBJ9Bz0zXg?purpose=inline)

**Controller**

Position, orientation, trigger, grip, buttons, joystick, velocity and haptic output.

Both should produce the same abstract interaction data, so the geometry system does not need to know whether the input came from a hand or controller.

A conceptual structure:

```
struct InteractionSource
{
    float3 Position;
    float3 Velocity;
    quaternion Rotation;

    float3 LinearAcceleration;

    float Trigger;
    float Grip;

    float PinchStrength;
    float GrabStrength;

    uint SourceID;
    uint SourceType;
    uint InteractionState;
}
```

For tracked hands, you could also expose individual finger positions and joint transforms through a separate hand-tracking buffer.

The important distinction is between **raw tracking data** and **interpreted interaction data**. Gesture recognition should turn noisy tracking into stable interaction states before the geometry simulation consumes it.

## **2. Build interaction volumes, not just interaction points**

A hand should not interact with geometry only through a single point. Different gestures need different spatial shapes.

[Tovi Grossman](https://images.openai.com/static-rsc-4/-BmaMhJ7ocLIuM_6wT9sXs56PIqCxr6mYcXw_2NO48i-EwVKDXFy5zzFEMLQaLKMY44VqWFYlsq45Hca2h8KaA9LL3WyK6nvjLN7ecSIm165befzT1xJ0Zki7CHnxFSg5bp1ofY6wbgi1ehPl9zLKdHAwqmnwuhjeLCRrYA9Bes?purpose=inline)

[Visionary – Advanced VISual feedback for neurorehabilitatION systems based on virtuAl RealitY](https://images.openai.com/static-rsc-4/dDvHTfVcjAf1-jA1c-8C57QnVIY8M983gcshk18EHxmVkFvnjJhIx7Gx2P3zwJHLO2oYSgza337zLLxIUMzINeT0pcdzf52tLNVNu0ttQB23QJnDHARdf2nAIprNecPcxH2IgB8sEJK3Sjb_6d_LkSvTe7QTy_amBXtTXYeSIGs?purpose=inline)

[Hand Physics Lab | Meta Quest Game](https://images.openai.com/static-rsc-4/SrNPAMvJE2HqZFO1IToXNvPOhRrLYEbx4i4gSZ6utqx_lRfveRkwr_0lJX1sQpCPc3W5ZgRCCMxPduYjUwEw33ggE8_jwo8ht-mDBZMlEAspDoDCyVYu3LjF_F175P7afNCqRUo9ZgrKpxATl95snrM7S8LZvn_8b-GSgqykbDs?purpose=inline)

13

I would start with these interaction primitives:

| Primitive            | Shape                                | Typical use           |
| -------------------- | ------------------------------------ | --------------------- |
| Point                | Small sphere                         | Touching, tapping     |
| Sphere               | Radius around palm or fingertip      | Proximity, pushing    |
| Capsule              | Along finger or controller direction | Sweeping, poking      |
| Cone                 | Directional volume                   | Punching, pushing     |
| Oriented box         | Palm-aligned volume                  | Pressing, flattening  |
| Two-point constraint | Between two hands or fingers         | Pinching, stretching  |
| Grab volume          | Selected region with attachment      | Grabbing and moving   |
| Impulse volume       | Short-lived expanding region         | Hitting, impact waves |

Each primitive can be evaluated against geometry positions or a spatial acceleration structure.

This is much more flexible than writing a separate collision implementation for every gesture.

## **3. Map gestures to reusable interaction fields**

The interaction field is the bridge between the VR input and the geometry metadata.

A field can calculate an influence based on distance, direction, velocity, gesture strength and the attributes of the affected geometry.

For example, a simple radial influence:

\\[ w(d)=\operatorname{saturate}\left(1-\frac{d}{R}\right) \\]

Where \\(d\\) is distance from the interaction source and \\(R\\) is its influence radius.

You can use smoother falloff functions to avoid abrupt transitions.

Here are the interaction types I would prioritise.

### **3.1 Touch — proximity and contact**

[Che cos'è l'informatica dei materiali?  | Ansys](https://images.openai.com/static-rsc-4/HtkqMIvXdkV_kLgDEtLFu_f10MV_wzOttbtDk9VNyyoharPjbSPXYdZ__tpwvJMlbI74Z1ow8LXMwqvXcgkGzgQtmKjOBlFO--17fq3NCieNXnR77mU7SKhmQkx-r3xEp5u_Ndrpk4ZIIaiA3sP36J6DpOqQ1DbL-Dqnzmdpyu0?purpose=inline)

[ansys.com](https://www.ansys.com/it-it/simulation-topics/what-is-materials-informatics?utm_source=chatgpt.com)

Touch should be the gentlest interaction. The fingertip or palm approaches the geometry and creates a local influence.

**Suggested behaviour:**

- A fingertip touching a grid creates a small ripple.
- Touching a branch causes a local bend or vibration.
- Touching a tube creates a local indentation.
- Touching a Voronoi cell highlights the cell and its neighbours.

**Data used:**

- `RestPosition` and current position for contact location.
- `DistanceToInteraction` for influence falloff.
- `Stiffness` to determine how much the geometry yields.
- `AudioSensitivity` to combine touch with audio response.
- `RegionID` to identify the touched region.

I would make touch continuous: the closer the finger gets, the stronger the effect. This gives a natural transition from proximity to contact.

### **3.2 Push — directional displacement**

[Visionary – Advanced VISual feedback for neurorehabilitatION systems based on virtuAl RealitY](https://images.openai.com/static-rsc-4/dDvHTfVcjAf1-jA1c-8C57QnVIY8M983gcshk18EHxmVkFvnjJhIx7Gx2P3zwJHLO2oYSgza337zLLxIUMzINeT0pcdzf52tLNVNu0ttQB23QJnDHARdf2nAIprNecPcxH2IgB8sEJK3Sjb_6d_LkSvTe7QTy_amBXtTXYeSIGs?purpose=inline)

[sites.unica.it](https://sites.unica.it/visionary-project/?utm_source=chatgpt.com)

Push should use palm orientation and hand velocity to establish a direction.

Instead of simply moving nearby vertices away from the hand, calculate a directional force:

\\[ F(v)=w(d)\\,S\\,\max(0,\mathbf{n}\cdot\mathbf{p})\\,\mathbf{p} \\]

Where:

- \\(w(d)\\) is the spatial falloff.
- \\(S\\) is push strength.
- \\(\mathbf{p}\\) is the push direction.
- \\(\mathbf{n}\\) is a relevant surface or structural direction.

**Possible behaviours:**

- Push a grid and create a broad depression.
- Push a tube sideways and bend it.
- Push a branch and make it flex.
- Push a particle network and displace particles, transmitting forces through connections.
- Push a Voronoi structure and compress a group of cells.

Use `Stiffness`, `Damping`, `Mass`, `Direction` and `ConstraintWeight` to determine the response.

A particularly useful feature would be to let the user push and hold. The geometry remains displaced while the hand maintains pressure, then springs back when released.

### **3.3 Pinch — local compression and selection**

[The Future is Spatial: Building for Android XR’s Revolutionary Platform | Medium](https://images.openai.com/static-rsc-4/GvsL5foZLQ-WPytR22NjMWTra-4IPgmhr-t7JWrTWa85jZE9nyuuy1ZLI-OB79OUepjGhnn5j_r0KqIAMN3dHbtyvrzsQ2p591bzvswVlEhcGCVhHfGJE2UpVabmU2kIBXm5UOVszdTUVEDLr1XQ3u2012BFKQH02ikOz1ohGvw?purpose=inline)

[medium.com](https://medium.com/%40hiren6997/the-future-is-spatial-building-for-android-xrs-revolutionary-platform-d871bece6d56?utm_source=chatgpt.com)

Pinch is especially interesting because it can mean two different things:

1. A physical squeeze between two fingers.
2. A selection gesture that identifies a small piece of geometry.

I would support both, depending on the interaction mode.

**Physical pinch:**

- Compress a tube between two fingers.
- Squeeze a branch and alter its radius.
- Compress a grid locally.
- Squeeze a Voronoi cell and deform its neighbours.

**Selection pinch:**

- Select a branch.
- Select a cell.
- Select a particle.
- Select a region and apply another operation to it.

The two fingertips provide two spatial constraints. The system can calculate their midpoint, separation and closing velocity.

Useful metadata:

- `RestRadius`
- `LocalScale`
- `BranchID`
- `CellID`
- `RegionID`
- `Stiffness`
- `ConstraintWeight`

For a tube, for example, the distance between fingers can control the local radius. For a branch, it could control bending or twisting.

### **3.4 Hit — impulse and propagation**

[Soul Axiom on Steam](https://images.openai.com/static-rsc-4/i3sMy2914Q2BsVv8sCddXEDjMb7XwnwH8jW9Zh7t39_8zL_SGXeJ8akdETInAUmzTNBFCuBppRfHarkNDmVpLBcNYEDqxKDOQXLZEhJeEr6ZtxjG_PbJwMPY0nDpvpSftjWAlynmhw-JcQ6X65V8GvuY1MHXTGfQns3jWPISFes?purpose=inline)

[store.steampowered.com](https://store.steampowered.com/app/279900/Soul_Axiom/?utm_source=chatgpt.com)

Hitting should be an impulse, not a sustained force.

Use hand or controller velocity immediately before contact to estimate impact strength:

\\[ J = m\_{\mathrm{effective}}\Delta v \\]

The impact can then create a temporary force or velocity change in the affected geometry.

**Possible behaviours:**

- Hit a grid and send a circular ripple across its surface.
- Hit a tube and send a pulse along its length.
- Hit a branch and propagate vibration towards the tips.
- Hit a particle network and spread an impulse through graph connections.
- Hit a Voronoi cell and trigger neighbouring cells in sequence.

This is where your structural metadata becomes particularly powerful.

For a branching structure, the hit location can become a new propagation source. Instead of always propagating from the root, the wave can travel from the point of impact along the branch graph.

Useful metadata:

- `NeighbourConnectivity`
- `DistanceFromInteraction`
- `DistanceFromRoot`
- `WaveSpeed`
- `WaveAttenuation`
- `Stiffness`
- `BranchID`
- `CellID`

The impact should also create visual and audio feedback: a flash, a deformation pulse, a sound response, and potentially controller haptics.

### **3.5 Grab — attachment and manipulation**

[Hand Physics Lab by Holonautic](https://images.openai.com/static-rsc-4/6l_EeVxx0mH6UGz6WWc4xYNn42e0dKArOdGvGEdqftO3NWGgdv2wuFJ5CdmoyfQ17biQoZn7W5rjdAkpBh4rFsj6osUcvjfXC2e5LkOHznJtljdWJpShvdTEJLYoUfRm6rhes28-A_eIuctUvp8AxQjI2M-PHxmQaCW5IbZWYt4?purpose=inline)

[holonautic.itch.io](https://holonautic.itch.io/hand-physics-lab?utm_source=chatgpt.com)

Grabbing is probably the most important interaction to get right.

When the user grabs a part of the geometry, establish an attachment between the hand and a selected point or region.

The grabbed geometry should follow the hand, while the rest of the structure responds according to its connectivity and stiffness.

**Examples:**

- Grab a branch and pull it away from the root.
- Grab the end of a tube and stretch or bend it.
- Grab a section of a grid and drag it.
- Grab a particle cluster and move it.
- Grab a Voronoi cell and pull it away from neighbouring cells.

I would use a **soft constraint**, rather than directly setting vertex positions to the hand position.

A soft constraint has:

- Target position.
- Constraint strength.
- Maximum force.
- Damping.
- Affected radius or selected region.

This gives a much more natural result. A stiff branch resists movement; a flexible branch bends. A constrained root remains fixed while the tip moves.

The metadata system can control the response automatically.

### **3.6 Two-hand interactions — stretch, twist and tension**

[Virtual AI - KURONYAM on Steam](https://images.openai.com/static-rsc-4/ywCr5iEDs-mxFBdtdXbD1T7N5C-KsIOAPj3bEJXgnZMkFs7vncG2jAHhHDkrCheRcB5g6T1UuQS7WpyN4maN529rm7bcNVhCMl5bci8m5pleXDF6WBw18DOjjqzxHVDhzwXJjmedskNK_EuvhC-fzFFyUyfsQiovHj_-ZtDx7nY?purpose=inline)

[store.steampowered.com](https://store.steampowered.com/app/2588860/Virtual_AI__KURONYAM/?utm_source=chatgpt.com)

Two-hand interaction opens up much more expressive possibilities.

Use the positions and orientations of both hands to calculate:

- Distance between hands.
- Change in distance.
- Relative rotation.
- Midpoint.
- Direction between hands.

Possible interactions:

| Gesture                               | Geometry response      |
| ------------------------------------- | ---------------------- |
| Pull hands apart                      | Stretch geometry       |
| Move hands together                   | Compress geometry      |
| Rotate hands oppositely               | Twist geometry         |
| Move hands in opposite directions     | Shear or bend          |
| Hold both hands still                 | Maintain tension       |
| Move one hand while holding the other | Pivot around an anchor |

This could be especially powerful for tubes, branches and grids.

## **4. Extend the geometry metadata for interaction**

I would add a dedicated interaction metadata category alongside the geometric, structural, deformation and rendering attributes.

| Attribute               | Meaning                                          |
| ----------------------- | ------------------------------------------------ |
| `InteractionWeight`     | How strongly this vertex responds to interaction |
| `InteractionRadius`     | Local influence radius                           |
| `TouchSensitivity`      | Response to light contact                        |
| `PushSensitivity`       | Response to directional force                    |
| `PinchSensitivity`      | Response to compression                          |
| `GrabWeight`            | How strongly this region can be grabbed          |
| `ImpactSensitivity`     | Response to impulses                             |
| `InteractionDamping`    | How quickly interaction energy dissipates        |
| `InteractionConstraint` | How strongly movement is restricted              |
| `InteractionGroupID`    | Region that can be selected or manipulated       |

These attributes should be optional. A generator should only produce the ones that make sense for its geometry.

For example, a branching generator could assign:

- High `GrabWeight` to branch tips.
- High `InteractionConstraint` to the root.
- High `PushSensitivity` to thin branches.
- Lower `InteractionWeight` to thick, rigid sections.

This allows the geometry to define its own interaction character.

## **5. The interaction pipeline**

I would keep the VR interaction system separate from the geometry generators and connect them through a shared GPU interaction representation.

**VR input**

Hands, fingers, controllers, tracking

**Gesture interpretation**

Touch, push, pinch, hit, grab, two-hand gestures

**Interaction sources and volumes**

Positions, directions, radii, velocities, strengths, constraints

**Geometry query and metadata lookup**

Find affected vertices, edges, branches, cells or regions

**Interaction fields**

Contact, force, impulse, grab constraint, propagation

**GPU simulation and deformation**

Update positions, velocities and dynamic attributes

**Visual and haptic feedback**

Geometry response, highlights, sound, controller vibration

The most important design decision is that the gesture system should not directly modify geometry. It should produce interaction sources and events. The geometry system decides what those mean.

## **6. Suggested GPU data structures**

I would introduce three core structures.

### **6.1 Interaction source**

Represents a tracked hand, fingertip or controller.

```
struct GPUInteractionSource
{
    float3 position;
    float3 velocity;

    float3 direction;
    float radius;

    float strength;
    float pinchStrength;
    float grabStrength;
    float impactStrength;

    uint sourceID;
    uint sourceType;
    uint interactionMode;
    uint flags;
};
```

One source could represent a palm, another a fingertip, and another a controller. This lets you support multiple simultaneous interaction points without changing the geometry data model.

### **6.2 Interaction event**

Represents a discrete event such as a hit, pinch start, grab start or release.

```
struct GPUInteractionEvent
{
    float3 position;
    float3 direction;

    float magnitude;
    float duration;

    uint sourceID;
    uint eventType;
    uint targetRegionID;
    uint flags;
};
```

Events should be distinct from continuous interaction sources. A hand held against a surface is a continuous influence; a hit is a momentary event.

### **6.3 Grab constraint**

Represents a persistent attachment between a hand and a geometry location.

```
struct GPUGrabConstraint
{
    float3 localAnchor;
    float3 targetPosition;

    float stiffness;
    float damping;
    float maxForce;

    uint sourceID;
    uint targetRegionID;
    uint active;
};
```

The `localAnchor` identifies the grabbed point in the geometry's rest or local coordinate system. The `targetPosition` follows the tracked hand.

The simulation uses this constraint to pull the geometry towards the target while allowing the rest of the structure to react naturally.

These structures are conceptual starting points. The final C#/HLSL layout should use matching alignment, packing and buffer definitions.

## **7. Finding the geometry affected by a hand**

This is an important performance consideration. You don't want every hand interaction to test every vertex if the geometry becomes large.

I would use a two-stage query.

**Stage 1: Broad phase**

Find geometry chunks or regions close to the hand using a spatial acceleration structure:

- Spatial hash grid.
- Uniform grid.
- Bounding volume hierarchy.
- Per-branch or per-region bounds.
- Cluster bounds for particle networks.

**Stage 2: Narrow phase**

Evaluate the actual influence against candidate vertices, edges or regions.

For example, a fingertip query might return:

- Nearby vertices.
- The closest triangle.
- The nearest branch segment.
- A Voronoi cell and its neighbours.

For a branching structure, querying branch segments rather than individual vertices is especially useful. Once a branch is identified, the system can affect its vertices and propagate effects through its parent/child relationships.

A further optimisation is to maintain a GPU interaction influence buffer. Each simulation step can update only the regions near active interaction sources.

## **8. Make the geometry feel different to touch**

The most interesting part is that the same gesture should produce different results depending on the geometry's structure and metadata.

| Interaction | Grid              | Tube             | Branching structure | Particle network     | Voronoi           |
| ----------- | ----------------- | ---------------- | ------------------- | -------------------- | ----------------- |
| Touch       | Ripple            | Local pulse      | Vibration           | Particle response    | Cell highlight    |
| Push        | Depression        | Bend             | Branch flex         | Network displacement | Cell compression  |
| Pinch       | Local compression | Radius squeeze   | Twist or squeeze    | Particle cluster     | Cell squeeze      |
| Hit         | Surface wave      | Travelling pulse | Branch vibration    | Graph impulse        | Neighbour cascade |
| Grab        | Pull surface      | Bend/stretch     | Pull branch         | Move cluster         | Pull cell         |
| Two-hand    | Stretch/twist     | Stretch/twist    | Branch tension      | Separate clusters    | Expand/compress   |

This is where the metadata architecture becomes valuable: the interaction system does not need a bespoke physics implementation for every possible combination.

Instead, a geometry profile can specify how its available attributes map to interaction fields.

## **9. Add interaction modes**

I would avoid making every gesture perform every possible action simultaneously. It can become confusing when a hand approaches a structure and unexpectedly pinches, grabs or deforms it.

Instead, provide a few clear interaction modes.

- **Sculpt mode:** touch, push, pinch and pull to deform the geometry.
- **Grab mode:** select and manipulate branches, cells, regions or clusters.
- **Impulse mode:** hit or tap to generate waves and propagation effects.
- **Field mode:** move hands through space to create attractors, repulsors, turbulence or force fields.
- **Selection mode:** pinch or point to select structural elements and change their properties.

Modes can be selected through controller buttons, a virtual wrist interface, or a deliberate hand gesture.

I particularly like the idea of a field mode where each hand becomes a programmable force source. One hand could attract geometry while the other repels it. Moving the hands through the structure would create fluid, sculptural effects without needing to touch individual vertices.

## **10. A practical implementation roadmap**

I would introduce this in stages rather than building the entire VR interaction system at once.

| Stage | Feature                          | What to prove                             |
| ----- | -------------------------------- | ----------------------------------------- |
| 1     | One fingertip interaction source | Tracking and proximity influence          |
| 2     | Touch and push on a grid         | Continuous local deformation              |
| 3     | Pinch and grab                   | Stable selection and constraints          |
| 4     | Hit and impulse                  | Velocity-based impact and wave response   |
| 5     | Branch interaction               | Structural metadata and graph propagation |
| 6     | Two-hand manipulation            | Stretching, twisting and tension          |
| 7     | Multiple simultaneous sources    | Multi-hand and controller interaction     |
| 8     | Haptics and audio feedback       | Convincing interaction feedback           |

I would use a parametric grid for the first prototype because it is easy to inspect and debug. Then use a branching structure to prove that the metadata system supports topology-aware interaction, not just spatial deformation.

For the Unity side, keep tracking and gesture interpretation in the VR input layer, with a provider-independent interaction representation passed to the GPU system. That keeps the geometry architecture independent of a particular headset or input SDK.

## **My strongest recommendation**

Build the system around three abstractions:

1. **Interaction Sources** — hands, fingertips and controllers.
2. **Interaction Fields** — touch, push, pinch, hit, grab, stretch and other influences.
3. **Geometry Response Profiles** — rules that map interaction fields onto each geometry type using its metadata.

**A particularly powerful example**

Imagine grabbing the tip of a procedural branching structure with one hand.

- The hand becomes a grab constraint.
- The tip follows the hand.
- The branch bends according to its stiffness.
- The deformation propagates towards the root.
- Connected branches respond according to their connectivity and flexibility.
- A wave travels through the structure.
- Audio can amplify or attenuate the movement.
- Releasing the branch lets it spring back and settle.

[Rethinking the Future: Artificial Intelligence Ethics](https://images.openai.com/static-rsc-4/3NYkwlvtNBJiBuc6xpKPYmjwWZkHx-87Il7sj-sSXlAOo_Jlse98RkHFb1BXG_TJTqry4QfDMmTggF6G_f0zQMQkKuEy623zRtNoO-oHzrROaC5gTV_JV0UqlySM2jbcXxv-MlA4lNnasLrOhUjUUE9X9e0VRAkvyLDG5Z63C0k?purpose=inline)

[stevegreenfieldforcongress.com](https://stevegreenfieldforcongress.com/rethinking-the-future-artificial-intelligence-ethics?utm_source=chatgpt.com)

That is the sort of interaction I would aim for: not simply grabbing a mesh, but physically and creatively manipulating a living procedural structure.

**One architectural principle I would strongly preserve:** VR interaction should operate on both geometry and topology. Spatial proximity determines what the hand touches; structural metadata determines how the effect spreads and how the rest of the geometry responds.

I would make this a dedicated **VR Interaction System** in the project architecture, sharing the Geometry Metadata and GPU Deformation Field systems rather than embedding VR-specific logic inside each generator.