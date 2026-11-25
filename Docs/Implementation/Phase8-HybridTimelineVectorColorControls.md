# Phase 8 - Hybrid Timeline, Vector and Color Controls

## Status

Planning document for Phase 8 of
[GPU Audio-Reactive Procedural Geometry Framework](../ProceduralGeometryFramework.md).

Phase 7 established provider-neutral control signals, XAS Timeline ingestion,
Graph Toolkit compilation and typed float/pulse control-slot routing. Phase 8
extends that foundation without changing its dependency direction:

- `com.sonosthesia.signals` remains independent of XAS and Timeline;
- `com.sonosthesia.signals.xas` remains the XAS integration layer;
- manual curves enter through a dedicated custom `ManualControlTrack` that
  uses Timeline's built-in Curves View;
- the graph decides how manual, XAS and future provider signals combine.

The completion slice is one Timeline containing an XAS control track and a
manual-control curve track, both feeding one control graph. The graph produces Vector2,
Vector3 and Color outputs into a generic test consumer. Procedural geometry
specific consumers are later adapters, not Phase 8 architecture owners.

## 1. Decisions

- Manual curve authoring uses a custom `ManualControlTrack` and
  `ManualControlPlayableAsset`.
- The track binds to `ControlSignalTarget` directly.
- A manual clip may own an arbitrary configured channel set. Each channel has a
  stable slot/index, a signal name and one float curve. There is no
  `ControlSignalTarget` slot bank and no transform/property hierarchy.
- Manual values remain separate named graph inputs. Graph nodes determine
  add, multiply, override, mix or modulation semantics.
- Overlapping manual clips with the same signal name use normalized Timeline
  input-weight blending.
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
5. provide a Timeline-native curve set for arbitrary named float controls;
6. merge a current XAS/provider frame with current manual-curve values
   before graph evaluation;
7. compile manual and provider source names into the same provider-neutral
   runtime graph;
8. provide deterministic Brownian and noise path generators suitable for
   scrubbing;
9. prove the hybrid signal path without steady-state managed allocation;
10. reuse the supported custom-track extension point without implementing a
    replacement curve/keyframe editor.

## 3. Non-goals

Phase 8 does not:

- add live audio analysis;
- implement a second curve/keyframe editor;
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
ManualControlTrack ----------+      |
clip with named curve channels|    |
                              v    v
                     ControlSignalTarget
                     - retain provider frame
                     - merge named manual values
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

The typed graph/runtime changes remain in the provider-neutral core:

```text
Packages/com.sonosthesia.signals
  Runtime/Core/
    ControlSignalData.cs
    CompiledControlGraph.cs
    ControlGraphExecutor.cs
    ControlSignalTarget.cs
    IControlVectorSlotTargets.cs
  Editor/Graph/
    ControlSignalNodes.cs
    ControlSignalGraphImporter.cs
  Tests/EditMode/
    TypedControlGraphTests.cs
  Tests/PlayMode/
    TimelineHybridControlTests.cs
```

Manual curve Timeline integration is a separate package so the core signal
package remains independent of Unity Timeline:

```text
Packages/com.sonosthesia.signals.timeline
  package.json
  Runtime/
    Sonosthesia.Signals.Timeline.Runtime.asmdef
    ManualControlTrack.cs
    ManualControlPlayableAsset.cs
    ManualControlBehaviour.cs
    ManualControlTrackMixerBehaviour.cs
  Editor/
    Sonosthesia.Signals.Timeline.Editor.asmdef
    ManualControlClipEditor.cs
  Tests/EditMode/
  Tests/PlayMode/
    ManualControlTrackTests.cs
```

Its dependencies are `com.sonosthesia.signals` and `com.unity.timeline`. It
does not depend on XAS, a particular audio package, or procedural geometry.

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

### Adapting the current node/compiler/dataflow architecture

The Phase 7 architecture is retained, but its scalar assumptions are removed:

```text
authoring node type
    -> IControlRuntimeNode
    -> ControlOperation records
    -> fixed ControlValue result array
    -> typed ControlGraphOutput
    -> typed control-slot route
```

#### Authoring templates

Use abstract generic node bases as **Editor-side templates**. They factor out
ports and common option/compile behavior, but are never instantiated or
serialized:

```csharp
[Serializable]
public abstract class UnaryControlNode<TValue> : ControlRuntimeNode
{
    protected override void OnDefinePorts(IPortDefinitionContext context)
    {
        context.AddInputPort<TValue>(InputAName)
            .WithCapacity(PortCapacity.Single)
            .Build();
        context.AddOutputPort<TValue>(OutputName)
            .WithCapacity(PortCapacity.Multi)
            .Build();
    }
}

[Serializable]
public abstract class BinaryControlNode<TValue> : ControlRuntimeNode
{
    protected override void OnDefinePorts(IPortDefinitionContext context)
    {
        context.AddInputPort<TValue>(InputAName)
            .WithCapacity(PortCapacity.Single)
            .Build();
        context.AddInputPort<TValue>(InputBName)
            .WithCapacity(PortCapacity.Single)
            .Build();
        context.AddOutputPort<TValue>(OutputName)
            .WithCapacity(PortCapacity.Multi)
            .Build();
    }
}
```

Only closed concrete subclasses are discovered and serialized by Graph Toolkit:

```csharp
[Serializable]
[Node("Signals/Vector3", null, "Normalize")]
public sealed class Vector3NormalizeNode : UnaryControlNode<Vector3>
{
    public override ControlOperationKind Kind
        => ControlOperationKind.Vector3Normalize;

    public override ControlSignalType OutputType
        => ControlSignalType.Vector3;
}

[Serializable]
[Node("Signals/Color", null, "Lerp")]
public sealed class ColorLerpNode : ControlRuntimeNode
{
    public override ControlOperationKind Kind
        => ControlOperationKind.ColorLerp;

    protected override void OnDefinePorts(IPortDefinitionContext context)
    {
        context.AddInputPort<Color>("A")
            .WithCapacity(PortCapacity.Single)
            .Build();
        context.AddInputPort<Color>("B")
            .WithCapacity(PortCapacity.Single)
            .Build();
        context.AddInputPort<float>("Amount")
            .WithCapacity(PortCapacity.Single)
            .Build();
        context.AddOutputPort<Color>(OutputName)
            .WithCapacity(PortCapacity.Multi)
            .Build();
    }
}
```

Open generic classes remain `abstract`, do not carry `[Node]`, and are not put
in saved graphs. Concrete classes carry `[Node]`, are `[Serializable]`, and
override only behavior not supplied by the template:

- `Kind`;
- `OutputType`;
- operation-specific ports or options;
- state/component counts;
- `CreateOperation`;
- type-specific validation.

This use of C# `override` is ordinary object-oriented authoring reuse. It is
not a Timeline override, a Graph Toolkit override clip, or a runtime
polymorphism mechanism. The concrete saved node and its serialized option
values are the source of truth.

For nodes with mixed types, such as Polar, Cylindrical or HSV, use an explicit
base such as `PolarNode` rather than forcing a generic signature. The generic
base is useful only when the complete port family really is identical.

#### Compiler interface

Replace the scalar-specific assumptions in `IControlRuntimeNode` and
`ControlRuntimeNode` with type-neutral metadata:

```csharp
public interface IControlRuntimeNode
{
    ControlOperationKind Kind { get; }
    ControlSignalType OutputType { get; }
    int ControlStateComponentCount { get; }
    bool UsesIntegerState { get; }

    IEnumerable<IPort> GetRequiredInputs();

    ControlOperation CreateOperation(
        int[] inputOperationIndices,
        int sourceIndex,
        int stateOffset,
        int integerStateOffset);
}
```

The `int[]` in that sketch belongs to importer-time compilation only. The
importer stores fixed runtime fields in `ControlOperation`, not the array:

```csharp
struct ControlOperation
{
    ControlOperationKind Kind;
    ControlSignalType OutputType;
    int InputA;
    int InputB;
    int InputC;
    int InputD;
    int SourceIndex;
    int StateOffset;
    int StateComponentCount;
    int DelayCapacity;
    float ParameterA;
    float ParameterB;
    float ParameterC;
    float ParameterD;
}
```

Four runtime input references are sufficient for the planned Phase 8 nodes.
A node needing more declares multiple operations or is rejected by import.
The Phase 7 two-input operations continue to compile with unused inputs set to
`-1`.

The importer still walks upstream ports recursively and produces topological
order, but now:

1. looks up every named required input;
2. verifies the upstream `ControlSignalType` expected by the node;
3. compiles a `ControlOperation` with `InputA` through `InputD`;
4. records `OutputType`;
5. allocates state by component count rather than treating state as one float;
6. leaves no Editor node, generic type or GTK reference in the runtime asset.

#### Runtime value flow

The executor replaces `_values: float[]` with:

```csharp
ControlValue[] _values;
```

where:

```csharp
struct ControlValue
{
    float X, Y, Z, W;
}
```

`EvaluateOperation` returns `ControlValue`, not `float`. Each operation reads
one or more `ControlValue` records and writes exactly one `ControlValue`.
Existing scalar operations are adapted mechanically:

- `Add(float, float)` reads `a.X`, `b.X`;
- `Pulse` reads strength/magnitude/start lanes;
- `LowPass` filters `X`;
- `Vector3Add` adds X, Y and Z;
- `ColorLerp` interpolates X, Y, Z and W;
- `Polar` reads scalar angle/radius inputs and writes a Vector2 result.

The runtime does not use `object`, `dynamic`, generic operation dispatch or
`delegate` calls. The `ControlOperationKind` switch remains the hot-path
dispatch mechanism.

#### State adaptation

State is allocated in scalar components:

- float filter: 1 component;
- Vector2 filter: 2 components;
- Vector3 filter: 3 components;
- Color filter: 4 components.

Phase 8 Brownian generators are stateless deterministic absolute-time
functions, so they require no historical state. Delay remains the only
operation requiring a ring. If Vector3/Color delay is added later, the
delay capacity is measured in values and storage length is
`capacity * componentCount`.

#### Outputs and routing

`ControlGraphOutput.Type` now selects a typed read:

```csharp
TryGetOutput(int outputIndex, out float value);
TryGetOutput(int outputIndex, out ControlPulse value);
TryGetOutput(int outputIndex, out Vector2 value);
TryGetOutput(int outputIndex, out Vector3 value);
TryGetOutput(int outputIndex, out Color value);
```

Each overload verifies `ControlGraphOutput.Type` before reading components.
The existing combined float/pulse method remains for Phase 7 compatibility.

`IControlSlotTarget` still provides stable slot lookup and type discovery.
Vector and Color transport is added through optional sink interfaces while
keeping all Phase 7 consumers source-compatible:

```csharp
IControlVector2SlotTarget
IControlVector3SlotTarget
IControlColorSlotTarget
```

The resolved route stores one `IControlSlotTarget`, integer slot handle,
operation index and `ControlSignalType`. Routing dispatches directly to the
appropriate typed interface; no type erasure or reflection is used.

#### Unity 6000.6 type discipline

On Unity 6000.6:

- every port has one concrete data type;
- each concrete node declares its full port layout;
- the compiler verifies operation input/output types;
- the graph does not rely on polymorphic `WithDataTypes`, which is a 6000.7+
  feature;
- generic abstract C# bases only generate the concrete port declarations at
  authoring time.

If a future Unity upgrade enables polymorphic ports, that may reduce the
number of visible node classes, but it is not a Phase 8 requirement.

## 7. Manual named-curve track

### Existing Timeline track candidates

- `AnimationTrack` provides rich curve editing, but it animates a bound
  Animator/AnimationClip and carries transform, hierarchy, masking, offsets and
  animation-state meaning that is unrelated to generic control signals.
- `AudioTrack` reuses Timeline's curve UI, but only for its audio-specific
  properties: Volume, Stereo Pan and Spatial Blend.
- Activation, Control, Signal and other standard tracks do not provide
  arbitrary named float curve data.

Therefore there is no existing built-in track with the desired semantics. The
appropriate extension is a custom `TrackAsset` plus `PlayableAsset`, while
reusing Timeline's existing curve UI through a serializable playables
template.

### Track and clip model

Add `ManualControlTrack` in `com.sonosthesia.signals.timeline`, a normal custom
`TrackAsset` that binds to
`ControlSignalTarget`.

The track accepts `ManualControlPlayableAsset` clips. The target model is a
multi-channel clip:

```csharp
[Serializable]
public sealed class ManualControlChannel
{
    public string signalName;
    public float value;
}

[Serializable]
public sealed class ManualControlBehaviour : PlayableBehaviour
{
    public List<ManualControlChannel> channels = new();
}
```

The asset creates its playable with:

```csharp
ScriptPlayable<ManualControlBehaviour>.Create(graph, template);
```

This follows Unity's supported Timeline customization pattern: a serializable
`PlayableBehaviour` is supplied as the template, and Timeline exposes its
serialized fields as animated clip properties. The built-in Timeline Curves
View then provides key insertion, tangent editing, selection, zoom and
scrubbing for each channel's `value` curve.

Unity's Timeline package demonstrates the same mechanism in its Time Dilation
custom-track sample: a serializable `PlayableBehaviour` template exposes one
float (`timeScale`), the custom track mixer reads the animated clone and the
normal Curves View edits the field. Phase 8 generalizes that pattern to an
author-configured list of channel values.

Implementation difficulty is moderate, not high:

- the runtime track/clip/mixer pattern is small and already proven by Unity;
- reusing the Curves View requires no Timeline internals and no custom UI
  toolkit curve editor;
- most work is validation, naming, normalized blending and provider identity,
  rather than Timeline playables or keyframe editing;
- a custom `ClipEditor` is optional and only affects clip title/color/Inspector
  presentation.

There is no fundamental limit on how many curves the Curves View can show. The
feasibility issue is mapping a dynamic user-visible channel name to the stable
serialized property path of an AnimationClip. A channel name cannot become a
C# field name at runtime. Phase 8 therefore targets stable list indices as
binding identities and a separate signal-name registry:

```text
binding path: channels.Array.data[3].value
signal name:  manual.hue
display name: Manual / Hue
```

Adding a channel appends a stable binding slot. Renaming changes only the
registry label. Removing a channel marks it retired instead of immediately
reusing its index, so existing curve bindings remain unambiguous. A clip may
compact retired indices through an explicit migration action.

This avoids AnimationTrack semantics:

- no Animator;
- no bound GameObject hierarchy;
- no Transform/property binding;
- no animation rig or humanoid/generic animation configuration;
- no custom keyframe editor.

One clip may contain `manual.radius`, `manual.hue` and `manual.excitation`
alongside XAS clips on another track. Multiple manual clips may also coexist,
each with a different or overlapping channel set.

### Curves View scope

The Curves View edits the selected clip's channel value fields:

- each channel maps by stable index to one control ID;
- each clip has independent duration, trim, extrapolation and blending;
- overlapping clips can be blended by their signal name;
- adding, renaming and retiring channels use an explicit registry operation.

Timeline remains the keyframe/tangent editor. A custom `ClipEditor`/Inspector
maps internal binding paths to readable channel names and provides
add/rename/retire actions.

### Feasibility spike

Before committing the multi-channel representation, M2 verifies on Unity
6000.6 that:

1. list elements on a serializable `PlayableBehaviour` template appear as
   editable Timeline clip curves;
2. appending a channel preserves earlier bindings;
3. animated clones provide the correct values to the mixer;
4. curves survive save, close and reopen;
5. reorder is prohibited or explicitly migrated;
6. the playable works in a player build.

If list-element bindings are not reliably supported, Phase 8 uses the proven
fallback: one serializable `value` field per clip with unlimited clips in the
track. That fallback still supports arbitrarily many named controls and uses
the same mixer and graph contracts; only grouping changes.

### Mixer behavior

`ManualControlTrackMixerBehaviour`:

1. resolves each channel's `signalName` to a stable `ControlSignalId`;
2. reads each Timeline-animated `value` from current behaviour clones;
3. groups active channels across all clips by signal ID;
4. blends same-signal clips with normalized `Playable.GetInputWeight`;
5. emits a provider-neutral manual continuous frame to the bound
   `ControlSignalTarget`.

Different signal names do not blend with one another. If one signal has no
active clip/channel or active weight, it is reported unavailable unless an
explicit track/target default-value policy is enabled.

The mixer stores no string-keyed dictionary on the frame path. Signal IDs,
channel indices and per-signal output indices are resolved once and stored in
compact arrays.

### Combining with XAS

The manual track and XAS track remain completely independent assets and
tracks. `ControlSignalTarget` merges provider frames by stable signal ID:

1. XAS submits its current provider frame;
2. manual curves submit a provider frame with only manual IDs;
3. the target combines the named sources into a single graph input buffer;
4. the graph performs add, multiply, mix, override or other combination
   semantics explicitly.

Manual and XAS IDs must be distinct unless a future blend policy is explicitly
authored. Duplicate IDs from independent providers are reported rather than
silently overwriting each other.

### Timeline evaluation order

The manual track is a normal Timeline mixer like the XAS track. The target may
receive either track first. It therefore does not immediately discard the
previous provider frame; it caches the most recent frame per independent
provider stream for the current Timeline evaluation.

Graph evaluation is triggered after both relevant mixer passes for the
Timeline frame, preferably from a provider-agnostic deferred target update.
PlayMode uses `LateUpdate`; Editor scrubbing uses Timeline's editor update
callback. If only one provider track is present, the target evaluates with
that provider's frame without waiting for a missing one.

This ordering mechanism is internal to the target/provider contract. It does
not couple XAS to manual curves or require either track to know the other's
implementation.

## 8. Graph authoring and compiler

### Input nodes

Phase 8 retains existing XAS and generic provider input nodes and adds:

- `Manual Signal Input`, configured with a stable signal name.

It compiles to a provider-neutral source ID and does not embed Timeline,
`ManualControlTrack`, or XAS types.

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
- one `ManualControlTrack` bound to `ControlSignalTarget`;
- one named manual curve clip per required manual signal;
- one `.controlgraph` asset;
- one `HybridControlConsumer` test component;
- one scene object with a `MeshRenderer` and a URP Lit material;
- one `UrpLitColorControlSlot` adapter.

Example graph:

```text
XAS RMS ------------- Remap ---+
                               Add ---> Polar radius ---> Vector2 Output
manual.radius -----------------+

manual.angle ----------------- Cylindrical angle ----------+
XAS Lows ----- Remap --------- Cylindrical radius ---------+-> Vector3 Output
manual.height ---------------- Cylindrical height ---------+

manual.hue ----- HSV Hue
XAS Highs ---- Remap ---- HSV Value --------------------------> Color Output
```

The generic consumer owns typed slots:

```text
hybrid.position2
hybrid.position3
material.color
```

The color route is an explicit, required visual proof:

```text
Color Control Output
        |
        v
material.color slot
        |
        v
UrpLitColorControlSlot
        |
        v
Material.SetColor("_BaseColor", value)
```

`UrpLitColorControlSlot` implements the generic typed slot interfaces and
targets a specific `Renderer` material slot. It uses a `MaterialPropertyBlock`
by default so the shared material asset is not mutated during Play Mode. A
test-scene option may explicitly request an instanced material when a persistent
visual asset change is desired.

The adapter:

- resolves `Renderer`, material index and `_BaseColor` support once;
- rejects a shader without `_BaseColor` during binding with a clear warning;
- writes only the Color slot in the frame loop;
- allocates no per-frame string, list or boxed value;
- caches one `MaterialPropertyBlock`;
- does not depend on XAS, Timeline or the graph runtime.

The scene visibly demonstrates all three typed outputs:

- the Vector2 output updates a 2D marker/debug position or line path;
- the Vector3 output updates a 3D marker transform;
- the Color output changes the `_BaseColor` of a visible URP Lit sphere,
  quad or procedural mesh.

This proves Color routing without coupling the graph to a material or
procedural geometry implementation.

The UI remains a simple UI Toolkit component inspector:

- active manual channel names and blend weights;
- current named manual values;
- latest typed graph outputs;
- current URP Lit base color;
- route state;
- evaluation timing;
- last discontinuity/reset reason.

## 13. Performance and memory plan

For the Phase 8 gate:

- one XAS track;
- one manual curve track;
- named manual clips as authored for the demo;
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
- no full graph rebuild when a manual clip value changes;
- CPU graph/routing time <= 0.35 ms at the 99th percentile on the documented
  desktop baseline;
- Brownian/oscillator output deterministic for identical Timeline time;
- no whole-file XAS loading.

Manual signal names and clip IDs resolve during Timeline graph build or target
rebuild. Typed route targets
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

- configured channel `value` fields appear in Timeline Curves View;
- manual and XAS inputs coexist in one target;
- graph sees manual values as separate named sources;
- one multi-channel clip updates each named graph input;
- overlapping same-name clips use normalized Timeline input-weight blending;
- different-name clips remain independent;
- clip extrapolation and default-value policy are deterministic;
- deferred evaluation prevents a one-frame provider-track ordering dependency;
- EditMode Timeline scrubbing updates manual curves and typed outputs;
- reversing or jumping Timeline time resets only stateful nodes and re-evaluates
  stateless vector/color/Brownian nodes exactly.

### Routing

- Vector2, Vector3 and Color outputs route to a generic target;
- Color output updates the URP Lit `_BaseColor` through a typed adapter;
- the adapter preserves the shared material asset by using a
  `MaterialPropertyBlock`;
- invalid typed slot binding reports a clear error;
- legacy float and pulse routes remain operational in the same graph/target;
- warmed typed routing allocates zero managed bytes.

### Regression

- Run Phase 7 XAS tests;
- run Phase 1-7 EditMode and PlayMode suites;
- verify no extra GPU buffers, geometry readbacks or resource lifetime changes;
- profile XAS plus the manual curve track plus typed graph execution together.

## 15. Milestones

### M1 - Typed runtime foundation

- Add Vector2, Vector3 and Color signal types.
- Introduce fixed typed operation value storage.
- Preserve existing float/pulse APIs and serialized data.
- Add typed output and route APIs.

**Gate:** legacy Phase 7 graph tests and a typed arithmetic test pass.

### M2 - Manual Timeline inputs

- Create `com.sonosthesia.signals.timeline`.
- Run the Unity 6000.6 multi-channel binding feasibility spike.
- Add `ManualControlTrack`, `ManualControlPlayableAsset` and
  `ManualControlBehaviour`.
- Add stable channel indices, signal-name resolution and normalized
  same-signal mixer behavior.
- Merge provider and manual values before execution.
- Add the standard serialized-behaviour Curves View authoring pattern.
- Add deferred evaluation for cross-track ordering.

**Gate:** one custom manual clip exposes multiple stable named channels through
the native Timeline Curves View and a test graph reads them without XAS. If
the spike rejects list bindings, the approved fallback is unlimited
single-value clips using the same track and mixer contracts.

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
- Add `UrpLitColorControlSlot` and a visible URP Lit test object.
- Add Phase 8 ManualControl/XAS/typed-output Timeline demo.
- Add UI Toolkit inspectors.

**Gate:** one Timeline drives three typed outputs: a Vector2 marker, a Vector3
marker and a visible URP Lit object's `_BaseColor`, while XAS and manual
inputs remain independently inspectable.

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
| Manual and XAS tracks evaluate in different orders | target caches provider frames by stream and evaluates once from a deferred update |
| Dynamic channel names create fragile serialized AnimationClip paths | stable list indices are binding identities; names live in separate metadata and channel insertion is append-only |
| Typed runtime values cause broad Phase 7 breakage | retain float/pulse enum values, APIs and tests; add typed operations incrementally |
| Graph types are connected incorrectly | explicit typed GTK ports plus importer validation on Unity 6000.6 |
| Brownian output depends on frame history | use deterministic absolute-time noise, not a random walk |
| Gradient storage requires Editor/runtime coupling | compact importer-owned runtime keys; no `AnimationCurve`/Editor graph dependency |
| Vector/Color boxing occurs at route dispatch | direct typed value storage and additive typed sink interfaces |
| Manual IDs collide with XAS/provider IDs | target validation rejects duplicate stable IDs |
| Multiple clips claim the same manual signal unintentionally | same-name overlap is a supported normalized blend; duplicate clip validation remains visible |
| Existing procedural targets must understand vectors/colors | slot interfaces are additive; no Phase 7 consumer is forced to implement them |
| URP material color update mutates a shared asset | use a cached `MaterialPropertyBlock` by default and require explicit opt-in for instancing |
| Color-space conversion produces inconsistent output | define and test linear/RGB/HSV policy explicitly |

## 17. Definition of done

Phase 8 is complete when:

1. existing Phase 7 float and pulse graphs still import and run unchanged;
2. Vector2, Vector3 and Color graph outputs exist and are type validated;
3. polar, cylindrical, spherical, direction/length, oscillator and Brownian
   generators pass reference tests;
4. gradient, HSV, palette and tint/intensity generators are available and
   bounded;
5. arbitrary configured manual channels can be edited in Timeline Curves View
   and independently reused with any provider track;
6. manual and XAS values remain distinct graph inputs and combine only through
   authored graph operations;
7. overlapping manual animation clips use normalized Timeline weighting;
8. typed values route through allocation-free integer handles;
9. one hybrid Timeline scene drives Vector2, Vector3 and Color outputs, with
   the Color value visibly changing a URP Lit material `_BaseColor`;
10. the URP Lit adapter uses a `MaterialPropertyBlock` by default and does not
    mutate a shared material asset during Play Mode;
11. arbitrary typed consumers can implement slot interfaces without depending
    on XAS, Timeline or procedural geometry;
12. deterministic seek/reverse-scrub behavior is tested;
13. warmed hybrid evaluation and routing allocate zero managed bytes;
14. Phase 1-8 EditMode/PlayMode suites pass;
15. performance and memory results are documented.

## 18. Later work

The Phase 8 contracts should allow, but do not include:

- custom Timeline keyframe/curve editor UI;
- Quaternion/Matrix/Texture/object signals;
- runtime-authored curves;
- spatial path or spline signal types;
- vector/color subgraphs and reusable graph libraries;
- graph live visualization;
- automatic material or component reflection bindings;
- dynamically generated C# fields or arbitrary binding paths beyond the stable
  channel registry;
- Phase 9 XR/provider interaction.

