# Phase 8 - Hybrid Timeline, Vector and Color Controls

## Status

Planning document for Phase 8 of
[GPU Audio-Reactive Procedural Geometry Framework](../ProceduralGeometryFramework.md).

Phase 7 established provider-neutral control signals, XAS Timeline ingestion,
Graph Toolkit compilation and typed float/pulse control-slot routing. Phase 8
extends that foundation without changing its dependency direction:

- `com.sonosthesia.signals` remains independent of XAS and Timeline;
- `com.sonosthesia.signals.xas` remains the XAS integration layer;
- manual curves enter through standard Unity AnimationTracks;
- the graph decides how manual, XAS and future provider signals combine.

The completion slice is one Timeline containing an XAS control track and an
AnimationTrack, both feeding one control graph. The graph produces Vector2,
Vector3 and Color outputs into a generic test consumer. Procedural geometry
specific consumers are later adapters, not Phase 8 architecture owners.

## 1. Decisions

- Manual curve authoring uses a standard Unity AnimationTrack.
- The AnimationTrack binds to `ControlSignalTarget`.
- Four animatable manual inputs are provided: `ManualInput0` through
  `ManualInput3`.
- Manual values remain separate named graph inputs. Graph nodes determine
  add, multiply, override, mix or modulation semantics.
- Overlapping AnimationTrack clips use Unity's normalized AnimationTrack clip
  blending.
- Phase 8 adds Vector2, Vector3 and Color graph values, operations, outputs
  and slots.
- Initial scalar-to-vector generators include polar, cylindrical, spherical,
  direction-with-length and deterministic oscillator/noise/Brownian paths.
- Initial scalar-to-color generators include gradient lookup, HSV, palette
  interpolation and intensity/tint.
- Brownian nodes are deterministic functions of Timeline time, so seeks and
  reverse scrubs produce the same output without replaying an event history.
- The Phase 8 gate is a hybrid XAS/manual graph driving one generic Vector2,
  Vector3 and Color consumer.

## 2. Goals

Phase 8 will:

1. preserve Phase 7 float and pulse assets and slot consumers;
2. add Vector2, Vector3 and Color to the control graph and routing contracts;
3. add typed vector and color operations;
4. add parameterized scalar-to-vector and scalar-to-color generators;
5. expose four manual Timeline-animatable scalar inputs on
   `ControlSignalTarget`;
6. merge a current provider frame with current manual AnimationTrack values
   before graph evaluation;
7. compile manual and provider source names into the same provider-neutral
   runtime graph;
8. provide deterministic Brownian and noise path generators suitable for
   scrubbing;
9. prove the hybrid signal path without steady-state managed allocation;
10. avoid adding a custom Timeline track or graph-specific Timeline mixer.

## 3. Non-goals

Phase 8 does not:

- add live audio analysis;
- create custom curve-editing UI that duplicates Unity's AnimationTrack;
- make XAS aware of manually authored curves;
- make manual curves aware of XAS;
- allow arbitrary reflection-based animation bindings;
- add Quaternion, Matrix, Texture or object-reference signal types;
- add a runtime graph editor or runtime animation clip authoring;
- redesign Timeline;
- require procedural geometry or a particular renderer;
- add spatial/XR interaction.

## 4. Architecture

The Phase 8 data path is:

```text
XAS Timeline track ----------------+
                                   |
                                   v
                         provider ControlSignalFrame
                                   |
standard AnimationTrack -----+      |
animates ManualInput0..3      |    |
                              v    v
                     ControlSignalTarget
                     - retain provider frame
                     - sample four manual inputs
                     - merge by stable signal ID
                     - evaluate typed graph
                              |
                              v
                     CompiledControlGraph
                              |
                              v
     float / pulse / Vector2 / Vector3 / Color routes
                              |
                              v
               additive IControl...SlotTarget adapters
```

The graph owns combination semantics. A graph may:

- add a manual offset to an audio signal;
- multiply audio by a manual amount;
- use manual curves as a base and audio as excitation;
- select or crossfade between manual and provider values;
- use manual curves as parameters of vector or color generators;
- use audio values and manual curves independently in different branches.

The target does not perform an implicit audio/manual blend.

## 5. Package and dependency boundaries

No XAS reference is added to `com.sonosthesia.signals`.

Changes are primarily within:

```text
Packages/com.sonosthesia.signals
  Runtime/Core/
    ControlSignalData.cs
    CompiledControlGraph.cs
    ControlGraphExecutor.cs
    ControlSignalTarget.cs
    IControlVectorSlotTargets.cs
    ManualControlInputBindings.cs
  Editor/Graph/
    ControlSignalNodes.cs
    ControlSignalGraphImporter.cs
  Tests/EditMode/
    TypedControlGraphTests.cs
    ManualInputTests.cs
  Tests/PlayMode/
    TimelineHybridControlTests.cs
```

`com.sonosthesia.signals.xas` remains unchanged in dependency direction. It
may gain Phase 8 interoperability tests, but it does not depend on vector,
color or manual-curve semantics.

`com.sonosthesia.procgeo` does not need to change for the Phase 8 gate. Any
geometry consumer is an adapter added later and depends on the expanded
provider-neutral contracts.

## 6. Typed control values

### Signal types

Extend `ControlSignalType` with:

```text
Continuous
Pulse
Vector2
Vector3
Color
```

Existing integer values for `Continuous` and `Pulse` remain stable for
serialized graph/output data.

### Internal value representation

Replace the Phase 7 float-only operation value array with a fixed
four-component `ControlValue` representation:

```csharp
struct ControlValue
{
    float X;
    float Y;
    float Z;
    float W;
}
```

Mapping:

| Signal type | Components |
| --- | --- |
| Continuous | X |
| Pulse | X strength, Y magnitude; started state remains in a byte lane |
| Vector2 | X, Y |
| Vector3 | X, Y, Z |
| Color | R, G, B, A in X, Y, Z, W |

The compiled operation record stores `OutputType`. The importer validates every
input/output type and does not rely on a runtime type tag. This keeps the
runtime graph flat and avoids boxing or polymorphic operation objects.

### Compatibility

- Existing float operations continue to read/write X.
- Existing pulse operations continue to use strength/magnitude/start lanes.
- Existing `TryGetOutput` float/pulse APIs remain available.
- Add typed output overloads:

```csharp
bool TryGetOutput(int index, out Vector2 value);
bool TryGetOutput(int index, out Vector3 value);
bool TryGetOutput(int index, out Color value);
```

Returning the wrong type is a false result or a clearly reported invalid access;
it must never reinterpret components silently.

## 7. Manual AnimationTrack inputs

### Fixed bank

`ControlSignalTarget` exposes four serialized, animatable fields:

```text
ManualInput0
ManualInput1
ManualInput2
ManualInput3
```

They are ordinary public properties backed by serialized floats so Unity's
AnimationTrack can discover and animate them.

Each slot also has an authoring binding:

```csharp
struct ManualControlInputBinding
{
    bool enabled;
    string signalName;
    float defaultValue;
}
```

The binding index is fixed. Signal names become stable `ControlSignalId`
values at target initialization.

Recommended names for the Phase 8 demo:

```text
manual.input0
manual.input1
manual.input2
manual.input3
```

Users may rename the IDs to meaningful graph inputs such as
`manual.baseAmplitude` or `manual.hue`.

### Availability and defaults

- Enabled manual slots are always available to the graph.
- Their current value is the serialized/animated property value.
- Outside an active AnimationTrack clip, Unity's normal AnimationTrack behavior
  returns the property to its authoring/default state according to the
  track's extrapolation settings.
- A disabled slot is unavailable and cannot satisfy a required graph source.
- A graph may reference a manual signal name even when no provider currently
  emits that name.

### Provider/profile merge

Before graph evaluation:

1. copy the active provider frame into target-owned graph input buffers;
2. resolve each manual binding to a compiled graph continuous source index;
3. write the current `ManualInput0...3` values;
4. mark those inputs available;
5. preserve pulse buffers from the provider;
6. evaluate the graph once.

If a provider and a manual slot use the same stable ID, target validation
reports a collision. Phase 8 does not define precedence implicitly.

### Timeline evaluation order

Standard Timeline tracks do not provide a reliable contract for arbitrary
cross-track `ProcessFrame` ordering. The XAS track therefore submits the
provider frame to `ControlSignalTarget`; when manual inputs are configured,
the target defers graph evaluation to `LateUpdate`.

This ensures the active AnimationTrack has written its properties before the
graph reads them. Targets without manual inputs may preserve Phase 7 immediate
evaluation.

During EditMode scrubbing, the target uses the editor update/evaluation path
rather than relying on PlayMode `LateUpdate` alone.

## 8. Graph authoring and compiler

### Input nodes

Phase 8 retains existing XAS and generic provider input nodes and adds:

- `Manual Input 0`
- `Manual Input 1`
- `Manual Input 2`
- `Manual Input 3`

These compile to provider-neutral source IDs selected by the corresponding
manual binding definitions. They do not embed AnimationTrack or XAS types.

### Basic typed operations

#### Vector2

- Vector2 constant;
- add/subtract;
- multiply/divide by float;
- multiply component-wise;
- magnitude;
- normalize;
- clamp magnitude;
- Vector2 lerp;
- split to X/Y;
- Vector2 from float X/Y.

#### Vector3

- Vector3 constant;
- add/subtract;
- multiply/divide by float;
- multiply component-wise;
- magnitude;
- normalize;
- clamp magnitude;
- Vector3 lerp;
- split to X/Y/Z;
- Vector3 from X/Y/Z floats.

#### Color

- Color constant;
- RGB/HSV conversion;
- multiply Color by float;
- multiply component-wise;
- Color lerp;
- saturation and brightness transforms;
- alpha replace;
- split to R/G/B/A floats.

### Output nodes

Add:

- `Vector2 Control Output`;
- `Vector3 Control Output`;
- `Color Control Output`.

Output IDs remain stable strings/hashes. Type is compiled into
`ControlGraphOutput.Type`.

### Validation

The importer and `OnGraphChanged` must reject:

- incompatible typed connections;
- Vector2 used where Vector3 or Color is required;
- Color interpreted as Vector3 without an explicit conversion node;
- mismatched manual binding/source IDs;
- duplicate manual IDs;
- Brownian/noise parameters outside supported ranges;
- unconnected generator controls that are not explicitly defaulted;
- graph operation output type not matching destination type;
- route output type not matching the resolved slot type.

On Unity 6000.6 the graph uses explicit typed ports and importer validation;
it does not rely on the 6000.7 polymorphic `WithDataTypes` API.

## 9. Float-to-vector generators

### Polar Vector2

Inputs:

- angle;
- radius;

Output:

```text
(cos(angle), sin(angle)) * radius
```

An option selects degrees or radians.

### Cylindrical Vector3

Inputs:

- angle;
- radius;
- height;

Output:

```text
(cos(angle) * radius, height, sin(angle) * radius)
```

Coordinate up-axis is explicit in node options.

### Spherical Vector3

Inputs:

- azimuth;
- elevation;
- radius.

Output:

```text
(cos(elevation) * cos(azimuth),
 sin(elevation),
 cos(elevation) * sin(azimuth)) * radius
```

### Direction with length

Inputs:

- base direction Vector3;
- length float.

Output:

```text
normalize(direction) * length
```

Zero/near-zero direction uses an explicit fallback direction node option.

### Oscillator path

Inputs:

- frequency;
- amplitude;
- excitation;
- phase.

Outputs separate Vector2 and Vector3 node variants. The default node combines
sine/cosine channels with configurable phase offsets. Output is a deterministic
function of absolute Timeline time and therefore supports exact seeks.

### Brownian Vector2 and Vector3

Add separate `Brownian Vector2` and `Brownian Vector3` nodes.

Inputs:

- amplitude;
- frequency;
- excitation.

Options:

- seed;
- octaves;
- persistence;
- lacunarity;
- axis offsets;
- output bounds.

The node evaluates a deterministic multi-octave smooth-noise field at:

```text
noiseTime = timelineTime * frequency
output = normalize/bound(sum(octaveNoise) * amplitude * excitation)
```

Different spatial axes use independent deterministic offsets from the seed.
Brownian output never accumulates random state across frames. This makes:

- repeated evaluation of a timeline time deterministic;
- reverse and arbitrary scrubbing exact;
- PlayableGraph rebuilds stable;
- separate targets with the same graph/time/seed identical.

Excitation below zero is clamped to zero or configured to use absolute value
explicitly. Bounds prevent huge values from destabilizing downstream consumers.

## 10. Float-to-color generators

### Gradient lookup

Inputs:

- sample float.

Options:

- Unity `Gradient` or a serialized equivalent;
- clamp/repeat/ping-pong sample mode;
- alpha policy.

The importer serializes the gradient into a compact sorted key representation
in the runtime graph so runtime evaluation performs no Unity object lookup.

### HSV

Inputs:

- hue;
- saturation;
- value;
- optional alpha.

Hue uses configurable wrap or clamp semantics. The output is an RGBA Color.

### Palette interpolation

Inputs:

- palette position;
- optional blend/energy.

Options:

- ordered Color keys;
- clamp/repeat/ping-pong.

The runtime stores inline Color values. Palette lookup is interpolation-only;
it performs no allocation.

### Intensity/tint

Inputs:

- base Color;
- scalar intensity.

Output:

```text
baseRGB * intensity, with optional alpha retention
```

Additional options provide saturation, clamp and gamma/linear interpretation.

## 11. Typed control slots

Preserve the Phase 7 `IControlSlotTarget` API for float and pulse consumers.
Add additive interfaces:

```csharp
public interface IControlVector2SlotTarget
{
    void WriteVector2(int slotHandle, Vector2 value);
}

public interface IControlVector3SlotTarget
{
    void WriteVector3(int slotHandle, Vector3 value);
}

public interface IControlColorSlotTarget
{
    void WriteColor(int slotHandle, Color value);
}
```

Every typed consumer also implements `IControlSlotTarget` so slot discovery and
type metadata remain centralized.

The router resolves one target reference, type and integer slot handle at
enable time. It performs type interface dispatch but no string lookup, dynamic
reflection or dictionary access in the frame loop.

Route gain/bias behavior is explicit:

- float: scalar gain/bias;
- Vector2/Vector3: component-wise scale and optional authored offset;
- Color: RGB scale and optional tint/offset;
- pulse: strength/magnitude transform only.

Route transforms use value types and do not allocate.

## 12. Hybrid XAS/manual demo

Create a Phase 8 test scene:

- one `PlayableDirector`;
- one Phase 7 `XasTrack`;
- one standard AnimationTrack bound to `ControlSignalTarget`;
- four animated manual properties, with no custom Timeline editor;
- one `.controlgraph` asset;
- one `HybridControlConsumer` test component.

Example graph:

```text
XAS RMS ------------- Remap ---+
                               Add ---> Polar radius ---> Vector2 Output
ManualInput0 ------------------+

ManualInput1 ----------------- Cylindrical angle ----------+
XAS Lows ----- Remap --------- Cylindrical radius ---------+-> Vector3 Output
ManualInput2 ----------------- Cylindrical height ---------+

ManualInput3 ---- HSV Hue
XAS Highs ---- Remap ---- HSV Value --------------------------> Color Output
```

The generic consumer owns typed slots:

```text
hybrid.position2
hybrid.position3
hybrid.color
```

The scene should visibly update a debug transform, line/path gizmo or material
preview without coupling the implementation to a procedural mesh.

The UI remains a simple UI Toolkit component inspector:

- manual slot names and enabled state;
- current manual values;
- latest typed graph outputs;
- route state;
- evaluation timing;
- last discontinuity/reset reason.

## 13. Performance and memory plan

For the Phase 8 gate:

- one XAS track;
- one AnimationTrack;
- four manual inputs;
- one graph with up to 64 operations;
- up to 12 typed routes;
- target evaluation at 120 Hz.

Budgets:

- 0 bytes of managed allocation after enable/warm-up;
- one fixed `ControlValue[]` per graph executor;
- no per-node vector or Color object allocation;
- no graph importer/runtime reference to `Unity.GraphToolkit.Editor`;
- no XAS dependency added to the core signals package;
- no reflection or dictionary lookup in the frame loop;
- no full graph rebuild when an AnimationTrack property changes;
- CPU graph/routing time <= 0.35 ms at the 99th percentile on the documented
  desktop baseline;
- Brownian/oscillator output deterministic for identical Timeline time;
- no whole-file XAS loading.

Manual slot names resolve to indices during target rebuild. Typed route targets
resolve by ID and handle once. Brownian functions use fixed bounded loops and
preallocated operation state; they do not generate lists or random-number
objects.

## 14. Test plan

### Compiler and runtime EditMode

- Vector2/3/Color ports compile to correct operation types;
- invalid cross-type connections fail import;
- float and existing pulse graphs remain compatible;
- vector and color output count/layout are correct;
- polar, cylindrical, spherical and direction generators match reference math;
- scalar/vector/color lerp and conversion nodes are deterministic;
- gradient, HSV and palette generation match sampled references;
- operation state/array sizing handles all supported types;
- Brownian output is deterministic across repeated target reconstruction;
- Brownian seed changes output and zero amplitude/excitation returns bounded zero;
- runtime assets have no `Unity.GraphToolkit.Editor` reference.

### Manual input and Timeline PlayMode

- four `ManualInput` properties are discoverable by AnimationTrack;
- manual and XAS inputs coexist in one target;
- graph sees manual values as separate named sources;
- one AnimationTrack clip updates the mapped manual input;
- overlapping clips use Unity's normalized animation blending;
- track silence/lead-in/outro returns serialized/default manual values;
- deferred `LateUpdate` evaluation prevents a one-frame ordering dependency;
- EditMode Timeline scrubbing updates manual inputs and typed outputs;
- reversing or jumping Timeline time resets only stateful nodes and re-evaluates
  stateless vector/color/Brownian nodes exactly.

### Routing

- Vector2, Vector3 and Color outputs route to a generic target;
- invalid typed slot binding reports a clear error;
- legacy float and pulse routes remain operational in the same graph/target;
- warmed typed routing allocates zero managed bytes.

### Regression

- Run Phase 7 XAS tests;
- run Phase 1-7 EditMode and PlayMode suites;
- verify no extra GPU buffers, geometry readbacks or resource lifetime changes;
- profile XAS plus AnimationTrack plus typed graph execution together.

## 15. Milestones

### M1 - Typed runtime foundation

- Add Vector2, Vector3 and Color signal types.
- Introduce fixed typed operation value storage.
- Preserve existing float/pulse APIs and serialized data.
- Add typed output and route APIs.

**Gate:** legacy Phase 7 graph tests and a typed arithmetic test pass.

### M2 - Manual Timeline inputs

- Add `ManualInput0...3`.
- Add four manual signal bindings and stable-ID resolution.
- Merge provider and manual values before execution.
- Add deferred evaluation for AnimationTrack ordering.

**Gate:** one standard AnimationTrack animates one manual signal and a test
graph reads it without XAS.

### M3 - Vector graph and generators

- Add Vector2/Vector3 authoring nodes.
- Add polar, cylindrical, spherical and direction/length generators.
- Add deterministic oscillator path nodes.
- Add Brownian Vector2 and Vector3 nodes.

**Gate:** reference-value and deterministic-seek tests pass for every
generator.

### M4 - Color graph and generators

- Add Color authoring/runtime values.
- Add gradient, HSV, palette and tint/intensity nodes.
- Add Color conversion/split/lerp nodes.

**Gate:** Color tests validate interpolation, alpha, wrap/clamp and numeric
boundedness.

### M5 - Typed routing and hybrid demo

- Add additive Vector2/Vector3/Color slot interfaces.
- Add generic `HybridControlConsumer`.
- Add Phase 8 Timeline/Animation/XAS demo.
- Add UI Toolkit inspectors.

**Gate:** one Timeline drives three typed outputs into the same generic
consumer while XAS and manual inputs remain independently inspectable.

### M6 - Hardening and performance

- Validate malformed bindings, missing graph outputs and unsupported consumer
  types.
- Test PlayableGraph stop/rebuild, clip overlap, reverse scrub and seek.
- Run Phase 1-8 regression suites.
- Publish 120 Hz allocation/timing/cache and Brownian determinism results.

**Gate:** warmed hybrid evaluation and typed routing allocate zero bytes and
all previous suites remain green.

## 16. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| AnimationTrack writes manual properties after the XAS track evaluates | deferred evaluation at `LateUpdate` / explicit edit-time evaluation |
| Fixed four-slot bank is too restrictive | stable bindings cover the phase gate; a generated/custom target can extend later |
| Typed runtime values cause broad Phase 7 breakage | retain float/pulse enum values, APIs and tests; add typed operations incrementally |
| Graph types are connected incorrectly | explicit typed GTK ports plus importer validation on Unity 6000.6 |
| Brownian output depends on frame history | use deterministic absolute-time noise, not a random walk |
| Gradient storage requires Editor/runtime coupling | compact importer-owned runtime keys; no `AnimationCurve`/Editor graph dependency |
| Vector/Color boxing occurs at route dispatch | direct typed value storage and additive typed sink interfaces |
| Manual IDs collide with XAS/provider IDs | target validation rejects duplicate stable IDs |
| Multiple AnimationTracks fight over one manual property | document one owning AnimationTrack and report conflicting bindings when detectable |
| Existing procedural targets must understand vectors/colors | slot interfaces are additive; no Phase 7 consumer is forced to implement them |
| Color-space conversion produces inconsistent output | define and test linear/RGB/HSV policy explicitly |

## 17. Definition of done

Phase 8 is complete when:

1. existing Phase 7 float and pulse graphs still import and run unchanged;
2. Vector2, Vector3 and Color graph outputs exist and are type validated;
3. polar, cylindrical, spherical, direction/length, oscillator and Brownian
   generators pass reference tests;
4. gradient, HSV, palette and tint/intensity generators are available and
   bounded;
5. four standard AnimationTrack properties on `ControlSignalTarget` can be
   edited and animated;
6. manual and XAS values remain distinct graph inputs and combine only through
   authored graph operations;
7. overlapping manual animation clips use normalized Timeline weighting;
8. typed values route through allocation-free integer handles;
9. one hybrid Timeline scene drives Vector2, Vector3 and Color outputs;
10. arbitrary typed consumers can implement slot interfaces without depending
    on XAS, Timeline or procedural geometry;
11. deterministic seek/reverse-scrub behavior is tested;
12. warmed hybrid evaluation and routing allocate zero managed bytes;
13. Phase 1-8 EditMode/PlayMode suites pass;
14. performance and memory results are documented.

## 18. Later work

The Phase 8 contracts should allow, but do not include:

- more than four standard animatable manual inputs;
- custom Timeline curve-track UI;
- Quaternion/Matrix/Texture/object signals;
- runtime-authored curves;
- spatial path or spline signal types;
- vector/color subgraphs and reusable graph libraries;
- graph live visualization;
- automatic material or component reflection bindings;
- automatic discovery of arbitrary AnimationTrack properties;
- Phase 9 XR/provider interaction.

