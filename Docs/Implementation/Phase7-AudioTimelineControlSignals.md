# Phase 7 - XAS Timeline and Control Signals

## Status

Planning document for Phase 7 of
[GPU Audio-Reactive Procedural Geometry Framework](../ProceduralGeometryFramework.md).

Phase 7 uses `.xas` files produced by `sonosthesia-audio analysis` as the
initial audio source. It intentionally does not add live FFT/RMS analysis in
this phase. The public source and control contracts must nevertheless allow a
live provider to be added later without changing graph or target code.

The initial completion slice is one Unity Timeline track driving one
`ProceduralMeshRenderer` through an authored control-signal graph. Future work
must not require a geometry-specific Timeline track or graph executor.

This intentionally establishes the Phase 7 architecture with one vertical
slice instead of making four geometry-specific demonstrations the first
delivery. Once the graph and adapter path is proven, the same controls can be
bound to Phase 4 surfaces, Phase 5 paths and Phase 6 branches without changing
the Timeline or graph runtime.

## Implementation status

The Phase 7 vertical slice is implemented:

- `com.sonosthesia.signals` supplies provider-neutral profiles, frames,
  Graph Toolkit compilation, fixed-buffer execution and control-slot routing;
- `com.sonosthesia.signals.xas` supplies the XAS provider, `XasTrack`,
  `XasPlayableAsset`, Timeline overlap blending and XAS convenience nodes;
- `com.sonosthesia.procgeo` supplies `ProceduralMeshControlSlots`;
- `Assets/TestScenes/Phase7` contains the generated graph, Timeline and
  single-renderer demo scene;
- `Assets/StreamingAssets/Phase7/Synthetic.xas` supplies a small repeatable
  demo input.

The graph importer, XAS bridge, zero-allocation warmed evaluator, procedural
adapter and existing Phase 1-6 EditMode/PlayMode regression suites are tested.
Live Play Mode verification showed the Timeline graph updating procedural
field amplitude and simulation stiffness through typed routes.

## 1. Decisions

The following choices define this plan:

- Audio source: precomputed XAS only.
- Timeline overlap: blend continuous values and merge discrete events.
- Initial graph signal types: continuous `float` and discrete pulse/event.
- Target routing: a small slot interface implemented by adapter components.
- Initial gate: one XAS track, one graph, and one procedural geometry target.
- Graph editor: Unity Graph Toolkit on Unity 6000.6.
- Timeline, graph execution, and target routing remain separate systems.

Vectors, booleans, colours, live analysis, Timeline marker generation, audio
playback synchronization and automatic clip-to-audio matching are later
extensions.

## 2. Goals

Phase 7 will:

1. expose XAS continuous and peak data through a Timeline clip and track;
2. support deterministic seeking and scrubbing;
3. blend continuous values from overlapping clips;
4. merge discrete events from overlapping clips;
5. feed one frame of raw audio controls to a runtime target;
6. compile an authored Graph Toolkit graph into an engine-free runtime graph;
7. process signals with operators, filters, delay and pulse envelopes;
8. route processed values and pulses to named control slots;
9. provide a procedural geometry adapter without coupling the generic signal
   system to procedural geometry;
10. prove the path with no steady-state managed allocation for the signal
    evaluation loop.

## 3. Non-goals

Phase 7 does not:

- execute Graph Toolkit assets directly at runtime;
- generate or analyze audio in realtime;
- let geometry inspect or depend on the `.xas` file format;
- embed Timeline types in Graph Toolkit authoring nodes;
- provide an arbitrary reflection-based property binding system;
- create a runtime control panel or custom visual track;
- implement XR, haptics, or interaction routing;
- replace the existing simulation, deformation, path, or branch APIs.

## 4. Architecture

The data path is:

```text
.xas file
   |
   v
XasReader (com.sonosthesia.xas)
   |
   v
XAS Timeline clip(s)
   |
   v
XasTrackMixerBehaviour
  - clip weighting
  - continuous blend
  - discrete-event merge
  - seek/scrub direction
   |
   v
ControlSignalTarget
  - raw float inputs
  - merged pulse inputs
  - graph evaluation context
   |
   v
CompiledControlGraphRuntime
   |
   v
ControlRouteBinding
   |
   v
IControlSlotTarget implementation
   |
   v
client / ProceduralMeshControlSlots
```

This is deliberately not:

```text
Timeline -> ProceduralMeshRenderer -> XAS reader
```

The renderer remains a consumer of controls and does not acquire an audio or
Timeline dependency.

### Dependency direction

```text
com.sonosthesia.procgeo
            |
            v
com.sonosthesia.signals <--- com.sonosthesia.signals.xas
                                      |
                                      v
                           com.sonosthesia.xas
```

`com.sonosthesia.signals` depends on neither XAS nor procedural geometry. It
defines provider-neutral signal values, graph execution and control-slot
contracts. `com.sonosthesia.signals.xas` is an integration package that depends
on both `com.sonosthesia.signals` and `com.sonosthesia.xas`.

The procedural geometry package depends only on `com.sonosthesia.signals` and
implements control-slot adapters. XAS file details and Timeline types therefore
cannot leak into graph execution or geometry consumers. Future live-audio,
interaction and XR providers can use the same contracts without referencing
XAS.

## 5. Package and assembly layout

Create two embedded packages.

Provider-neutral core:

```text
Packages/com.sonosthesia.signals
  package.json
  README.md
  Runtime/
    Sonosthesia.Signals.Runtime.asmdef
    Core/
      ControlSignalFrame.cs
      ControlPulse.cs
      ControlSlotId.cs
      IControlSlotTarget.cs
      ControlSignalTarget.cs
      ControlRouteBinding.cs
      CompiledControlGraph.cs
      ControlGraphExecutor.cs
      IControlSignalProvider.cs
  Editor/
    Sonosthesia.Signals.Editor.asmdef
    Graph/
      ControlSignalGraph.cs
      ControlSignalGraphImporter.cs
      ControlSignalNodes.cs
      ControlSignalGraphValidation.cs
  Tests/
    EditMode/
    PlayMode/
```

XAS and Timeline integration:

```text
Packages/com.sonosthesia.signals.xas
  package.json
  README.md
  Runtime/
    Sonosthesia.Signals.Xas.Runtime.asmdef
    XasAnalysisAsset.cs
    XasPlayableAsset.cs
    XasPlayableBehaviour.cs
    XasTrack.cs
    XasTrackMixerBehaviour.cs
    XasControlSignalProvider.cs
  Editor/
    Sonosthesia.Signals.Xas.Editor.asmdef
    XasPlayableAssetEditor.cs
    XasTrackEditor.cs
  Tests/
    EditMode/
    PlayMode/
```

`com.sonosthesia.signals` depends only on the test framework and whichever
generic Unity modules it uses. It has no dependency on
`com.sonosthesia.xas`.

`com.sonosthesia.signals.xas` depends on:

- `com.sonosthesia.signals`;
- `com.sonosthesia.xas`;
- `com.unity.test-framework`;
- the built-in Unity Timeline module.

The `com.sonosthesia.procgeo` package adds:

```text
Runtime.URP/Authoring/ProceduralMeshControlSlots.cs
Tests/EditMode/...ControlSlotTests.cs
Tests/PlayMode/...AudioControlIntegrationTests.cs
```

Its runtime URP asmdef references `Sonosthesia.Signals.Runtime`.

The graph authoring asmdef references `Sonosthesia.Signals.Runtime`, not the
XAS package. Runtime assemblies never reference
`Unity.GraphToolkit.Editor`.

## 6. XAS asset and path policy

An `.xas` asset is represented by a `XasAnalysisAsset` in
`com.sonosthesia.signals.xas`.

The asset stores:

- a stable asset GUID;
- a display name;
- source file identity/hash when available;
- duration;
- ticks per second;
- analysis hop;
- descriptor summary;
- a player-safe path under `StreamingAssets`;
- an editor-only absolute/project-relative source path.

For Phase 7, runtime XAS files must live under `Assets/StreamingAssets`. The
asset importer or validator rejects files that cannot be resolved at player
runtime. It does not load the frame/event payload while importing.

`XasAnalysisAsset` owns no `XasReader`. It is immutable authoring metadata.
Readers belong to playback contexts and are opened lazily when first needed.

### Reader sharing and lifetime

- One reader per unique XAS asset per XAS provider/playback context.
- Readers are reused by all clips using the same source.
- Readers close when the `PlayableGraph` target is disabled, destroyed, or the
  playable graph is released.
- Reader caches retain only the configured number of chunks per stream.
- A provider does not copy or materialize a complete `.xas` file.

Reader `ObjectDisposedException`, file replacement, descriptor mismatch and CRC
failure are reported by the XAS provider through the provider interface with
the source asset and clip name. They are not converted into silent zero-valued
success.

## 7. Timeline contracts

### Track

`XasTrack`:

- derives from `TrackAsset`;
- accepts `XasPlayableAsset`;
- binds to `ControlSignalTarget`;
- contains one mixer;
- validates that the bound object is present and enabled;
- validates that every clip references an XAS asset;
- owns only playback-time policy.

### Clip

`XasPlayableAsset` stores:

- the `XasAnalysisAsset`;
- an optional source trim/time offset;
- an optional playback-speed multiplier;
- a clip gain;
- discrete-event inclusion policy;
- user-authored clip colour/display metadata.

The source sample time is:

```text
sourceTime = trim + localClipTime * playbackSpeed
```

Looping is not part of the Phase 7 gate. Out-of-range source time returns
unavailable and contributes no continuous value. Events outside the clip are
not emitted.

### Continuous overlap blend

Each active clip supplies a continuous frame and its effective Timeline weight.
The mixer writes a normalized weighted value:

```text
outputValue =
    sum(clipValue * clipWeight) / sum(clipWeight)
```

The formula is applied independently to RMS, lows, mids, highs and centroid.
A clip that cannot sample its source at the requested time contributes no
weight. If all clips are unavailable, the mixed frame is marked unavailable
rather than being represented as valid zero.

This rule makes a 50/50 crossfade preserve amplitude instead of doubling it.
If only one clip is active, its value is unchanged.

### Discrete overlap merge

XAS events are merged into an XAS-bridge-local compact pulse frame before
being mapped to generic profile indices:

```csharp
struct XasMixedPulseFrame
{
    float Main;
    float Lows;
    float Mids;
    float Highs;
    float MainMagnitude;
    float LowsMagnitude;
    float MidsMagnitude;
    float HighsMagnitude;
    uint StartedMask;
}
```

For overlapping events:

- active pulse strength is `max(strength * clipWeight)` per channel;
- a channel can also expose normalized magnitude through a separate
  `PulseMagnitude`;
- `StartedMask` contains a channel bit when an event starts while crossing the
  current Timeline frame;
- duplicate delivery is avoided by source-asset ID, clip instance ID, channel,
  and event start tick;
- reverse scrubbing does not emit stale positive starts;
- a scrub at rest within an event exposes its active strength but not a new
  start.

The event merge is intentionally bounded by four channels in XAS v1. A later
format/version may use variable event slots without changing the mixer
contract.

### Frame direction and discontinuity

The mixer passes:

- absolute Timeline time;
- source time;
- delta time;
- `PlaybackDirection`: forward, reverse, stationary, or discontinuous;
- continuous availability;
- clip blend weights;
- merged pulse frame.

A discontinuity is reported when:

- the requested time jumps beyond the configured fixed-step tolerance;
- direction changes;
- Timeline is rebuilt;
- source/clip selection changes.

Raw XAS continuous values remain exact on seeks. Stateful graph nodes use this
flag to reset or initialize their state as described below.

## 8. Runtime control contract

### Stable identifiers

Use a small value-type ID, for example:

```csharp
public readonly struct ControlSignalId : IEquatable<ControlSignalId>
```

It wraps a stable 64- or 128-bit hash of an authored name. String conversion and
lookup occur only during authoring, import, or target initialization. Steady-
state route evaluation uses integer output/slot indices.

For example, the XAS provider profile maps XAS v1 to these IDs:

```text
xas.continuous.rms
xas.continuous.lows
xas.continuous.mids
xas.continuous.highs
xas.continuous.centroid
xas.pulse.main
xas.pulse.lows
xas.pulse.mids
xas.pulse.highs
```

### Control frame

Providers do not pass a struct that names RMS, lows or any other audio field
to the graph. They fill a preallocated generic value buffer whose layout is
declared by a `ControlSignalProfile`.

```csharp
public readonly struct ControlSignalFrame
{
    double TimelineTime;
    double DeltaTime;
    ControlPlaybackDirection Direction;
    bool IsDiscontinuous;
    int ContinuousCount;
    int PulseCount;
    ReadOnlyMemory<float> ContinuousValues;
    ReadOnlyMemory<byte> ContinuousAvailable;
    ReadOnlyMemory<float> PulseStrengths;
    ReadOnlyMemory<float> PulseMagnitudes;
    ReadOnlyMemory<byte> PulseStarted;
}
```

`ReadOnlyMemory<T>` is used in the conceptual API to show caller-owned,
preallocated storage. If Unity's supported runtime target or hot-path profiling
shows unwanted wrappers, the concrete evaluator may use explicit arrays plus
offset/count integers. The frame is passed by `in`; it is not boxed, queued, or
copied into a heap-allocated collection.

The corresponding `ControlSignalProfile` owns a fixed ordered array of
`ControlSignalId` descriptors and their continuous/pulse type. Provider and
target resolve profile IDs to integer indices at initialization. Graph source
nodes reference those stable IDs, then compile to numeric source indices.

### Target

`ControlSignalTarget` is a provider-neutral receiver and may serve as the
Timeline binding object. It:

- owns graph evaluation and routing state;
- accepts a provider-neutral `ControlSignalFrame`;
- does not own XAS readers or parse an audio-analysis file;
- exposes raw source values for low-level clients;
- evaluates one compiled control graph;
- writes processed outputs into configured routes;
- invalidates state on Timeline discontinuity;
- optionally exposes last-frame values by output index;
- is not thread-safe and executes on Unity's main thread.

It does not synchronize audio playback or retain a provider-specific
collection. Provider ownership remains with the concrete integration. For the
XAS bridge, `XasTrackMixerBehaviour` owns or accesses the XAS provider context
and writes the mixed generic frame into the target.

Clients can consume values by pulling the target's raw or graph-output value.
They do not need to receive Unity Events for continuous signals.

## 9. Compiled Graph Toolkit graph

### Authoring asset

`ControlSignalGraph` is a Graph Toolkit graph with extension `.controlgraph`.
It is created through:

```text
Assets/Create/Sonosthesia/Control Signal Graph
```

Its Editor-only node definitions and validation live in
`Sonosthesia.Signals.Editor`.

### Source provider contract

Raw graph inputs use stable `ControlSignalId` values and do not know their
provider. A provider translates its domain into a generic frame:

```csharp
public interface IControlSignalProvider
{
    ControlSignalProfile Profile { get; }
    bool TrySample(
        double time,
        float[] continuousValues,
        float[] pulseStrengths,
        float[] pulseMagnitudes,
        out ControlSignalFrameHeader header);
    void ResetEvaluationState();
}
```

Timeline, live analysis, interaction, XR, generated envelopes and test fixtures
can each implement this contract or use an equivalent mixer adapter. The core
signal package does not name XAS, peak channels, audio files or an audio sample
rate. XAS-to-control ID mapping belongs to `com.sonosthesia.signals.xas` as a
provider profile. The raw arrays are owned by the target and reused every
evaluation.

### Runtime representation

A `ScriptedImporter` loads the authoring graph with
`GraphDatabase.LoadGraphForImporter` and compiles it into a
`CompiledControlGraph`.

The runtime asset contains only engine-serializable data:

- graph ID for optional GraphVisualization;
- node records;
- operation kind;
- stable node ID;
- source/input/output indices;
- constant/default values;
- filter or delay configuration;
- topological execution order;
- output descriptors;
- optional IDs needed by editor visualization.

No runtime object references `Unity.GraphToolkit.Editor`.

### Runtime execution

The compiler produces a flat topological operation list. The executor owns
fixed-size `float[]`, pulse, and state arrays sized once when a target binds a
graph. One evaluation:

1. copies raw source values into input slots;
2. clears the current pulse-start mask;
3. evaluates operations in compiled order;
4. writes output values to target-owned buffers;
5. dispatches route writes through target-local resolved slot handles.

It performs no LINQ, boxing, dynamic reflection, dictionary lookup, string
comparison, list growth, or temporary object allocation.

Invalid graphs fail import with graph markers. Runtime evaluation never tries
to recover by compiling or interpreting Editor graph nodes.

## 10. Initial node set

### Source nodes

- generic `Continuous Signal Input` identified by `ControlSignalId`;
- generic `Pulse Strength Input` identified by `ControlSignalId`;
- generic `Pulse Magnitude Input` identified by `ControlSignalId`;
- generic `Pulse Started Input` identified by `ControlSignalId`;
- optional provider-profile nodes supplied by integration packages.

`com.sonosthesia.signals.xas` adds authoring conveniences such as `XAS RMS`,
`XAS Lows` and `XAS Pulse Main`. Those nodes compile to the same generic source
IDs and operations as any other provider; the runtime graph contains no XAS
node kind. A live analyser, XR provider or test fixture can expose its own
profile and either use the generic input node or provide equivalent authoring
shortcuts.

### Continuous operators

- constant;
- add;
- multiply;
- subtract/divide with explicit divide-by-zero policy;
- clamp;
- remap;
- smoothstep;
- invert;
- min/max;
- weighted mix;
- threshold with hysteresis;
- sample and hold.

### Filtering and time

- one-pole low-pass filter;
- separate attack/release envelope follower;
- deterministic delay;
- slew-rate limiter.

### Pulse operators

- pulse strength to float;
- pulse magnitude to float;
- started pulse to trigger;
- pulse-to-envelope;
- pulse-to-decay;
- pulse channel gate;
- pulse merge.

### Output nodes

- `Float Control Output` with stable ID, display name and optional range;
- `Pulse Control Output` with stable ID and display name.

Input ports that accept more than one wire must be explicitly declared as
multi-capacity. Single-value math inputs use single capacity.

### Delay semantics

`Delay` is exact during seeks for a stateless upstream subgraph:

```text
output(t) = upstream(t - delay)
```

The executor may recursively evaluate a stateless upstream branch at an
earlier Timeline time. The delay is rejected at import when its upstream
contains a stateful filter unless the graph explicitly selects a stateful
history mode.

Stateful history mode is not part of the Phase 7 minimum gate.

### Stateful filter semantics

One-pole, attack/release and slew nodes hold per-target state. On a
discontinuity, they:

1. clear previous-state validity;
2. initialize to the current upstream value;
3. resume integration from the next forward frame.

This produces deterministic raw XAS seeking and stable stateful behaviour
without attempting to reconstruct an unbounded audio history. A future
lookback-warmup mode may evaluate a bounded pre-roll.

## 11. Graph validation

`ControlSignalGraph.OnGraphChanged` and importer validation must report:

- no output nodes;
- disconnected required inputs;
- mismatched signal and port types;
- pulse data connected to float ports without an explicit conversion node;
- cycles;
- multiple nodes writing the same output ID;
- delay feeding a stateful upstream branch;
- invalid normalization ranges;
- non-positive filter times or delay;
- graph size beyond the supported runtime record budget;
- route/output ID collisions;
- inaccessible or unsupported source inputs.

Errors appear as Graph Toolkit graph/node markers. Import produces no runtime
asset for an invalid graph.

## 12. Control slot routing

### Slot target contract

Procedural geometry and arbitrary clients implement a small interface:

```csharp
public interface IControlSlotTarget
{
    int ControlSlotCount { get; }
    bool TryGetControlSlot(ControlSignalId id, out int slotHandle);
    ControlSignalType GetControlSlotType(int slotHandle);
    void WriteFloat(int slotHandle, float value);
    void WritePulse(int slotHandle, in ControlPulse pulse);
}
```

The interface is deliberately numeric on the hot path:

- `TryGetControlSlot` is called during target initialization, not per frame;
- resolved routes store the sink reference and integer handle;
- `WriteFloat` and `WritePulse` contain no lookup or string work;
- consumers may clamp or validate in their adapter.

A `MonoBehaviour` implementing this interface is an adapter. Generic signal
runtime code does not inspect component fields through reflection.

### Route bindings

`ControlSignalTarget` stores a serialized list of:

- graph output ID;
- target `Component`;
- target slot ID;
- resolved target slot handle;
- signal type;
- optional gain/bias policy;
- enabled state.

Binding is resolved at enable time and again after relevant Inspector changes.
Invalid bindings remain explicit Editor/runtime warnings and are not silently
skipped as success.

### Procedural geometry adapter

Phase 7 adds `ProceduralMeshControlSlots` with initial slots:

```text
procgeo.field.pulseAmplitude       float
procgeo.field.directionalStrength  float
procgeo.simulation.stiffness       float
procgeo.simulation.damping         float
procgeo.impulse.trigger            pulse
```

The adapter:

- holds a reference to `ProceduralMeshRenderer`;
- copies and validates the current `SimulationParameters` before setting
  changed values;
- copies and sets `GeometryFieldSettings`;
- optionally references an `ImpulseWaveSource` for pulse events;
- writes no slot during `Awake`;
- performs no per-slot allocation;
- exposes an explicit reset-to-authoring-values operation.

The completion demo may route:

- RMS through remap/smooth into `pulseAmplitude`;
- lows through attack/release into stiffness;
- main peak pulse into `procgeo.impulse.trigger`.

The other slots establish the general adapter shape and support later demos.

## 13. Client consumption

In addition to routed adapters, clients can read a target's latest raw or
processed data:

```csharp
bool TryGetRawFloat(ControlSignalId id, out float value);
bool TryGetOutputFloat(ControlSignalId id, out float value);
bool TryGetOutputPulse(ControlSignalId id, out ControlPulse pulse);
```

Lookup overloads returning by integer output index are provided for hot paths.
Authoring convenience lookup happens outside the per-frame loop.

No callback is required for continuous data. Pulse callbacks may later be added
as an optional event stream, but Phase 7's primary transport remains a
caller-owned current-frame struct.

## 14. Performance and memory plan

### Required budgets

For the Phase 7 completion slice:

- one active XAS track;
- one compiled graph with up to 32 float operations;
- up to 4 pulse channels;
- up to 8 routes;
- target evaluation at 120 Hz.

Budgets:

- 0 bytes of managed allocation after target enable and chunk warm-up;
- at most two cached continuous chunks and two event chunks per reader;
- no whole-file payload array;
- no runtime graph allocation;
- no route dictionary lookup or reflection;
- one bounded `XasReader` seek/read for a current chunk miss;
- signal/control CPU evaluation <= 0.25 ms at the 99th percentile on the
  documented desktop baseline;
- graph bind/reset performed once per enable, not once per frame;
- route writes omitted when the output value and pulse mask have not changed,
  where the consumer supports this safely.

Chunk misses during a large scrub may allocate only when a preconfigured cache
size changes; ordinary scrubbing must reuse the cache slots. Reader cache
buffers are sized or pooled at enable time.

### Instrumentation

Add development counters for:

- XAS chunk cache hits/misses;
- source seek count;
- continuous samples evaluated;
- events scanned and merged;
- graph nodes evaluated;
- stateful resets;
- route writes;
- target evaluation time;
- managed bytes around a warmed PlayMode frame window.

Instrumentation must be zero-allocation in release/player builds and disabled
or compiled out when not selected.

## 15. Test plan

### XAS and Timeline EditMode

- XAS asset validation and descriptor summary;
- player-safe path validation;
- source trim and speed conversion;
- normalized continuous overlap blend;
- unavailable clip/weight handling;
- pulse merge and `StartedMask`;
- event delivery across clip edges;
- duplicate suppression after stationary repeated frames;
- reverse scrub does not replay stale starts;
- jump/discontinuity propagation.

### Graph compiler EditMode

- topological order;
- constant and scalar operator values;
- remap/clamp/filter defaults;
- delay timing;
- delay/stateful-upstream validation;
- output ID and route ID uniqueness;
- malformed graph produces an import error marker;
- runtime graph contains no `Unity.GraphToolkit.Editor` dependency.

### Runtime and PlayMode

- one XAS track drives one `ProceduralMeshControlSlots`;
- crossfade changes one continuous route with no discontinuity;
- peak events trigger exactly once per forward crossing;
- seek to an earlier or later Timeline time resets stateful filters;
- control target survives PlayableGraph rebuild/stop/restart;
- target disable disposes readers;
- warmed signal loop allocates zero managed bytes;
- unsupported/over-limit values are clamped by the consumer adapter;
- simulation remains finite and bounded under maximum documented signals.

### Phase regression

- Run Phase 1-6 EditMode and PlayMode suites.
- Add a GPU lifecycle assertion proving the adapter introduces no extra
  buffers, readbacks, or topology rebuilds.
- Verify geometry remains bounded at maximum configured audio signal values.

## 16. Test scene and tooling

Create one Phase 7 scene:

- one audio source or timeline reference for synchronization;
- one `PlayableDirector`;
- one `XasTrack`;
- one `ControlSignalTarget`;
- one `.controlgraph` asset;
- one icosphere `ProceduralMeshRenderer`;
- one `ProceduralMeshControlSlots`;
- one optional `ImpulseWaveSource`.

The scene UI follows the repository convention: simple, functional UI Toolkit
component inspectors on GameObjects, not a runtime control dashboard.

The Inspector for `ControlSignalTarget` should display:

- raw control values;
- generic pulse strengths/started state;
- graph evaluation time;
- route binding state;
- last discontinuity/reset reason;
- optional performance counters.

XAS-specific details such as resolved asset paths, reader state and chunk
cache counters belong to the XAS provider/track Inspector in
`com.sonosthesia.signals.xas`, not to `ControlSignalTarget`.

Timeline assets, tracks, graph assets and targets are the delivered authoring
surfaces. A custom polished Timeline track editor is out of scope unless needed
to display validation errors clearly.

## 17. Milestones

### M1 - Signal contracts and package

- Create `com.sonosthesia.signals`.
- Add runtime frame, pulse, ID and slot contracts.
- Add the provider profile and provider-neutral sample contract.
- Add the generic `ControlSignalTarget`.
- Add package and assembly tests.

**Gate:** a mock, non-XAS provider can update a mock slot target with no
allocation and `com.sonosthesia.signals` has no package reference to
`com.sonosthesia.xas`.

### M2 - XAS Timeline ingestion

- Create `com.sonosthesia.signals.xas`.
- Add XAS source asset and player-safe path validation.
- Map XAS fields into a generic `ControlSignalProfile`.
- Add clip, track and mixer behaviour.
- Add reader ownership and cache lifetime.
- Implement continuous blending and pulse merge.
- Add seek/direction/discontinuity reporting.

**Gate:** a PlayMode Timeline fixture blends two XAS clips and merges their
events without loading either complete file.

### M3 - Graph Toolkit authoring and importer

- Add the 6000.6 `ControlSignalGraph`.
- Add generic source, scalar, filter, delay, pulse and output nodes.
- Add XAS convenience source nodes in the XAS bridge Editor assembly.
- Add graph validation.
- Add ScriptedImporter and runtime graph representation.

**Gate:** a saved graph reopens, reimports, and produces a runtime asset whose
operation stream contains only generic source IDs and no XAS reader or Timeline
types.

### M4 - Runtime graph and routing

- Implement fixed-buffer executor and state management.
- Resolve graph output routes to slot handles once.
- Add stateless delay and stateful filter reset semantics.
- Add instrumentation.

**Gate:** warmed graph evaluation and routing allocate zero managed bytes.

### M5 - Procedural geometry adapter

- Implement `ProceduralMeshControlSlots`.
- Route RMS/pulse examples into field, simulation and impulse controls.
- Add one Phase 7 Timeline scene and tests.

**Gate:** one graph-controlled XAS track visibly drives one
`ProceduralMeshRenderer` while simulation remains stable.

### M6 - Hardening and performance

- Test corruption, missing files, build path resolution, invalid bindings,
  graph changes, Timeline stop/rebuild and repeated enable/disable.
- Publish baseline timing, allocation and cache-miss results.
- Run Phase 1-7 regression suites.

**Gate:** the documented 120 Hz slice meets its CPU/allocation limits and no
pre-existing geometry tests regress.

## 18. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Graph Toolkit is mistaken for a runtime executor | compile it to a plain flat runtime asset during import |
| Timeline scrubbing makes stateful filters non-deterministic | exact raw seeks plus explicit discontinuity reset; delay is exact only for stateless branches |
| Overlapping clips amplify signals unexpectedly | normalized weighted continuous blending |
| Pulse overlap emits duplicate triggers | merge by channel and suppress duplicate source/clip/event identity |
| Property reflection is easy but allocates and is fragile | require typed `IControlSlotTarget` adapters |
| Geometry package becomes coupled to XAS | depend only on generic signal contracts, not the reader or Timeline types |
| Reader opening is repeated per clip | reader cache is owned by target and keyed by source asset |
| Runtime cannot find an imported XAS asset path | enforce StreamingAssets path validation and build checks |
| Route lookup causes per-frame dictionary work | resolve output and slot handles once at enable/import time |
| Graph delay duplicates full graph work | bound delay evaluation, cache within one evaluation, and reject stateful-upstream delay |
| A control drives unstable geometry settings | clamp/validate in procedural adapter and retain simulation limits |
| New graph types become hard to extend | operation records and node compiler cases are versioned and tested independently |

## 19. Definition of done

Phase 7 is complete when:

1. XAS Timeline clips read only bounded chunks during playback and seek;
2. overlapping continuous clips blend with normalized weights;
3. overlapping pulse channels merge without duplicate starts;
4. discontinuity and reverse scrub behaviour is explicit and tested;
5. a Graph Toolkit asset compiles to an Editor-independent runtime graph;
6. initial continuous, pulse, operator, filter and delay nodes work;
7. graph output routes resolve through typed control-slot adapters;
8. arbitrary non-geometry adapters can implement the slot interface;
9. one XAS graph drives a procedural icosphere's field/simulation/impulse
   controls;
10. the warmed signal and route loop allocates zero managed bytes;
11. XAS target enable/disable and Timeline rebuild have no reader or GPU
    lifecycle leaks;
12. the Phase 7 scene exposes functional UI Toolkit component inspectors;
13. Phase 1-7 EditMode/PlayMode suites pass;
14. performance, memory and allocation results are documented.

## 20. Later work

The Phase 7 contracts should allow, but do not include:

- live audio providers implementing the same source contract;
- vector, quaternion and colour graph values;
- target-side event queues beyond the compact pulse frame;
- graph subgraphs and reusable operator libraries;
- exact warmup for stateful filters after arbitrary scrubs;
- variable XAS event types beyond peak channels;
- automatic graph input discovery from future XAS streams;
- runtime graph visualization and per-wire previews;
- Phase 9 XR targets and mixed audio/interaction graphs;
- custom Timeline clip/track visuals.
