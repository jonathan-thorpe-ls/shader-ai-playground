# Sonosthesia Control Signals

Provider-neutral control signal graphs, fixed-buffer execution and typed slot
routing. The package does not depend on XAS, Timeline or procedural geometry.

Implement `IControlSignalProvider` to publish values, feed a
`ControlSignalFrame` into `ControlSignalTarget`, and implement
`IControlSlotTarget` on consumer adapters.

Control graphs are authored as `.controlgraph` assets through
`Assets/Create/Sonosthesia/Control Signal Graph`. Graph Toolkit is Editor-only;
the importer produces `CompiledControlGraph`, which is the runtime executor
asset.
